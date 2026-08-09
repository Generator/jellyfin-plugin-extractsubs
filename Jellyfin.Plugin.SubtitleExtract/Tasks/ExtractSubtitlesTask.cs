using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.SubtitleExtract.Tasks;

/// <summary>
/// Scheduled task to extract embedded subtitles for immediate access in web player.
/// </summary>
public class ExtractSubtitlesTask : IScheduledTask
{
    private const int QueryPageLimit = 250;

    private readonly ILibraryManager _libraryManager;
    private readonly ILocalizationManager _localization;
    private readonly SubtitleExtractionService _extractionService;

    private static readonly BaseItemKind[] _itemTypes = [BaseItemKind.Episode, BaseItemKind.Movie];
    private static readonly MediaType[] _mediaTypes = [MediaType.Video];
    private static readonly SourceType[] _sourceTypes = [SourceType.Library];
    private static readonly DtoOptions _dtoOptions = new(false);

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtractSubtitlesTask" /> class.
    /// </summary>
    /// <param name="libraryManager">Instance of <see cref="ILibraryManager"/> interface.</param>
    /// <param name="extractionService"><see cref="SubtitleExtractionService"/> instance.</param>
    /// <param name="localization">Instance of <see cref="ILocalizationManager"/> interface.</param>
    public ExtractSubtitlesTask(
        ILibraryManager libraryManager,
        SubtitleExtractionService extractionService,
        ILocalizationManager localization)
    {
        _libraryManager = libraryManager;
        _localization = localization;
        _extractionService = extractionService;
    }

    /// <inheritdoc />
    public string Key => "ExtractSubtitles";

    /// <inheritdoc />
    public string Name => "Extract Subtitles";

    /// <inheritdoc />
    public string Description => "Extracts embedded subtitles.";

    /// <inheritdoc />
    public string Category => _localization.GetLocalizedString("TasksLibraryCategory");

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return [];
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var startProgress = 0d;

        var config = SubtitleExtractPlugin.Current.Configuration;
        var libs = config.SelectedSubtitlesLibraries;

        Guid[] parentIds = [];
        if (libs.Length > 0)
        {
            parentIds = _libraryManager.GetVirtualFolders()
                .Where(vf => libs.Contains(vf.Name))
                .Select(vf => Guid.Parse(vf.ItemId))
                .ToArray();
        }

        if (parentIds.Length > 0)
        {
            foreach (var parentId in parentIds)
            {
                startProgress = await RunExtractionWithProgress(progress, parentId, parentIds, startProgress, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            await RunExtractionWithProgress(progress, null, [], startProgress, cancellationToken).ConfigureAwait(false);
        }

        progress.Report(100);
    }

    private async Task<double> RunExtractionWithProgress(
        IProgress<double> progress,
        Guid? parentId,
        IReadOnlyCollection<Guid> parentIds,
        double startProgress,
        CancellationToken cancellationToken)
    {
        var libsCount = parentIds.Count > 0 ? parentIds.Count : 1;
        var config = SubtitleExtractPlugin.Current.Configuration;
        var workerThreads = Math.Max(1, config.WorkerThreads);

        // Note: HasSubtitles is intentionally NOT set here. The server strips embedded subtitle
        // streams from the database when AllowEmbeddedSubtitles is disabled, so filtering on
        // HasSubtitles would skip every item. The extraction service probes each file directly.
        var query = new InternalItemsQuery
        {
            Recursive = true,
            IsVirtualItem = false,
            IncludeItemTypes = _itemTypes,
            DtoOptions = _dtoOptions,
            MediaTypes = _mediaTypes,
            SourceTypes = _sourceTypes,
            Limit = QueryPageLimit
        };

        if (!parentId.IsNullOrEmpty())
        {
            query.ParentId = parentId.Value;
        }

        var numberOfVideos = _libraryManager.GetCount(query);
        if (numberOfVideos == 0)
        {
            return startProgress + (100d / libsCount);
        }

        var startIndex = 0;
        var completedVideos = 0;

        while (startIndex < numberOfVideos)
        {
            query.StartIndex = startIndex;
            var videos = _libraryManager.GetItemList(query);

            await Parallel.ForEachAsync(
                videos,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = workerThreads,
                    CancellationToken = cancellationToken
                },
                async (video, ct) =>
                {
                    await _extractionService.ExtractSubtitlesAsync(video, ct).ConfigureAwait(false);

                    var completed = Interlocked.Increment(ref completedVideos);
                    progress.Report(startProgress + (100d * completed / numberOfVideos / libsCount));
                }).ConfigureAwait(false);

            startIndex += QueryPageLimit;
        }

        startProgress += 100d / libsCount;
        return startProgress;
    }
}
