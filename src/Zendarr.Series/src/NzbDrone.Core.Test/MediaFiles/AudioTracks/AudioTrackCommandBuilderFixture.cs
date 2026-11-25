using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.AudioTracks;

namespace NzbDrone.Core.Test.MediaFiles.AudioTracks
{
    [TestFixture]
    public class AudioTrackCommandBuilderFixture
    {
        private readonly AudioTrackCommandBuilder _builder = new AudioTrackCommandBuilder();

        [Test]
        public void should_reorder_audio_and_set_the_default_disposition()
        {
            var command = _builder.BuildReorder(new AudioReorderCommandInput
            {
                SourcePath = "/media/Movie/movie.mkv",
                OutputPath = "/media/Movie/movie.reordered.mkv",
                PrimaryVideoStreamIndex = 0,
                Tracks = new List<AudioTrackInfo>
                {
                    new AudioTrackInfo { AudioIndex = 1, Language = "eng", Title = "English", IsDefault = true },
                    new AudioTrackInfo { AudioIndex = 0, Language = "spa", Title = "Spanish" }
                }
            });

            command.Should().Contain("-map 0:v:0?");
            command.Should().Contain("-map 0:a:1 -map 0:a:0");
            command.Should().Contain("-c copy");
            command.Should().Contain("-disposition:a:0 default");
            command.Should().Contain("-disposition:a:1 0");
            command.Should().Contain("-metadata:s:a:0 language=eng");
            command.Should().Contain("-metadata:s:a:1 language=spa");
            command.Should().Contain("-map_metadata 0");
            command.Should().Contain("-map_chapters 0");
        }

        [Test]
        public void should_merge_candidate_audio_with_an_offset_and_trim_to_the_base()
        {
            var command = _builder.BuildMerge(new AudioMergeCommandInput
            {
                BasePath = "/media/Movie/movie.mkv",
                OutputPath = "/media/Movie/movie.merged.mkv",
                PrimaryVideoStreamIndex = 0,
                DurationSeconds = 10.5,
                BaseTracks = new List<AudioTrackInfo>
                {
                    new AudioTrackInfo { AudioIndex = 0, Language = "eng", Title = "English", IsDefault = true },
                    new AudioTrackInfo { AudioIndex = 1, Language = "spa", Title = "Spanish" }
                },
                Candidates = new List<AudioMergeCandidateInput>
                {
                    new AudioMergeCandidateInput
                    {
                        Path = "/media/Other/other.mkv",
                        OffsetSeconds = -1.5,
                        Tracks = new List<AudioTrackInfo>
                        {
                            new AudioTrackInfo { AudioIndex = 2, Language = "fra", Title = "French" }
                        }
                    }
                }
            });

            command.Should().Contain("-itsoffset -1.5");
            command.Should().Contain("-map 1:a:2");
            command.Should().Contain("-copyts");
            command.Should().Contain("-avoid_negative_ts disabled");
            command.Should().Contain("-t 10.5");
            command.Should().Contain("-disposition:a:0 default");
            command.Should().Contain("-disposition:a:2 0");
            command.Should().Contain("-metadata:s:a:2 language=fra");
        }

        [Test]
        public void should_omit_itsoffset_when_the_candidate_is_already_aligned()
        {
            var command = _builder.BuildMerge(new AudioMergeCommandInput
            {
                BasePath = "/media/Movie/movie.mkv",
                OutputPath = "/media/Movie/movie.merged.mkv",
                PrimaryVideoStreamIndex = 0,
                BaseTracks = new List<AudioTrackInfo>
                {
                    new AudioTrackInfo { AudioIndex = 0, Language = "eng", IsDefault = true }
                },
                Candidates = new List<AudioMergeCandidateInput>
                {
                    new AudioMergeCandidateInput
                    {
                        Path = "/media/Other/other.mkv",
                        OffsetSeconds = 0,
                        Tracks = new List<AudioTrackInfo>
                        {
                            new AudioTrackInfo { AudioIndex = 0, Language = "fra" }
                        }
                    }
                }
            });

            command.Should().NotContain("-itsoffset");
            command.Should().Contain("-map 1:a:0");
        }

        [Test]
        public void should_quote_paths_with_spaces()
        {
            var command = _builder.BuildReorder(new AudioReorderCommandInput
            {
                SourcePath = "/media/My Movie/movie.mkv",
                OutputPath = "/media/My Movie/movie.reordered.mkv",
                PrimaryVideoStreamIndex = 0,
                Tracks = new List<AudioTrackInfo>
                {
                    new AudioTrackInfo { AudioIndex = 0, Language = "eng", IsDefault = true }
                }
            });

            command.Should().Contain("\"/media/My Movie/movie.mkv\"");
            command.Should().Contain("\"/media/My Movie/movie.reordered.mkv\"");
        }
    }
}
