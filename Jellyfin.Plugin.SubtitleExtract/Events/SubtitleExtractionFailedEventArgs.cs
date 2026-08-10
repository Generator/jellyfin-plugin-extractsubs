using System;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace Jellyfin.Plugin.SubtitleExtract.Events;

/// <summary>
/// Event arguments for subtitle extraction failures.
/// </summary>
public class SubtitleExtractionFailedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SubtitleExtractionFailedEventArgs"/> class.
    /// </summary>
    /// <param name="item">The media item that failed.</param>
    /// <param name="stream">The subtitle stream that failed.</param>
    /// <param name="outputPath">The intended output path.</param>
    /// <param name="exception">The failure exception.</param>
    public SubtitleExtractionFailedEventArgs(BaseItem item, MediaStream stream, string outputPath, Exception exception)
    {
        Item = item;
        Stream = stream;
        OutputPath = outputPath;
        Exception = exception;
    }

    /// <summary>
    /// Gets the media item that failed.
    /// </summary>
    public BaseItem Item { get; }

    /// <summary>
    /// Gets the subtitle stream that failed.
    /// </summary>
    public MediaStream Stream { get; }

    /// <summary>
    /// Gets the intended output path.
    /// </summary>
    public string OutputPath { get; }

    /// <summary>
    /// Gets the failure exception.
    /// </summary>
    public Exception Exception { get; }
}
