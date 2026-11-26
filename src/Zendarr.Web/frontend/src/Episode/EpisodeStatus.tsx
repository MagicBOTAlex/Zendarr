import React from 'react';
import { useQueueItemForEpisode } from 'Activity/Queue/Details/QueueDetailsProvider';
import QueueDetails from 'Activity/Queue/QueueDetails';
import Icon from 'Components/Icon';
import ProgressBar from 'Components/ProgressBar';
import StatusIndicator from 'Components/StatusIndicator';
import useEpisode, { EpisodeEntity } from 'Episode/useEpisode';
import { useEpisodeFile } from 'EpisodeFile/EpisodeFileProvider';
import { icons, kinds, sizes } from 'Helpers/Props';
import {
  getProgressBarKind,
  getQueueStatusBadgeFromStatus,
} from 'Library/Activity/mediaActivityStatus';
import isBefore from 'Utilities/Date/isBefore';
import translate from 'Utilities/String/translate';
import EpisodeQuality from './EpisodeQuality';

interface EpisodeStatusProps {
  episodeId: number;
  episodeEntity?: EpisodeEntity;
  episodeFileId: number | undefined;
}

function EpisodeStatus({
  episodeId,
  episodeEntity = 'episodes',
  episodeFileId,
}: EpisodeStatusProps) {
  const episode = useEpisode(episodeId, episodeEntity);
  const queueItem = useQueueItemForEpisode(episodeId);
  const episodeFile = useEpisodeFile(episodeFileId);

  const { airDateUtc, grabbed, monitored, hasFile } = episode || {};
  const hasEpisodeFile = !!episodeFile || !!hasFile;
  const isQueued = !!queueItem;
  const hasAired = isBefore(airDateUtc);

  if (!episode) {
    return null;
  }

  if (hasEpisodeFile) {
    const label = translate('EpisodeDownloaded');

    return (
      <StatusIndicator className="flex justify-center" label={label}>
        {episodeFile ? (
          <EpisodeQuality
            quality={episodeFile.quality}
            size={episodeFile.size}
            isCutoffNotMet={episodeFile.qualityCutoffNotMet}
            title={label}
          />
        ) : (
          <Icon name={icons.DOWNLOADED} title={label} />
        )}
      </StatusIndicator>
    );
  }

  if (isQueued) {
    const { sizeLeft, size } = queueItem;
    const badge = getQueueStatusBadgeFromStatus(
      queueItem.status,
      queueItem.trackedDownloadStatus,
      queueItem.trackedDownloadState
    );

    const progress = size ? 100 - (sizeLeft / size) * 100 : 0;

    return (
      <StatusIndicator className="flex justify-center" label={badge.label}>
        <QueueDetails
          {...queueItem}
          progressBar={
            <ProgressBar
              progress={progress}
              kind={getProgressBarKind(badge.kind)}
              size={sizes.MEDIUM}
            />
          }
        />
      </StatusIndicator>
    );
  }

  if (grabbed) {
    const label = translate('EpisodeIsDownloading');

    return (
      <StatusIndicator
        className="flex justify-center"
        label={label}
        title={label}
      >
        <Icon name={icons.DOWNLOADING} />
      </StatusIndicator>
    );
  }

  if (!airDateUtc) {
    const label = translate('Tba');

    return (
      <StatusIndicator
        className="flex justify-center"
        label={label}
        title={label}
      >
        <Icon name={icons.TBA} />
      </StatusIndicator>
    );
  }

  if (!monitored) {
    const label = translate('EpisodeIsNotMonitored');

    return (
      <StatusIndicator
        className="flex justify-center"
        label={label}
        title={label}
      >
        <Icon name={icons.UNMONITORED} kind={kinds.DISABLED} />
      </StatusIndicator>
    );
  }

  if (hasAired) {
    const label = translate('EpisodeMissingFromDisk');

    return (
      <StatusIndicator
        className="flex justify-center"
        label={label}
        title={label}
      >
        <Icon name={icons.MISSING} />
      </StatusIndicator>
    );
  }

  const label = translate('EpisodeHasNotAired');

  return (
    <StatusIndicator
      className="flex justify-center"
      label={label}
      title={label}
    >
      <Icon name={icons.NOT_AIRED} />
    </StatusIndicator>
  );
}

export default EpisodeStatus;
