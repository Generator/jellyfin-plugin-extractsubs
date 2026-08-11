using System;
using System.Collections.Generic;
using Jellyfin.Plugin.SubtitleExtract.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.SubtitleExtract;

/// <summary>
/// Plugin entrypoint.
/// </summary>
public class SubtitleExtractPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SubtitleExtractPlugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public SubtitleExtractPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Current = this;
    }

    /// <inheritdoc />
    public override string Name => "Better Subtitle Extractor";

    /// <inheritdoc />
    public override Guid Id => new("77BE2143-68BE-4E77-AFC8-82859969038A");

    /// <inheritdoc />
    public override string Description => "Extracts embedded subtitles and attachments";

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static SubtitleExtractPlugin Current { get; private set; } = null!;

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = "Better Subtitle Extractor",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.Web.config.html",
            },
            new PluginPageInfo
            {
                Name = "Better Subtitle Extractor.js",
                EmbeddedResourcePath = GetType().Namespace + ".Configuration.Web.config.js",
            }
        ];
    }
}
