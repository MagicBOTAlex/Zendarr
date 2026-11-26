using System;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Queue;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class AlreadyImportedSpecification : DownloadDecisionEngineSpecification
    {
        private readonly IHistoryService _historyService;
        private readonly IDownloadHistoryService _downloadHistoryService;
        private readonly IQueueService _queueService;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public AlreadyImportedSpecification(IHistoryService historyService,
                                            IDownloadHistoryService downloadHistoryService,
                                            IQueueService queueService,
                                            IConfigService configService,
                                            Logger logger)
        {
            _historyService = historyService;
            _downloadHistoryService = downloadHistoryService;
            _queueService = queueService;
            _configService = configService;
            _logger = logger;
        }

        public override SpecificationPriority Priority => SpecificationPriority.Database;
        public override RejectionType Type => RejectionType.Permanent;

        public override bool AppliesTo(IRemoteSubject subject)
        {
            return subject is RemoteEpisode || subject is RemoteMovie;
        }

        // The decision engine is not on the grab path: a manual grab calls
        // DownloadService.DownloadReport directly and the automatic processor has already made
        // its decision, so neither sees these guards. Re-apply the durable history guards at the
        // grab boundary so an already-imported release, or a release whose grab is still live,
        // cannot be grabbed again. Order: the terminal already-imported guard first, then the
        // in-flight same-release guard.
        public DownloadSpecDecision IsSatisfiedByGrab(IRemoteSubject subject)
        {
            if (subject?.Release == null)
            {
                return DownloadSpecDecision.Accept();
            }

            if (subject is RemoteMovie || subject is RemoteEpisode { Episodes: not null })
            {
                var imported = IsSatisfiedBy(subject, new ReleaseDecisionInformation());

                if (!imported.Accepted)
                {
                    return imported;
                }
            }

            return IsSatisfiedBySameRelease(subject);
        }

        private DownloadSpecDecision IsSatisfiedBySameRelease(IRemoteSubject subject)
        {
            var queue = _queueService.GetQueue() ?? new System.Collections.Generic.List<NzbDrone.Core.Queue.Queue>();

            if (subject is RemoteEpisode remoteEpisode && remoteEpisode.Episodes != null)
            {
                var grabbed = remoteEpisode.Episodes
                    .SelectMany(e => _historyService.FindByEpisodeId(e.Id) ?? Enumerable.Empty<EpisodeHistory>())
                    .Where(h => h.EventType == EpisodeHistoryEventType.Grabbed &&
                                SameRelease(subject.Release, h.SourceTitle, h.DownloadId));

                foreach (var history in grabbed)
                {
                    var laterEvents = _historyService.FindByDownloadId(history.DownloadId) ?? Enumerable.Empty<EpisodeHistory>();

                    if (IsActiveGrab(history.DownloadId, history.Date, queue) &&
                        !laterEvents.Any(h => h.Date >= history.Date &&
                                              (h.EventType == EpisodeHistoryEventType.DownloadFailed ||
                                               h.EventType == EpisodeHistoryEventType.DownloadIgnored ||
                                               h.EventType == EpisodeHistoryEventType.DownloadFolderImported)))
                    {
                        _logger.Debug("Release '{0}' has already been grabbed and is still in progress: {1}", subject.Release.Title, history.SourceTitle);
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.QueueHigherPreference, "Release has already been grabbed and is still in progress: {0}", history.SourceTitle);
                    }
                }
            }
            else if (subject is RemoteMovie remoteMovie && remoteMovie.Movie != null)
            {
                var grabbed = (_historyService.GetByMovieId(remoteMovie.Movie.Id, MovieHistoryEventType.Grabbed) ?? Enumerable.Empty<MovieHistory>())
                    .Where(h => SameRelease(subject.Release, h.SourceTitle, h.DownloadId));

                foreach (var history in grabbed)
                {
                    var laterEvents = _historyService.FindMovieByDownloadId(history.DownloadId) ?? Enumerable.Empty<MovieHistory>();

                    if (IsActiveGrab(history.DownloadId, history.Date, queue) &&
                        !laterEvents.Any(h => h.Date >= history.Date &&
                                              (h.EventType == MovieHistoryEventType.DownloadFailed ||
                                               h.EventType == MovieHistoryEventType.DownloadIgnored ||
                                               h.EventType == MovieHistoryEventType.DownloadFolderImported)))
                    {
                        _logger.Debug("Release '{0}' has already been grabbed and is still in progress: {1}", subject.Release.Title, history.SourceTitle);
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.QueueHigherPreference, "Release has already been grabbed and is still in progress: {0}", history.SourceTitle);
                    }
                }
            }

            return DownloadSpecDecision.Accept();
        }

        private static bool SameRelease(ReleaseInfo release, string title, string downloadId)
        {
            return !string.IsNullOrWhiteSpace(downloadId) &&
                   ((!string.IsNullOrWhiteSpace(release.Title) &&
                     string.Equals(release.Title, title, StringComparison.OrdinalIgnoreCase)) ||
                    (release is TorrentInfo torrent && !string.IsNullOrWhiteSpace(torrent.InfoHash) &&
                     string.Equals(torrent.InfoHash, downloadId, StringComparison.OrdinalIgnoreCase)));
        }

        private bool IsActiveGrab(string downloadId, DateTime grabbedAt, System.Collections.Generic.List<NzbDrone.Core.Queue.Queue> queue)
        {
            // The projection may not yet have been refreshed after startup. History is
            // durable, but a failed-pending item must remain eligible for replacement.
            if (queue.Any(q => q.DownloadId == downloadId && q.TrackedDownloadState == TrackedDownloadState.FailedPending))
            {
                return false;
            }

            var latest = _downloadHistoryService.GetLatestDownloadHistoryItem(downloadId);
            return latest == null || latest.Date < grabbedAt || latest.EventType == DownloadHistoryEventType.DownloadGrabbed;
        }

        protected override DownloadSpecDecision IsSatisfiedBy(RemoteEpisode subject, ReleaseDecisionInformation information)
        {
            var cdhEnabled = _configService.EnableCompletedDownloadHandling;

            if (!cdhEnabled)
            {
                _logger.Debug("Skipping already imported check because CDH is disabled");
                return DownloadSpecDecision.Accept();
            }

            _logger.Debug("Performing already imported check on report");
            foreach (var episode in subject.Episodes)
            {
                if (!episode.HasFile)
                {
                    _logger.Debug("Skipping already imported check for episode without file");
                    continue;
                }

                var historyForEpisode = _historyService.FindByEpisodeId(episode.Id);
                var lastGrabbed = historyForEpisode.FirstOrDefault(h => h.EventType == EpisodeHistoryEventType.Grabbed);

                if (lastGrabbed == null)
                {
                    continue;
                }

                var imported = historyForEpisode.FirstOrDefault(h =>
                    h.EventType == EpisodeHistoryEventType.DownloadFolderImported &&
                    h.DownloadId == lastGrabbed.DownloadId);

                if (imported == null)
                {
                    continue;
                }

                // This is really only a guard against redownloading the same release over
                // and over when the grabbed and imported qualities do not match, if they do
                // match skip this check.
                if (lastGrabbed.Quality.Equals(imported.Quality))
                {
                    continue;
                }

                var release = subject.Release;

                if (release.DownloadProtocol == DownloadProtocol.Torrent)
                {
                    var torrentInfo = release as TorrentInfo;

                    if (torrentInfo?.InfoHash != null && torrentInfo.InfoHash.ToUpper() == lastGrabbed.DownloadId)
                    {
                        _logger.Debug("Has same torrent hash as a grabbed and imported release");
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.AlreadyImportedSameHash, "Has same torrent hash as a grabbed and imported release");
                    }
                }

                // Only based on title because a release with the same title on another indexer/released at
                // a different time very likely has the exact same content and we don't need to also try it.

                if (release.Title.Equals(lastGrabbed.SourceTitle, StringComparison.InvariantCultureIgnoreCase))
                {
                    _logger.Debug("Has same release name as a grabbed and imported release");
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.AlreadyImportedSameName, "Has same release name as a grabbed and imported release");
                }
            }

            return DownloadSpecDecision.Accept();
        }

        protected override DownloadSpecDecision IsSatisfiedBy(RemoteMovie subject, ReleaseDecisionInformation information)
        {
            var cdhEnabled = _configService.EnableCompletedDownloadHandling;

            if (!cdhEnabled)
            {
                _logger.Debug("Skipping already imported check because CDH is disabled");
                return DownloadSpecDecision.Accept();
            }

            var movie = subject.Movie;

            _logger.Debug("Performing already imported check on report");
            if (movie == null)
            {
                return DownloadSpecDecision.Accept();
            }

            if (!movie.HasFile)
            {
                _logger.Debug("Skipping already imported check for movie without file");
                return DownloadSpecDecision.Accept();
            }

            var historyForMovie = _historyService.GetByMovieId(movie.Id, null);
            var lastGrabbed = historyForMovie.FirstOrDefault(h => h.EventType == MovieHistoryEventType.Grabbed);

            if (lastGrabbed == null)
            {
                return DownloadSpecDecision.Accept();
            }

            var imported = historyForMovie.FirstOrDefault(h =>
                h.EventType == MovieHistoryEventType.DownloadFolderImported &&
                h.DownloadId == lastGrabbed.DownloadId);

            if (imported == null)
            {
                return DownloadSpecDecision.Accept();
            }

            // This is really only a guard against redownloading the same release over
            // and over when the grabbed and imported qualities do not match, if they do
            // match skip this check.
            if (lastGrabbed.Quality.Equals(imported.Quality))
            {
                return DownloadSpecDecision.Accept();
            }

            var release = subject.Release;

            if (release.DownloadProtocol == DownloadProtocol.Torrent)
            {
                var torrentInfo = release as TorrentInfo;

                if (torrentInfo?.InfoHash != null && torrentInfo.InfoHash.ToUpper() == lastGrabbed.DownloadId)
                {
                    _logger.Debug("Has same torrent hash as a grabbed and imported release");
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.AlreadyImportedSameHash, "Has same torrent hash as a grabbed and imported release");
                }
            }

            // Only based on title because a release with the same title on another indexer/released at
            // a different time very likely has the exact same content and we don't need to also try it.
            if (release.Title.Equals(lastGrabbed.SourceTitle, StringComparison.InvariantCultureIgnoreCase))
            {
                _logger.Debug("Has same release name as a grabbed and imported release");
                return DownloadSpecDecision.Reject(DownloadRejectionReason.AlreadyImportedSameName, "Has same release name as a grabbed and imported release");
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
