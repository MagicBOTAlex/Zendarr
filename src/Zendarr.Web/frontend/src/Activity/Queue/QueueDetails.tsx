import React from 'react';
import Icon from 'Components/Icon';
import Popover from 'Components/Tooltip/Popover';
import { icons, tooltipPositions } from 'Helpers/Props';
import { getQueueStatusBadgeFromStatus } from 'Library/Activity/mediaActivityStatus';
import {
  QueueTrackedDownloadState,
  QueueTrackedDownloadStatus,
  StatusMessage,
} from 'typings/Queue';
import QueueStatus from './QueueStatus';

interface QueueDetailsProps {
  title: string;
  size: number;
  sizeLeft: number;
  estimatedCompletionTime?: string;
  status: string;
  trackedDownloadState?: QueueTrackedDownloadState;
  trackedDownloadStatus?: QueueTrackedDownloadStatus;
  statusMessages?: StatusMessage[];
  errorMessage?: string;
  progressBar: React.ReactNode;
}

function QueueDetails(props: QueueDetailsProps) {
  const {
    title,
    size,
    sizeLeft,
    status,
    trackedDownloadState = 'downloading',
    trackedDownloadStatus = 'ok',
    statusMessages,
    errorMessage,
    progressBar,
  } = props;

  const progress = size ? 100 - (sizeLeft / size) * 100 : 0;
  const badge = getQueueStatusBadgeFromStatus(
    status,
    trackedDownloadStatus,
    trackedDownloadState
  );
  const isDownloading = status === 'downloading';
  const isPaused = status === 'paused';
  const hasWarning = trackedDownloadStatus === 'warning';
  const hasError = trackedDownloadStatus === 'error';

  if (
    (isDownloading || isPaused) &&
    !hasWarning &&
    !hasError &&
    badge.kind !== 'danger'
  ) {
    if (progress < 5) {
      return (
        <Icon
          name={isPaused ? icons.PAUSED : icons.DOWNLOADING}
          kind={badge.kind}
          title={`${badge.label} - ${progress.toFixed(1)}% ${title}`}
        />
      );
    }

    return (
      <Popover
        className="flex w-full justify-center"
        anchor={progressBar!}
        title={`${badge.label} - ${progress.toFixed(1)}%`}
        body={<div>{title}</div>}
        position="bottom-start"
      />
    );
  }

  return (
    <QueueStatus
      sourceTitle={title}
      status={status}
      trackedDownloadStatus={trackedDownloadStatus}
      trackedDownloadState={trackedDownloadState}
      statusMessages={statusMessages}
      errorMessage={errorMessage}
      position={tooltipPositions.LEFT}
    />
  );
}

export default QueueDetails;
