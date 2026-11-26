using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download
{
    public interface IFailedDownloadService
    {
        void MarkAsFailed(int historyId, string message = null, string source = null, bool skipRedownload = false);
        void MarkAsFailed(TrackedDownload trackedDownload, string message = null, string source = null, bool skipRedownload = false);
        void MarkAsFailedMovie(int historyId, string message = null, string source = null, bool skipRedownload = false);
        void Check(TrackedDownload trackedDownload);
        void ProcessFailed(TrackedDownload trackedDownload);
    }

    public class FailedDownloadService : IFailedDownloadService
    {
        private const int MaxImportAttempts = 3;

        private readonly IHistoryService _historyService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public FailedDownloadService(IHistoryService historyService,
                                     ITrackedDownloadService trackedDownloadService,
                                     IEventAggregator eventAggregator,
                                     Logger logger)
        {
            _historyService = historyService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public void MarkAsFailed(int historyId, string message, string source = null, bool skipRedownload = false)
        {
            message ??= "Manually marked as failed";

            var history = _historyService.Get(historyId);
            var downloadId = history.DownloadId;

            if (downloadId.IsNullOrWhiteSpace())
            {
                if (history.EventType == EpisodeHistoryEventType.Grabbed)
                {
                    PublishDownloadFailedEvent(history, new List<int> { history.EpisodeId }, message, source, skipRedownload: skipRedownload);
                    return;
                }

                throw new InvalidOperationException("Unable to mark download as failed, history item was not grabbed and has no download ID");
            }

            var grabbedHistory = new List<EpisodeHistory>();

            // If the history item is a grabbed item (it should be, at least from the UI) add it as the first history item
            if (history.EventType == EpisodeHistoryEventType.Grabbed)
            {
                grabbedHistory.Add(history);
            }

            // Add any other history items for the download ID then filter out any duplicate history items.
            grabbedHistory.AddRange(GetGrabbedHistory(downloadId));
            grabbedHistory = grabbedHistory.DistinctBy(h => h.Id).ToList();

            if (grabbedHistory.Any())
            {
                PublishDownloadFailedEvent(grabbedHistory.First(), GetEpisodeIds(grabbedHistory), message, source, skipRedownload: skipRedownload);
                return;
            }

            throw new InvalidOperationException("Unable to mark download as failed, no grabbed history available");
        }

        public void MarkAsFailed(TrackedDownload trackedDownload, string message, string source = null, bool skipRedownload = false)
        {
            if (trackedDownload.RemoteMovie != null)
            {
                var movieHistory = GetGrabbedMovieHistory(trackedDownload.DownloadItem.DownloadId);

                if (movieHistory.Any())
                {
                    PublishMovieDownloadFailedEvent(movieHistory.First(), message ?? "Manually marked as failed", trackedDownload, skipRedownload);
                    return;
                }

                throw new InvalidOperationException("Unable to mark download as failed, no grabbed history available");
            }

            var history = GetGrabbedHistory(trackedDownload.DownloadItem.DownloadId);

            if (history.Any())
            {
                PublishDownloadFailedEvent(history.First(), GetEpisodeIds(history), message ?? "Manually marked as failed", source, trackedDownload, skipRedownload: skipRedownload);
                return;
            }

            throw new InvalidOperationException("Unable to mark download as failed, no grabbed history available");
        }

        public void MarkAsFailedMovie(int historyId, string message = null, string source = null, bool skipRedownload = false)
        {
            message ??= "Manually marked as failed";

            var history = _historyService.GetMovie(historyId);
            var downloadId = history.DownloadId;

            if (downloadId.IsNullOrWhiteSpace())
            {
                PublishMovieDownloadFailedEvent(history, message, skipRedownload: skipRedownload);
                return;
            }

            var grabbedHistory = new List<MovieHistory>();

            if (history.EventType == MovieHistoryEventType.Grabbed)
            {
                grabbedHistory.Add(history);
            }

            grabbedHistory.AddRange(GetGrabbedMovieHistory(downloadId));
            grabbedHistory = grabbedHistory.DistinctBy(h => h.Id).ToList();

            if (grabbedHistory.Any())
            {
                PublishMovieDownloadFailedEvent(grabbedHistory.First(), message, skipRedownload: skipRedownload);
                return;
            }

            throw new InvalidOperationException("Unable to mark download as failed, no grabbed history available");
        }

        public void Check(TrackedDownload trackedDownload)
        {
            // Only process tracked downloads that are still downloading or import is blocked (if they fail after attempting to be processed)
            if (trackedDownload.State != TrackedDownloadState.Downloading && trackedDownload.State != TrackedDownloadState.ImportBlocked)
            {
                return;
            }

            var downloadIsFailed = trackedDownload.DownloadItem.IsEncrypted ||
                                   trackedDownload.DownloadItem.Status == DownloadItemStatus.Failed;

            if (downloadIsFailed)
            {
                if (trackedDownload.RemoteMovie != null)
                {
                    var grabbedMovieItems = GetGrabbedMovieHistory(trackedDownload.DownloadItem.DownloadId);

                    if (grabbedMovieItems.Empty())
                    {
                        trackedDownload.Warn(trackedDownload.DownloadItem.IsEncrypted ? $"Download is encrypted and wasn't grabbed by {BuildInfo.AppName}, skipping automatic download handling" : $"Download has failed wasn't grabbed by {BuildInfo.AppName}, skipping automatic download handling");
                        CountUnsuccessfulCheck(trackedDownload);
                        return;
                    }

                    trackedDownload.State = TrackedDownloadState.FailedPending;

                    return;
                }

                var grabbedItems = GetGrabbedHistory(trackedDownload.DownloadItem.DownloadId);

                if (grabbedItems.Empty())
                {
                    trackedDownload.Warn(trackedDownload.DownloadItem.IsEncrypted ? $"Download is encrypted and wasn't grabbed by {BuildInfo.AppName}, skipping automatic download handling" : $"Download has failed wasn't grabbed by {BuildInfo.AppName}, skipping automatic download handling");
                    CountUnsuccessfulCheck(trackedDownload);
                    return;
                }

                trackedDownload.State = TrackedDownloadState.FailedPending;

                return;
            }

            // A client-reported Warning is not Failed, so it reached neither branch above. When
            // data is still outstanding the download is not finished and completed download
            // handling will not import it. If it was not grabbed by the app either, nothing else
            // would ever consume the item (e.g. a download the client abandoned with an error),
            // so warn about it and count it against the same retry budget. A warned download we
            // did grab stays in the Downloading state, because the client may still finish it.
            if (trackedDownload.DownloadItem.Status != DownloadItemStatus.Warning ||
                trackedDownload.DownloadItem.RemainingSize <= 0)
            {
                return;
            }

            var wasNotGrabbed = trackedDownload.RemoteMovie != null
                ? GetGrabbedMovieHistory(trackedDownload.DownloadItem.DownloadId).Empty()
                : GetGrabbedHistory(trackedDownload.DownloadItem.DownloadId).Empty();

            if (!wasNotGrabbed)
            {
                return;
            }

            trackedDownload.Warn($"Download is in a warning state and wasn't grabbed by {BuildInfo.AppName}, skipping automatic download handling");
            CountUnsuccessfulCheck(trackedDownload);
        }

        // A failed, encrypted or warned download that was not grabbed by the app can only be
        // warned about, and that warning would otherwise have no consumer: the item would stay in
        // the queue forever. Count every pass against the retry budget and fail it once the budget
        // is spent so the download reaches a terminal state.
        private void CountUnsuccessfulCheck(TrackedDownload trackedDownload)
        {
            trackedDownload.ImportAttempts++;

            if (trackedDownload.ImportAttempts >= MaxImportAttempts)
            {
                _logger.Debug("Download '{0}' is still unhandled after {1} checks, marking as failed",
                    trackedDownload.DownloadItem.Title,
                    trackedDownload.ImportAttempts);

                trackedDownload.Fail();
            }
        }

        public void ProcessFailed(TrackedDownload trackedDownload)
        {
            if (trackedDownload.State != TrackedDownloadState.FailedPending)
            {
                return;
            }

            if (trackedDownload.RemoteMovie != null)
            {
                var grabbedMovieItems = GetGrabbedMovieHistory(trackedDownload.DownloadItem.DownloadId);

                if (grabbedMovieItems.Empty())
                {
                    _logger.Debug("Download '{0}' has no grabbed history and cannot be marked as failed through history, marking it failed anyway", trackedDownload.DownloadItem.Title);
                    trackedDownload.State = TrackedDownloadState.Failed;
                    return;
                }

                var movieFailure = "Failed download detected";

                if (trackedDownload.DownloadItem.IsEncrypted)
                {
                    movieFailure = "Encrypted download detected";
                }
                else if (trackedDownload.DownloadItem.Status == DownloadItemStatus.Failed && trackedDownload.DownloadItem.Message.IsNotNullOrWhiteSpace())
                {
                    movieFailure = trackedDownload.DownloadItem.Message;
                }

                PublishMovieDownloadFailedEvent(grabbedMovieItems.First(), movieFailure, trackedDownload);
                trackedDownload.State = TrackedDownloadState.Failed;

                return;
            }

            var grabbedItems = GetGrabbedHistory(trackedDownload.DownloadItem.DownloadId);

            if (grabbedItems.Empty())
            {
                _logger.Debug("Download '{0}' has no grabbed history and cannot be marked as failed through history, marking it failed anyway", trackedDownload.DownloadItem.Title);
                trackedDownload.State = TrackedDownloadState.Failed;
                return;
            }

            var failure = "Failed download detected";

            if (trackedDownload.DownloadItem.IsEncrypted)
            {
                failure = "Encrypted download detected";
            }
            else if (trackedDownload.DownloadItem.Status == DownloadItemStatus.Failed && trackedDownload.DownloadItem.Message.IsNotNullOrWhiteSpace())
            {
                failure = trackedDownload.DownloadItem.Message;
            }

            PublishDownloadFailedEvent(grabbedItems.First(), GetEpisodeIds(grabbedItems), failure, $"{BuildInfo.AppName} Failed Download Handling", trackedDownload);
            trackedDownload.State = TrackedDownloadState.Failed;
        }

        private void PublishDownloadFailedEvent(EpisodeHistory historyItem, List<int> episodeIds, string message, string source, TrackedDownload trackedDownload = null, bool skipRedownload = false)
        {
            Enum.TryParse(historyItem.Data.GetValueOrDefault(EpisodeHistory.RELEASE_SOURCE, ReleaseSourceType.Unknown.ToString()), out ReleaseSourceType releaseSource);

            var downloadFailedEvent = new DownloadFailedEvent
            {
                SeriesId = historyItem.SeriesId,
                EpisodeIds = episodeIds,
                Quality = historyItem.Quality,
                SourceTitle = historyItem.SourceTitle,
                DownloadClient = historyItem.Data.GetValueOrDefault(EpisodeHistory.DOWNLOAD_CLIENT),
                DownloadId = historyItem.DownloadId,
                Message = message,
                Source = source,
                Data = historyItem.Data,
                TrackedDownload = trackedDownload,
                Languages = historyItem.Languages,
                SkipRedownload = skipRedownload,
                ReleaseSource = releaseSource,
            };

            _eventAggregator.PublishEvent(downloadFailedEvent);
        }

        private List<int> GetEpisodeIds(List<EpisodeHistory> historyItems)
        {
            return historyItems.Select(h => h.EpisodeId).Distinct().ToList();
        }

        private List<EpisodeHistory> GetGrabbedHistory(string downloadId)
        {
            // Sort by date so items are always in the same order
            return _historyService.Find(downloadId, EpisodeHistoryEventType.Grabbed)
                .OrderByDescending(h => h.Date)
                .ToList();
        }

        private void PublishMovieDownloadFailedEvent(MovieHistory historyItem, string message, TrackedDownload trackedDownload = null, bool skipRedownload = false)
        {
            Enum.TryParse(historyItem.Data.GetValueOrDefault(MovieHistory.RELEASE_SOURCE, ReleaseSourceType.Unknown.ToString()), out ReleaseSourceType releaseSource);

            var downloadFailedEvent = new DownloadFailedEvent
            {
                MovieId = historyItem.MovieId,
                Quality = historyItem.Quality,
                SourceTitle = historyItem.SourceTitle,
                DownloadClient = historyItem.Data.GetValueOrDefault(MovieHistory.DOWNLOAD_CLIENT),
                DownloadId = historyItem.DownloadId,
                Message = message,
                Data = historyItem.Data,
                TrackedDownload = trackedDownload,
                Languages = historyItem.Languages,
                SkipRedownload = skipRedownload,
                ReleaseSource = releaseSource,
            };

            _eventAggregator.PublishEvent(downloadFailedEvent);
        }

        private List<MovieHistory> GetGrabbedMovieHistory(string downloadId)
        {
            // Sort by date so items are always in the same order
            return _historyService.FindMovie(downloadId, MovieHistoryEventType.Grabbed)
                .OrderByDescending(h => h.Date)
                .ToList();
        }
    }
}
