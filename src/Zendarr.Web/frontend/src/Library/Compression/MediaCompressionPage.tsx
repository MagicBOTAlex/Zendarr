import classNames from 'classnames';
import React, {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import { hideMessage, showMessage } from 'App/messagesStore';
import Icon, { IconName } from 'Components/Icon';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import ProgressBar from 'Components/ProgressBar';
import { icons, kinds, sizes } from 'Helpers/Props';
import { ERROR, INFO, SUCCESS, WARNING } from 'Helpers/Props/messageTypes';
import {
  getDeviceLabel,
  useMediaCompressionCapabilities,
} from 'Settings/Compression/useMediaCompressionCapabilities';
import { useTranscodeProfiles } from 'Settings/Compression/useTranscodeProfiles';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import getToggledRange from 'Utilities/Table/getToggledRange';
import { buildJobGroups, flattenJobGroups, JobSeriesGroup } from './jobGroups';
import TranscodeHistoryRow from './TranscodeHistoryRow';
import TranscodeJobRow from './TranscodeJobRow';
import {
  TRANSCODE_TONE_BG_CLASSES,
  TRANSCODE_TONE_TEXT_CLASSES,
  TranscodeTone,
} from './transcodeStatus';
import {
  useBulkResolveTranscodeJobs,
  useClearTranscodeHistory,
  useCreateTranscodeJobs,
  useTranscodeJobs,
} from './useTranscodeJobs';

const BULK_MESSAGE_ID = -1000;
const REQUEUE_MESSAGE_ID = -1014;
const CLEAR_HISTORY_MESSAGE_ID = -1015;
const BULK_DETAIL_MESSAGE_ID = -1016;

// A bulk resolve is only finished once its jobs reach a terminal status (the copies can run for a
// long time after the batch is claimed), so progress is measured here rather than by review
// membership.
const TERMINAL_STATUSES = ['Completed', 'Failed', 'Cancelled', 'Skipped'];

interface StatCardProps {
  icon: IconName;
  tone: TranscodeTone;
  value: React.ReactNode;
  label: string;
}

function StatCard({ icon, tone, value, label }: StatCardProps) {
  return (
    <div className="flex min-w-0 items-center gap-[10px] rounded-[6px] border border-[var(--defaultBorderColor)] bg-[var(--cardBackgroundColor)] px-[12px] py-[10px] shadow-[0_1px_2px_var(--cardShadowColor)]">
      <span
        className={classNames(
          'flex h-[32px] w-[32px] shrink-0 items-center justify-center rounded-[6px]',
          TRANSCODE_TONE_BG_CLASSES[tone],
          TRANSCODE_TONE_TEXT_CLASSES[tone]
        )}
      >
        <Icon name={icon} size={15} aria-hidden={true} />
      </span>

      <div className="min-w-0">
        <div className="truncate text-[16px] font-semibold leading-none tabular-nums text-[var(--textColor)]">
          {value}
        </div>
        <div className="mt-[4px] truncate text-[11px] leading-none text-[var(--helpTextColor)]">
          {label}
        </div>
      </div>
    </div>
  );
}

interface SectionCardProps {
  icon: IconName;
  title: string;
  count: number;
  actions?: React.ReactNode;
  children: React.ReactNode;
}

function SectionCard({
  icon,
  title,
  count,
  actions,
  children,
}: SectionCardProps) {
  return (
    <section className="overflow-hidden rounded-[6px] border border-[var(--defaultBorderColor)] bg-[var(--cardBackgroundColor)] shadow-[0_1px_2px_var(--cardShadowColor)]">
      <div className="flex flex-wrap items-center justify-between gap-[8px] border-b border-[var(--defaultBorderColor)] bg-[var(--pageFooterBackground)] px-[14px] py-[8px]">
        <div className="flex min-w-0 items-center gap-[7px]">
          <Icon
            name={icon}
            size={13}
            className="shrink-0 text-[var(--textColor)]"
            aria-hidden={true}
          />
          <h2 className="truncate text-[13px] font-semibold text-[var(--textColor)]">
            {title}
          </h2>
          <span className="rounded-full bg-[var(--cardBackgroundColor)] px-[7px] py-[1px] text-[11px] font-semibold tabular-nums text-[var(--helpTextColor)]">
            {count}
          </span>
        </div>

        {actions ? (
          <div className="flex flex-wrap items-center gap-[6px]">{actions}</div>
        ) : null}
      </div>

      {children}
    </section>
  );
}

function SubHeader({ label, count }: { label: string; count: number }) {
  return (
    <div className="flex items-center justify-between gap-[8px] border-b border-[var(--borderColor)] bg-[var(--cardBackgroundColor)] px-[16px] py-[5px]">
      <span className="text-[11px] font-semibold uppercase tracking-[0.06em] text-[var(--helpTextColor)]">
        {label}
      </span>
      <span className="text-[11px] tabular-nums text-[var(--helpTextColor)]">
        {count}
      </span>
    </div>
  );
}

function GroupHeader({
  title,
  count,
  sticky = false,
}: {
  title: string;
  count: number;
  sticky?: boolean;
}) {
  return (
    <div
      className={classNames(
        'flex items-center justify-between gap-[8px] border-b border-[var(--borderColor)] bg-[var(--tableRowHoverBackgroundColor)] px-[16px] py-[6px]',
        sticky && 'sticky top-0 z-10'
      )}
    >
      <h3 className="truncate text-[12px] font-semibold text-[var(--textColor)]">
        {title}
      </h3>
      <span className="shrink-0 text-[11px] tabular-nums text-[var(--helpTextColor)]">
        {count}
      </span>
    </div>
  );
}

function SeasonHeader({ label }: { label: string }) {
  return (
    <div className="border-b border-[var(--borderColor)] bg-[var(--cardBackgroundColor)] px-[16px] py-[4px]">
      <h4 className="truncate text-[11px] font-medium text-[var(--helpTextColor)]">
        {label}
      </h4>
    </div>
  );
}

function LoadingSkeleton() {
  return (
    <div className="space-y-[12px]" aria-hidden={true}>
      <div className="animate-pulse rounded-[6px] border border-[var(--defaultBorderColor)] bg-[var(--cardBackgroundColor)] p-[16px]">
        <div className="h-[13px] w-[120px] rounded bg-[var(--pageFooterBackground)]" />
        <div className="mt-[14px] space-y-[8px]">
          <div className="h-[42px] rounded bg-[var(--pageFooterBackground)]" />
          <div className="h-[42px] rounded bg-[var(--pageFooterBackground)]" />
          <div className="h-[42px] rounded bg-[var(--pageFooterBackground)]" />
        </div>
      </div>
    </div>
  );
}

function EmptyState() {
  return (
    <div className="flex flex-col items-center justify-center rounded-[6px] border border-[var(--defaultBorderColor)] bg-[var(--cardBackgroundColor)] px-[24px] py-[64px] text-center shadow-[0_1px_2px_var(--cardShadowColor)]">
      <span className="flex h-[44px] w-[44px] items-center justify-center rounded-full bg-[var(--pageFooterBackground)] text-[var(--textColor)]">
        <Icon name={icons.MOVIE_FILE} size={20} aria-hidden={true} />
      </span>
      <h2 className="mt-[14px] text-[15px] font-semibold text-[var(--textColor)]">
        {translate('CompressionNoJobs')}
      </h2>
      <p className="mt-[4px] max-w-[46ch] text-[12px] leading-[1.6] text-[var(--helpTextColor)]">
        {translate('CompressionNoJobsDescription')}
      </p>
    </div>
  );
}

function MediaCompressionPage() {
  const { isFetching, isFetched, error, jobs, refetch } = useTranscodeJobs();
  const { capabilities } = useMediaCompressionCapabilities();
  const { profiles } = useTranscodeProfiles();
  const { bulkResolveJobs } = useBulkResolveTranscodeJobs();
  const { clearHistory, isClearingHistory } = useClearTranscodeHistory();
  const { createJobs, isCreating } = useCreateTranscodeJobs();

  const [isRefreshing, setIsRefreshing] = useState(false);

  const handleRefresh = useCallback(() => {
    setIsRefreshing(true);
    Promise.resolve(refetch()).finally(() => setIsRefreshing(false));
  }, [refetch]);

  const active = useMemo(
    () =>
      jobs
        .filter((job) => job.status === 'Queued' || job.status === 'Running')
        .sort((a, b) => a.id - b.id),
    [jobs]
  );

  const transferring = useMemo(
    () =>
      jobs
        .filter((job) => job.status === 'Transferring')
        .sort((a, b) => a.id - b.id),
    [jobs]
  );

  const reviewGroups = useMemo(
    () => buildJobGroups(jobs.filter((job) => job.status === 'AwaitingReview')),
    [jobs]
  );

  // Flattened in the same order the groups render so shift-select ranges stay contiguous.
  const review = useMemo(() => flattenJobGroups(reviewGroups), [reviewGroups]);

  const activeGroups = useMemo(() => buildJobGroups(active), [active]);
  const transferringGroups = useMemo(
    () => buildJobGroups(transferring),
    [transferring]
  );

  const history = useMemo(
    () =>
      jobs.filter(
        (job) =>
          job.status !== 'Queued' &&
          job.status !== 'Running' &&
          job.status !== 'Transferring' &&
          job.status !== 'AwaitingReview'
      ),
    [jobs]
  );

  const historyGroups = useMemo(() => buildJobGroups(history), [history]);

  const requeueJobIds = useMemo(
    () =>
      jobs
        .filter(
          (job) =>
            job.status === 'Failed' &&
            job.requeuedJobId == null &&
            (job.episodeFileId != null || job.movieFileId != null)
        )
        .map((job) => job.id),
    [jobs]
  );

  const requeueCount = requeueJobIds.length;

  const [selectedIds, setSelectedIds] = useState<number[]>([]);
  const [bulk, setBulk] = useState<{ ids: number[]; total: number } | null>(
    null
  );
  const [isClearHistoryModalOpen, setIsClearHistoryModalOpen] = useState(false);
  const [pendingBulkAction, setPendingBulkAction] = useState<string | null>(
    null
  );
  const lastSelected = useRef<number | null>(null);
  const selectAllRef = useRef<HTMLInputElement>(null);

  const deviceNames = useMemo(() => {
    const names: Record<string, string> = {};

    (capabilities.devices ?? []).forEach((device) => {
      names[device.deviceId] = getDeviceLabel(device);
    });

    return names;
  }, [capabilities.devices]);

  const profileNames = useMemo(() => {
    const names: Record<number, string> = {};

    profiles.forEach((profile) => {
      names[profile.id] = profile.name;
    });

    return names;
  }, [profiles]);

  const selectedSet = useMemo(() => new Set(selectedIds), [selectedIds]);

  const reviewKey = review.map((job) => job.id).join(',');
  const allSelected =
    review.length > 0 && review.every((job) => selectedSet.has(job.id));
  const someSelected = selectedIds.length > 0 && !allSelected;

  const terminalIds = useMemo(
    () =>
      new Set(
        jobs
          .filter((job) => TERMINAL_STATUSES.includes(job.status))
          .map((job) => job.id)
      ),
    [jobs]
  );

  const bulkDone = bulk
    ? bulk.ids.filter((id) => terminalIds.has(id)).length
    : 0;
  const bulkProgress = bulk && bulk.total ? (bulkDone * 100) / bulk.total : 0;

  const savedBytes = useMemo(
    () =>
      jobs.reduce((total, job) => {
        if (job.status !== 'Completed' || !job.sourceSize || !job.outputSize) {
          return total;
        }

        return total + Math.max(job.sourceSize - job.outputSize, 0);
      }, 0),
    [jobs]
  );

  const activeCount = active.length + transferring.length;
  const reviewCount = review.length;
  const failedCount = history.filter((job) => job.status === 'Failed').length;
  const hasLive = activeCount > 0;
  const hasReview = reviewCount > 0;
  const hasHistory = history.length > 0;
  const isInitialLoading = isFetching && !isFetched;
  const showEmpty = !isInitialLoading && !error && isFetched && !jobs.length;

  const stats: StatCardProps[] = [
    {
      icon: icons.ACTIONS,
      tone: activeCount ? 'primary' : 'neutral',
      value: activeCount,
      label: translate('CompressionActive'),
    },
    {
      icon: icons.VIEW,
      tone: reviewCount ? 'warning' : 'neutral',
      value: reviewCount,
      label: translate('CompressionReview'),
    },
    {
      icon: icons.DANGER,
      tone: failedCount ? 'danger' : 'neutral',
      value: failedCount,
      label: translate('Failed'),
    },
    {
      icon: icons.DRIVE,
      tone: savedBytes ? 'success' : 'neutral',
      value: formatBytes(savedBytes),
      label: translate('TranscodeSpaceSaved'),
    },
  ];

  const getDeviceName = useCallback(
    (deviceId?: string) => (deviceId ? deviceNames[deviceId] : undefined),
    [deviceNames]
  );

  const getProfileName = useCallback(
    (profileId?: number) => (profileId ? profileNames[profileId] : undefined),
    [profileNames]
  );

  const handleSelect = useCallback(
    (jobId: number, isSelected: boolean, shiftKey: boolean) => {
      // Capture the anchor before scheduling the update. The updater below runs later (React
      // invokes it while re-rendering), so reading `lastSelected.current` inside it would see the
      // id we just clicked and collapse the shift range to a single row.
      const anchor = lastSelected.current;
      lastSelected.current = jobId;

      setSelectedIds((current) => {
        const next = new Set(current);

        if (shiftKey && anchor != null) {
          const { lower, upper } = getToggledRange(review, jobId, anchor);
          const start = Math.max(0, lower);
          const end = Math.min(upper, review.length);

          for (let index = start; index < end; index++) {
            if (isSelected) {
              next.add(review[index].id);
            } else {
              next.delete(review[index].id);
            }
          }
        }

        if (isSelected) {
          next.add(jobId);
        } else {
          next.delete(jobId);
        }

        return Array.from(next);
      });
    },
    [review]
  );

  const handleSelectAll = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      lastSelected.current = null;
      setSelectedIds(event.target.checked ? review.map((job) => job.id) : []);
    },
    [review]
  );

  const renderJobGroups = useCallback(
    (groups: JobSeriesGroup[], selectable = false) => (
      <>
        {groups.map((group) => (
          <React.Fragment key={group.key}>
            <GroupHeader
              title={group.title}
              count={group.seasons.reduce(
                (total, season) => total + season.jobs.length,
                0
              )}
            />

            {group.seasons.map((season) => (
              <React.Fragment key={season.key}>
                {season.label ? <SeasonHeader label={season.label} /> : null}

                {season.jobs.map((job) => (
                  <TranscodeJobRow
                    key={job.id}
                    job={job}
                    deviceName={getDeviceName(job.deviceId)}
                    profileName={getProfileName(job.profileId)}
                    isSelected={selectable ? selectedSet.has(job.id) : false}
                    isBulkRunning={selectable ? !!bulk : false}
                    onSelect={selectable ? handleSelect : undefined}
                  />
                ))}
              </React.Fragment>
            ))}
          </React.Fragment>
        ))}
      </>
    ),
    [bulk, getDeviceName, getProfileName, handleSelect, selectedSet]
  );

  const renderHistoryGroups = useCallback(
    (groups: JobSeriesGroup[]) => (
      <>
        {groups.map((group) => (
          <React.Fragment key={group.key}>
            <GroupHeader
              title={group.title}
              count={group.seasons.reduce(
                (total, season) => total + season.jobs.length,
                0
              )}
              sticky={true}
            />

            {group.seasons.map((season) => (
              <React.Fragment key={season.key}>
                {season.label ? <SeasonHeader label={season.label} /> : null}

                {season.jobs.map((job) => (
                  <TranscodeHistoryRow
                    key={job.id}
                    job={job}
                    deviceName={getDeviceName(job.deviceId)}
                    profileName={getProfileName(job.profileId)}
                  />
                ))}
              </React.Fragment>
            ))}
          </React.Fragment>
        ))}
      </>
    ),
    [getDeviceName, getProfileName]
  );

  useEffect(() => {
    const ids = new Set(reviewKey ? reviewKey.split(',').map(Number) : []);

    setSelectedIds((current) => current.filter((id) => ids.has(id)));

    if (lastSelected.current != null && !ids.has(lastSelected.current)) {
      lastSelected.current = null;
    }
  }, [reviewKey]);

  useEffect(() => {
    if (selectAllRef.current) {
      selectAllRef.current.indeterminate = someSelected;
    }
  }, [someSelected]);

  useEffect(() => {
    if (!bulk) {
      return;
    }

    showMessage({
      id: BULK_MESSAGE_ID,
      name: translate('Transcoding'),
      message: translate('BulkResolveProgress', {
        done: Math.min(bulkDone, bulk.total),
        total: bulk.total,
      }),
      type: INFO,
      hideAfter: 0,
    });
  }, [bulk, bulkDone]);

  // Navigating away before the request settles would otherwise leave the persistent
  // (hideAfter: 0) progress message in the sidebar forever.
  useEffect(() => {
    return () => hideMessage({ id: BULK_MESSAGE_ID });
  }, []);

  const startBulkResolve = useCallback(
    (action: string) => {
      const ids = [...selectedIds];

      if (!ids.length) {
        return;
      }

      lastSelected.current = null;
      setSelectedIds([]);
      setBulk({ ids, total: ids.length });

      showMessage({
        id: BULK_MESSAGE_ID,
        name: translate('Transcoding'),
        message: translate('BulkResolveStarted', { count: ids.length }),
        type: INFO,
        hideAfter: 0,
      });

      bulkResolveJobs(
        { action, jobIds: ids },
        {
          onSuccess: (result) => {
            setBulk(null);

            const resolvedCount = result?.resolved?.length ?? 0;
            const failures = result?.failed ?? [];

            if (failures.length > 0) {
              showMessage({
                id: BULK_MESSAGE_ID,
                name: translate('Transcoding'),
                message: translate('BulkResolvePartial', {
                  resolved: resolvedCount,
                  total: ids.length,
                  failed: failures.length,
                }),
                type: WARNING,
                hideAfter: 10,
              });

              // Surface the first per-id reason (V1): the response now carries it.
              showMessage({
                id: BULK_DETAIL_MESSAGE_ID,
                name: translate('Transcoding'),
                message: failures[0].error,
                type: ERROR,
                hideAfter: 15,
              });

              return;
            }

            showMessage({
              id: BULK_MESSAGE_ID,
              name: translate('Transcoding'),
              message: translate('BulkResolveComplete', { count: ids.length }),
              type: SUCCESS,
              hideAfter: 4,
            });
          },
          onError: () => {
            setBulk(null);
            showMessage({
              id: BULK_MESSAGE_ID,
              name: translate('Transcoding'),
              message: translate('BulkResolveFailed'),
              type: ERROR,
              hideAfter: 10,
            });
          },
        }
      );
    },
    [bulkResolveJobs, selectedIds]
  );

  const handleOverwriteSelected = useCallback(() => {
    setPendingBulkAction('Overwrite');
  }, []);

  const handleKeepBothSelected = useCallback(
    () => startBulkResolve('KeepBoth'),
    [startBulkResolve]
  );

  const handleDiscardSelected = useCallback(() => {
    setPendingBulkAction('Discard');
  }, []);

  const handleBulkResolveConfirmed = useCallback(() => {
    const action = pendingBulkAction;

    setPendingBulkAction(null);

    if (action) {
      startBulkResolve(action);
    }
  }, [pendingBulkAction, startBulkResolve]);

  const handleBulkResolveCancelled = useCallback(() => {
    setPendingBulkAction(null);
  }, []);

  const bulkConfirmMessage =
    pendingBulkAction === 'Discard'
      ? translate('TranscodeBulkDiscardConfirmMessage', {
          count: selectedIds.length,
        })
      : translate('TranscodeBulkOverwriteConfirmMessage', {
          count: selectedIds.length,
        });

  const handleClearHistoryPress = useCallback(() => {
    setIsClearHistoryModalOpen(true);
  }, []);

  const handleClearHistoryModalClose = useCallback(() => {
    setIsClearHistoryModalOpen(false);
  }, []);

  const handleClearHistoryConfirmed = useCallback(() => {
    clearHistory(undefined, {
      onSuccess: ({ clearedCount }) => {
        setIsClearHistoryModalOpen(false);

        showMessage({
          id: CLEAR_HISTORY_MESSAGE_ID,
          name: translate('Transcoding'),
          message: translate('TranscodeHistoryCleared', {
            count: clearedCount,
          }),
          type: SUCCESS,
          hideAfter: 4,
        });
      },
      onError: () => {
        setIsClearHistoryModalOpen(false);

        showMessage({
          id: CLEAR_HISTORY_MESSAGE_ID,
          name: translate('Transcoding'),
          message: translate('TranscodeHistoryClearFailed'),
          type: ERROR,
          hideAfter: 10,
        });
      },
    });
  }, [clearHistory]);

  const handleRequeueFailed = useCallback(() => {
    if (!requeueCount) {
      return;
    }

    createJobs(
      {
        force: true,
        requeueJobIds,
      },
      {
        onSuccess: (response) => {
          const queued = response?.jobs?.length ?? 0;
          const skipped = response?.skippedCount ?? 0;

          showMessage({
            id: REQUEUE_MESSAGE_ID,
            name: translate('Transcoding'),
            message: skipped
              ? translate('RequeueFailedPartial', {
                  queued,
                  total: requeueCount,
                })
              : translate('RequeueFailedComplete', { count: queued }),
            type: queued ? SUCCESS : WARNING,
            hideAfter: queued ? 4 : 10,
          });
        },
        onError: () => {
          showMessage({
            id: REQUEUE_MESSAGE_ID,
            name: translate('Transcoding'),
            message: translate('RequeueFailedError'),
            type: ERROR,
            hideAfter: 10,
          });
        },
      }
    );
  }, [createJobs, requeueCount, requeueJobIds]);

  const bulkProgressLabel = bulk
    ? translate('BulkResolveProgress', {
        done: Math.min(bulkDone, bulk.total),
        total: bulk.total,
      })
    : '';

  return (
    <PageContent title={translate('Transcoding')}>
      <header className="border-b border-[var(--defaultBorderColor)] px-[20px] pb-[14px] pt-[16px] max-[768px]:px-[10px]">
        <div className="flex flex-wrap items-start justify-between gap-[12px]">
          <div className="flex min-w-0 items-center gap-[10px]">
            <span className="flex h-[38px] w-[38px] shrink-0 items-center justify-center rounded-[6px] bg-[color-mix(in_srgb,var(--themeBlue)_16%,transparent)] text-[light-dark(var(--alertInfoColor),var(--themeBlue))]">
              <Icon name={icons.ACTIONS} size={18} aria-hidden={true} />
            </span>

            <div className="min-w-0">
              <h1 className="truncate text-[20px] font-semibold leading-tight text-[var(--textColor)]">
                {translate('Transcoding')}
              </h1>
              <p className="mt-[2px] truncate text-[12px] text-[var(--helpTextColor)]">
                {translate('TranscodingDescription')}
              </p>
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-[6px]">
            <SpinnerButton
              size={sizes.SMALL}
              isSpinning={isRefreshing}
              onPress={handleRefresh}
            >
              {translate('Refresh')}
            </SpinnerButton>
          </div>
        </div>

        <div className="mt-[12px] grid grid-cols-2 gap-[8px] min-[900px]:grid-cols-4">
          {stats.map((stat) => (
            <StatCard key={stat.label} {...stat} />
          ))}
        </div>
      </header>

      <PageContentBody innerClassName="p-[20px] max-[768px]:p-[10px]">
        <div className="flex flex-col gap-[12px]">
          {isInitialLoading ? <LoadingSkeleton /> : null}

          {error ? (
            <div
              role="alert"
              className="flex flex-wrap items-start gap-[12px] rounded-[6px] border border-[var(--alertDangerBorderColor)] bg-[var(--alertDangerBackgroundColor)] px-[14px] py-[12px]"
            >
              <span className="flex h-[30px] w-[30px] shrink-0 items-center justify-center rounded-full bg-[color-mix(in_srgb,var(--dangerColor)_16%,transparent)] text-[light-dark(var(--alertDangerColor),var(--dangerColor))]">
                <Icon name={icons.DANGER} size={15} aria-hidden={true} />
              </span>

              <div className="min-w-0 flex-1">
                <div className="text-[13px] font-semibold text-[var(--textColor)]">
                  {translate('CompressionJobsLoadError')}
                </div>
                <div className="mt-[2px] text-[12px] leading-[1.5] text-[var(--helpTextColor)]">
                  {translate('TranscodeLoadErrorHint')}
                </div>
              </div>

              <SpinnerButton
                size={sizes.SMALL}
                isSpinning={isRefreshing}
                onPress={handleRefresh}
              >
                {translate('TranscodeJobsRetry')}
              </SpinnerButton>
            </div>
          ) : null}

          {showEmpty ? <EmptyState /> : null}

          {!isInitialLoading && jobs.length > 0 ? (
            <>
              {hasLive ? (
                <SectionCard
                  icon={icons.ACTIONS}
                  title={translate('CompressionLive')}
                  count={activeCount}
                >
                  {active.length ? (
                    <>
                      <SubHeader
                        label={translate('CompressionEncoding')}
                        count={active.length}
                      />
                      {renderJobGroups(activeGroups)}
                    </>
                  ) : null}

                  {transferring.length ? (
                    <>
                      <SubHeader
                        label={translate('Transferring')}
                        count={transferring.length}
                      />
                      {renderJobGroups(transferringGroups)}
                    </>
                  ) : null}
                </SectionCard>
              ) : null}

              {hasReview ? (
                <SectionCard
                  icon={icons.VIEW}
                  title={translate('CompressionReview')}
                  count={reviewCount}
                >
                  <div className="flex flex-wrap items-center gap-x-[12px] gap-y-[8px] border-b border-[var(--borderColor)] bg-[color-mix(in_srgb,var(--warningColor)_10%,transparent)] px-[14px] py-[9px]">
                    <label className="flex items-center gap-[6px] text-[12px] font-medium text-[var(--textColor)]">
                      <input
                        ref={selectAllRef}
                        type="checkbox"
                        checked={allSelected}
                        onChange={handleSelectAll}
                      />
                      {translate('SelectAll')}
                    </label>

                    <span className="text-[12px] text-[var(--helpTextColor)]">
                      {selectedIds.length
                        ? translate('CountSelectedFiles', {
                            selectedCount: selectedIds.length,
                          })
                        : translate('CompressionReviewHint')}
                    </span>

                    <div className="ml-auto flex flex-wrap items-center gap-[6px]">
                      <SpinnerButton
                        size={sizes.SMALL}
                        kind={kinds.PRIMARY}
                        isSpinning={!!bulk}
                        isDisabled={!selectedIds.length}
                        onPress={handleOverwriteSelected}
                      >
                        {translate('OverwriteSelected')}
                      </SpinnerButton>

                      <SpinnerButton
                        size={sizes.SMALL}
                        isSpinning={!!bulk}
                        isDisabled={!selectedIds.length}
                        onPress={handleKeepBothSelected}
                      >
                        {translate('KeepBothSelected')}
                      </SpinnerButton>

                      <SpinnerButton
                        size={sizes.SMALL}
                        kind={kinds.DANGER}
                        isSpinning={!!bulk}
                        isDisabled={!selectedIds.length}
                        onPress={handleDiscardSelected}
                      >
                        {translate('DiscardSelected')}
                      </SpinnerButton>
                    </div>
                  </div>

                  {bulk ? (
                    <div className="border-b border-[var(--borderColor)] px-[14px] py-[10px]">
                      <div className="mb-[6px] flex items-center justify-between gap-[8px] text-[12px] text-[var(--helpTextColor)]">
                        <span>{bulkProgressLabel}</span>
                        <span className="tabular-nums">
                          {Math.round(bulkProgress)}%
                        </span>
                      </div>
                      <ProgressBar
                        ariaLabel={bulkProgressLabel}
                        progress={bulkProgress}
                        showText={false}
                        size={sizes.SMALL}
                      />
                    </div>
                  ) : null}

                  {renderJobGroups(reviewGroups, true)}
                </SectionCard>
              ) : null}

              {hasHistory ? (
                <SectionCard
                  icon={icons.HISTORY}
                  title={translate('History')}
                  count={history.length}
                  actions={
                    <>
                      {requeueCount ? (
                        <SpinnerButton
                          size={sizes.SMALL}
                          isSpinning={isCreating}
                          isDisabled={isCreating}
                          onPress={handleRequeueFailed}
                        >
                          {translate('RequeueAllFailed')}
                        </SpinnerButton>
                      ) : null}

                      <SpinnerButton
                        size={sizes.SMALL}
                        kind={kinds.DANGER}
                        isSpinning={isClearingHistory}
                        onPress={handleClearHistoryPress}
                      >
                        {translate('ClearHistory')}
                      </SpinnerButton>
                    </>
                  }
                >
                  <div className="max-h-[420px] overflow-y-auto">
                    {renderHistoryGroups(historyGroups)}
                  </div>
                </SectionCard>
              ) : null}
            </>
          ) : null}
        </div>
      </PageContentBody>

      <ConfirmModal
        isOpen={isClearHistoryModalOpen}
        kind={kinds.DANGER}
        title={translate('ClearHistory')}
        message={translate('ClearHistoryConfirm', {
          count: history.length,
        })}
        confirmLabel={translate('ClearHistory')}
        isSpinning={isClearingHistory}
        onConfirm={handleClearHistoryConfirmed}
        onCancel={handleClearHistoryModalClose}
      />

      <ConfirmModal
        isOpen={pendingBulkAction !== null}
        kind={kinds.DANGER}
        title={translate('CompressionReview')}
        message={bulkConfirmMessage}
        confirmLabel={
          pendingBulkAction === 'Discard'
            ? translate('Discard')
            : translate('OverwriteOriginal')
        }
        onConfirm={handleBulkResolveConfirmed}
        onCancel={handleBulkResolveCancelled}
      />
    </PageContent>
  );
}

export default MediaCompressionPage;
