import React, { useCallback, useEffect, useState } from 'react';
import CheckInput from 'Components/Form/CheckInput';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import ProgressBar from 'Components/ProgressBar';
import { kinds, sizes } from 'Helpers/Props';
import { CheckInputChanged } from 'typings/inputs';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import { getRateControlLabel } from './transcodeStatus';
import TranscodeStatusBadge from './TranscodeStatusBadge';
import {
  TranscodeJob,
  useCancelTranscodeJob,
  useResolveTranscodeJob,
} from './useTranscodeJobs';

const ROW_CLASS =
  'flex flex-wrap items-start gap-x-[14px] gap-y-[8px] border-b border-[var(--borderColor)] px-[16px] py-[12px] last:border-b-0 hover:bg-[var(--tableRowHoverBackgroundColor)]';
const META_CLASS =
  'flex flex-wrap items-center gap-x-[6px] gap-y-[2px] text-[12px] leading-[1.5] text-[var(--helpTextColor)]';
const META_SEPARATOR_CLASS = 'text-[var(--helpTextColor)] opacity-50';
const ACTION_CLASS = 'flex shrink-0 flex-wrap items-center gap-[6px]';
const ERROR_CLASS =
  'mt-[6px] break-words text-[12px] leading-[1.5] text-[light-dark(var(--alertDangerColor),var(--dangerColor))]';

interface TranscodeJobRowProps {
  job: TranscodeJob;
  deviceName?: string;
  profileName?: string;
  isSelected?: boolean;
  isBulkRunning?: boolean;
  onSelect?(jobId: number, isSelected: boolean, shiftKey: boolean): void;
}

function TranscodeJobRow({
  job,
  deviceName,
  profileName,
  isSelected = false,
  isBulkRunning = false,
  onSelect,
}: TranscodeJobRowProps) {
  const { cancelJob, isCancelling, cancelError, resetCancelError } =
    useCancelTranscodeJob();
  const { resolveJob, isResolving, resolveError, resetResolveError } =
    useResolveTranscodeJob();

  // The jobs query polls every 3s; a transient action failure must not stay pinned to the row
  // forever. Clear it whenever the poll reports a new status/timestamp.
  useEffect(() => {
    resetCancelError();
    resetResolveError();
  }, [job.status, job.lastUpdatedAt, resetCancelError, resetResolveError]);

  const handleCancel = useCallback(
    () => cancelJob({ jobId: job.id }),
    [cancelJob, job.id]
  );
  const handleKeepBoth = useCallback(
    () => resolveJob({ jobId: job.id, action: 'KeepBoth' }),
    [resolveJob, job.id]
  );

  const [confirmAction, setConfirmAction] = useState<string | null>(null);
  const handleOverwritePress = useCallback(
    () => setConfirmAction('Overwrite'),
    []
  );
  const handleDiscardPress = useCallback(() => setConfirmAction('Discard'), []);
  const handleConfirmClose = useCallback(() => setConfirmAction(null), []);
  const handleConfirmAction = useCallback(() => {
    const action = confirmAction;

    setConfirmAction(null);

    if (action === 'Overwrite') {
      resolveJob({ jobId: job.id, action: 'Overwrite' });
    } else if (action === 'Discard') {
      resolveJob({ jobId: job.id, action: 'Discard' });
    }
  }, [confirmAction, resolveJob, job.id]);

  const handleSelectChange = useCallback(
    ({ value, shiftKey }: CheckInputChanged) => {
      onSelect?.(job.id, value, shiftKey);
    },
    [job.id, onSelect]
  );

  const sourceName = job.sourcePath.split(/[\\/]/).pop() || job.sourcePath;
  const planLabel =
    profileName ?? (job.maxHeight ? `${job.maxHeight}p` : undefined);
  const isActive = job.status === 'Queued' || job.status === 'Running';
  const isReview = job.status === 'AwaitingReview';
  const isRunning = job.status === 'Running';
  const isTransferring = job.status === 'Transferring';

  const metaItems = [
    planLabel,
    job.deviceId
      ? `${translate('TranscodeDevices')}: ${deviceName ?? job.deviceId}`
      : undefined,
    job.videoCodec ? job.videoCodec.toUpperCase() : undefined,
    job.rateControl ? getRateControlLabel(job.rateControl) : undefined,
    job.sourceSize
      ? `${formatBytes(job.sourceSize)}${
          job.outputSize ? ` → ${formatBytes(job.outputSize)}` : ''
        }`
      : undefined,
    (isRunning || isTransferring) && job.speed ? job.speed : undefined,
    isRunning && job.eta ? `${translate('Eta')} ${job.eta}` : undefined,
  ].filter((item): item is string => Boolean(item));

  const actionError = cancelError ?? resolveError;

  return (
    <div className={ROW_CLASS}>
      {isReview && onSelect ? (
        <div className="flex h-[22px] shrink-0 items-center">
          <CheckInput
            className="m-0!"
            containerClassName="relative flex flex-[0_0_auto] select-none"
            name={String(job.id)}
            ariaLabel={translate('SelectTranscodeJobNamed', {
              name: `${sourceName} (#${job.id})`,
            })}
            value={isSelected}
            onChange={handleSelectChange}
          />
        </div>
      ) : null}

      <div className="min-w-0 flex-1">
        <div className="flex flex-wrap items-center gap-x-[8px] gap-y-[4px]">
          <TranscodeStatusBadge status={job.status} />

          <span
            className="min-w-0 break-all text-[13px] font-semibold leading-[1.35] text-[var(--textColor)]"
            title={job.sourcePath}
          >
            {sourceName}
          </span>

          {isReview ? (
            <span className="text-[11px] tabular-nums text-[var(--textColor)] opacity-60">
              #{job.id}
            </span>
          ) : null}
        </div>

        {metaItems.length ? (
          <div className={`mt-[4px] ${META_CLASS}`}>
            {metaItems.map((item, index) => (
              <React.Fragment key={`${item}-${index}`}>
                {index > 0 ? (
                  <span className={META_SEPARATOR_CLASS}>·</span>
                ) : null}
                <span>{item}</span>
              </React.Fragment>
            ))}
          </div>
        ) : null}

        {isRunning || isTransferring ? (
          <div className="mt-[8px]">
            <ProgressBar
              ariaLabel={translate('TranscodeJobProgress', {
                name: sourceName,
              })}
              progress={job.progress}
              showText={true}
              size={sizes.SMALL}
            />
          </div>
        ) : null}

        {job.error ? <div className={ERROR_CLASS}>{job.error}</div> : null}

        {actionError ? (
          <div className={ERROR_CLASS}>
            {translate('TranscodeActionFailed')}
          </div>
        ) : null}

        {isReview && job.message ? (
          <div className="mt-[4px] break-words text-[12px] leading-[1.5] text-[var(--textColor)]">
            {job.message}
          </div>
        ) : null}
      </div>

      <div className={ACTION_CLASS}>
        {isActive || isTransferring ? (
          <SpinnerButton
            size={sizes.SMALL}
            kind={kinds.DANGER}
            isSpinning={isCancelling}
            onPress={handleCancel}
          >
            {isTransferring ? translate('CancelTransfer') : translate('Cancel')}
          </SpinnerButton>
        ) : null}

        {isReview ? (
          <>
            <SpinnerButton
              size={sizes.SMALL}
              kind={kinds.PRIMARY}
              isSpinning={isResolving}
              isDisabled={isBulkRunning}
              onPress={handleOverwritePress}
            >
              {translate('OverwriteOriginal')}
            </SpinnerButton>

            <SpinnerButton
              size={sizes.SMALL}
              isSpinning={isResolving}
              isDisabled={isBulkRunning}
              onPress={handleKeepBoth}
            >
              {translate('KeepBoth')}
            </SpinnerButton>

            <SpinnerButton
              size={sizes.SMALL}
              kind={kinds.DANGER}
              isSpinning={isResolving}
              isDisabled={isBulkRunning}
              onPress={handleDiscardPress}
            >
              {translate('Discard')}
            </SpinnerButton>
          </>
        ) : null}
      </div>

      <ConfirmModal
        isOpen={confirmAction !== null}
        kind={kinds.DANGER}
        title={
          confirmAction === 'Discard'
            ? translate('Discard')
            : translate('OverwriteOriginal')
        }
        message={
          confirmAction === 'Discard'
            ? translate('TranscodeDiscardConfirmMessage')
            : translate('TranscodeOverwriteConfirmMessage')
        }
        confirmLabel={
          confirmAction === 'Discard'
            ? translate('Discard')
            : translate('OverwriteOriginal')
        }
        isSpinning={isResolving}
        onConfirm={handleConfirmAction}
        onCancel={handleConfirmClose}
      />
    </div>
  );
}

export default TranscodeJobRow;
