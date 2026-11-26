import React from 'react';
import Icon from 'Components/Icon';
import Popover from 'Components/Tooltip/Popover';
import { icons } from 'Helpers/Props';
import { TooltipPosition } from 'Helpers/Props/tooltipPositions';
import { getQueueStatusBadgeFromStatus } from 'Library/Activity/mediaActivityStatus';
import {
  QueueTrackedDownloadState,
  QueueTrackedDownloadStatus,
  StatusMessage,
} from 'typings/Queue';

function getDetailedPopoverBody(statusMessages: StatusMessage[]) {
  return (
    <div>
      {statusMessages.map(({ title, messages }) => {
        return (
          <div
            key={title}
            className={messages.length ? undefined : 'mb-[10px]'}
          >
            {title}
            <ul>
              {messages.map((message) => {
                return <li key={message}>{message}</li>;
              })}
            </ul>
          </div>
        );
      })}
    </div>
  );
}

interface QueueStatusProps {
  sourceTitle: string;
  status: string;
  trackedDownloadStatus?: QueueTrackedDownloadStatus;
  trackedDownloadState?: QueueTrackedDownloadState;
  statusMessages?: StatusMessage[];
  errorMessage?: string;
  position: TooltipPosition;
  canFlip?: boolean;
}

function QueueStatus(props: QueueStatusProps) {
  const {
    sourceTitle,
    status,
    trackedDownloadStatus = 'ok',
    trackedDownloadState = 'downloading',
    statusMessages = [],
    errorMessage,
    position,
    canFlip = false,
  } = props;

  const hasWarning = trackedDownloadStatus === 'warning';
  const hasError = trackedDownloadStatus === 'error';

  const badge = getQueueStatusBadgeFromStatus(
    status,
    trackedDownloadStatus,
    trackedDownloadState
  );
  let iconName = icons.DOWNLOADING;

  if (status === 'paused') {
    iconName = icons.PAUSED;
  } else if (status === 'queued') {
    iconName = icons.QUEUED;
  } else if (status === 'completed') {
    iconName = hasError ? icons.DOWNLOAD : icons.DOWNLOADED;
  } else if (status === 'delay' || status === 'downloadClientUnavailable') {
    iconName = icons.PENDING;
  }

  return (
    <Popover
      anchor={<Icon name={iconName} kind={badge.kind} />}
      accessibleLabel={badge.label}
      title={badge.label}
      body={
        hasWarning || hasError
          ? getDetailedPopoverBody(statusMessages)
          : errorMessage || sourceTitle
      }
      position={position}
      canFlip={canFlip}
    />
  );
}

export default QueueStatus;
