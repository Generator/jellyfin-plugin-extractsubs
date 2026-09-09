using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleExtract;

/// <summary>
/// Hosted service that extracts embedded subtitles when a library item is added or updated.
/// </summary>
/// <remarks>
/// The metadata-provider path (<see cref="Providers.SubtitleExtractionProvider"/>) only runs during
/// metadata refresh and can be skipped when an item is considered unchanged. Subscribing to both
/// <see cref="ILibraryManager.ItemAdded"/> and <see cref="ILibraryManager.ItemUpdated"/> guarantees
/// extraction is triggered reliably whether an item is newly added or re-scanned/refreshed,
/// mirroring the pattern used by the webhook plugin.
/// </remarks>
public sealed class SubtitleExtractItemAddedEntryPoint : IHostedService, IDisposable
{
    private readonly ILibraryManager _libraryManager;
    private readonly SubtitleExtractionService _extractionService;
    private readonly ILogger<SubtitleExtractItemAddedEntryPoint> _logger;
    private readonly CancellationTokenSource _cts = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SubtitleExtractItemAddedEntryPoint"/> class.
    /// </summary>
    /// <param name="libraryManager">Instance of the <see cref="ILibraryManager"/> interface.</param>
    /// <param name="extractionService">Instance of the <see cref="SubtitleExtractionService"/> class.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{T}"/> interface.</param>
    public SubtitleExtractItemAddedEntryPoint(
        ILibraryManager libraryManager,
        SubtitleExtractionService extractionService,
        ILogger<SubtitleExtractItemAddedEntryPoint> logger)
    {
        _libraryManager = libraryManager;
        _extractionService = extractionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded += OnItemChanged;
        _libraryManager.ItemUpdated += OnItemChanged;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemAdded -= OnItemChanged;
        _libraryManager.ItemUpdated -= OnItemChanged;
        await _cts.CancelAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts.Dispose();
        }
    }

    private void OnItemChanged(object? sender, ItemChangeEventArgs eventArgs)
    {
        if (eventArgs.Item.IsVirtualItem || !eventArgs.Item.IsFileProtocol)
        {
            return;
        }

        if (eventArgs.Item is not Video)
        {
            return;
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "Triggering subtitle extraction for {Reason} item {Path}",
                eventArgs.UpdateReason,
                eventArgs.Item.Path);
        }

        _ = ExtractAsync(eventArgs.Item, _cts.Token);
    }

#pragma warning disable CA1031 // Fire-and-forget: must observe all exceptions to avoid unobserved task faults.
    private async Task ExtractAsync(BaseItem item, CancellationToken cancellationToken)
    {
        try
        {
            await _extractionService.ExtractSubtitlesAsync(item, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Extraction was canceled; nothing else to do.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract subtitles for item {Path}", item.Path);
        }
    }
#pragma warning restore CA1031
}
