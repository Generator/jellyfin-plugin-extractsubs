using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleExtract.Providers;

/// <summary>
/// Extracts embedded subtitles while library scanning for immediate access in web player.
/// </summary>
public class SubtitleExtractionProvider : ICustomMetadataProvider<Episode>,
    ICustomMetadataProvider<Movie>,
    ICustomMetadataProvider<Video>,
    IHasItemChangeMonitor,
    IHasOrder,
    IForcedProvider
{
    private readonly ILogger<SubtitleExtractionProvider> _logger;

    private readonly SubtitleExtractionService _extractionService;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubtitleExtractionProvider"/> class.
    /// </summary>
    /// <param name="extractionService"><see cref="SubtitleExtractionService"/> instance.</param>
    /// <param name="logger">Instance of the <see cref="ILogger"/> interface.</param>
    public SubtitleExtractionProvider(
        SubtitleExtractionService extractionService,
        ILogger<SubtitleExtractionProvider> logger)
    {
        _logger = logger;
        _extractionService = extractionService;
    }

    /// <inheritdoc />
    public string Name => "Subtitle Extraction";

    /// <summary>
    /// Gets the order in which the provider should be called. (Core provider is = 100).
    /// </summary>
    public int Order => 1000;

    /// <inheritdoc/>
    public bool HasChanged(BaseItem item, IDirectoryService directoryService)
    {
        if (item.IsFileProtocol)
        {
            var file = directoryService.GetFile(item.Path);
            if (file is not null && item.HasChanged(file.LastWriteTimeUtc))
            {
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public Task<ItemUpdateType> FetchAsync(Episode item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        return FetchSubtitles(item, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ItemUpdateType> FetchAsync(Movie item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        return FetchSubtitles(item, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ItemUpdateType> FetchAsync(Video item, MetadataRefreshOptions options, CancellationToken cancellationToken)
    {
        return FetchSubtitles(item, cancellationToken);
    }

    private async Task<ItemUpdateType> FetchSubtitles(BaseItem item, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(item);
        var config = SubtitleExtractPlugin.Current!.Configuration;
        if (config.ExtractionDuringLibraryScan)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Extracting subtitles for: {Video}", item.Path);
            }

            try
            {
                await _extractionService.ExtractSubtitlesAsync(item, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Subtitle extraction failed for: {Video}", item.Path);
            }

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                _logger.LogDebug("Finished subtitle extraction for: {Video}", item.Path);
            }
        }

        return ItemUpdateType.None;
    }
}
