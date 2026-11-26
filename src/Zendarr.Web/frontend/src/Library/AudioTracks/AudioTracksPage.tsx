import React, { useCallback, useEffect, useMemo, useState } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { kinds } from 'Helpers/Props';
import useMovies from 'Movies/useMovies';
import formatBytes from 'Utilities/Number/formatBytes';
import translate from 'Utilities/String/translate';
import {
  AudioTrack,
  AudioTrackMergeResult,
  AudioTrackReorderResult,
  useAudioTracks,
  useMergeAudioTracks,
  useReorderAudioTracks,
} from './useAudioTracks';

const PAGE_CLASS = 'p-[12px]';
const TOOLBAR_CLASS = 'flex flex-wrap items-end gap-[12px] mb-[12px]';
const FIELD_CLASS = 'flex flex-col gap-[4px] min-w-[160px]';
const LABEL_CLASS = 'text-[12px] font-semibold text-[var(--textColor)]';
const INPUT_CLASS =
  'min-w-[160px] py-[6px] px-[8px] text-[13px] text-[var(--textColor)] bg-[var(--inputBackgroundColor,transparent)] border border-solid border-[var(--defaultBorderColor)] rounded-[3px]';
const CHECKBOX_LABEL_CLASS =
  'flex items-center gap-[6px] text-[13px] text-[var(--textColor)]';
const TABLE_CLASS =
  'w-full text-[13px] border-collapse [&_th]:p-[8px] [&_th]:text-left [&_th]:font-semibold [&_th]:border-b [&_th]:border-b-solid [&_th]:border-b-[color-mix(in_srgb,var(--color-base-200),transparent_50%)] [&_td]:p-[8px] [&_td]:align-top [&_td]:border-b [&_td]:border-b-solid [&_td]:border-b-[color-mix(in_srgb,var(--color-base-200),transparent_50%)]';
const STATE_CLASS = 'p-[24px] text-center text-[var(--helpTextColor)]';
const ERROR_CLASS = 'text-[var(--dangerColor)]';
const SUCCESS_CLASS = 'text-[var(--successColor)]';
const META_CLASS = 'text-[12px] text-[var(--helpTextColor)]';
const RESULT_CLASS =
  'mt-[12px] p-[12px] border border-solid border-[var(--defaultBorderColor)] rounded-[4px] bg-[var(--cardBackgroundColor)]';
const COMMAND_CLASS =
  'mt-[8px] max-h-[120px] overflow-auto whitespace-pre-wrap break-all font-mono text-[11px] text-[var(--helpTextColor)]';

const ORDER_OPTIONS = [
  { value: 'language', label: 'Language' },
  { value: 'name', label: 'Name' },
  { value: 'bitrate', label: 'Bitrate' },
  { value: 'source', label: 'Original order' },
];

function parseLanguages(value: string): string[] {
  return value
    .split(',')
    .map((language) => language.trim())
    .filter(Boolean);
}

function formatBitrate(bitrate: number): string {
  if (!bitrate) {
    return '-';
  }

  return `${Math.round(bitrate / 1000)} kbps`;
}

function formatTrackLabel(track: AudioTrack): string {
  const language = track.languageName || track.language || 'Unknown';
  const title = track.title || track.displayName || '';

  return title ? `${language} - ${title}` : language;
}

function TrackTable({ tracks }: { tracks: AudioTrack[] }) {
  if (!tracks.length) {
    return <div className={STATE_CLASS}>No audio tracks found.</div>;
  }

  return (
    <table className={TABLE_CLASS}>
      <thead>
        <tr>
          <th>#</th>
          <th>Language</th>
          <th>Title</th>
          <th>Codec</th>
          <th>Channels</th>
          <th>Bitrate</th>
          <th>Default</th>
        </tr>
      </thead>
      <tbody>
        {tracks.map((track) => (
          <tr key={track.audioIndex}>
            <td>{track.audioIndex}</td>
            <td>{track.languageName || track.language || '-'}</td>
            <td>{track.title || track.displayName || '-'}</td>
            <td>{track.codec || '-'}</td>
            <td>{track.channels || '-'}</td>
            <td>{formatBitrate(track.bitrate)}</td>
            <td>{track.isDefault ? 'Yes' : ''}</td>
          </tr>
        ))}
      </tbody>
    </table>
  );
}

function ReorderResult({ result }: { result: AudioTrackReorderResult }) {
  const statusClass = result.success ? SUCCESS_CLASS : ERROR_CLASS;
  let statusText = result.error || 'Reorder failed.';

  if (result.success) {
    statusText = result.executed
      ? 'Reorder completed.'
      : 'Reorder preview completed.';
  }

  return (
    <div className={RESULT_CLASS}>
      <div className={statusClass}>{statusText}</div>

      {result.outputPath ? (
        <div className={META_CLASS}>Output: {result.outputPath}</div>
      ) : null}

      {result.executed ? (
        <div className={META_CLASS}>
          Wrote {formatBytes(result.outputSize)}
          {result.replacedOriginal ? ' (original replaced)' : ''}
        </div>
      ) : null}

      {result.tracks?.length ? (
        <div className="mt-[12px]">
          <TrackTable tracks={result.tracks} />
        </div>
      ) : null}

      {result.command ? (
        <pre className={COMMAND_CLASS}>{result.command}</pre>
      ) : null}
    </div>
  );
}

function MergeResult({ result }: { result: AudioTrackMergeResult }) {
  const statusClass = result.success ? SUCCESS_CLASS : ERROR_CLASS;
  let statusText = result.error || 'Merge failed.';

  if (result.success) {
    statusText = result.executed
      ? 'Merge completed.'
      : 'Merge preview completed.';
  }

  return (
    <div className={RESULT_CLASS}>
      <div className={statusClass}>{statusText}</div>

      {result.outputPath ? (
        <div className={META_CLASS}>Output: {result.outputPath}</div>
      ) : null}

      {result.executed ? (
        <div className={META_CLASS}>
          Wrote {formatBytes(result.outputSize)}
          {result.replacedOriginal ? ' (original replaced)' : ''}
        </div>
      ) : null}

      {result.matches?.length ? (
        <div className="mt-[12px]">
          <table className={TABLE_CLASS}>
            <thead>
              <tr>
                <th>File</th>
                <th>Language</th>
                <th>Matched</th>
                <th>Score</th>
                <th>Offset</th>
                <th>Samples</th>
              </tr>
            </thead>
            <tbody>
              {result.matches.map((match) => (
                <tr key={`${match.movieFileId}-${match.audioIndex}`}>
                  <td>{match.path}</td>
                  <td>{match.language || '-'}</td>
                  <td>{match.matched ? 'Yes' : 'No'}</td>
                  <td>{match.score ? match.score.toFixed(2) : '-'}</td>
                  <td>
                    {match.offsetSeconds
                      ? `${match.offsetSeconds.toFixed(3)}s`
                      : '-'}
                  </td>
                  <td>
                    {match.samples?.length
                      ? `${match.samples.length} checked`
                      : '-'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : null}

      {result.tracks?.length ? (
        <div className="mt-[12px]">
          <TrackTable tracks={result.tracks} />
        </div>
      ) : null}

      {result.command ? (
        <pre className={COMMAND_CLASS}>{result.command}</pre>
      ) : null}
    </div>
  );
}

function AudioTracksPage() {
  const {
    data: movies,
    isFetching: isMoviesFetching,
    error: moviesError,
  } = useMovies();

  const moviesWithFiles = useMemo(
    () => movies.filter((movie) => movie.movieFileId > 0),
    [movies]
  );

  const [selectedMovieFileId, setSelectedMovieFileId] = useState<
    number | undefined
  >(undefined);
  const [preferredLanguages, setPreferredLanguages] = useState('eng');
  const [orderBy, setOrderBy] = useState('language');
  const [descending, setDescending] = useState(false);
  const [defaultAudioIndex, setDefaultAudioIndex] = useState('');
  const [outputPath, setOutputPath] = useState('');
  const [replaceOriginal, setReplaceOriginal] = useState(false);
  const [dryRun, setDryRun] = useState(false);

  const [baseMovieFileId, setBaseMovieFileId] = useState<number | undefined>(
    undefined
  );
  const [additionalMovieFileIds, setAdditionalMovieFileIds] = useState<
    number[]
  >([]);
  const [mergeOutputPath, setMergeOutputPath] = useState('');
  const [mergeReplaceOriginal, setMergeReplaceOriginal] = useState(false);
  const [mergeDryRun, setMergeDryRun] = useState(false);
  const [mergeSampleSeconds, setMergeSampleSeconds] = useState('2');
  const [mergeMinScore, setMergeMinScore] = useState('0.55');
  const [mergeMaxSamples, setMergeMaxSamples] = useState('3');
  const [mergeSampleRate, setMergeSampleRate] = useState('8000');
  const [mergeIncludeBaseAudio, setMergeIncludeBaseAudio] = useState(true);
  const [mergeIncludeCandidateAudio, setMergeIncludeCandidateAudio] =
    useState(true);
  const [mergeSkipDuplicateTracks, setMergeSkipDuplicateTracks] =
    useState(true);

  useEffect(() => {
    setBaseMovieFileId((current) => current ?? selectedMovieFileId);
  }, [selectedMovieFileId]);

  const tracksQuery = useAudioTracks(selectedMovieFileId, {
    preferredLanguages: parseLanguages(preferredLanguages),
    orderBy,
    descending,
    defaultAudioIndex: defaultAudioIndex
      ? Number(defaultAudioIndex)
      : undefined,
  });

  const reorder = useReorderAudioTracks();
  const merge = useMergeAudioTracks();

  const selectedMovie = useMemo(
    () =>
      moviesWithFiles.find(
        (movie) => movie.movieFileId === selectedMovieFileId
      ),
    [moviesWithFiles, selectedMovieFileId]
  );

  const baseMovie = useMemo(
    () =>
      moviesWithFiles.find((movie) => movie.movieFileId === baseMovieFileId),
    [moviesWithFiles, baseMovieFileId]
  );

  const additionalOptions = useMemo(
    () =>
      moviesWithFiles.filter((movie) => movie.movieFileId !== baseMovieFileId),
    [moviesWithFiles, baseMovieFileId]
  );

  const handleSelectedMovieFileChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      const value = event.target.value;
      setSelectedMovieFileId(value ? Number(value) : undefined);
    },
    []
  );

  const handleBaseMovieFileChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      const value = event.target.value;
      setBaseMovieFileId(value ? Number(value) : undefined);
      setAdditionalMovieFileIds([]);
    },
    []
  );

  const handleAdditionalChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      const values = Array.from(event.target.selectedOptions).map((option) =>
        Number(option.value)
      );
      setAdditionalMovieFileIds(values);
    },
    []
  );

  const handleReorder = useCallback(() => {
    if (!selectedMovieFileId) {
      return;
    }

    reorder.mutate({
      movieFileId: selectedMovieFileId,
      preferredLanguages: parseLanguages(preferredLanguages),
      orderBy,
      descending,
      defaultAudioIndex: defaultAudioIndex
        ? Number(defaultAudioIndex)
        : undefined,
      outputPath: outputPath.trim() || undefined,
      replaceOriginal,
      dryRun,
    });
  }, [
    selectedMovieFileId,
    preferredLanguages,
    orderBy,
    descending,
    defaultAudioIndex,
    outputPath,
    replaceOriginal,
    dryRun,
    reorder,
  ]);

  const handleMerge = useCallback(() => {
    if (!baseMovieFileId || !additionalMovieFileIds.length) {
      return;
    }

    merge.mutate({
      baseMovieFileId,
      additionalMovieFileIds,
      preferredLanguages: parseLanguages(preferredLanguages),
      orderBy,
      descending,
      defaultAudioIndex: defaultAudioIndex
        ? Number(defaultAudioIndex)
        : undefined,
      outputPath: mergeOutputPath.trim() || undefined,
      replaceOriginal: mergeReplaceOriginal,
      dryRun: mergeDryRun,
      sampleSeconds: Number(mergeSampleSeconds) || 2,
      minScore: Number(mergeMinScore) || 0.55,
      maxSamples: Number(mergeMaxSamples) || 3,
      sampleRate: Number(mergeSampleRate) || 8000,
      includeBaseAudio: mergeIncludeBaseAudio,
      includeCandidateAudio: mergeIncludeCandidateAudio,
      skipDuplicateTracks: mergeSkipDuplicateTracks,
    });
  }, [
    baseMovieFileId,
    additionalMovieFileIds,
    preferredLanguages,
    orderBy,
    descending,
    defaultAudioIndex,
    mergeOutputPath,
    mergeReplaceOriginal,
    mergeDryRun,
    mergeSampleSeconds,
    mergeMinScore,
    mergeMaxSamples,
    mergeSampleRate,
    mergeIncludeBaseAudio,
    mergeIncludeCandidateAudio,
    mergeSkipDuplicateTracks,
    merge,
  ]);

  const handleOrderByChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      setOrderBy(event.target.value);
    },
    []
  );

  const handlePreferredLanguagesChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setPreferredLanguages(event.target.value);
    },
    []
  );

  const handleDefaultAudioIndexChange = useCallback(
    (event: React.ChangeEvent<HTMLSelectElement>) => {
      setDefaultAudioIndex(event.target.value);
    },
    []
  );

  const handleDescendingChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setDescending(event.target.checked);
    },
    []
  );

  const handleReplaceOriginalChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setReplaceOriginal(event.target.checked);
    },
    []
  );

  const handleDryRunChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setDryRun(event.target.checked);
    },
    []
  );

  const handleOutputPathChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setOutputPath(event.target.value);
    },
    []
  );

  const handleMergeSampleSecondsChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setMergeSampleSeconds(event.target.value);
    },
    []
  );

  const handleMergeMinScoreChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setMergeMinScore(event.target.value);
    },
    []
  );

  const handleMergeMaxSamplesChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setMergeMaxSamples(event.target.value);
    },
    []
  );

  const handleMergeSampleRateChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setMergeSampleRate(event.target.value);
    },
    []
  );

  const handleMergeReplaceOriginalChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setMergeReplaceOriginal(event.target.checked);
    },
    []
  );

  const handleMergeDryRunChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setMergeDryRun(event.target.checked);
    },
    []
  );

  const handleMergeOutputPathChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setMergeOutputPath(event.target.value);
    },
    []
  );

  const handleMergeIncludeBaseAudioChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setMergeIncludeBaseAudio(event.target.checked);
    },
    []
  );

  const handleMergeIncludeCandidateAudioChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setMergeIncludeCandidateAudio(event.target.checked);
    },
    []
  );

  const handleMergeSkipDuplicateTracksChange = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) => {
      setMergeSkipDuplicateTracks(event.target.checked);
    },
    []
  );

  const tracks = tracksQuery.data?.orderedTracks ?? [];

  let tracksContent: React.ReactNode = null;

  if (tracksQuery.isFetching) {
    tracksContent = <LoadingIndicator />;
  } else if (tracksQuery.error) {
    tracksContent = (
      <Alert kind={kinds.DANGER}>
        Could not load audio tracks: {tracksQuery.error.message}
      </Alert>
    );
  } else if (tracksQuery.data) {
    tracksContent = (
      <div className="mt-[12px]">
        <div className={META_CLASS}>
          Container: {tracksQuery.data.container || 'unknown'} | Current
          default: {tracksQuery.data.currentDefaultAudioIndex} | Proposed
          default: {tracksQuery.data.defaultAudioIndex}
          {tracksQuery.data.preferredLanguageFound
            ? ' (preferred language found)'
            : ''}
        </div>
        <TrackTable tracks={tracks} />
      </div>
    );
  } else {
    tracksContent = (
      <div className={STATE_CLASS}>
        Select a movie file to list its audio tracks.
      </div>
    );
  }

  return (
    <PageContent title={translate('AudioTracks')}>
      <PageContentBody>
        <div className={PAGE_CLASS}>
          {moviesError ? (
            <Alert kind={kinds.DANGER}>
              Could not load movies: {moviesError.message}
            </Alert>
          ) : null}

          <FieldSet legend="Audio tracks">
            <div className={TOOLBAR_CLASS}>
              <label className={FIELD_CLASS}>
                <span className={LABEL_CLASS}>Movie file</span>
                <select
                  className={INPUT_CLASS}
                  value={selectedMovieFileId ?? ''}
                  disabled={isMoviesFetching}
                  onChange={handleSelectedMovieFileChange}
                >
                  <option value="">Select a movie file...</option>
                  {moviesWithFiles.map((movie) => (
                    <option key={movie.movieFileId} value={movie.movieFileId}>
                      {movie.title} ({movie.year})
                    </option>
                  ))}
                </select>
              </label>

              <label className={FIELD_CLASS}>
                <span className={LABEL_CLASS}>Order by</span>
                <select
                  className={INPUT_CLASS}
                  value={orderBy}
                  onChange={handleOrderByChange}
                >
                  {ORDER_OPTIONS.map((option) => (
                    <option key={option.value} value={option.value}>
                      {option.label}
                    </option>
                  ))}
                </select>
              </label>

              <label className={FIELD_CLASS}>
                <span className={LABEL_CLASS}>Preferred languages</span>
                <input
                  className={INPUT_CLASS}
                  type="text"
                  value={preferredLanguages}
                  placeholder="eng, fra"
                  onChange={handlePreferredLanguagesChange}
                />
              </label>

              <label className={FIELD_CLASS}>
                <span className={LABEL_CLASS}>Default audio index</span>
                <select
                  className={INPUT_CLASS}
                  value={defaultAudioIndex}
                  onChange={handleDefaultAudioIndexChange}
                >
                  <option value="">Automatic</option>
                  {(tracksQuery.data?.tracks ?? []).map((track) => (
                    <option key={track.audioIndex} value={track.audioIndex}>
                      {track.audioIndex}: {formatTrackLabel(track)}
                    </option>
                  ))}
                </select>
              </label>

              <label className={CHECKBOX_LABEL_CLASS}>
                <input
                  type="checkbox"
                  checked={descending}
                  onChange={handleDescendingChange}
                />
                Descending
              </label>

              <label className={CHECKBOX_LABEL_CLASS}>
                <input
                  type="checkbox"
                  checked={replaceOriginal}
                  onChange={handleReplaceOriginalChange}
                />
                Replace original
              </label>

              <label className={CHECKBOX_LABEL_CLASS}>
                <input
                  type="checkbox"
                  checked={dryRun}
                  onChange={handleDryRunChange}
                />
                Preview only
              </label>
            </div>

            <div className={TOOLBAR_CLASS}>
              <label className={FIELD_CLASS}>
                <span className={LABEL_CLASS}>Output path (optional)</span>
                <input
                  className={INPUT_CLASS}
                  type="text"
                  value={outputPath}
                  placeholder="Leave empty for the movie folder"
                  onChange={handleOutputPathChange}
                />
              </label>

              <SpinnerButton
                isSpinning={reorder.isPending}
                isDisabled={!selectedMovieFileId}
                onPress={handleReorder}
              >
                {dryRun ? 'Preview reorder' : 'Reorder tracks'}
              </SpinnerButton>
            </div>

            {selectedMovie ? (
              <div className={META_CLASS}>
                {selectedMovie.title} - {selectedMovie.path}
              </div>
            ) : null}

            {tracksContent}

            {reorder.error ? (
              <Alert kind={kinds.DANGER}>
                Reorder failed: {reorder.error.message}
              </Alert>
            ) : null}

            {reorder.data ? <ReorderResult result={reorder.data} /> : null}
          </FieldSet>

          <FieldSet legend="Merge audio tracks">
            <div className={TOOLBAR_CLASS}>
              <label className={FIELD_CLASS}>
                <span className={LABEL_CLASS}>Base movie file</span>
                <select
                  className={INPUT_CLASS}
                  value={baseMovieFileId ?? ''}
                  onChange={handleBaseMovieFileChange}
                >
                  <option value="">Select a base file...</option>
                  {moviesWithFiles.map((movie) => (
                    <option key={movie.movieFileId} value={movie.movieFileId}>
                      {movie.title} ({movie.year})
                    </option>
                  ))}
                </select>
              </label>

              <label className={FIELD_CLASS}>
                <span className={LABEL_CLASS}>
                  Additional files (Ctrl/Cmd-click)
                </span>
                <select
                  className={`${INPUT_CLASS} min-h-[90px]`}
                  multiple={true}
                  value={additionalMovieFileIds.map(String)}
                  onChange={handleAdditionalChange}
                >
                  {additionalOptions.map((movie) => (
                    <option key={movie.movieFileId} value={movie.movieFileId}>
                      {movie.title} ({movie.year})
                    </option>
                  ))}
                </select>
              </label>

              <label className={FIELD_CLASS}>
                <span className={LABEL_CLASS}>Sample seconds</span>
                <input
                  className={INPUT_CLASS}
                  type="number"
                  min="0.25"
                  step="0.25"
                  value={mergeSampleSeconds}
                  onChange={handleMergeSampleSecondsChange}
                />
              </label>

              <label className={FIELD_CLASS}>
                <span className={LABEL_CLASS}>Minimum score</span>
                <input
                  className={INPUT_CLASS}
                  type="number"
                  min="0"
                  max="1"
                  step="0.05"
                  value={mergeMinScore}
                  onChange={handleMergeMinScoreChange}
                />
              </label>

              <label className={FIELD_CLASS}>
                <span className={LABEL_CLASS}>Max samples</span>
                <input
                  className={INPUT_CLASS}
                  type="number"
                  min="1"
                  step="1"
                  value={mergeMaxSamples}
                  onChange={handleMergeMaxSamplesChange}
                />
              </label>

              <label className={FIELD_CLASS}>
                <span className={LABEL_CLASS}>Sample rate</span>
                <input
                  className={INPUT_CLASS}
                  type="number"
                  min="1000"
                  step="1000"
                  value={mergeSampleRate}
                  onChange={handleMergeSampleRateChange}
                />
              </label>

              <label className={CHECKBOX_LABEL_CLASS}>
                <input
                  type="checkbox"
                  checked={mergeReplaceOriginal}
                  onChange={handleMergeReplaceOriginalChange}
                />
                Replace original
              </label>

              <label className={CHECKBOX_LABEL_CLASS}>
                <input
                  type="checkbox"
                  checked={mergeDryRun}
                  onChange={handleMergeDryRunChange}
                />
                Preview only
              </label>

              <label className={CHECKBOX_LABEL_CLASS}>
                <input
                  type="checkbox"
                  checked={mergeIncludeBaseAudio}
                  onChange={handleMergeIncludeBaseAudioChange}
                />
                Include base audio
              </label>

              <label className={CHECKBOX_LABEL_CLASS}>
                <input
                  type="checkbox"
                  checked={mergeIncludeCandidateAudio}
                  onChange={handleMergeIncludeCandidateAudioChange}
                />
                Include candidate audio
              </label>

              <label className={CHECKBOX_LABEL_CLASS}>
                <input
                  type="checkbox"
                  checked={mergeSkipDuplicateTracks}
                  onChange={handleMergeSkipDuplicateTracksChange}
                />
                Skip duplicate tracks
              </label>
            </div>

            <div className={TOOLBAR_CLASS}>
              <label className={FIELD_CLASS}>
                <span className={LABEL_CLASS}>Output path (optional)</span>
                <input
                  className={INPUT_CLASS}
                  type="text"
                  value={mergeOutputPath}
                  placeholder="Leave empty for the movie folder"
                  onChange={handleMergeOutputPathChange}
                />
              </label>

              <SpinnerButton
                isSpinning={merge.isPending}
                isDisabled={!baseMovieFileId || !additionalMovieFileIds.length}
                onPress={handleMerge}
              >
                {mergeDryRun ? 'Preview merge' : 'Merge audio tracks'}
              </SpinnerButton>
            </div>

            {baseMovie ? (
              <div className={META_CLASS}>
                Base: {baseMovie.title} - {baseMovie.path}
              </div>
            ) : null}

            {additionalMovieFileIds.length ? (
              <div className={META_CLASS}>
                Additional files: {additionalMovieFileIds.length}
              </div>
            ) : null}

            {merge.error ? (
              <Alert kind={kinds.DANGER}>
                Merge failed: {merge.error.message}
              </Alert>
            ) : null}

            {merge.data ? <MergeResult result={merge.data} /> : null}
          </FieldSet>
        </div>
      </PageContentBody>
    </PageContent>
  );
}

export default AudioTracksPage;
