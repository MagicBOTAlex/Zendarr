import { useQueryClient } from '@tanstack/react-query';
import useApiMutation from 'Helpers/Hooks/useApiMutation';
import useApiQuery from 'Helpers/Hooks/useApiQuery';

export interface AudioTrack {
  audioIndex: number;
  streamIndex: number;
  language: string;
  languageName: string;
  title?: string;
  displayName: string;
  codec: string;
  profile?: string;
  bitrate: number;
  channels: number;
  isDefault: boolean;
  isHearingImpaired: boolean;
  isCommentary: boolean;
  isPreferred: boolean;
}

export interface AudioTrackList {
  movieFileId: number;
  movieId: number;
  movieTitle: string;
  path: string;
  container: string;
  tracks: AudioTrack[];
  orderedTracks: AudioTrack[];
  currentDefaultAudioIndex: number;
  defaultAudioIndex: number;
  defaultLanguage: string;
  preferredLanguageFound: boolean;
}

export interface AudioTrackOrderOptions {
  preferredLanguages?: string[];
  orderBy?: string;
  descending?: boolean;
  defaultLanguage?: string;
  defaultAudioIndex?: number;
  preserveDefaultWhenNoPreferred?: boolean;
}

export interface AudioTrackReorderRequest extends AudioTrackOrderOptions {
  movieFileId: number;
  outputPath?: string;
  replaceOriginal?: boolean;
  dryRun?: boolean;
  includeCopiedStreams?: boolean;
}

export interface AudioTrackReorderResult {
  success: boolean;
  movieFileId: number;
  sourcePath: string;
  outputPath?: string;
  backupPath?: string;
  command?: string;
  executed: boolean;
  replacedOriginal: boolean;
  outputSize: number;
  tracks: AudioTrack[];
  defaultAudioIndex: number;
  defaultLanguage: string;
  error?: string;
}

export interface AudioSamplePoint {
  positionSeconds: number;
  offsetSeconds: number;
  score: number;
  error?: string;
}

export interface AudioTrackMatch {
  movieFileId: number;
  path: string;
  audioIndex: number;
  language: string;
  title?: string;
  matched: boolean;
  offsetSeconds: number;
  score: number;
  sampleCount: number;
  matchedSampleCount: number;
  error?: string;
  samples: AudioSamplePoint[];
  mergedTracks: AudioTrack[];
}

export interface AudioTrackMergeRequest extends AudioTrackOrderOptions {
  baseMovieFileId: number;
  additionalMovieFileIds: number[];
  outputPath?: string;
  replaceOriginal?: boolean;
  dryRun?: boolean;
  includeBaseAudio?: boolean;
  includeCandidateAudio?: boolean;
  includeCopiedStreams?: boolean;
  skipDuplicateTracks?: boolean;
  sampleSeconds?: number;
  minScore?: number;
  maxSampleOffsetSeconds?: number;
  sampleRate?: number;
  maxSamples?: number;
}

export interface AudioTrackMergeResult {
  success: boolean;
  baseMovieFileId: number;
  basePath: string;
  outputPath?: string;
  backupPath?: string;
  command?: string;
  executed: boolean;
  replacedOriginal: boolean;
  outputSize: number;
  matches: AudioTrackMatch[];
  tracks: AudioTrack[];
  error?: string;
}

export const getAudioTracksPath = (movieFileId: number) =>
  `/audio-tracks/movie/${movieFileId}`;

export const getAudioTracksReorderPath = (movieFileId: number) =>
  `/audio-tracks/movie/${movieFileId}/reorder`;

export const AUDIO_TRACKS_MERGE_PATH = '/audio-tracks/merge';

export const useAudioTracks = (
  movieFileId: number | undefined,
  options: AudioTrackOrderOptions = {}
) => {
  return useApiQuery<AudioTrackList>({
    service: 'movies',
    path: getAudioTracksPath(movieFileId ?? 0),
    queryParams: {
      preferredLanguages: options.preferredLanguages,
      orderBy: options.orderBy,
      descending: options.descending,
      defaultLanguage: options.defaultLanguage,
      defaultAudioIndex: options.defaultAudioIndex,
      preserveDefaultWhenNoPreferred: options.preserveDefaultWhenNoPreferred,
    },
    queryOptions: {
      enabled: !!movieFileId,
    },
  });
};

export const useReorderAudioTracks = () => {
  const queryClient = useQueryClient();

  return useApiMutation<AudioTrackReorderResult, AudioTrackReorderRequest>({
    service: 'movies',
    method: 'POST',
    path: (payload) => getAudioTracksReorderPath(payload.movieFileId),
    mutationOptions: {
      onSuccess: (result, payload) => {
        queryClient.invalidateQueries({
          queryKey: ['movies', getAudioTracksPath(payload.movieFileId)],
        });

        if (result.executed) {
          queryClient.invalidateQueries({ queryKey: ['movies', '/movie'] });
        }
      },
    },
  });
};

export const useMergeAudioTracks = () => {
  const queryClient = useQueryClient();

  return useApiMutation<AudioTrackMergeResult, AudioTrackMergeRequest>({
    service: 'movies',
    method: 'POST',
    path: AUDIO_TRACKS_MERGE_PATH,
    mutationOptions: {
      onSuccess: (result, payload) => {
        queryClient.invalidateQueries({
          queryKey: ['movies', getAudioTracksPath(payload.baseMovieFileId)],
        });

        if (result.executed) {
          queryClient.invalidateQueries({ queryKey: ['movies', '/movie'] });
        }
      },
    },
  });
};
