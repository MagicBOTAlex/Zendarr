using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Tv;
using MovieImport = NzbDrone.Core.MediaFiles.MovieImport;

namespace NzbDrone.Core.Download
{
    public interface ICompletedDownloadService
    {
        void Check(TrackedDownload trackedDownload);
        void Import(TrackedDownload trackedDownload);
        bool VerifyImport(TrackedDownload trackedDownload, List<ImportResult> importResults);
        bool VerifyImport(TrackedDownload trackedDownload, List<NzbDrone.Core.MediaFiles.MovieImport.ImportResult> importResults);
    }

    public class CompletedDownloadService : ICompletedDownloadService
    {
        private const int MaxImportAttempts = 3;

        // An ImportBlocked download is waiting for a human, not for another automatic retry.
        // Three one-minute refreshes (~3 minutes) is not a window anyone can act in, and failing
        // the item then blocklists a healthy release and triggers a replacement search. Give a
        // manual-interaction download a real, time-based window instead; only the automatic
        // paths that never reach ImportBlocked keep the 3-attempt budget.
        private static readonly TimeSpan ManualInteractionWindow = TimeSpan.FromHours(24);

        private readonly ConcurrentDictionary<string, DateTime> _manualInteractionStarted = new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        private readonly IEventAggregator _eventAggregator;
        private readonly IConfigService _configService;
        private readonly IHistoryService _historyService;
        private readonly IProvideImportItemService _provideImportItemService;
        private readonly IDownloadedEpisodesImportService _downloadedEpisodesImportService;
        private readonly IDownloadedMovieImportService _downloadedMovieImportService;
        private readonly IParsingService _parsingService;
        private readonly ISeriesService _seriesService;
        private readonly IMovieService _movieService;
        private readonly ITrackedDownloadAlreadyImported _trackedDownloadAlreadyImported;
        private readonly IEpisodeService _episodeService;
        private readonly IMediaFileService _mediaFileService;
        private readonly IRejectedImportService _rejectedImportService;
        private readonly Logger _logger;

        public CompletedDownloadService(IEventAggregator eventAggregator,
                                        IConfigService configService,
                                        IHistoryService historyService,
                                        IProvideImportItemService provideImportItemService,
                                        IDownloadedEpisodesImportService downloadedEpisodesImportService,
                                        IDownloadedMovieImportService downloadedMovieImportService,
                                        IParsingService parsingService,
                                        ISeriesService seriesService,
                                        IMovieService movieService,
                                        ITrackedDownloadAlreadyImported trackedDownloadAlreadyImported,
                                        IEpisodeService episodeService,
                                        IMediaFileService mediaFileService,
                                        IRejectedImportService rejectedImportService,
                                        Logger logger)
        {
            _eventAggregator = eventAggregator;
            _configService = configService;
            _historyService = historyService;
            _provideImportItemService = provideImportItemService;
            _downloadedEpisodesImportService = downloadedEpisodesImportService;
            _downloadedMovieImportService = downloadedMovieImportService;
            _parsingService = parsingService;
            _seriesService = seriesService;
            _movieService = movieService;
            _trackedDownloadAlreadyImported = trackedDownloadAlreadyImported;
            _episodeService = episodeService;
            _mediaFileService = mediaFileService;
            _rejectedImportService = rejectedImportService;
            _logger = logger;
        }

        public void Check(TrackedDownload trackedDownload)
        {
            if (!IsImportCandidate(trackedDownload.DownloadItem))
            {
                return;
            }

            SetImportItem(trackedDownload);

            // Only process tracked downloads that are still downloading or have been blocked for importing due to an issue with matching
            if (trackedDownload.State != TrackedDownloadState.Downloading && trackedDownload.State != TrackedDownloadState.ImportBlocked)
            {
                return;
            }

            // Stop re-attempting an import that keeps failing (e.g. a destination conflict).
            // The download stays ImportBlocked so it is clearly shown as requiring manual
            // interaction instead of flickering through ImportPending every refresh. A warned
            // download is the exception: it entered the state machine with a client-side failure
            // and cannot be retried forever, so once its budget is spent it is failed instead of
            // being parked in ImportBlocked with no way out. An ImportBlocked download is waiting
            // for a human, though, so it only becomes eligible for that failure after the
            // manual-interaction window has elapsed.
            if (trackedDownload.State == TrackedDownloadState.ImportBlocked &&
                trackedDownload.ImportAttempts >= MaxImportAttempts)
            {
                if (IsWithinManualInteractionWindow(trackedDownload))
                {
                    return;
                }

                if (trackedDownload.DownloadItem.Status == DownloadItemStatus.Warning)
                {
                    _logger.Debug("Warned download '{0}' could not be imported after {1} attempts, marking as failed",
                        trackedDownload.DownloadItem.Title,
                        trackedDownload.ImportAttempts);

                    trackedDownload.Fail();
                    ForgetManualInteraction(trackedDownload);
                }

                return;
            }

            if (trackedDownload.RemoteMovie != null)
            {
                CheckMovie(trackedDownload);
                return;
            }

            var grabbedHistories = _historyService.FindByDownloadId(trackedDownload.DownloadItem.DownloadId).Where(h => h.EventType == EpisodeHistoryEventType.Grabbed).ToList();
            var historyItem = grabbedHistories.MaxBy(h => h.Date);

            if (historyItem == null && trackedDownload.DownloadItem.Category.IsNullOrWhiteSpace())
            {
                trackedDownload.Warn($"Download wasn't grabbed by {BuildInfo.AppName} and not in a category, Skipping.");
                CountUnsuccessfulAttempt(trackedDownload);
                return;
            }

            if (!ValidatePath(trackedDownload))
            {
                CountUnsuccessfulAttempt(trackedDownload);
                return;
            }

            var series = _parsingService.GetSeries(trackedDownload.DownloadItem.Title);

            if (series == null)
            {
                if (historyItem != null)
                {
                    series = _seriesService.GetSeries(historyItem.SeriesId);
                }

                if (series == null)
                {
                    trackedDownload.Warn("Series title mismatch; automatic import is not possible. Check the download troubleshooting entry on the wiki for common causes.");
                    SetStateToImportBlocked(trackedDownload);
                    CountUnsuccessfulAttempt(trackedDownload);

                    return;
                }

                Enum.TryParse(historyItem.Data.GetValueOrDefault(EpisodeHistory.SERIES_MATCH_TYPE, SeriesMatchType.Unknown.ToString()), out SeriesMatchType seriesMatchType);
                Enum.TryParse(historyItem.Data.GetValueOrDefault(EpisodeHistory.RELEASE_SOURCE, ReleaseSourceType.Unknown.ToString()), out ReleaseSourceType releaseSource);

                // Show a warning if the release was matched by ID and the source is not interactive search
                if (seriesMatchType == SeriesMatchType.Id && releaseSource != ReleaseSourceType.InteractiveSearch)
                {
                    trackedDownload.Warn("Found matching series via grab history, but release was matched to series by ID. Automatic import is not possible. See the FAQ for details.");
                    SetStateToImportBlocked(trackedDownload);
                    CountUnsuccessfulAttempt(trackedDownload);

                    return;
                }
            }

            trackedDownload.State = TrackedDownloadState.ImportPending;
        }

        private void CheckMovie(TrackedDownload trackedDownload)
        {
            var grabbedHistories = _historyService.FindMovieByDownloadId(trackedDownload.DownloadItem.DownloadId).Where(h => h.EventType == MovieHistoryEventType.Grabbed).ToList();
            var historyItem = grabbedHistories.MaxBy(h => h.Date);

            if (historyItem == null && trackedDownload.DownloadItem.Category.IsNullOrWhiteSpace())
            {
                trackedDownload.Warn("Download wasn't grabbed by Zendarr and not in a category, Skipping.");
                CountUnsuccessfulAttempt(trackedDownload);
                return;
            }

            if (!ValidatePath(trackedDownload))
            {
                CountUnsuccessfulAttempt(trackedDownload);
                return;
            }

            var movie = _parsingService.GetMovie(trackedDownload.DownloadItem.Title);

            if (movie == null)
            {
                if (historyItem != null)
                {
                    movie = _movieService.GetMovie(historyItem.MovieId);
                }

                if (movie == null)
                {
                    trackedDownload.Warn("Movie title mismatch; automatic import is not possible. Check the download troubleshooting entry on the wiki for common causes.");
                    SetStateToImportBlockedMovie(trackedDownload);
                    CountUnsuccessfulAttempt(trackedDownload);

                    return;
                }

                Enum.TryParse(historyItem.Data.GetValueOrDefault(MovieHistory.MOVIE_MATCH_TYPE, MovieMatchType.Unknown.ToString()), out MovieMatchType movieMatchType);
                Enum.TryParse(historyItem.Data.GetValueOrDefault(MovieHistory.RELEASE_SOURCE, ReleaseSourceType.Unknown.ToString()), out ReleaseSourceType releaseSource);

                // Show a warning if the release was matched by ID and the source is not interactive search
                if (movieMatchType == MovieMatchType.Id && releaseSource != ReleaseSourceType.InteractiveSearch)
                {
                    trackedDownload.Warn("Found matching movie via grab history, but release was matched to movie by ID. Automatic import is not possible. See the FAQ for details.");
                    SetStateToImportBlockedMovie(trackedDownload);
                    CountUnsuccessfulAttempt(trackedDownload);

                    return;
                }
            }

            trackedDownload.State = TrackedDownloadState.ImportPending;
        }

        public void Import(TrackedDownload trackedDownload)
        {
            try
            {
                ImportInternal(trackedDownload);
            }
            catch (Exception)
            {
                // Do not leave an interrupted import in a state that has no consumer.
                // Preserve terminal states if verification completed before an event failed.
                if (trackedDownload.State == TrackedDownloadState.ImportPending ||
                    trackedDownload.State == TrackedDownloadState.Importing)
                {
                    trackedDownload.ImportAttempts++;
                    trackedDownload.Warn("Import failed unexpectedly. Check the logs for details.");
                    SetImportBlocked(trackedDownload);
                }

                throw;
            }
        }

        private void ImportInternal(TrackedDownload trackedDownload)
        {
            SetImportItem(trackedDownload);

            if (!ValidatePath(trackedDownload))
            {
                trackedDownload.ImportAttempts++;
                SetImportBlocked(trackedDownload);
                return;
            }

            if (trackedDownload.RemoteMovie != null)
            {
                ImportMovie(trackedDownload);
                return;
            }

            if (trackedDownload.RemoteEpisode == null)
            {
                trackedDownload.Warn("Unable to parse download, automatic import is not possible.");
                SetStateToImportBlocked(trackedDownload);

                return;
            }

            trackedDownload.State = TrackedDownloadState.Importing;

            var outputPath = trackedDownload.ImportItem.OutputPath.FullPath;
            var importResults = _downloadedEpisodesImportService.ProcessPath(outputPath,
                ImportMode.Auto,
                trackedDownload.RemoteEpisode.Series,
                trackedDownload.ImportItem);

            if (VerifyImport(trackedDownload, importResults))
            {
                trackedDownload.ImportAttempts = 0;
                trackedDownload.CorruptFileDetected = false;
                return;
            }

            trackedDownload.CorruptFileDetected = ContainsUnreadableFile(importResults);
            trackedDownload.ImportAttempts++;
            trackedDownload.State = TrackedDownloadState.ImportPending;

            if (importResults.Empty())
            {
                trackedDownload.Warn("No files found are eligible for import in {0}", outputPath);
                SetStateToImportBlocked(trackedDownload);

                return;
            }

            if (importResults.Count == 1)
            {
                var firstResult = importResults.First();

                if (_rejectedImportService.Process(trackedDownload, firstResult))
                {
                    if (trackedDownload.State != TrackedDownloadState.FailedPending)
                    {
                        SetStateToImportBlocked(trackedDownload);
                    }

                    return;
                }

                if (firstResult.ImportDecision.Rejections.FirstOrDefault()?.Reason == ImportRejectionReason.MultiSeason)
                {
                    trackedDownload.Warn(new TrackedDownloadStatusMessage(trackedDownload.DownloadItem.Title, firstResult.Errors));
                    SetStateToImportBlocked(trackedDownload);
                    return;
                }
            }
            else if (ProcessRejectedImports(trackedDownload, importResults))
            {
                return;
            }

            var statusMessages = new List<TrackedDownloadStatusMessage>
                                 {
                                    new TrackedDownloadStatusMessage("One or more episodes expected in this release were not imported or missing from the release", new List<string>())
                                 };

            if (importResults.Any(c => c.Result != ImportResultType.Imported))
            {
                statusMessages.AddRange(
                    importResults
                        .Where(v => v.Result != ImportResultType.Imported && v.ImportDecision.LocalEpisode != null)
                        .OrderBy(v => v.ImportDecision.LocalEpisode.Path)
                        .Select(v =>
                            new TrackedDownloadStatusMessage(Path.GetFileName(v.ImportDecision.LocalEpisode.Path),
                                v.Errors)));
            }

            if (statusMessages.Any())
            {
                trackedDownload.Warn(statusMessages.ToArray());
                SetStateToImportBlocked(trackedDownload);
            }
        }

        private void ImportMovie(TrackedDownload trackedDownload)
        {
            if (trackedDownload.RemoteMovie.Movie == null)
            {
                trackedDownload.Warn("Unable to parse download, automatic import is not possible.");
                SetStateToImportBlockedMovie(trackedDownload);

                return;
            }

            trackedDownload.State = TrackedDownloadState.Importing;

            var outputPath = trackedDownload.ImportItem.OutputPath.FullPath;
            var importResults = _downloadedMovieImportService.ProcessPath(outputPath,
                MovieImport.ImportMode.Auto,
                trackedDownload.RemoteMovie.Movie,
                trackedDownload.ImportItem);

            if (VerifyImport(trackedDownload, importResults))
            {
                trackedDownload.ImportAttempts = 0;
                trackedDownload.CorruptFileDetected = false;
                return;
            }

            trackedDownload.CorruptFileDetected = ContainsUnreadableFile(importResults);
            trackedDownload.ImportAttempts++;
            trackedDownload.State = TrackedDownloadState.ImportPending;

            if (importResults.Empty())
            {
                trackedDownload.Warn("No files found are eligible for import in {0}", outputPath);
                SetStateToImportBlockedMovie(trackedDownload);

                return;
            }

            if (importResults.Count == 1)
            {
                var firstResult = importResults.First();

                if (_rejectedImportService.Process(trackedDownload, firstResult))
                {
                    if (trackedDownload.State != TrackedDownloadState.FailedPending)
                    {
                        SetStateToImportBlockedMovie(trackedDownload);
                    }

                    return;
                }
            }
            else if (ProcessRejectedImports(trackedDownload, importResults))
            {
                return;
            }

            var statusMessages = new List<TrackedDownloadStatusMessage>
                                 {
                                    new TrackedDownloadStatusMessage("One or more movies expected in this release were not imported or missing from the release", new List<string>())
                                 };

            if (importResults.Any(c => c.Result != MovieImport.ImportResultType.Imported))
            {
                statusMessages.AddRange(
                    importResults
                        .Where(v => v.Result != MovieImport.ImportResultType.Imported && v.ImportDecision.LocalMovie != null)
                        .OrderBy(v => v.ImportDecision.LocalMovie.Path)
                        .Select(v =>
                            new TrackedDownloadStatusMessage(Path.GetFileName(v.ImportDecision.LocalMovie.Path),
                                v.Errors)));
            }

            if (statusMessages.Any())
            {
                trackedDownload.Warn(statusMessages.ToArray());
                SetStateToImportBlockedMovie(trackedDownload);
            }
        }

        // A single-file import only has one result, so its rejection is the download's fate.
        // A multi-file download (e.g. a season pack) can contain a dangerous, executable or
        // user-rejected file alongside valid files, so check every result and let the rejection
        // guard fail the whole download instead of only warning and retrying forever.
        private bool ProcessRejectedImports(TrackedDownload trackedDownload, List<ImportResult> importResults)
        {
            foreach (var importResult in importResults)
            {
                if (_rejectedImportService.Process(trackedDownload, importResult) &&
                    trackedDownload.State == TrackedDownloadState.FailedPending)
                {
                    return true;
                }
            }

            return false;
        }

        private bool ProcessRejectedImports(TrackedDownload trackedDownload, List<MovieImport.ImportResult> importResults)
        {
            foreach (var importResult in importResults)
            {
                if (_rejectedImportService.Process(trackedDownload, importResult) &&
                    trackedDownload.State == TrackedDownloadState.FailedPending)
                {
                    return true;
                }
            }

            return false;
        }

        public bool VerifyImport(TrackedDownload trackedDownload, List<ImportResult> importResults)
        {
            var allEpisodesImported = importResults.Where(c => c.Result == ImportResultType.Imported)
                                                   .SelectMany(c => c.ImportDecision.LocalEpisode.Episodes)
                                                   .Count() >= Math.Max(1,
                                          trackedDownload.RemoteEpisode.Episodes.Count);

            var historyItems = _historyService.FindByDownloadId(trackedDownload.DownloadItem.DownloadId)
                .OrderByDescending(h => h.Date)
                .ToList();

            var grabbedHistory = historyItems.Where(h => h.EventType == EpisodeHistoryEventType.Grabbed).ToList();
            var releaseInfo = grabbedHistory.Count > 0 ? new GrabbedReleaseInfo(grabbedHistory) : null;

            if (allEpisodesImported)
            {
                _logger.Debug("All episodes were imported for {0}", trackedDownload.DownloadItem.Title);
                trackedDownload.State = TrackedDownloadState.Imported;
                ForgetManualInteraction(trackedDownload);

                _eventAggregator.PublishEvent(new DownloadCompletedEvent(trackedDownload,
                    trackedDownload.RemoteEpisode.Series.Id,
                    importResults.Where(c => c.Result == ImportResultType.Imported).Select(c => c.EpisodeFile).ToList(),
                    releaseInfo));

                return true;
            }

            // Double check if all episodes were imported by checking the history if at least one
            // file was imported. This will allow the decision engine to reject already imported
            // episode files and still mark the download complete when all files are imported.

            // EDGE CASE: This process relies on EpisodeIds being consistent between executions, if a series is updated
            // and an episode is removed, but later comes back with a different ID then Sonarr will treat it as incomplete.
            // Since imports should be relatively fast and these types of data changes are infrequent this should be quite
            // safe, but commenting for future benefit.

            var atLeastOneEpisodeImported = importResults.Any(c => c.Result == ImportResultType.Imported);
            var allEpisodesImportedInHistory = _trackedDownloadAlreadyImported.IsImported(trackedDownload, historyItems);

            if (allEpisodesImportedInHistory)
            {
                // Log different error messages depending on the circumstances, but treat both as fully imported, because that's the reality.
                // The second message shouldn't be logged in most cases, but continued reporting would indicate an ongoing issue.

                if (atLeastOneEpisodeImported)
                {
                    _logger.Debug("All episodes were imported in history for {0}", trackedDownload.DownloadItem.Title);
                }
                else
                {
                    _logger.ForDebugEvent()
                           .Message("No Episodes were just imported, but all episodes were previously imported, possible issue with download history.")
                           .Property("SeriesId", trackedDownload.RemoteEpisode.Series.Id)
                           .Property("DownloadId", trackedDownload.DownloadItem.DownloadId)
                           .Property("Title", trackedDownload.DownloadItem.Title)
                           .Property("Path", trackedDownload.ImportItem.OutputPath.ToString())
                           .WriteSentryWarn("DownloadHistoryIncomplete")
                           .Log();
                }

                var episodes = _episodeService.GetEpisodes(trackedDownload.RemoteEpisode.Episodes.Select(e => e.Id));
                var files = _mediaFileService.GetFiles(episodes.Select(e => e.EpisodeFileId).Where(i => i > 0).Distinct());

                trackedDownload.State = TrackedDownloadState.Imported;
                ForgetManualInteraction(trackedDownload);
                _eventAggregator.PublishEvent(new DownloadCompletedEvent(trackedDownload, trackedDownload.RemoteEpisode.Series.Id, files, releaseInfo));

                return true;
            }

            _logger.Debug("Not all episodes have been imported for the release '{0}'", trackedDownload.DownloadItem.Title);
            return false;
        }

        public bool VerifyImport(TrackedDownload trackedDownload, List<NzbDrone.Core.MediaFiles.MovieImport.ImportResult> importResults)
        {
            var allMoviesImported = importResults.Where(c => c.Result == NzbDrone.Core.MediaFiles.MovieImport.ImportResultType.Imported)
                                                 .Select(c => c.ImportDecision.LocalMovie?.Movie)
                                                 .Any(m => m != null);

            if (allMoviesImported)
            {
                _logger.Debug("All movies were imported for {0}", trackedDownload.DownloadItem.Title);
                trackedDownload.State = TrackedDownloadState.Imported;
                ForgetManualInteraction(trackedDownload);

                _eventAggregator.PublishEvent(new DownloadCompletedEvent(trackedDownload, trackedDownload.RemoteMovie.Movie.Id));

                return true;
            }

            // Double check if all movies were imported by checking the history if at least one
            // file was imported. This will allow the decision engine to reject already imported
            // movie files and still mark the download complete when all files are imported.
            var atLeastOneMovieImported = importResults.Any(c => c.Result == MovieImport.ImportResultType.Imported);

            var historyItems = _historyService.FindMovieByDownloadId(trackedDownload.DownloadItem.DownloadId)
                                              .OrderByDescending(h => h.Date)
                                              .ToList();

            var allMoviesImportedInHistory = _trackedDownloadAlreadyImported.IsImported(trackedDownload, historyItems);

            if (allMoviesImportedInHistory)
            {
                // Log different error messages depending on the circumstances, but treat both as fully imported, because that's the reality.
                // The second message shouldn't be logged in most cases, but continued reporting would indicate an ongoing issue.
                if (atLeastOneMovieImported)
                {
                    _logger.Debug("All movies were imported in history for {0}", trackedDownload.DownloadItem.Title);
                }
                else
                {
                    _logger.ForDebugEvent()
                           .Message("No Movies were just imported, but all movies were previously imported, possible issue with download history.")
                           .Property("MovieId", trackedDownload.RemoteMovie.Movie.Id)
                           .Property("DownloadId", trackedDownload.DownloadItem.DownloadId)
                           .Property("Title", trackedDownload.DownloadItem.Title)
                           .Property("Path", trackedDownload.ImportItem.OutputPath.ToString())
                           .WriteSentryWarn("DownloadHistoryIncomplete")
                           .Log();
                }

                trackedDownload.State = TrackedDownloadState.Imported;
                ForgetManualInteraction(trackedDownload);

                _eventAggregator.PublishEvent(new DownloadCompletedEvent(trackedDownload, trackedDownload.RemoteMovie.Movie.Id));

                return true;
            }

            _logger.Debug("Not all movies have been imported for the release '{0}'", trackedDownload.DownloadItem.Title);
            return false;
        }

        // Download clients report Warning for two different situations. A finished download whose
        // post-processing failed (unpack, move, final path) has nothing left to download and is a
        // completed download with a warning: it must enter the normal import path so its files are
        // retried deliberately, and every warning raised there counts the attempt
        // (CountUnsuccessfulAttempt) so the item fails once the retry budget is spent instead of
        // sitting in the queue forever. A warning while data is still outstanding (e.g. a stalled
        // torrent) is a transient client warning on an in-progress download: it stays in the
        // Downloading state, which keeps refreshing it until it finishes, and is not imported.
        // Encrypted downloads belong to FailedDownloadService, which counts them until they fail.
        private bool IsImportCandidate(DownloadClientItem downloadItem)
        {
            if (downloadItem.Status == DownloadItemStatus.Completed)
            {
                return true;
            }

            if (downloadItem.Status != DownloadItemStatus.Warning ||
                downloadItem.IsEncrypted ||
                downloadItem.RemainingSize > 0)
            {
                return false;
            }

            // ImportPending is only processed while completed download handling is enabled. When it
            // is disabled, leave the warned item in its client state instead of parking it in a
            // state nothing consumes.
            return _configService.EnableCompletedDownloadHandling;
        }

        // A pass that cannot progress (no grab history, a path that is not local, a title
        // mismatch) counts against the same retry budget as a failed import. Once the budget is
        // spent the download is failed, so a warning does not leave it queued forever. A download
        // already parked in ImportBlocked is waiting for a human: it must not be failed (and
        // blocklisted) by the attempt counter, because the time-based manual-interaction window
        // decides when it may be failed.
        private void CountUnsuccessfulAttempt(TrackedDownload trackedDownload)
        {
            trackedDownload.ImportAttempts++;

            if (IsWithinManualInteractionWindow(trackedDownload))
            {
                return;
            }

            if (trackedDownload.ImportAttempts >= MaxImportAttempts)
            {
                _logger.Debug("Download '{0}' could not be imported after {1} attempts, marking as failed",
                    trackedDownload.DownloadItem.Title,
                    trackedDownload.ImportAttempts);

                trackedDownload.Fail();
                ForgetManualInteraction(trackedDownload);
            }
        }

        // The window starts when the item is first reported as needing manual interaction and is
        // dropped as soon as it is no longer waiting for a human (successful import, corrected
        // path, deleted destination file), so a later block gets a fresh window.
        private bool IsWithinManualInteractionWindow(TrackedDownload trackedDownload)
        {
            var downloadId = trackedDownload.DownloadItem?.DownloadId;

            if (downloadId.IsNullOrWhiteSpace())
            {
                return false;
            }

            if (trackedDownload.State != TrackedDownloadState.ImportBlocked ||
                !trackedDownload.HasNotifiedManualInteractionRequired)
            {
                ForgetManualInteraction(trackedDownload);
                return false;
            }

            if (!_manualInteractionStarted.TryGetValue(downloadId, out var firstBlocked))
            {
                // Defensive: ImportBlocked without having passed through the notifier.
                firstBlocked = DateTime.UtcNow;
                _manualInteractionStarted[downloadId] = firstBlocked;
            }

            return DateTime.UtcNow - firstBlocked < ManualInteractionWindow;
        }

        private void RememberManualInteraction(TrackedDownload trackedDownload)
        {
            var downloadId = trackedDownload.DownloadItem?.DownloadId;

            if (downloadId.IsNotNullOrWhiteSpace())
            {
                _manualInteractionStarted[downloadId] = DateTime.UtcNow;
            }
        }

        private void ForgetManualInteraction(TrackedDownload trackedDownload)
        {
            var downloadId = trackedDownload.DownloadItem?.DownloadId;

            if (downloadId.IsNotNullOrWhiteSpace())
            {
                _manualInteractionStarted.TryRemove(downloadId, out _);
            }
        }

        private void SetStateToImportBlocked(TrackedDownload trackedDownload)
        {
            trackedDownload.State = TrackedDownloadState.ImportBlocked;

            if (!trackedDownload.HasNotifiedManualInteractionRequired)
            {
                var grabbedHistories = _historyService.FindByDownloadId(trackedDownload.DownloadItem.DownloadId).Where(h => h.EventType == EpisodeHistoryEventType.Grabbed).ToList();

                trackedDownload.HasNotifiedManualInteractionRequired = true;
                RememberManualInteraction(trackedDownload);

                var releaseInfo = grabbedHistories.Count > 0 ? new GrabbedReleaseInfo(grabbedHistories) : null;
                var manualInteractionEvent = new ManualInteractionRequiredEvent(trackedDownload, releaseInfo);

                _eventAggregator.PublishEvent(manualInteractionEvent);
            }
        }

        private void SetImportBlocked(TrackedDownload trackedDownload)
        {
            if (trackedDownload.RemoteMovie != null)
            {
                SetStateToImportBlockedMovie(trackedDownload);
            }
            else
            {
                SetStateToImportBlocked(trackedDownload);
            }
        }

        private void SetStateToImportBlockedMovie(TrackedDownload trackedDownload)
        {
            trackedDownload.State = TrackedDownloadState.ImportBlocked;

            if (!trackedDownload.HasNotifiedManualInteractionRequired)
            {
                var grabbedHistories = _historyService.FindMovieByDownloadId(trackedDownload.DownloadItem.DownloadId).Where(h => h.EventType == MovieHistoryEventType.Grabbed).ToList();

                trackedDownload.HasNotifiedManualInteractionRequired = true;
                RememberManualInteraction(trackedDownload);

                var releaseInfo = grabbedHistories.Count > 0 ? new GrabbedReleaseInfo(grabbedHistories) : null;
                var manualInteractionEvent = new ManualInteractionRequiredEvent(trackedDownload, releaseInfo);

                _eventAggregator.PublishEvent(manualInteractionEvent);
            }
        }

        private static bool ContainsUnreadableFile(List<ImportResult> importResults)
        {
            return importResults.Any(r =>
                r.ImportDecision?.Rejections?.Any(rejection => rejection.Reason == ImportRejectionReason.SampleIndeterminate) == true);
        }

        private static bool ContainsUnreadableFile(List<NzbDrone.Core.MediaFiles.MovieImport.ImportResult> importResults)
        {
            return importResults.Any(r =>
                r.ImportDecision?.Rejections?.Any(rejection => rejection.Reason == NzbDrone.Core.MediaFiles.MovieImport.ImportRejectionReason.SampleIndeterminate) == true);
        }

        private void SetImportItem(TrackedDownload trackedDownload)
        {
            trackedDownload.DownloadItem.MediaType = trackedDownload.RemoteMovie != null
                ? MediaType.Movie
                : trackedDownload.RemoteEpisode != null
                    ? MediaType.Series
                    : null;

            trackedDownload.ImportItem = _provideImportItemService.ProvideImportItem(trackedDownload.DownloadItem, trackedDownload.ImportItem);
        }

        private bool ValidatePath(TrackedDownload trackedDownload)
        {
            var downloadItemOutputPath = trackedDownload.ImportItem.OutputPath;

            if (downloadItemOutputPath.IsEmpty)
            {
                trackedDownload.Warn("Download doesn't contain intermediate path, Skipping.");
                return false;
            }

            if ((OsInfo.IsWindows && !downloadItemOutputPath.IsWindowsPath) ||
                (OsInfo.IsNotWindows && !downloadItemOutputPath.IsUnixPath))
            {
                trackedDownload.Warn("[{0}] is not a valid local path. You may need a Remote Path Mapping. Check the download troubleshooting entry on the wiki for details.", downloadItemOutputPath);
                return false;
            }

            return true;
        }
    }
}
