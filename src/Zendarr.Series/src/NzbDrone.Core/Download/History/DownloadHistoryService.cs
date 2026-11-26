using System;
using System.IO;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.Download.History
{
    public interface IDownloadHistoryService
    {
        DownloadHistory GetLatestDownloadHistoryItem(string downloadId);
        DownloadHistory GetLatestGrab(string downloadId);

        // Terminal outcome for a download the user removed from the download client (or that
        // vanished from it): the grab never became an import or a failure, so it is recorded
        // as "ignored" to release the durable already-grabbed guard.
        void RecordRemovedDownload(TrackedDownload trackedDownload, string message = "Removed from queue");

        // Terminal outcome for a download the user re-filed in the client by changing its
        // category and the client actually applied that move. That is a hand-off (normally to
        // the client's "imported" category), not a rejection, so it is recorded as an import
        // rather than as "ignored".
        void RecordCategoryChangedDownload(TrackedDownload trackedDownload, string message = "Category changed");

        // Terminal outcome for a changeCategory removal where the client did not actually move
        // the item (its post-import category is unset or equal to the category it is already in).
        // Nothing was imported, so this is recorded as "ignored": recording an import would let
        // RemoveCompletedDownloads delete a download that was never imported.
        void RecordCategoryChangeNotApplied(TrackedDownload trackedDownload, string message = "Category change not applied");
    }

    public class DownloadHistoryService : IDownloadHistoryService,
                                          IHandle<EpisodeGrabbedEvent>,
                                          IHandle<EpisodeImportedEvent>,
                                          IHandle<MovieGrabbedEvent>,
                                          IHandle<MovieFileImportedEvent>,
                                          IHandle<DownloadCompletedEvent>,
                                          IHandle<DownloadFailedEvent>,
                                          IHandle<DownloadIgnoredEvent>,
                                          IHandle<SeriesDeletedEvent>,
                                          IHandle<MoviesDeletedEvent>
    {
        private readonly IDownloadHistoryRepository _repository;
        private readonly IHistoryService _historyService;

        public DownloadHistoryService(IDownloadHistoryRepository repository, IHistoryService historyService)
        {
            _repository = repository;
            _historyService = historyService;
        }

        public DownloadHistory GetLatestDownloadHistoryItem(string downloadId)
        {
            var events = _repository.FindByDownloadId(downloadId);

            // Events are ordered by date descending. We'll return the most recent expected event.
            foreach (var e in events)
            {
                if (e.EventType == DownloadHistoryEventType.DownloadIgnored)
                {
                    return e;
                }

                if (e.EventType == DownloadHistoryEventType.DownloadGrabbed)
                {
                    return e;
                }

                if (e.EventType == DownloadHistoryEventType.DownloadImported)
                {
                    return e;
                }

                if (e.EventType == DownloadHistoryEventType.FileImported)
                {
                    return e;
                }

                if (e.EventType == DownloadHistoryEventType.DownloadFailed)
                {
                    return e;
                }
            }

            return null;
        }

        public DownloadHistory GetLatestGrab(string downloadId)
        {
            return _repository.FindByDownloadId(downloadId)
                              .FirstOrDefault(d => d.EventType == DownloadHistoryEventType.DownloadGrabbed);
        }

        public void RecordRemovedDownload(TrackedDownload trackedDownload, string message = "Removed from queue")
        {
            // Removing the download stops tracking without a failed/imported event and the user
            // did not ask for the release to be grabbed again, so mark it "ignored".
            RecordTerminalDownload(trackedDownload, DownloadHistoryEventType.DownloadIgnored, message);
        }

        public void RecordCategoryChangedDownload(TrackedDownload trackedDownload, string message = "Category changed")
        {
            // Changing the category hands the item off inside the download client (the
            // changeCategory action calls MarkItemAsImported), so the download was moved out of
            // Zendarr's keeping rather than rejected. Recording "ignored" would claim the user
            // threw the download away; record the terminal import instead.
            RecordTerminalDownload(trackedDownload, DownloadHistoryEventType.DownloadImported, message);
        }

        public void RecordCategoryChangeNotApplied(TrackedDownload trackedDownload, string message = "Category change not applied")
        {
            // The client accepted the changeCategory request but the item is still in the
            // category Zendarr tracks, so it was never imported (and is still owned by the
            // client). Record "ignored" to release the durable guard without claiming an import.
            RecordTerminalDownload(trackedDownload, DownloadHistoryEventType.DownloadIgnored, message);
        }

        private void RecordTerminalDownload(TrackedDownload trackedDownload, DownloadHistoryEventType eventType, string message)
        {
            var downloadItem = trackedDownload?.DownloadItem;

            if (downloadItem == null || downloadItem.DownloadId.IsNullOrWhiteSpace())
            {
                return;
            }

            // Removing the download from the client, or changing its category, stops tracking
            // without a failed/ignored/imported event. Persist the supplied terminal event so the
            // durable "already grabbed" guard does not keep rejecting the release forever.
            //
            // Never overwrite an existing terminal event: a download that was already imported
            // or failed must keep that outcome when the client later auto-removes it.
            var latestEvent = GetLatestDownloadHistoryItem(downloadItem.DownloadId);

            if (latestEvent != null && latestEvent.EventType != DownloadHistoryEventType.DownloadGrabbed)
            {
                return;
            }

            var history = new DownloadHistory
            {
                EventType = eventType,
                SeriesId = trackedDownload.RemoteEpisode?.Series?.Id ?? 0,
                MovieId = trackedDownload.RemoteMovie?.Movie?.Id ?? 0,
                DownloadId = downloadItem.DownloadId,
                SourceTitle = downloadItem.Title,
                Date = DateTime.UtcNow,
                Protocol = trackedDownload.Protocol,
                DownloadClientId = trackedDownload.DownloadClient
            };

            history.Data.Add("DownloadClient", downloadItem.DownloadClientInfo?.Type);
            history.Data.Add("DownloadClientName", downloadItem.DownloadClientInfo?.Name);
            history.Data.Add("Message", message);

            _repository.Insert(history);
        }

        public void Handle(EpisodeGrabbedEvent message)
        {
            // Don't store grabbed events for clients that don't download IDs
            if (message.DownloadId.IsNullOrWhiteSpace())
            {
                return;
            }

            var history = new DownloadHistory
            {
                EventType = DownloadHistoryEventType.DownloadGrabbed,
                SeriesId = message.Episode.Series.Id,
                DownloadId = message.DownloadId,
                SourceTitle = message.Episode.Release.Title,
                Date = DateTime.UtcNow,
                Protocol = message.Episode.Release.DownloadProtocol,
                IndexerId = message.Episode.Release.IndexerId,
                DownloadClientId = message.DownloadClientId,
                Release =  message.Episode.Release
            };

            history.Data.Add("Indexer", message.Episode.Release.Indexer);
            history.Data.Add("DownloadClient", message.DownloadClient);
            history.Data.Add("DownloadClientName", message.DownloadClientName);
            history.Data.Add("CustomFormatScore", message.Episode.CustomFormatScore.ToString());

            _repository.Insert(history);
        }

        public void Handle(MovieGrabbedEvent message)
        {
            if (message.DownloadId.IsNullOrWhiteSpace())
            {
                return;
            }

            var history = new DownloadHistory
            {
                EventType = DownloadHistoryEventType.DownloadGrabbed,
                MovieId = message.Movie.Movie.Id,
                DownloadId = message.DownloadId,
                SourceTitle = message.Movie.Release.Title,
                Date = DateTime.UtcNow,
                Protocol = message.Movie.Release.DownloadProtocol,
                IndexerId = message.Movie.Release.IndexerId,
                DownloadClientId = message.DownloadClientId,
                Release = message.Movie.Release
            };

            history.Data.Add("Indexer", message.Movie.Release.Indexer);
            history.Data.Add("DownloadClient", message.DownloadClient);
            history.Data.Add("DownloadClientName", message.DownloadClientName);
            history.Data.Add("CustomFormatScore", message.Movie.CustomFormatScore.ToString());

            _repository.Insert(history);
        }

        public void Handle(EpisodeImportedEvent message)
        {
            if (!message.NewDownload)
            {
                return;
            }

            var downloadId = message.DownloadId;

            // Try to find the downloadId if the user used manual import (from wanted: missing) or the
            // API to import and downloadId wasn't provided.

            if (downloadId.IsNullOrWhiteSpace())
            {
                downloadId = _historyService.FindDownloadId(message);
            }

            if (downloadId.IsNullOrWhiteSpace())
            {
                return;
            }

            var history = new DownloadHistory
            {
                EventType = DownloadHistoryEventType.FileImported,
                SeriesId = message.ImportedEpisode.SeriesId,
                DownloadId = downloadId,
                SourceTitle = message.EpisodeInfo.Path,
                Date = DateTime.UtcNow,
                Protocol = message.DownloadClientInfo.Protocol,
                DownloadClientId = message.DownloadClientInfo.Id
            };

            history.Data.Add("DownloadClient", message.DownloadClientInfo.Type);
            history.Data.Add("DownloadClientName", message.DownloadClientInfo.Name);
            history.Data.Add("SourcePath", message.EpisodeInfo.Path);
            history.Data.Add("DestinationPath", Path.Combine(message.EpisodeInfo.Series.Path, message.ImportedEpisode.RelativePath));

            _repository.Insert(history);
        }

        public void Handle(MovieFileImportedEvent message)
        {
            if (!message.NewDownload)
            {
                return;
            }

            var downloadId = message.DownloadId;

            if (downloadId.IsNullOrWhiteSpace())
            {
                downloadId = _historyService.FindDownloadId(message);
            }

            if (downloadId.IsNullOrWhiteSpace())
            {
                return;
            }

            var history = new DownloadHistory
            {
                EventType = DownloadHistoryEventType.FileImported,
                MovieId = message.ImportedMovie.MovieId,
                DownloadId = downloadId,
                SourceTitle = message.MovieInfo.Path,
                Date = DateTime.UtcNow,
                Protocol = message.DownloadClientInfo.Protocol,
                DownloadClientId = message.DownloadClientInfo.Id
            };

            history.Data.Add("DownloadClient", message.DownloadClientInfo.Type);
            history.Data.Add("DownloadClientName", message.DownloadClientInfo.Name);
            history.Data.Add("SourcePath", message.MovieInfo.Path);

            _repository.Insert(history);
        }

        public void Handle(DownloadCompletedEvent message)
        {
            var downloadItem = message.TrackedDownload.DownloadItem;

            var history = new DownloadHistory
            {
                EventType = DownloadHistoryEventType.DownloadImported,
                SeriesId = message.SeriesId,
                MovieId = message.MovieId,
                DownloadId = downloadItem.DownloadId,
                SourceTitle = downloadItem.Title,
                Date = DateTime.UtcNow,
                Protocol = message.TrackedDownload.Protocol,
                DownloadClientId = message.TrackedDownload.DownloadClient
            };

            history.Data.Add("DownloadClient", downloadItem.DownloadClientInfo.Type);
            history.Data.Add("DownloadClientName", downloadItem.DownloadClientInfo.Name);

            _repository.Insert(history);
        }

        public void Handle(DownloadFailedEvent message)
        {
            // Don't track failed download for an unknown download
            if (message.TrackedDownload == null)
            {
                return;
            }

            var history = new DownloadHistory
            {
                EventType = DownloadHistoryEventType.DownloadFailed,
                SeriesId = message.SeriesId,
                MovieId = message.MovieId,
                DownloadId = message.DownloadId,
                SourceTitle = message.SourceTitle,
                Date = DateTime.UtcNow,
                Protocol = message.TrackedDownload.Protocol,
                DownloadClientId = message.TrackedDownload.DownloadClient
            };

            history.Data.Add("DownloadClient", message.TrackedDownload.DownloadItem.DownloadClientInfo.Type);
            history.Data.Add("DownloadClientName", message.TrackedDownload.DownloadItem.DownloadClientInfo.Name);

            _repository.Insert(history);
        }

        public void Handle(DownloadIgnoredEvent message)
        {
            var history = new DownloadHistory
            {
                EventType = DownloadHistoryEventType.DownloadIgnored,
                SeriesId = message.SeriesId,
                MovieId = message.MovieId,
                DownloadId = message.DownloadId,
                SourceTitle = message.SourceTitle,
                Date = DateTime.UtcNow,
                Protocol = message.DownloadClientInfo.Protocol,
                DownloadClientId = message.DownloadClientInfo.Id
            };

            history.Data.Add("DownloadClient", message.DownloadClientInfo.Type);
            history.Data.Add("DownloadClientName", message.DownloadClientInfo.Name);

            _repository.Insert(history);
        }

        public void Handle(SeriesDeletedEvent message)
        {
            _repository.DeleteBySeriesIds(message.Series.Select(m => m.Id).ToList());
        }

        public void Handle(MoviesDeletedEvent message)
        {
            _repository.DeleteByMovieIds(message.Movies.Select(m => m.Id).ToList());
        }
    }
}
