using System;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download.History;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Queue;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class QueueSpecification : DownloadDecisionEngineSpecification
    {
        // A grab is only plausibly in flight for a bounded time. Without a terminal event a
        // very old grab would otherwise make its release permanently ungrabbable, e.g. the
        // fileless episodes left behind by grabs whose terminal event was never recorded.
        private static readonly TimeSpan MaxActiveGrabAge = TimeSpan.FromDays(7);

        private readonly IQueueService _queueService;
        private readonly UpgradableSpecification _upgradableSpecification;
        private readonly ICustomFormatCalculationService _formatService;
        private readonly IConfigService _configService;
        private readonly IHistoryService _historyService;
        private readonly IDownloadHistoryService _downloadHistoryService;
        private readonly Logger _logger;

        public QueueSpecification(IQueueService queueService,
                                  UpgradableSpecification upgradableSpecification,
                                  ICustomFormatCalculationService formatService,
                                  IConfigService configService,
                                  IHistoryService historyService,
                                  IDownloadHistoryService downloadHistoryService,
                                  Logger logger)
        {
            _queueService = queueService;
            _upgradableSpecification = upgradableSpecification;
            _formatService = formatService;
            _configService = configService;
            _historyService = historyService;
            _downloadHistoryService = downloadHistoryService;
            _logger = logger;
        }

        public override SpecificationPriority Priority => SpecificationPriority.Default;
        public override RejectionType Type => RejectionType.Permanent;

        public override bool AppliesTo(IRemoteSubject subject)
        {
            return subject is RemoteEpisode || subject is RemoteMovie;
        }

        protected override DownloadSpecDecision IsSatisfiedBy(RemoteEpisode subject, ReleaseDecisionInformation information)
        {
            var queue = _queueService.GetQueue();
            var grabbed = subject.Episodes.SelectMany(e => _historyService.FindByEpisodeId(e.Id))
                .Where(h => h.EventType == EpisodeHistoryEventType.Grabbed &&
                            SameRelease(subject.Release, h.SourceTitle, h.DownloadId));

            foreach (var history in grabbed)
            {
                if (IsActiveGrab(history.DownloadId, history.Date, queue) &&
                    !_historyService.FindByDownloadId(history.DownloadId).Any(h =>
                        h.Date >= history.Date &&
                        (h.EventType == EpisodeHistoryEventType.DownloadFailed ||
                         h.EventType == EpisodeHistoryEventType.DownloadIgnored ||
                         h.EventType == EpisodeHistoryEventType.DownloadFolderImported)))
                {
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.QueueHigherPreference, "Release has already been grabbed and is still in progress: {0}", history.SourceTitle);
                }
            }

            var matchingEpisode = queue.Where(q => q.RemoteEpisode?.Series != null &&
                                                   q.RemoteEpisode.Series.Id == subject.Series.Id &&
                                                   q.RemoteEpisode.Episodes.Select(e => e.Id).Intersect(subject.Episodes.Select(e => e.Id)).Any())
                                       .ToList();

            foreach (var queueItem in matchingEpisode)
            {
                var remoteEpisode = queueItem.RemoteEpisode;
                var qualityProfile = subject.Series.QualityProfile.Value;

                // To avoid a race make sure it's not FailedPending (failed awaiting removal/search).
                // Failed items (already searching for a replacement) won't be part of the queue since
                // it's a copy, of the tracked download, not a reference.

                if (queueItem.TrackedDownloadState == TrackedDownloadState.FailedPending)
                {
                    continue;
                }

                var queuedItemCustomFormats = _formatService.ParseCustomFormat(remoteEpisode, (long)queueItem.Size);

                _logger.Debug("Checking if existing release in queue meets cutoff. Queued: {0}", remoteEpisode.ParsedEpisodeInfo.Quality);

                if (!_upgradableSpecification.CutoffNotMet(qualityProfile,
                    remoteEpisode.ParsedEpisodeInfo.Quality,
                    queuedItemCustomFormats,
                    subject.ParsedEpisodeInfo.Quality))
                {
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.QueueCutoffMet, "Release in queue already meets cutoff: {0}", remoteEpisode.ParsedEpisodeInfo.Quality);
                }

                _logger.Debug("Checking if release is higher quality than queued release. Queued: {0}", remoteEpisode.ParsedEpisodeInfo.Quality);

                var upgradeableRejectReason = _upgradableSpecification.IsUpgradable(qualityProfile,
                    remoteEpisode.ParsedEpisodeInfo.Quality,
                    queuedItemCustomFormats,
                    subject.ParsedEpisodeInfo.Quality,
                    subject.CustomFormats);

                switch (upgradeableRejectReason)
                {
                    case UpgradeableRejectReason.BetterQuality:
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.QueueHigherPreference, "Release in queue is of equal or higher preference: {0}", remoteEpisode.ParsedEpisodeInfo.Quality);

                    case UpgradeableRejectReason.BetterRevision:
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.QueueHigherRevision, "Release in queue is of equal or higher revision: {0}", remoteEpisode.ParsedEpisodeInfo.Quality.Revision);

                    case UpgradeableRejectReason.QualityCutoff:
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.QueueCutoffMet, "Release in queue meets quality cutoff: {0}", qualityProfile.Items[qualityProfile.GetIndex(qualityProfile.Cutoff).Index]);

                    case UpgradeableRejectReason.CustomFormatCutoff:
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.QueueCustomFormatCutoffMet, "Release in queue meets Custom Format cutoff: {0}", qualityProfile.CutoffFormatScore);

                    case UpgradeableRejectReason.CustomFormatScore:
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.QueueCustomFormatScore, "Release in queue has an equal or higher Custom Format score: {0}", qualityProfile.CalculateCustomFormatScore(queuedItemCustomFormats));

                    case UpgradeableRejectReason.MinCustomFormatScore:
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.QueueCustomFormatScoreIncrement, "Release in queue has Custom Format score within Custom Format score increment: {0}", qualityProfile.MinUpgradeFormatScore);

                    case UpgradeableRejectReason.UpgradesNotAllowed:
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.QueueUpgradesNotAllowed, "Release in queue and Quality Profile '{0}' does not allow upgrades", qualityProfile.Name);
                }

                if (_upgradableSpecification.IsRevisionUpgrade(remoteEpisode.ParsedEpisodeInfo.Quality, subject.ParsedEpisodeInfo.Quality))
                {
                    if (_configService.DownloadPropersAndRepacks == ProperDownloadTypes.DoNotUpgrade)
                    {
                        _logger.Debug("Auto downloading of propers is disabled");
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.QueuePropersDisabled, "Proper downloading is disabled");
                    }
                }
            }

            return DownloadSpecDecision.Accept();
        }

        protected override DownloadSpecDecision IsSatisfiedBy(RemoteMovie subject, ReleaseDecisionInformation information)
        {
            var queue = _queueService.GetQueue();
            var grabbed = _historyService.GetByMovieId(subject.Movie.Id, MovieHistoryEventType.Grabbed)
                .Where(h => SameRelease(subject.Release, h.SourceTitle, h.DownloadId));

            foreach (var history in grabbed)
            {
                if (IsActiveGrab(history.DownloadId, history.Date, queue) &&
                    !_historyService.FindMovieByDownloadId(history.DownloadId).Any(h =>
                        h.Date >= history.Date &&
                        (h.EventType == MovieHistoryEventType.DownloadFailed ||
                         h.EventType == MovieHistoryEventType.DownloadIgnored ||
                         h.EventType == MovieHistoryEventType.DownloadFolderImported)))
                {
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.QueueHigherPreference, "Release has already been grabbed and is still in progress: {0}", history.SourceTitle);
                }
            }

            var matchingMovies = queue.Where(q => q.RemoteMovie?.Movie != null &&
                                                  q.RemoteMovie.Movie.Id == subject.Movie.Id)
                                      .ToList();

            foreach (var queueItem in matchingMovies)
            {
                var remoteMovie = queueItem.RemoteMovie;
                var qualityProfile = subject.Movie.QualityProfile;

                // To avoid a race make sure it's not FailedPending (failed awaiting removal/search).
                if (queueItem.TrackedDownloadState == TrackedDownloadState.FailedPending)
                {
                    continue;
                }

                var queuedItemCustomFormats = _formatService.ParseCustomFormat(remoteMovie, (long)queueItem.Size);

                _logger.Debug("Checking if existing release in queue meets cutoff. Queued quality is: {0} - {1}",
                              remoteMovie.ParsedMovieInfo.Quality,
                              queuedItemCustomFormats.ConcatToString());

                if (!_upgradableSpecification.CutoffNotMet(qualityProfile,
                    remoteMovie.ParsedMovieInfo.Quality,
                    queuedItemCustomFormats,
                    subject.ParsedMovieInfo.Quality))
                {
                    return DownloadSpecDecision.Reject(DownloadRejectionReason.QueueCutoffMet, "Quality for release in queue already meets cutoff: {0}", remoteMovie.ParsedMovieInfo.Quality);
                }

                var upgradeableRejectReason = _upgradableSpecification.IsUpgradable(qualityProfile,
                    remoteMovie.ParsedMovieInfo.Quality,
                    queuedItemCustomFormats,
                    subject.ParsedMovieInfo.Quality,
                    subject.CustomFormats);

                switch (upgradeableRejectReason)
                {
                    case UpgradeableRejectReason.BetterQuality:
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.QueueHigherPreference, "Release in queue is of equal or higher preference: {0}", remoteMovie.ParsedMovieInfo.Quality);

                    case UpgradeableRejectReason.BetterRevision:
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.QueueHigherRevision, "Release in queue is of equal or higher revision: {0}", remoteMovie.ParsedMovieInfo.Quality.Revision);

                    case UpgradeableRejectReason.QualityCutoff:
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.QueueCutoffMet, "Release in queue meets quality cutoff: {0}", qualityProfile.Items[qualityProfile.GetIndex(qualityProfile.Cutoff).Index]);

                    case UpgradeableRejectReason.CustomFormatCutoff:
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.QueueCustomFormatCutoffMet, "Release in queue meets Custom Format cutoff: {0}", qualityProfile.CutoffFormatScore);

                    case UpgradeableRejectReason.CustomFormatScore:
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.QueueCustomFormatScore, "Release in queue has an equal or higher Custom Format score: {0}", qualityProfile.CalculateCustomFormatScore(queuedItemCustomFormats));

                    case UpgradeableRejectReason.MinCustomFormatScore:
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.QueueCustomFormatScoreIncrement, "Release in queue has Custom Format score within Custom Format score increment: {0}", qualityProfile.MinUpgradeFormatScore);

                    case UpgradeableRejectReason.UpgradesNotAllowed:
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.QueueUpgradesNotAllowed, "Release in queue and Quality Profile '{0}' does not allow upgrades", qualityProfile.Name);
                }

                if (_upgradableSpecification.IsRevisionUpgrade(remoteMovie.ParsedMovieInfo.Quality, subject.ParsedMovieInfo.Quality))
                {
                    if (_configService.DownloadPropersAndRepacks == ProperDownloadTypes.DoNotUpgrade)
                    {
                        _logger.Debug("Auto downloading of propers is disabled");
                        return DownloadSpecDecision.Reject(DownloadRejectionReason.QueuePropersDisabled, "Proper downloading is disabled");
                    }
                }
            }

            return DownloadSpecDecision.Accept();
        }

        private static bool SameRelease(ReleaseInfo release, string title, string downloadId)
        {
            return release != null && !string.IsNullOrWhiteSpace(downloadId) &&
                   ((!string.IsNullOrWhiteSpace(release.Title) &&
                     string.Equals(release.Title, title, StringComparison.OrdinalIgnoreCase)) ||
                    (release is TorrentInfo torrent && !string.IsNullOrWhiteSpace(torrent.InfoHash) &&
                     string.Equals(torrent.InfoHash, downloadId, StringComparison.OrdinalIgnoreCase)));
        }

        private bool IsActiveGrab(string downloadId, DateTime grabbedAt, System.Collections.Generic.List<NzbDrone.Core.Queue.Queue> queue)
        {
            // Stop treating a grab as in flight once it is older than any plausible download.
            // An old grab with no terminal event must not block a re-grab forever.
            if (DateTime.UtcNow - grabbedAt > MaxActiveGrabAge)
            {
                return false;
            }

            // The projection may not yet have been refreshed after startup. History is
            // durable, but a failed-pending item must remain eligible for replacement.
            if (queue.Any(q => q.DownloadId == downloadId && q.TrackedDownloadState == TrackedDownloadState.FailedPending))
            {
                return false;
            }

            var latest = _downloadHistoryService.GetLatestDownloadHistoryItem(downloadId);
            return latest == null || latest.Date < grabbedAt || latest.EventType == DownloadHistoryEventType.DownloadGrabbed;
        }
    }
}
