using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Processes;
using NzbDrone.Core.MediaFiles.AudioTracks;
using NzbDrone.Core.MediaFiles.Transcoding;

namespace NzbDrone.Core.Test.MediaFiles.AudioTracks
{
    // Real ffmpeg end-to-end coverage for audio-track listing, reordering and sample-matched merging.
    // Excluded from ./test.sh (Category=ManualTest); run explicitly:
    //   dotnet test ... --filter "FullyQualifiedName~AudioTrackIntegrationFixture"
    [TestFixture]
    [Category("ManualTest")]
    public class AudioTrackIntegrationFixture
    {
        private string _folder;
        private string _basePath;
        private string _candidatePath;
        private AudioTrackService _service;
        private IProcessProvider _processProvider;

        [SetUp]
        public void Setup()
        {
            _folder = Path.Combine(Path.GetTempPath(), "zendarr-audio-tracks-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_folder);

            var logger = LogManager.GetCurrentClassLogger();
            _processProvider = new ProcessProvider(logger);

            var ffmpeg = FindFfmpeg();

            if (ffmpeg == null)
            {
                Assert.Ignore("ffmpeg is not available on this host");
            }

            var runner = new AudioCommandRunner(new TestFFmpegProvider(ffmpeg), _processProvider, logger);
            var probe = new AudioTrackProbe(logger);
            var extractor = new AudioSampleExtractor(runner, logger);
            var builder = new AudioTrackCommandBuilder();

            _service = new AudioTrackService(probe, extractor, builder, runner, logger);

            _basePath = Path.Combine(_folder, "base.mkv");
            _candidatePath = Path.Combine(_folder, "candidate.mkv");

            Run(ffmpeg, "-hide_banner -loglevel error -y " +
                       "-f lavfi -i testsrc2=size=320x240:rate=15:duration=4 " +
                       "-f lavfi -i anoisesrc=color=white:amplitude=0.4:sample_rate=48000:seed=123:duration=4 " +
                       "-f lavfi -i sine=frequency=880:sample_rate=48000:duration=4 " +
                       "-map 0:v -map 1:a -map 2:a -c:v libx264 -preset ultrafast -c:a aac " +
                       "-metadata:s:a:0 language=eng -metadata:s:a:0 title=English " +
                       "-metadata:s:a:1 language=spa -metadata:s:a:1 title=Spanish " +
                       "-disposition:a:0 0 -disposition:a:1 default " +
                       Quote(_basePath));

            Run(ffmpeg, "-hide_banner -loglevel error -y " +
                       "-f lavfi -i testsrc2=size=320x240:rate=15:duration=5 " +
                       "-f lavfi -i anoisesrc=color=white:amplitude=0.4:sample_rate=48000:seed=123:duration=5 " +
                       "-f lavfi -i sine=frequency=1320:sample_rate=48000:duration=5 " +
                       "-map 0:v -map 1:a -map 2:a -c:v libx264 -preset ultrafast -c:a aac " +
                       "-af adelay=1000|1000 " +
                       "-metadata:s:a:0 language=eng -metadata:s:a:0 title=English " +
                       "-metadata:s:a:1 language=fra -metadata:s:a:1 title=French " +
                       "-disposition:a:0 default -disposition:a:1 0 " +
                       Quote(_candidatePath));
        }

        [TearDown]
        public void Cleanup()
        {
            if (Directory.Exists(_folder))
            {
                Directory.Delete(_folder, true);
            }
        }

        [Test]
        public void should_list_and_reorder_audio_tracks()
        {
            var listed = _service.GetTracks(_basePath, new AudioTrackOrderOptions());

            listed.Success.Should().BeTrue(listed.Error);
            listed.Tracks.Should().HaveCount(2);
            listed.Order.Tracks[0].Language.Should().Be("eng");
            listed.Order.Tracks[0].IsDefault.Should().BeTrue();
            listed.Order.DefaultLanguage.Should().Be("eng");

            var output = Path.Combine(_folder, "reordered.mkv");
            var reordered = _service.Reorder(new AudioTrackReorderRequest
            {
                SourcePath = _basePath,
                OutputPath = output,
                OrderOptions = new AudioTrackOrderOptions()
            });

            reordered.Success.Should().BeTrue(reordered.Error);
            reordered.Executed.Should().BeTrue();
            File.Exists(output).Should().BeTrue();

            var probed = _service.GetTracks(output, new AudioTrackOrderOptions());
            probed.Tracks.Should().HaveCount(2);
            probed.Tracks[0].Language.Should().Be("eng");
            probed.Tracks[0].IsDefault.Should().BeTrue();
        }

        [Test]
        public void should_replace_the_original_in_place()
        {
            var source = Path.Combine(_folder, "replace.mkv");
            File.Copy(_basePath, source);

            var reordered = _service.Reorder(new AudioTrackReorderRequest
            {
                SourcePath = source,
                ReplaceOriginal = true,
                OrderOptions = new AudioTrackOrderOptions()
            });

            reordered.Success.Should().BeTrue(reordered.Error);
            reordered.ReplacedOriginal.Should().BeTrue();
            reordered.OutputPath.Should().Be(source);
            File.Exists(source).Should().BeTrue();
            File.Exists(reordered.BackupPath).Should().BeTrue();

            var probed = _service.GetTracks(source, new AudioTrackOrderOptions());
            probed.Tracks[0].Language.Should().Be("eng");
            probed.Tracks[0].IsDefault.Should().BeTrue();
        }

        [Test]
        public void should_match_and_merge_candidate_audio_into_the_base()
        {
            var output = Path.Combine(_folder, "merged.mkv");
            var merged = _service.Merge(new AudioTrackMergeRequest
            {
                BasePath = _basePath,
                AdditionalPaths = new List<string> { _candidatePath },
                OutputPath = output,
                OrderOptions = new AudioTrackOrderOptions(),
                SampleSeconds = 1.0,
                MinMatchScore = 0.55,
                MaxSampleOffsetSeconds = 0.25,
                SampleRate = 8000,
                MaxSamples = 3
            });

            merged.Success.Should().BeTrue($"{merged.Error} | {string.Join("; ", merged.Matches.Select(match => $"{match.Path}: {match.Error} score={match.Score} samples={match.MatchedSampleCount}/{match.SampleCount} [{string.Join(", ", match.Samples.Select(sample => $"pos={sample.PositionSeconds:0.###} matched={sample.Matched} offset={sample.OffsetSeconds:0.###} score={sample.Score:0.###} error={sample.Error}"))}]"))}");
            merged.Executed.Should().BeTrue();
            merged.Matches.Should().HaveCount(1);
            merged.Matches[0].Matched.Should().BeTrue(merged.Matches[0].Error);
            merged.Matches[0].OffsetSeconds.Should().BeApproximately(-1.0, 0.2);
            merged.Matches[0].MergedTracks.Should().NotBeEmpty();
            merged.Matches[0].MergedTracks.Should().OnlyContain(track => track.Language == "fra");

            File.Exists(output).Should().BeTrue();

            var probed = _service.GetTracks(output, new AudioTrackOrderOptions());
            probed.Success.Should().BeTrue(probed.Error);
            probed.Tracks.Should().HaveCount(3);
            probed.Tracks.Select(track => track.Language).Should().BeEquivalentTo(new[] { "eng", "spa", "fra" });
            probed.Tracks.First(track => track.Language == "eng").IsDefault.Should().BeTrue();
            probed.DurationSeconds.Should().BeApproximately(4.0, 0.5);
        }

        private void Run(string ffmpeg, string arguments)
        {
            var result = _processProvider.StartAndCapture(ffmpeg, arguments);

            if (result.ExitCode != 0)
            {
                Assert.Fail($"ffmpeg failed ({result.ExitCode}): {string.Join("\n", result.Standard.Select(line => line.Content))}");
            }
        }

        private static string FindFfmpeg()
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

            foreach (var folder in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = Path.Combine(folder, "ffmpeg");

                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private class TestFFmpegProvider : IFFmpegProvider
        {
            private readonly string _path;

            public TestFFmpegProvider(string path)
            {
                _path = path;
            }

            public string GetFFmpegPath()
            {
                return _path;
            }

            public string GetVersion(string ffmpegPath = null)
            {
                return "test";
            }
        }
    }
}
