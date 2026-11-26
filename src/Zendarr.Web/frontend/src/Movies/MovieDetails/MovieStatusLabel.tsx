import React from 'react';
import Label from 'Components/Label';
import { kinds, sizes } from 'Helpers/Props';
import { Kind } from 'Helpers/Props/kinds';
import { getQueueStatusBadgeFromStatus } from 'Library/Activity/mediaActivityStatus';
import { MovieStatus } from 'Movies/Movie';
import firstCharToUpper from 'Utilities/String/firstCharToUpper';
import translate from 'Utilities/String/translate';
import useMovieQueue from './useMovieQueue';

const STATUS_STYLES: Record<string, string> = {
  availNotMonitored: 'border-l-4 border-l-[var(--darkGray)] pl-[2px]',
  continuing: 'border-l-4 border-l-[var(--primaryColor)] pl-[2px]',
  deleted:
    'border-l-4 border-l-[var(--inverseLabelColor)] pl-[2px] text-[var(--dangerColor)]',
  ended: 'border-l-4 border-l-[var(--successColor)] pl-[2px]',
  missingMonitored: 'border-l-4 border-l-[var(--dangerColor)] pl-[2px]',
  missingUnmonitored: 'border-l-4 border-l-[var(--warningColor)] pl-[2px]',
  queue: 'border-l-4 border-l-[var(--queueColor)] pl-[2px]',
};

const QUEUE_STYLES: Record<string, string> = {
  default: STATUS_STYLES.availNotMonitored,
  info: STATUS_STYLES.continuing,
  primary: STATUS_STYLES.queue,
  purple: STATUS_STYLES.queue,
  success: STATUS_STYLES.ended,
  warning: STATUS_STYLES.missingUnmonitored,
  danger: STATUS_STYLES.missingMonitored,
};

function getMovieStatus(
  status: MovieStatus,
  isMonitored: boolean,
  isAvailable: boolean,
  hasFiles: boolean
) {
  if (hasFiles && !isMonitored) {
    return 'availNotMonitored';
  }

  if (hasFiles) {
    return 'ended';
  }

  if (status === 'deleted') {
    return 'deleted';
  }

  if (isAvailable && !isMonitored && !hasFiles) {
    return 'missingUnmonitored';
  }

  if (isAvailable && !hasFiles) {
    return 'missingMonitored';
  }

  return 'continuing';
}

interface MovieStatusLabelProps {
  movieId: number;
  monitored: boolean;
  isAvailable: boolean;
  hasMovieFiles: boolean;
  status: MovieStatus;
  useLabel?: boolean;
}

function MovieStatusLabel({
  movieId,
  monitored,
  isAvailable,
  hasMovieFiles,
  status,
  useLabel = false,
}: MovieStatusLabelProps) {
  const queueItem = useMovieQueue(movieId);

  if (queueItem) {
    const badge = getQueueStatusBadgeFromStatus(
      queueItem.status,
      queueItem.trackedDownloadStatus,
      queueItem.trackedDownloadState
    );

    if (useLabel) {
      return (
        <Label kind={badge.kind} size={sizes.LARGE}>
          {badge.label}
        </Label>
      );
    }

    return <span className={QUEUE_STYLES[badge.kind]}>{badge.label}</span>;
  }

  let movieStatus = getMovieStatus(
    status,
    monitored,
    isAvailable,
    hasMovieFiles
  );

  const statusClass = movieStatus;

  if (movieStatus === 'availNotMonitored' || movieStatus === 'ended') {
    movieStatus = 'downloaded';
  } else if (
    movieStatus === 'missingMonitored' ||
    movieStatus === 'missingUnmonitored'
  ) {
    movieStatus = 'missing';
  } else if (movieStatus === 'continuing') {
    movieStatus = 'notAvailable';
  }

  const statusStyle = STATUS_STYLES[statusClass];

  if (useLabel) {
    let kind: Kind = kinds.SUCCESS;

    switch (statusClass) {
      case 'queue':
        kind = kinds.QUEUE;
        break;
      case 'missingMonitored':
        kind = kinds.DANGER;
        break;
      case 'continuing':
        kind = kinds.INFO;
        break;
      case 'availNotMonitored':
        kind = kinds.DEFAULT;
        break;
      case 'missingUnmonitored':
        kind = kinds.WARNING;
        break;
      case 'deleted':
        kind = kinds.INVERSE;
        break;
      default:
    }

    return (
      <Label kind={kind} size={sizes.LARGE}>
        {translate(firstCharToUpper(movieStatus))}
      </Label>
    );
  }

  return (
    <span className={statusStyle}>
      {translate(firstCharToUpper(movieStatus))}
    </span>
  );
}

export default MovieStatusLabel;
