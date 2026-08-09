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
using MediaBrowser.Common;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.MediaEncoding;
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
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _pathLocks = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SubtitleExtractionService"/> class.
    /// </summary>
    /// <param name="mediaEncoder">Instance of <see cref="IMediaEncoder"/> interface.</param>
    /// <param name="localization">Instance of <see cref="ILocalizationManager"/> interface.</param>
    /// <param name="logger">Instance of <see cref="ILogger"/> interface.</param>
    public SubtitleExtractionService(
        IMediaEncoder mediaEncoder,
        ILocalizationManager localization,
        ILogger<SubtitleExtractionService> logger)
    {
        _mediaEncoder = mediaEncoder;
        _localization = localization;
        _logger = logger;
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
        var pathLock = _pathLocks.GetOrAdd(item.Path, _ => new SemaphoreSlim(1, 1));
        await pathLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var mediaInfo = await ProbeAsync(item.Path, cancellationToken).ConfigureAwait(false);
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
                await ExtractStreamAsync(mediaInfo, stream, config, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            pathLock.Release();
        }
    }

    private async Task<MediaInfo?> ProbeAsync(string path, CancellationToken cancellationToken)
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

        return await _mediaEncoder.GetMediaInfo(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task ExtractStreamAsync(
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
        var tempPath = outputPath + ".tmp";
        var outputCodec = IsCodecCopyable(stream.Codec) ? "copy" : "srt";
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
        else
        {
            var ext = Path.GetExtension(outputPath);
            if (!string.IsNullOrEmpty(ext) && ext.Length > 1)
            {
                var format = ext[1..].ToLowerInvariant();
                arguments.Add("-f");
                arguments.Add(format);
            }
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
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "Failed to delete partial subtitle file {TempPath}", tempPath);
            }

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
}
