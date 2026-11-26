using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.TPL;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download.TrackedDownloads
{
    public class DownloadMonitoringService : IExecute<RefreshMonitoredDownloadsCommand>,
                                             IExecute<CheckForFinishedDownloadCommand>,
                                             IHandle<EpisodeGrabbedEvent>,
                                             IHandle<EpisodeImportedEvent>,
                                             IHandle<MovieGrabbedEvent>,
                                             IHandle<MovieFileImportedEvent>,
                                             IHandle<ManualInteractionRequiredEvent>,
                                             IHandle<DownloadsProcessedEvent>,
                                             IHandle<TrackedDownloadsRemovedEvent>
    {
        private readonly IDownloadClientStatusService _downloadClientStatusService;
        private readonly IDownloadClientFactory _downloadClientFactory;
        private readonly IEventAggregator _eventAggregator;
        private readonly IManageCommandQueue _manageCommandQueue;
        private readonly IConfigService _configService;
        private readonly IFailedDownloadService _failedDownloadService;
        private readonly ICompletedDownloadService _completedDownloadService;
        private readonly ITrackedDownloadService _trackedDownloadService;
        private readonly IDownloadHistoryService _downloadHistoryService;
        private readonly Logger _logger;
        private readonly Debouncer _refreshDebounce;

        public DownloadMonitoringService(IDownloadClientStatusService downloadClientStatusService,
                                         IDownloadClientFactory downloadClientFactory,
                                         IEventAggregator eventAggregator,
                                         IManageCommandQueue manageCommandQueue,
                                         IConfigService configService,
                                         IFailedDownloadService failedDownloadService,
                                         ICompletedDownloadService completedDownloadService,
                                         ITrackedDownloadService trackedDownloadService,
                                         IDownloadHistoryService downloadHistoryService,
                                         Logger logger)
        {
            _downloadClientStatusService = downloadClientStatusService;
            _downloadClientFactory = downloadClientFactory;
            _eventAggregator = eventAggregator;
            _manageCommandQueue = manageCommandQueue;
            _configService = configService;
            _failedDownloadService = failedDownloadService;
            _completedDownloadService = completedDownloadService;
            _trackedDownloadService = trackedDownloadService;
            _downloadHistoryService = downloadHistoryService;
            _logger = logger;

            _refreshDebounce = new Debouncer(QueueRefresh, TimeSpan.FromSeconds(5));
        }

        private void QueueRefresh()
        {
            _manageCommandQueue.Push(new RefreshMonitoredDownloadsCommand(), CommandPriority.High);
        }

        private void Refresh()
        {
            _refreshDebounce.Pause();
            try
            {
                var downloadClients = _downloadClientFactory.DownloadHandlingEnabled();

                var trackedDownloads = new List<TrackedDownload>();

                foreach (var downloadClient in downloadClients)
                {
                    var clientTrackedDownloads = ProcessClientDownloads(downloadClient);

                    trackedDownloads.AddRange(clientTrackedDownloads.Where(DownloadIsTrackable));
                }

                _trackedDownloadService.UpdateTrackable(trackedDownloads);
                _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(trackedDownloads));
                _manageCommandQueue.Push(new ProcessMonitoredDownloadsCommand(), CommandPriority.High);
            }
            finally
            {
                _refreshDebounce.Resume();
            }
        }

        private List<TrackedDownload> ProcessClientDownloads(IDownloadClient downloadClient)
        {
            var downloadClientItems = new List<DownloadClientItem>();
            var trackedDownloads = new List<TrackedDownload>();
            var clientItemsRetrieved = false;

            try
            {
                downloadClientItems = downloadClient.GetItems().ToList();

                _downloadClientStatusService.RecordSuccess(downloadClient.Definition.Id);
                clientItemsRetrieved = true;
            }
            catch (Exception ex)
            {
                // TODO: Stop tracking items for the offline client

                _downloadClientStatusService.RecordFailure(downloadClient.Definition.Id);
                _logger.Warn(ex, "Unable to retrieve queue and history items from " + downloadClient.Definition.Name);
            }

            // Only reconcile against the client when it actually answered. An offline client
            // returns no items, and treating that as "removed" would terminal-ise every
            // in-flight download it still owns.
            if (clientItemsRetrieved)
            {
                RecordVanishedDownloads(downloadClient, downloadClientItems);
            }

            foreach (var downloadItem in downloadClientItems)
            {
                var item = ProcessClientItem(downloadClient, downloadItem);
                trackedDownloads.AddIfNotNull(item);
            }

            return trackedDownloads;
        }

        // A download can disappear from the client without Zendarr importing, failing or
        // ignoring it (the client hit a seed limit, or the torrent was deleted externally).
        // Nothing then wrote a terminal DownloadHistory event, so the durable "already grabbed"
        // guard kept treating the grab as in flight and rejected the release forever. Record
        // the terminal event before dropping the tracked object, but only when the item really
        // is gone from the client and no terminal history exists yet.
        private void RecordVanishedDownloads(IDownloadClient downloadClient, List<DownloadClientItem> downloadClientItems)
        {
            var clientDownloadIds = new HashSet<string>(
                downloadClientItems.Select(i => i.DownloadId).Where(id => id.IsNotNullOrWhiteSpace()),
                StringComparer.OrdinalIgnoreCase);

            foreach (var trackedDownload in _trackedDownloadService.GetTrackedDownloads())
            {
                var downloadItem = trackedDownload?.DownloadItem;

                if (downloadItem == null ||
                    downloadItem.DownloadId.IsNullOrWhiteSpace() ||
                    trackedDownload.DownloadClient != downloadClient.Definition.Id ||
                    clientDownloadIds.Contains(downloadItem.DownloadId))
                {
                    continue;
                }

                // Imported/failed/ignored downloads already reached a terminal state; their own
                // path recorded the history and the item removal is expected cleanup.
                if (IsTerminalState(trackedDownload.State))
                {
                    continue;
                }

                var latestHistory = _downloadHistoryService.GetLatestDownloadHistoryItem(downloadItem.DownloadId);

                if (latestHistory != null && latestHistory.EventType != DownloadHistoryEventType.DownloadGrabbed)
                {
                    continue;
                }

                try
                {
                    _logger.Info("Download '{0}' ({1}) is no longer in download client '{2}', recording terminal history",
                        downloadItem.Title,
                        downloadItem.DownloadId,
                        downloadClient.Definition.Name);

                    _downloadHistoryService.RecordRemovedDownload(trackedDownload, "Removed from download client");

                    _trackedDownloadService.StopTracking(downloadItem.DownloadId);
                }
                catch (Exception e)
                {
                    // Leave the item tracked so the next refresh retries.
                    _logger.Error(e, "Unable to record terminal history for vanished download {0} ({1})", downloadItem.Title, downloadItem.DownloadId);
                }
            }
        }

        private TrackedDownload ProcessClientItem(IDownloadClient downloadClient, DownloadClientItem downloadItem)
        {
            TrackedDownload trackedDownload = null;

            try
            {
                trackedDownload =
                    _trackedDownloadService.TrackDownload((DownloadClientDefinition)downloadClient.Definition,
                        downloadItem);

                if (trackedDownload is { State: TrackedDownloadState.Downloading or TrackedDownloadState.ImportBlocked })
                {
                    _failedDownloadService.Check(trackedDownload);
                    _completedDownloadService.Check(trackedDownload);
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, "Couldn't process tracked download {0}", downloadItem.Title);
            }

            return trackedDownload;
        }

        private bool DownloadIsTrackable(TrackedDownload trackedDownload)
        {
            // If the download has already been imported, failed or the user ignored it don't track it
            if (IsTerminalState(trackedDownload.State))
            {
                return false;
            }

            // If CDH is disabled and the download status is complete don't track it
            if (!_configService.EnableCompletedDownloadHandling && trackedDownload.DownloadItem.Status == DownloadItemStatus.Completed)
            {
                return false;
            }

            return true;
        }

        private static bool IsTerminalState(TrackedDownloadState state)
        {
            return state is TrackedDownloadState.Imported or
                            TrackedDownloadState.Failed or
                            TrackedDownloadState.Ignored;
        }

        public void Execute(RefreshMonitoredDownloadsCommand message)
        {
            Refresh();
        }

        public void Execute(CheckForFinishedDownloadCommand message)
        {
            _logger.Warn("A third party app used the deprecated CheckForFinishedDownload command, it should be updated RefreshMonitoredDownloads instead");
            Refresh();
        }

        public void Handle(EpisodeGrabbedEvent message)
        {
            _refreshDebounce.Execute();
        }

        public void Handle(ManualInteractionRequiredEvent message)
        {
            _refreshDebounce.Execute();
        }

        public void Handle(EpisodeImportedEvent message)
        {
            _refreshDebounce.Execute();
        }

        public void Handle(MovieGrabbedEvent message)
        {
            _refreshDebounce.Execute();
        }

        public void Handle(MovieFileImportedEvent message)
        {
            _refreshDebounce.Execute();
        }

        public void Handle(DownloadsProcessedEvent message)
        {
            var trackedDownloads = _trackedDownloadService.GetTrackedDownloads().Where(t => t.IsTrackable && DownloadIsTrackable(t)).ToList();

            _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(trackedDownloads));
        }

        public void Handle(TrackedDownloadsRemovedEvent message)
        {
            var trackedDownloads = _trackedDownloadService.GetTrackedDownloads().Where(t => t.IsTrackable && DownloadIsTrackable(t)).ToList();

            _eventAggregator.PublishEvent(new TrackedDownloadRefreshedEvent(trackedDownloads));
        }
    }
}
