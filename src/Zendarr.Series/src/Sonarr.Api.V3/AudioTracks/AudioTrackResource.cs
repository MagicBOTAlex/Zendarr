using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.MediaFiles.AudioTracks;

namespace Sonarr.Api.V3.AudioTracks
{
    public class AudioTrackResource
    {
        public int AudioIndex { get; set; }
        public int StreamIndex { get; set; }
        public string Language { get; set; }
        public string LanguageName { get; set; }
        public string Title { get; set; }
        public string DisplayName { get; set; }
        public string Codec { get; set; }
        public string Profile { get; set; }
        public long Bitrate { get; set; }
        public int Channels { get; set; }
        public bool IsDefault { get; set; }
        public bool IsHearingImpaired { get; set; }
        public bool IsCommentary { get; set; }
        public bool IsPreferred { get; set; }
    }

    public class AudioTrackListResource
    {
        public int MovieFileId { get; set; }
        public int MovieId { get; set; }
        public string MovieTitle { get; set; }
        public string Path { get; set; }
        public string Container { get; set; }
        public List<AudioTrackResource> Tracks { get; set; }
        public List<AudioTrackResource> OrderedTracks { get; set; }
        public int CurrentDefaultAudioIndex { get; set; }
        public int DefaultAudioIndex { get; set; }
        public string DefaultLanguage { get; set; }
        public bool PreferredLanguageFound { get; set; }
    }

    public class AudioTrackReorderResource
    {
        public string[] PreferredLanguages { get; set; }
        public string OrderBy { get; set; }
        public bool Descending { get; set; }
        public string DefaultLanguage { get; set; }
        public int? DefaultAudioIndex { get; set; }
        public bool PreserveDefaultWhenNoPreferred { get; set; } = true;
        public bool IncludeCopiedStreams { get; set; } = true;
        public string OutputPath { get; set; }
        public bool ReplaceOriginal { get; set; }
        public bool DryRun { get; set; }
    }

    public class AudioTrackReorderResultResource
    {
        public bool Success { get; set; }
        public int MovieFileId { get; set; }
        public string SourcePath { get; set; }
        public string OutputPath { get; set; }
        public string BackupPath { get; set; }
        public string Command { get; set; }
        public bool Executed { get; set; }
        public bool ReplacedOriginal { get; set; }
        public long OutputSize { get; set; }
        public List<AudioTrackResource> Tracks { get; set; }
        public int DefaultAudioIndex { get; set; }
        public string DefaultLanguage { get; set; }
        public string Error { get; set; }
    }

    public class AudioTrackMergeResource
    {
        public int BaseMovieFileId { get; set; }
        public List<int> AdditionalMovieFileIds { get; set; }
        public string[] PreferredLanguages { get; set; }
        public string OrderBy { get; set; }
        public bool Descending { get; set; }
        public string DefaultLanguage { get; set; }
        public int? DefaultAudioIndex { get; set; }
        public bool PreserveDefaultWhenNoPreferred { get; set; } = true;
        public bool IncludeBaseAudio { get; set; } = true;
        public bool IncludeCandidateAudio { get; set; } = true;
        public bool IncludeCopiedStreams { get; set; } = true;
        public bool SkipDuplicateTracks { get; set; } = true;
        public bool ReplaceOriginal { get; set; }
        public bool DryRun { get; set; }
        public string OutputPath { get; set; }
        public double SampleSeconds { get; set; } = 2;
        public double MinScore { get; set; } = 0.55;
        public double MaxSampleOffsetSeconds { get; set; } = 0.5;
        public int SampleRate { get; set; } = 8000;
        public int MaxSamples { get; set; } = 3;
    }

    public class AudioSamplePointResource
    {
        public double PositionSeconds { get; set; }
        public double OffsetSeconds { get; set; }
        public double Score { get; set; }
        public string Error { get; set; }
    }

    public class AudioTrackMatchResource
    {
        public int MovieFileId { get; set; }
        public string Path { get; set; }
        public int AudioIndex { get; set; }
        public string Language { get; set; }
        public string Title { get; set; }
        public bool Matched { get; set; }
        public double OffsetSeconds { get; set; }
        public double Score { get; set; }
        public int SampleCount { get; set; }
        public int MatchedSampleCount { get; set; }
        public string Error { get; set; }
        public List<AudioSamplePointResource> Samples { get; set; }
        public List<AudioTrackResource> MergedTracks { get; set; }
    }

    public class AudioTrackMergeResultResource
    {
        public bool Success { get; set; }
        public int BaseMovieFileId { get; set; }
        public string BasePath { get; set; }
        public string OutputPath { get; set; }
        public string BackupPath { get; set; }
        public string Command { get; set; }
        public bool Executed { get; set; }
        public bool ReplacedOriginal { get; set; }
        public long OutputSize { get; set; }
        public List<AudioTrackMatchResource> Matches { get; set; }
        public List<AudioTrackResource> Tracks { get; set; }
        public string Error { get; set; }
    }

    public static class AudioTrackResourceMapper
    {
        public static AudioTrackResource ToResource(this AudioTrackInfo track, IReadOnlyList<string> preferredLanguages)
        {
            return new AudioTrackResource
            {
                AudioIndex = track.AudioIndex,
                StreamIndex = track.StreamIndex,
                Language = track.Language,
                LanguageName = track.LanguageName,
                Title = track.Title,
                DisplayName = track.DisplayName,
                Codec = track.Codec,
                Profile = track.Profile,
                Bitrate = track.Bitrate,
                Channels = track.Channels,
                IsDefault = track.IsDefault,
                IsHearingImpaired = track.IsHearingImpaired,
                IsCommentary = track.IsCommentary,
                IsPreferred = AudioLanguage.IsPreferred(track.Language, preferredLanguages)
            };
        }

        public static List<AudioTrackResource> ToResources(this IEnumerable<AudioTrackInfo> tracks, IReadOnlyList<string> preferredLanguages)
        {
            return tracks?.Select(track => track.ToResource(preferredLanguages)).ToList() ?? new List<AudioTrackResource>();
        }
    }
}
