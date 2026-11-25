import moment from 'moment-timezone';
import React, { useCallback, useEffect } from 'react';
import SpinnerButton from 'Components/Link/SpinnerButton';
import { sizes } from 'Helpers/Props';
import { useUiSettingsValues } from 'Settings/UI/useUiSettings';
import formatDate from 'Utilities/Date/formatDate';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import { TRANSCODE_TONE_TEXT_CLASSES } from './transcodeStatus';
import TranscodeStatusBadge from './TranscodeStatusBadge';
import { TranscodeJob, useCreateTranscodeJobs } from './useTranscodeJobs';

const ROW_CLASS =
  'flex flex-wrap items-center gap-x-[8px] gap-y-[3px] border-b border-[var(--borderColor)] px-[16px] py-[8px] text-[12px] leading-[1.4] last:border-b-0 hover:bg-[var(--tableRowHoverBackgroundColor)]';
const ERROR_CLASS =
  'w-full break-words text-[11px] leading-[1.4] text-[light-dark(var(--alertDangerColor),var(--dangerColor))]';

interface TranscodeHistoryRowProps {
  job: TranscodeJob;
  deviceName?: string;
  profileName?: string;
}

function getDateLabel(
  date: string | undefined,
  showRelativeDates: boolean,
  shortDateFormat: string
) {
  if (!date) {
    return '';
  }

  return showRelativeDates
    ? moment(date).fromNow()
    : formatDate(date, shortDateFormat);
}

function TranscodeHistoryRow({
  job,
  deviceName,
  profileName,
}: TranscodeHistoryRowProps) {
  const { showRelativeDates, shortDateFormat } = useUiSettingsValues();
  const { createJobs, isCreating, createError, resetCreateError } =
    useCreateTranscodeJobs();

  const sourceName = job.sourcePath.split(/[\\/]/).pop() || job.sourcePath;
  const planLabel =
    profileName ?? (job.maxHeight ? `${job.maxHeight}p` : undefined);
  const isRequeued = job.requeuedJobId != null;
  const canForceRequeue =
    !isRequeued && (job.episodeFileId != null || job.movieFileId != null);

  // The jobs query polls every 3s; a transient requeue failure must not stay pinned to the row
  // forever. Clear it whenever the poll reports a new status/timestamp.
  useEffect(() => {
    resetCreateError();
  }, [job.status, job.lastUpdatedAt, resetCreateError]);

  const handleForceRequeue = useCallback(() => {
    createJobs({ force: true, requeueJobIds: [job.id] });
  }, [createJobs, job.id]);

  const size = job.sourceSize
    ? `${formatBytes(job.sourceSize)}${
        job.outputSize ? ` → ${formatBytes(job.outputSize)}` : ''
      }`
    : '';

  const savedPercent =
    job.sourceSize && job.outputSize && job.outputSize < job.sourceSize
      ? Math.round((1 - job.outputSize / job.sourceSize) * 100)
      : null;

  const dateLabel = getDateLabel(
    job.endedAt ?? job.lastUpdatedAt,
    showRelativeDates,
    shortDateFormat
  );

  let requeueControl: React.ReactNode = null;

  if (isRequeued) {
    requeueControl = (
      <span className="shrink-0 text-[var(--helpTextColor)]">
        {translate('Requeued')}
      </span>
    );
  } else if (canForceRequeue) {
    requeueControl = (
      <SpinnerButton
        size={sizes.SMALL}
        isSpinning={isCreating}
        onPress={handleForceRequeue}
      >
        {translate('ForceRequeue')}
      </SpinnerButton>
    );
  }

  return (
    <div
      className={ROW_CLASS}
      title={`${job.sourcePath}${deviceName ? ` (${deviceName})` : ''}`}
    >
      <TranscodeStatusBadge status={job.status} />

      <span className="min-w-0 grow basis-[160px] truncate font-medium text-[var(--textColor)]">
        {sourceName}
      </span>

      {planLabel ? (
        <span className="shrink-0 text-[var(--helpTextColor)]">
          {planLabel}
        </span>
      ) : null}

      <span className="shrink-0 text-[var(--helpTextColor)]">
        {job.videoCodec ? `${job.videoCodec.toUpperCase()} · ` : ''}
        {size}
        {savedPercent ? (
          <span
            className={`ml-[4px] font-semibold ${TRANSCODE_TONE_TEXT_CLASSES.success}`}
          >
            −{savedPercent}%
          </span>
        ) : null}
      </span>

      {dateLabel ? (
        <span className="shrink-0 text-[var(--helpTextColor)]">
          {dateLabel}
        </span>
      ) : null}

      {requeueControl}

      {createError ? (
        <span className={ERROR_CLASS}>
          {translate('TranscodeActionFailed')}
        </span>
      ) : null}

      {job.error ? (
        <span className={ERROR_CLASS} title={job.error}>
          {job.error}
        </span>
      ) : null}
    </div>
  );
}

export default TranscodeHistoryRow;
