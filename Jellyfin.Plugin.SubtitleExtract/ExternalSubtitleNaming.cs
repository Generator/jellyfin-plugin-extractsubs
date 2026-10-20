using System;
using System.IO;
using Jellyfin.Plugin.SubtitleExtract.Configuration;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;

namespace Jellyfin.Plugin.SubtitleExtract;

/// <summary>
/// Builds external subtitle file names following the Jellyfin naming convention.
/// </summary>
public static class ExternalSubtitleNaming
{
    /// <summary>
    /// Builds the external subtitle file name for a stream.
    /// Pattern: {basename}[.default].{lang}[.{marker}].{ext}.
    /// </summary>
    /// <param name="videoPath">The path of the video file.</param>
    /// <param name="stream">The subtitle stream.</param>
    /// <param name="config">The plugin configuration.</param>
    /// <param name="localization">The localization manager used to normalize language codes.</param>
    /// <param name="forceDefault">Kept for compatibility; when IncludeDefaultMarker is true, .default is always added regardless of IsDefault.</param>
    /// <param name="extensionOverride">Overrides the extension derived from the stream codec (used for converted copies).</param>
    /// <param name="includeDefaultMarker">When false, the .default marker is omitted even if the configuration enables it.</param>
    /// <returns>The external subtitle file name.</returns>
    public static string BuildFileName(
        string videoPath,
        MediaStream stream,
        PluginConfiguration config,
        ILocalizationManager localization,
        bool forceDefault = false,
        string? extensionOverride = null,
        bool includeDefaultMarker = true)
    {
        var basename = Path.GetFileNameWithoutExtension(videoPath);
        var language = NormalizeLanguage(stream.Language, localization);
        var extension = extensionOverride ?? GetFileExtension(stream);

        var name = basename;
        if (includeDefaultMarker && config.IncludeDefaultMarker && !stream.IsForced && !IsSdh(stream))
        {
            name += ".default";
        }

        name += "." + language;

        if (stream.IsForced)
        {
            name += ".forced";
        }
        else if (IsSdh(stream))
        {
            name += ".sdh";
        }

        name += "." + extension;
        return name;
    }

    /// <summary>
    /// Gets the file extension that matches the stream's own (unconverted) format.
    /// </summary>
    /// <param name="stream">The subtitle stream.</param>
    /// <returns>The file extension without the leading dot.</returns>
    internal static string GetFileExtension(MediaStream stream)
    {
        if (string.Equals(stream.Codec, "pgssub", StringComparison.OrdinalIgnoreCase))
        {
            return "sup";
        }

        if (MediaStream.IsVobSubFormat(stream.Codec))
        {
            return "mks";
        }

        if (string.Equals(stream.Codec, "ass", StringComparison.OrdinalIgnoreCase)
            || string.Equals(stream.Codec, "ssa", StringComparison.OrdinalIgnoreCase))
        {
            return "ass";
        }

        if (string.Equals(stream.Codec, "webvtt", StringComparison.OrdinalIgnoreCase)
            || string.Equals(stream.Codec, "vtt", StringComparison.OrdinalIgnoreCase))
        {
            return "vtt";
        }

        return "srt";
    }

    private static string NormalizeLanguage(string? language, ILocalizationManager localization)
    {
        if (string.IsNullOrEmpty(language))
        {
            return "und";
        }

        var culture = localization.FindLanguageInfo(language);
        if (culture?.ThreeLetterISOLanguageName is not null)
        {
            return culture.ThreeLetterISOLanguageName;
        }

        // Fallback: if it's already 3 letters, use it; otherwise lowercase the raw value.
        return language.Length == 3 ? language.ToLowerInvariant() : language.ToLowerInvariant();
    }

    private static bool IsSdh(MediaStream stream)
    {
        return stream.IsHearingImpaired
            || stream.Title?.IndexOf("SDH", StringComparison.OrdinalIgnoreCase) >= 0
            || stream.Title?.IndexOf("hearing impaired", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
