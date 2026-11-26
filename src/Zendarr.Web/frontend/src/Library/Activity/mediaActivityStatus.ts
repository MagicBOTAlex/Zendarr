import {
  QueueTrackedDownloadState,
  QueueTrackedDownloadStatus,
} from 'typings/Queue';
import translate from 'Utilities/String/translate';
import { MediaHistoryItem, MediaQueueItem } from './mediaActivity';

export type BadgeKind =
  | 'default'
  | 'info'
  | 'primary'
  | 'success'
  | 'warning'
  | 'danger'
  | 'purple';

export interface StatusBadge {
  label: string;
  kind: BadgeKind;
}

export type ProgressBarKind =
  | 'danger'
  | 'info'
  | 'primary'
  | 'purple'
  | 'success'
  | 'warning';

const PROGRESS_BAR_KINDS: Record<BadgeKind, ProgressBarKind> = {
  default: 'primary',
  info: 'info',
  primary: 'primary',
  success: 'success',
  warning: 'warning',
  danger: 'danger',
  purple: 'purple',
};

export function getProgressBarKind(kind: BadgeKind): ProgressBarKind {
  return PROGRESS_BAR_KINDS[kind];
}

function getTrackedDownload(item: MediaQueueItem) {
  const isSeries = item.type === 'series';

  return {
    status: isSeries
      ? item.series?.trackedDownloadStatus
      : item.movie?.trackedDownloadStatus,
    state: isSeries
      ? item.series?.trackedDownloadState
      : item.movie?.trackedDownloadState,
  };
}

const IMPORTING_STATES = new Set([
  'importBlocked',
  'importPending',
  'importing',
  'failedPending',
  'failed',
]);

export function getQueueStatusBadge(item: MediaQueueItem): StatusBadge {
  const { status: trackedStatus, state } = getTrackedDownload(item);

  return getQueueStatusBadgeFromStatus(item.status, trackedStatus, state);
}

// Shared by Activity and episode views so the same queue tuple has one label
// and tone regardless of where it is displayed.
export function getQueueStatusBadgeFromStatus(
  status: string,
  trackedStatus?: QueueTrackedDownloadStatus,
  state?: QueueTrackedDownloadState
): StatusBadge {
  if (
    status === 'failed' ||
    trackedStatus === 'error' ||
    state === 'failedPending' ||
    state === 'failed'
  ) {
    return { label: translate('Failed'), kind: 'danger' };
  }

  if (status === 'completed') {
    switch (state) {
      case 'importBlocked':
        // Finished downloading, but Sonarr cannot import it on its own, so it
        // must not be presented as a completed item.
        return { label: translate('ManualImport'), kind: 'warning' };

      case 'importPending':
        return {
          label: translate('WaitingToImport'),
          kind: trackedStatus === 'warning' ? 'warning' : 'purple',
        };

      case 'importing':
        return {
          label: translate('Importing'),
          kind: trackedStatus === 'warning' ? 'warning' : 'primary',
        };

      default:
        return {
          label: translate('Completed'),
          kind: trackedStatus === 'warning' ? 'warning' : 'success',
        };
    }
  }

  switch (status) {
    case 'downloading':
      return {
        label: translate('Downloading'),
        kind: trackedStatus === 'warning' ? 'warning' : 'primary',
      };

    case 'queued':
      return {
        label: translate('Queued'),
        kind: trackedStatus === 'warning' ? 'warning' : 'info',
      };

    case 'paused':
      return { label: translate('Paused'), kind: 'warning' };

    case 'delay':
    case 'downloadClientUnavailable':
      return { label: translate('Pending'), kind: 'warning' };

    case 'warning':
      return { label: translate('Warning'), kind: 'warning' };

    default:
      return {
        label: status,
        kind: trackedStatus === 'warning' ? 'warning' : 'default',
      };
  }
}

// A download only counts as completed once it has actually been imported. Items
// still waiting on (or blocked from) import are reported on their own.
export function isQueueItemCompleted(item: MediaQueueItem): boolean {
  return (
    item.status === 'completed' &&
    !IMPORTING_STATES.has(getTrackedDownload(item).state ?? '')
  );
}

// "Needs attention" means the user has to do something: a failed download or an
// item Sonarr could not import automatically. Warnings such as a stalled
// download are left out.
export function isQueueItemAttention(item: MediaQueueItem): boolean {
  const { status: trackedStatus, state } = getTrackedDownload(item);

  return (
    item.status === 'failed' ||
    trackedStatus === 'error' ||
    state === 'failedPending' ||
    state === 'failed' ||
    state === 'importBlocked'
  );
}

const HISTORY_BADGES: Record<
  string,
  { labelKey: string; kind: BadgeKind } | undefined
> = {
  grabbed: { labelKey: 'Grabbed', kind: 'info' },
  seriesFolderImported: { labelKey: 'Imported', kind: 'success' },
  downloadFolderImported: { labelKey: 'Imported', kind: 'success' },
  downloadFailed: { labelKey: 'Failed', kind: 'danger' },
  episodeFileDeleted: { labelKey: 'FileDeleted', kind: 'default' },
  movieFileDeleted: { labelKey: 'FileDeleted', kind: 'default' },
  episodeFileRenamed: { labelKey: 'Renamed', kind: 'default' },
  movieFileRenamed: { labelKey: 'Renamed', kind: 'default' },
  downloadIgnored: { labelKey: 'Ignored', kind: 'default' },
};

export function getHistoryEventBadge(
  item: MediaHistoryItem
): StatusBadge | null {
  const eventType =
    item.type === 'series' ? item.historyEventType : item.movieEventType;

  if (!eventType) {
    return null;
  }

  const badge = HISTORY_BADGES[eventType];

  if (!badge) {
    return { label: eventType, kind: 'default' };
  }

  return { label: translate(badge.labelKey), kind: badge.kind };
}

export function isQueueItemActive(item: MediaQueueItem): boolean {
  return item.status === 'downloading' || item.status === 'queued';
}

export function getQueueProgress(item: MediaQueueItem): number {
  if (item.status === 'completed') {
    return 100;
  }

  if (!item.size || item.size <= 0) {
    return 0;
  }

  const progress = ((item.size - item.sizeLeft) / item.size) * 100;

  return Math.min(100, Math.max(0, progress));
}
