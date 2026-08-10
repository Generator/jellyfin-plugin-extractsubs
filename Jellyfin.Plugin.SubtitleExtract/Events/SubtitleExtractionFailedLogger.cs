using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.SubtitleExtract.Events;
using MediaBrowser.Controller.Events;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.SubtitleExtract.Events;

/// <summary>
/// Logs subtitle extraction failures.
/// </summary>
public class SubtitleExtractionFailedLogger : IEventConsumer<SubtitleExtractionFailedEventArgs>
{
    private readonly ILogger<SubtitleExtractionFailedLogger> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubtitleExtractionFailedLogger"/> class.
    /// </summary>
    /// <param name="logger">Instance of <see cref="ILogger"/> interface.</param>
    public SubtitleExtractionFailedLogger(ILogger<SubtitleExtractionFailedLogger> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public Task OnEvent(SubtitleExtractionFailedEventArgs eventArgs)
    {
        ArgumentNullException.ThrowIfNull(eventArgs);

        _logger.LogError(
            eventArgs.Exception,
            "Subtitle extraction failed for {ItemName} stream {StreamIndex}",
            eventArgs.Item?.Name ?? "Unknown item",
            eventArgs.Stream?.Index ?? -1);

        return Task.CompletedTask;
    }
}
