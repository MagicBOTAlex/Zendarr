using System;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.AudioTracks;

namespace NzbDrone.Core.Test.MediaFiles.AudioTracks
{
    [TestFixture]
    public class AudioSampleMatcherFixture
    {
        private const int SampleRate = 8000;

        [Test]
        public void should_find_a_reference_sample_at_a_known_lag()
        {
            var reference = Noise(16000, 1);
            var candidate = new short[40000];
            Array.Copy(reference, 0, candidate, 8000, reference.Length);

            var match = AudioSampleMatcher.Match(reference, candidate, SampleRate);

            match.Matched.Should().BeTrue(match.Error);
            match.LagSamples.Should().Be(8000);
            match.LagSeconds.Should().BeApproximately(1.0, 0.001);
            match.Score.Should().BeGreaterThan(0.9);
        }

        [Test]
        public void should_respect_the_search_window()
        {
            var reference = Noise(8000, 2);
            var candidate = Noise(64000, 3);
            Array.Copy(reference, 0, candidate, 24000, reference.Length);

            var match = AudioSampleMatcher.Match(reference, candidate, SampleRate, new AudioSampleMatchOptions
            {
                SearchStartSeconds = 2.5,
                SearchDurationSeconds = 1.0
            });

            match.Matched.Should().BeTrue($"error={match.Error}, score={match.Score}, lag={match.LagSamples}");
            match.LagSamples.Should().Be(24000);
        }

        [Test]
        public void should_not_match_unrelated_audio()
        {
            var reference = Noise(8000, 4);
            var candidate = Noise(32000, 5);

            var match = AudioSampleMatcher.Match(reference, candidate, SampleRate);

            match.Matched.Should().BeFalse();
        }

        [Test]
        public void should_reject_an_empty_reference()
        {
            var match = AudioSampleMatcher.Match(Array.Empty<short>(), Noise(8000, 6), SampleRate);

            match.Matched.Should().BeFalse();
            match.Error.Should().NotBeNullOrWhiteSpace();
        }

        [Test]
        public void should_reject_an_empty_candidate()
        {
            var match = AudioSampleMatcher.Match(Noise(8000, 7), Array.Empty<short>(), SampleRate);

            match.Matched.Should().BeFalse();
            match.Error.Should().NotBeNullOrWhiteSpace();
        }

        private static short[] Noise(int length, int seed)
        {
            var random = new Random(seed);
            var samples = new short[length];

            for (var index = 0; index < length; index++)
            {
                samples[index] = (short)random.Next(-12000, 12000);
            }

            return samples;
        }
    }
}
