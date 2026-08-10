using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitleExtract.Configuration;
using Jellyfin.Plugin.SubtitleExtract.Events;
using MediaBrowser.Common;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Model.Configuration;
using MediaBrowser.Model.Dlna;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleExtract;

/// <summary>
/// Probes media files directly and extracts embedded subtitles as external files next to the media.
/// </summary>
/// <remarks>
/// Uses <see cref="IMediaEncoder.GetMediaInfo(MediaInfoRequest, CancellationToken)"/> to bypass the
/// server's <c>AllowEmbeddedSubtitles</c> setting, which strips embedded subtitle streams from the
/// library database before this plugin ever sees them.
/// </remarks>
public class SubtitleExtractionService
{
    private const int FfmpegTimeoutMinutes = 10;

    private readonly IMediaEncoder _mediaEncoder;
    private readonly ILocalizationManager _localization;
    private readonly ILogger<SubtitleExtractionService> _logger;
    private readonly ConcurrentDictionary<string, PathLockEntry> _pathLocks = new();
    private readonly ILibraryManager _libraryManager;
    private readonly IEventManager _eventManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubtitleExtractionService"/> class.
    /// </summary>
    /// <param name="mediaEncoder">Instance of <see cref="IMediaEncoder"/> interface.</param>
    /// <param name="localization">Instance of <see cref="ILocalizationManager"/> interface.</param>
    /// <param name="logger">Instance of <see cref="ILogger"/> interface.</param>
    /// <param name="libraryManager">Instance of <see cref="ILibraryManager"/> interface.</param>
    /// <param name="eventManager">Instance of <see cref="IEventManager"/> interface.</param>
    public SubtitleExtractionService(
        IMediaEncoder mediaEncoder,
        ILocalizationManager localization,
        ILogger<SubtitleExtractionService> logger,
        ILibraryManager libraryManager,
        IEventManager eventManager)
    {
        _mediaEncoder = mediaEncoder;
        _localization = localization;
        _logger = logger;
        _libraryManager = libraryManager;
        _eventManager = eventManager;
    }

    /// <summary>
    /// Extracts embedded subtitles from an item to external files next to the media.
    /// </summary>
    /// <param name="item">The media item.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task ExtractSubtitlesAsync(BaseItem item, CancellationToken cancellationToken)
    {
        var config = SubtitleExtractPlugin.Current!.Configuration;
        if (!item.IsFileProtocol)
        {
            return;
        }

        // Serialize concurrent extractions for the same media path so only one caller
        // publishes each subtitle file, while different paths remain independent.
        var pathLock = AcquirePathLock(item.Path);
        await pathLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var mediaInfo = await ProbeAsync(item.Path, item, cancellationToken).ConfigureAwait(false);
            if (mediaInfo is null)
            {
                return;
            }

            var streams = mediaInfo.MediaStreams
                .Where(s => s.Type == MediaStreamType.Subtitle)
                .Where(s => SubtitleStreamFilter.ShouldExtractStream(s, config))
                .ToList();

            if (streams.Count == 0)
            {
                return;
            }

            foreach (var stream in streams)
            {
                await ExtractStreamAsync(item, mediaInfo, stream, config, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            pathLock.Release();
            ReleasePathLock(item.Path);
        }
    }

    private async Task<MediaInfo?> ProbeAsync(string path, BaseItem item, CancellationToken cancellationToken)
    {
        var request = new MediaInfoRequest
        {
            MediaSource = new MediaSourceInfo
            {
                Path = path,
                Protocol = MediaProtocol.File
            },
            MediaType = DlnaProfileType.Video,
            ExtractChapters = false
        };

        var mediaInfo = await _mediaEncoder.GetMediaInfo(request, cancellationToken).ConfigureAwait(false);

        // Respect the library's AllowEmbeddedSubtitles setting so we do not
        // unintentionally override Jellyfin's configuration.
        if (mediaInfo is not null)
        {
            var libraryOptions = _libraryManager.GetLibraryOptions(item);
            if (libraryOptions.AllowEmbeddedSubtitles != EmbeddedSubtitleOptions.AllowAll)
            {
                mediaInfo.MediaStreams = mediaInfo.MediaStreams
                    .Where(s => s.Type != MediaStreamType.Subtitle || IsSubtitleAllowed(s, libraryOptions.AllowEmbeddedSubtitles))
                    .ToArray();
            }
        }

        return mediaInfo;
    }

    private static bool IsSubtitleAllowed(MediaStream stream, EmbeddedSubtitleOptions options)
    {
        if (options == EmbeddedSubtitleOptions.AllowNone)
        {
            return false;
        }

        if (options == EmbeddedSubtitleOptions.AllowText)
        {
            return !IsImageBasedSubtitle(stream);
        }

        if (options == EmbeddedSubtitleOptions.AllowImage)
        {
            return IsImageBasedSubtitle(stream);
        }

        return true;
    }

    private static bool IsImageBasedSubtitle(MediaStream stream)
    {
        return string.Equals(stream.Codec, "pgssub", StringComparison.OrdinalIgnoreCase)
            || string.Equals(stream.Codec, "dvdsub", StringComparison.OrdinalIgnoreCase)
            || string.Equals(stream.Codec, "vobsub", StringComparison.OrdinalIgnoreCase)
            || MediaStream.IsVobSubFormat(stream.Codec);
    }

    private async Task ExtractStreamAsync(
        BaseItem item,
        MediaSourceInfo mediaSource,
        MediaStream stream,
        PluginConfiguration config,
        CancellationToken cancellationToken)
    {
        var outputPath = Path.Combine(
            Path.GetDirectoryName(mediaSource.Path)!,
            ExternalSubtitleNaming.BuildFileName(mediaSource.Path, stream, config, _localization));

        if (File.Exists(outputPath) && !config.OverwriteExisting)
        {
            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Skipping existing subtitle file: {OutputPath}", outputPath);
            }

            return;
        }

        // Write to a unique temporary file first, then atomically publish it to the final
        // path so a failed or interrupted extraction never leaves a partial subtitle behind.
        var tempPath = string.Format(CultureInfo.InvariantCulture, "{0}.{1}.tmp", outputPath, Guid.NewGuid().ToString("N"));
        var outputCodec = IsCodecCopyable(stream.Codec) ? "copy" : GetOutputCodec(stream.Codec);
        var arguments = new List<string>
        {
            "-y",
            "-i",
            mediaSource.Path,
            "-map",
            string.Format(CultureInfo.InvariantCulture, "0:{0}", stream.Index),
            "-an",
            "-vn",
            "-c:s",
            outputCodec
        };

        if (MediaStream.IsVobSubFormat(stream.Codec))
        {
            arguments.Add("-f");
            arguments.Add("matroska");
        }
        else if (!IsCodecCopyable(stream.Codec) && !string.Equals(outputCodec, "srt", StringComparison.OrdinalIgnoreCase))
        {
            arguments.Add("-f");
            arguments.Add(outputCodec);
        }

        arguments.Add("-flush_packets");
        arguments.Add("1");
        arguments.Add(tempPath);

        try
        {
            await RunFfmpegAsync(arguments, cancellationToken).ConfigureAwait(false);

            var fileInfo = new FileInfo(tempPath);
            if (!fileInfo.Exists || fileInfo.Length == 0)
            {
                throw new FfmpegException(string.Format(CultureInfo.InvariantCulture, "ffmpeg produced no output for subtitle stream {0}", stream.Index));
            }

            File.Move(tempPath, outputPath, true);
        }
        catch (Exception ex)
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(cleanupEx, "Failed to delete partial subtitle file {TempPath}", tempPath);
            }

            _logger.LogError(ex, "Subtitle extraction failed for {Video}", mediaSource.Path);
            await _eventManager.PublishAsync(new SubtitleExtractionFailedEventArgs(item, stream, outputPath, ex)).ConfigureAwait(false);
            throw;
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Extracted subtitle stream {Index} to {OutputPath}", stream.Index, outputPath);
        }
    }

    private async Task RunFfmpegAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                FileName = _mediaEncoder.EncoderPath,
                WindowStyle = ProcessWindowStyle.Hidden,
                ErrorDialog = false
            },
            EnableRaisingEvents = true
        };

        process.StartInfo.ArgumentList.Add("-nostdin");
        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("{File} {Arguments}", process.StartInfo.FileName, string.Join(' ', process.StartInfo.ArgumentList));
        }

        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting ffmpeg");
            throw;
        }

        process.StandardInput.Close();
        var standardErrorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var waitSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        waitSource.CancelAfter(TimeSpan.FromMinutes(FfmpegTimeoutMinutes));

        var exitCode = 0;
        try
        {
            await process.WaitForExitAsync(waitSource.Token).ConfigureAwait(false);
            exitCode = process.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(true);
            }
            catch (InvalidOperationException)
            {
                // Process already exited.
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new FfmpegException("ffmpeg subtitle extraction timed out.");
        }

        var standardError = await standardErrorTask.ConfigureAwait(false);
        if (exitCode != 0)
        {
            _logger.LogError("ffmpeg subtitle extraction failed for {Arguments}: {FfmpegOutput}", string.Join(' ', arguments), standardError);
            throw new FfmpegException(string.Format(CultureInfo.InvariantCulture, "ffmpeg subtitle extraction failed for {0}", string.Join(' ', arguments)));
        }
    }

    private static bool IsCodecCopyable(string? codec)
    {
        return string.Equals(codec, "ass", StringComparison.OrdinalIgnoreCase)
            || string.Equals(codec, "ssa", StringComparison.OrdinalIgnoreCase)
            || string.Equals(codec, "srt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(codec, "subrip", StringComparison.OrdinalIgnoreCase)
            || string.Equals(codec, "pgssub", StringComparison.OrdinalIgnoreCase)
            || string.Equals(codec, "dvbsub", StringComparison.OrdinalIgnoreCase)
            || MediaStream.IsVobSubFormat(codec);
    }

    private static string GetOutputCodec(string? codec)
    {
        if (string.Equals(codec, "ass", StringComparison.OrdinalIgnoreCase)
            || string.Equals(codec, "ssa", StringComparison.OrdinalIgnoreCase))
        {
            return "ass";
        }

        if (string.Equals(codec, "webvtt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(codec, "vtt", StringComparison.OrdinalIgnoreCase))
        {
            return "webvtt";
        }

        if (string.Equals(codec, "srt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(codec, "subrip", StringComparison.OrdinalIgnoreCase))
        {
            return "srt";
        }

        // For other text-based codecs, convert to SRT.
        if (!IsImageBasedSubtitleCodec(codec))
        {
            return "srt";
        }

        throw new NotSupportedException(string.Format(CultureInfo.InvariantCulture, "Unsupported subtitle codec: {0}", codec));
    }

    private static bool IsImageBasedSubtitleCodec(string? codec)
    {
        return string.Equals(codec, "pgssub", StringComparison.OrdinalIgnoreCase)
            || string.Equals(codec, "dvdsub", StringComparison.OrdinalIgnoreCase)
            || string.Equals(codec, "dvbsub", StringComparison.OrdinalIgnoreCase)
            || string.Equals(codec, "vobsub", StringComparison.OrdinalIgnoreCase)
            || MediaStream.IsVobSubFormat(codec);
    }

    private SemaphoreSlim AcquirePathLock(string path)
    {
        var entry = _pathLocks.GetOrAdd(path, _ => new PathLockEntry());
        entry.IncrementReference();
        return entry.Semaphore;
    }

    private void ReleasePathLock(string path)
    {
        if (_pathLocks.TryGetValue(path, out var entry))
        {
            if (entry.DecrementReference())
            {
                if (_pathLocks.TryRemove(path, out var removedEntry) && ReferenceEquals(removedEntry, entry))
                {
                    removedEntry.Dispose();
                }
            }
        }
    }

    private sealed class PathLockEntry : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private int _referenceCount;

        public PathLockEntry()
        {
            _semaphore = new SemaphoreSlim(1, 1);
            _referenceCount = 1;
        }

        public SemaphoreSlim Semaphore => _semaphore;

        public void IncrementReference()
        {
            Interlocked.Increment(ref _referenceCount);
        }

        public bool DecrementReference()
        {
            return Interlocked.Decrement(ref _referenceCount) == 0;
        }

        public void Dispose()
        {
            _semaphore.Dispose();
        }
    }
}
