using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles.Transcoding;

namespace NzbDrone.Core.MediaFiles.AudioTracks
{
    public class AudioReorderCommandInput
    {
        public string SourcePath { get; set; }
        public string OutputPath { get; set; }
        public int PrimaryVideoStreamIndex { get; set; }
        public List<AudioTrackInfo> Tracks { get; set; } = new List<AudioTrackInfo>();
        public int DefaultAudioIndex { get; set; }
        public bool IncludeCopiedStreams { get; set; } = true;
    }

    public class AudioMergeCandidateInput
    {
        public string Path { get; set; }

        // The audio tracks to copy from this candidate. The reference track used for the sample
        // match is included only when it is not a duplicate of a base track.
        public List<AudioTrackInfo> Tracks { get; set; } = new List<AudioTrackInfo>();
        public AudioTrackInfo ReferenceTrack { get; set; }
        public double OffsetSeconds { get; set; }
    }

    public class AudioMergeCommandInput
    {
        public string BasePath { get; set; }
        public string OutputPath { get; set; }
        public int PrimaryVideoStreamIndex { get; set; }
        public List<AudioTrackInfo> BaseTracks { get; set; } = new List<AudioTrackInfo>();
        public List<AudioMergeCandidateInput> Candidates { get; set; } = new List<AudioMergeCandidateInput>();
        public bool IncludeBaseAudio { get; set; } = true;
        public bool IncludeCopiedStreams { get; set; } = true;

        // When > 0 the output is trimmed to the base duration so a longer candidate does not extend
        // the movie past its video.
        public double DurationSeconds { get; set; }
    }

    public interface IAudioTrackCommandBuilder
    {
        string BuildReorder(AudioReorderCommandInput input);
        string BuildMerge(AudioMergeCommandInput input);
    }

    public class AudioTrackCommandBuilder : IAudioTrackCommandBuilder
    {
        public string BuildReorder(AudioReorderCommandInput input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (input.SourcePath.IsNullOrWhiteSpace() || input.OutputPath.IsNullOrWhiteSpace())
            {
                throw new ArgumentException("Source and output paths are required");
            }

            var tracks = input.Tracks ?? new List<AudioTrackInfo>();
            var maps = new List<string> { $"-map 0:v:{Math.Max(0, input.PrimaryVideoStreamIndex)}?" };

            foreach (var track in tracks)
            {
                maps.Add($"-map 0:a:{track.AudioIndex}");
            }

            if (input.IncludeCopiedStreams)
            {
                maps.Add("-map 0:s?");
                maps.Add("-map 0:t?");
                maps.Add("-map 0:d?");
            }

            maps.Add("-map_metadata 0");
            maps.Add("-map_chapters 0");

            var metadata = BuildAudioMetadata(tracks);

            return $"-i {TranscodeArgumentBuilder.Quote(input.SourcePath)} {string.Join(" ", maps)} -c copy {metadata} {TranscodeArgumentBuilder.Quote(input.OutputPath)}";
        }

        public string BuildMerge(AudioMergeCommandInput input)
        {
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }

            if (input.BasePath.IsNullOrWhiteSpace() || input.OutputPath.IsNullOrWhiteSpace())
            {
                throw new ArgumentException("Base and output paths are required");
            }

            var candidates = input.Candidates ?? new List<AudioMergeCandidateInput>();
            var baseTracks = input.IncludeBaseAudio ? input.BaseTracks ?? new List<AudioTrackInfo>() : new List<AudioTrackInfo>();

            var inputs = new List<string> { $"-i {TranscodeArgumentBuilder.Quote(input.BasePath)}" };
            var maps = new List<string> { $"-map 0:v:{Math.Max(0, input.PrimaryVideoStreamIndex)}?" };

            foreach (var track in baseTracks)
            {
                maps.Add($"-map 0:a:{track.AudioIndex}");
            }

            var outputTracks = new List<AudioTrackInfo>(baseTracks);

            for (var index = 0; index < candidates.Count; index++)
            {
                var candidate = candidates[index];
                var inputIndex = index + 1;

                if (Math.Abs(candidate.OffsetSeconds) > 0.0005)
                {
                    inputs.Add($"-itsoffset {FormatSeconds(candidate.OffsetSeconds)} -i {TranscodeArgumentBuilder.Quote(candidate.Path)}");
                }
                else
                {
                    inputs.Add($"-i {TranscodeArgumentBuilder.Quote(candidate.Path)}");
                }

                foreach (var track in candidate.Tracks ?? new List<AudioTrackInfo>())
                {
                    maps.Add($"-map {inputIndex}:a:{track.AudioIndex}");

                    // Candidate tracks are appended in the order the service selected them. Clear
                    // the source default flag so only the base default wins the disposition.
                    outputTracks.Add(new AudioTrackInfo
                    {
                        AudioIndex = track.AudioIndex,
                        StreamIndex = track.StreamIndex,
                        Language = track.Language,
                        LanguageName = track.LanguageName,
                        Title = track.Title,
                        Codec = track.Codec,
                        Profile = track.Profile,
                        Bitrate = track.Bitrate,
                        Channels = track.Channels,
                        SampleRate = track.SampleRate,
                        DurationSeconds = track.DurationSeconds,
                        IsDefault = false,
                        IsHearingImpaired = track.IsHearingImpaired,
                        IsCommentary = track.IsCommentary
                    });
                }
            }

            if (input.IncludeCopiedStreams)
            {
                maps.Add("-map 0:s?");
                maps.Add("-map 0:t?");
                maps.Add("-map 0:d?");
            }

            maps.Add("-map_metadata 0");
            maps.Add("-map_chapters 0");

            var metadata = BuildAudioMetadata(outputTracks);
            var trim = input.DurationSeconds > 0 ? $"-t {FormatSeconds(input.DurationSeconds)} " : string.Empty;

            // -copyts keeps the base timeline intact and lets a negative candidate offset stay
            // negative; without it ffmpeg shifts the whole output to make the earliest stream zero,
            // which would desynchronise the base video from the merged audio.
            return $"{string.Join(" ", inputs)} {string.Join(" ", maps)} -c copy -copyts -avoid_negative_ts disabled {trim}{metadata} {TranscodeArgumentBuilder.Quote(input.OutputPath)}";
        }

        private static string BuildAudioMetadata(List<AudioTrackInfo> tracks)
        {
            var arguments = new List<string>();
            var defaultOutputIndex = tracks.FindIndex(track => track.IsDefault);

            if (defaultOutputIndex < 0)
            {
                defaultOutputIndex = 0;
            }

            for (var index = 0; index < tracks.Count; index++)
            {
                var track = tracks[index];

                arguments.Add($"-disposition:a:{index} {(index == defaultOutputIndex ? "default" : "0")}");

                if (track.Language.IsNotNullOrWhiteSpace())
                {
                    arguments.Add($"-metadata:s:a:{index} language={SanitizeMetadata(track.Language)}");
                }

                if (track.Title.IsNotNullOrWhiteSpace())
                {
                    arguments.Add($"-metadata:s:a:{index} {TranscodeArgumentBuilder.Quote($"title={SanitizeMetadata(track.Title)}")}");
                }
            }

            return string.Join(" ", arguments);
        }

        private static string FormatSeconds(double seconds)
        {
            return seconds.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string SanitizeMetadata(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            return new string(value.Where(character => !char.IsControl(character)).ToArray()).Trim();
        }
    }
}
