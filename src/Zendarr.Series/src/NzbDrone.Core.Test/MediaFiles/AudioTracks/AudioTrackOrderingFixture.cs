using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.AudioTracks;

namespace NzbDrone.Core.Test.MediaFiles.AudioTracks
{
    [TestFixture]
    public class AudioTrackOrderingFixture
    {
        private static AudioTrackInfo Track(int index, string language, string title = null, int channels = 2)
        {
            return new AudioTrackInfo
            {
                AudioIndex = index,
                StreamIndex = index + 1,
                Language = language,
                Title = title,
                Channels = channels,
                Codec = "aac"
            };
        }

        [Test]
        public void should_put_english_first_and_make_it_default()
        {
            var tracks = new List<AudioTrackInfo>
            {
                Track(0, "spa", "Spanish"),
                Track(1, "eng", "English")
            };

            var result = AudioTrackOrdering.Order(tracks, new AudioTrackOrderOptions());

            result.Tracks.Select(track => track.AudioIndex).Should().Equal(1, 0);
            result.Tracks[0].IsDefault.Should().BeTrue();
            result.Tracks[1].IsDefault.Should().BeFalse();
            result.DefaultAudioIndex.Should().Be(1);
            result.DefaultLanguage.Should().Be("eng");
            result.PreferredLanguageFound.Should().BeTrue();
        }

        [Test]
        public void should_keep_a_custom_preferred_language_order()
        {
            var tracks = new List<AudioTrackInfo>
            {
                Track(0, "eng"),
                Track(1, "spa"),
                Track(2, "fra")
            };

            var result = AudioTrackOrdering.Order(tracks, new AudioTrackOrderOptions
            {
                DefaultLanguage = "spa",
                PreferredLanguages = new List<string> { "spa" }
            });

            result.Tracks.Select(track => track.AudioIndex).Should().Equal(1, 0, 2);
            result.Tracks[0].IsDefault.Should().BeTrue();
            result.DefaultAudioIndex.Should().Be(1);
            result.DefaultLanguage.Should().Be("spa");
        }

        [Test]
        public void should_sort_by_language_name_then_title()
        {
            var tracks = new List<AudioTrackInfo>
            {
                Track(0, "spa", "Latin"),
                Track(1, "eng", "Commentary"),
                Track(2, "eng", "Main")
            };

            var result = AudioTrackOrdering.Order(tracks, new AudioTrackOrderOptions
            {
                OrderBy = AudioTrackOrderField.Language
            });

            result.Tracks.Select(track => track.AudioIndex).Should().Equal(1, 2, 0);
            result.Tracks[0].IsDefault.Should().BeTrue();
        }

        [Test]
        public void should_sort_by_title_when_requested()
        {
            var tracks = new List<AudioTrackInfo>
            {
                Track(0, "spa", "Zeta"),
                Track(1, "eng", "Alpha"),
                Track(2, "eng", "Beta")
            };

            var result = AudioTrackOrdering.Order(tracks, new AudioTrackOrderOptions
            {
                OrderBy = AudioTrackOrderField.Name
            });

            result.Tracks.Select(track => track.AudioIndex).Should().Equal(1, 2, 0);
        }

        [Test]
        public void should_keep_unknown_languages_last()
        {
            var tracks = new List<AudioTrackInfo>
            {
                Track(0, "und"),
                Track(1, "eng"),
                Track(2, "spa")
            };

            var result = AudioTrackOrdering.Order(tracks, new AudioTrackOrderOptions());

            result.Tracks.Select(track => track.AudioIndex).Should().Equal(1, 2, 0);
        }

        [Test]
        public void should_fall_back_to_the_source_default_when_no_preferred_language_matches()
        {
            var tracks = new List<AudioTrackInfo>
            {
                Track(0, "fra"),
                Track(1, "deu")
            };

            tracks[1].IsDefault = true;

            var result = AudioTrackOrdering.Order(tracks, new AudioTrackOrderOptions
            {
                PreferredLanguages = new List<string> { "eng" }
            });

            result.Tracks.Select(track => track.AudioIndex).Should().Equal(1, 0);
            result.Tracks[0].IsDefault.Should().BeTrue();
            result.DefaultAudioIndex.Should().Be(1);
            result.PreferredLanguageFound.Should().BeFalse();
        }

        [Test]
        public void should_honour_an_explicit_default_audio_index()
        {
            var tracks = new List<AudioTrackInfo>
            {
                Track(0, "eng"),
                Track(1, "spa"),
                Track(2, "fra")
            };

            var result = AudioTrackOrdering.Order(tracks, new AudioTrackOrderOptions
            {
                DefaultAudioIndex = 2
            });

            result.Tracks.Select(track => track.AudioIndex).Should().Equal(2, 0, 1);
            result.Tracks[0].IsDefault.Should().BeTrue();
            result.DefaultAudioIndex.Should().Be(2);
        }

        [Test]
        public void should_reverse_the_order_field_when_descending()
        {
            var tracks = new List<AudioTrackInfo>
            {
                Track(0, "eng", "Alpha"),
                Track(1, "eng", "Beta"),
                Track(2, "eng", "Gamma")
            };

            var result = AudioTrackOrdering.Order(tracks, new AudioTrackOrderOptions
            {
                OrderBy = AudioTrackOrderField.Name,
                Descending = true
            });

            // The default (Alpha) stays first; the remaining names are reversed.
            result.Tracks.Select(track => track.AudioIndex).Should().Equal(0, 2, 1);
        }

        [TestCase("eng", "en")]
        [TestCase("en", "en")]
        [TestCase("English", "en")]
        [TestCase("spa", "es")]
        [TestCase("Spanish", "es")]
        [TestCase("und", "und")]
        [TestCase("", "und")]
        public void should_canonicalize_language_codes(string input, string expected)
        {
            AudioLanguage.Canonicalize(input).Should().Be(expected);
        }

        [TestCase("eng", "English")]
        [TestCase("spa", "Spanish")]
        [TestCase("und", "Undetermined")]
        public void should_map_language_codes_to_display_names(string input, string expected)
        {
            AudioLanguage.GetDisplayName(input).Should().Be(expected);
        }
    }
}
