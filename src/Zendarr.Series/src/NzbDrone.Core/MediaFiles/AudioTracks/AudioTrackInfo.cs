using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MediaFiles.AudioTracks
{
    // A single audio stream as reported by ffprobe. The audio ordinal is the N in "-map 0:a:N";
    // the absolute stream index is kept for diagnostics only.
    public class AudioTrackInfo
    {
        public int AudioIndex { get; set; }
        public int StreamIndex { get; set; }
        public string Language { get; set; }
        public string LanguageName { get; set; }
        public string Title { get; set; }
        public string Codec { get; set; }
        public string Profile { get; set; }
        public long Bitrate { get; set; }
        public int Channels { get; set; }
        public int SampleRate { get; set; }
        public double DurationSeconds { get; set; }
        public bool IsDefault { get; set; }
        public bool IsHearingImpaired { get; set; }
        public bool IsCommentary { get; set; }

        public string DisplayName
        {
            get
            {
                if (Title.IsNotNullOrWhiteSpace())
                {
                    return Title;
                }

                return LanguageName.IsNotNullOrWhiteSpace() ? LanguageName : "Unknown";
            }
        }
    }

    public enum AudioTrackOrderField
    {
        Language = 0,
        Name = 1,
        Bitrate = 2,
        Source = 3
    }

    public class AudioTrackOrderOptions
    {
        public AudioTrackOrderOptions()
        {
            PreferredLanguages = new List<string> { "eng" };
        }

        // Language codes in priority order (ISO 639-1 or 639-2, case-insensitive). English first by
        // default; the owner can put "spa" first for a Spanish-first library.
        public List<string> PreferredLanguages { get; set; }

        public AudioTrackOrderField OrderBy { get; set; } = AudioTrackOrderField.Language;

        public bool Descending { get; set; }

        // Optional explicit default language. When null, the first preferred track is the default.
        public string DefaultLanguage { get; set; }

        // Optional explicit default audio ordinal. Wins over DefaultLanguage and the preferred list.
        public int? DefaultAudioIndex { get; set; }

        public bool PreserveDefaultWhenNoPreferred { get; set; } = true;
    }

    public class AudioTrackOrderResult
    {
        public List<AudioTrackInfo> Tracks { get; set; } = new List<AudioTrackInfo>();
        public int DefaultAudioIndex { get; set; }
        public string DefaultLanguage { get; set; }
        public bool PreferredLanguageFound { get; set; }
    }

    // Normalises the language tags ffprobe writes (eng, en, English, spa, es, ...) and provides the
    // English display name used for "order by language name". The app's Language model is not used
    // here because audio stream tags are ISO 639 codes, not the app's internal language enum.
    public static class AudioLanguage
    {
        private static readonly Dictionary<string, string> Canonical = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "eng", "en" }, { "en", "en" }, { "english", "en" },
            { "spa", "es" }, { "es", "es" }, { "spanish", "es" }, { "castellano", "es" },
            { "fre", "fr" }, { "fra", "fr" }, { "fr", "fr" }, { "french", "fr" },
            { "ger", "de" }, { "deu", "de" }, { "de", "de" }, { "german", "de" }, { "deutsch", "de" },
            { "ita", "it" }, { "it", "it" }, { "italian", "it" },
            { "por", "pt" }, { "pt", "pt" }, { "portuguese", "pt" },
            { "rus", "ru" }, { "ru", "ru" }, { "russian", "ru" },
            { "jpn", "ja" }, { "ja", "ja" }, { "japanese", "ja" },
            { "kor", "ko" }, { "ko", "ko" }, { "korean", "ko" },
            { "chi", "zh" }, { "zho", "zh" }, { "zh", "zh" }, { "chinese", "zh" }, { "mandarin", "zh" },
            { "ara", "ar" }, { "ar", "ar" }, { "arabic", "ar" },
            { "hin", "hi" }, { "hi", "hi" }, { "hindi", "hi" },
            { "dut", "nl" }, { "nld", "nl" }, { "nl", "nl" }, { "dutch", "nl" },
            { "swe", "sv" }, { "sv", "sv" }, { "swedish", "sv" },
            { "nor", "no" }, { "no", "no" }, { "norwegian", "no" },
            { "dan", "da" }, { "da", "da" }, { "danish", "da" },
            { "fin", "fi" }, { "fi", "fi" }, { "finnish", "fi" },
            { "pol", "pl" }, { "pl", "pl" }, { "polish", "pl" },
            { "tur", "tr" }, { "tr", "tr" }, { "turkish", "tr" },
            { "gre", "el" }, { "ell", "el" }, { "el", "el" }, { "greek", "el" },
            { "heb", "he" }, { "he", "he" }, { "hebrew", "he" },
            { "ukr", "uk" }, { "uk", "uk" }, { "ukrainian", "uk" },
            { "ces", "cs" }, { "cze", "cs" }, { "cs", "cs" }, { "czech", "cs" },
            { "hun", "hu" }, { "hu", "hu" }, { "hungarian", "hu" },
            { "ron", "ro" }, { "rum", "ro" }, { "ro", "ro" }, { "romanian", "ro" },
            { "tha", "th" }, { "th", "th" }, { "thai", "th" },
            { "vie", "vi" }, { "vi", "vi" }, { "vietnamese", "vi" },
            { "ind", "id" }, { "id", "id" }, { "indonesian", "id" },
            { "msa", "ms" }, { "may", "ms" }, { "ms", "ms" }, { "malay", "ms" },
            { "und", "und" }, { "unknown", "und" }, { "undetermined", "und" },
            { "mul", "mul" }, { "multiple", "mul" }
        };

        private static readonly Dictionary<string, string> DisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "en", "English" }, { "es", "Spanish" }, { "fr", "French" }, { "de", "German" },
            { "it", "Italian" }, { "pt", "Portuguese" }, { "ru", "Russian" }, { "ja", "Japanese" },
            { "ko", "Korean" }, { "zh", "Chinese" }, { "ar", "Arabic" }, { "hi", "Hindi" },
            { "nl", "Dutch" }, { "sv", "Swedish" }, { "no", "Norwegian" }, { "da", "Danish" },
            { "fi", "Finnish" }, { "pl", "Polish" }, { "tr", "Turkish" }, { "el", "Greek" },
            { "he", "Hebrew" }, { "uk", "Ukrainian" }, { "cs", "Czech" }, { "hu", "Hungarian" },
            { "ro", "Romanian" }, { "th", "Thai" }, { "vi", "Vietnamese" }, { "id", "Indonesian" },
            { "ms", "Malay" }, { "und", "Undetermined" }, { "mul", "Multiple" }
        };

        public static string Canonicalize(string language)
        {
            if (language.IsNullOrWhiteSpace())
            {
                return "und";
            }

            var trimmed = language.Trim();

            // Handle "en-US", "pt-BR", "es-419" and "eng (English)" forms.
            var separator = trimmed.IndexOfAny(new[] { '-', '_', ' ', '(', ';' });

            if (separator > 0)
            {
                trimmed = trimmed.Substring(0, separator);
            }

            if (Canonical.TryGetValue(trimmed, out var canonical))
            {
                return canonical;
            }

            return trimmed.ToLowerInvariant();
        }

        public static string GetDisplayName(string language)
        {
            var canonical = Canonicalize(language);

            if (DisplayNames.TryGetValue(canonical, out var name))
            {
                return name;
            }

            try
            {
                var culture = CultureInfo.GetCultureInfo(canonical);
                return culture.EnglishName;
            }
            catch (CultureNotFoundException)
            {
                return canonical.ToUpperInvariant();
            }
        }

        public static bool IsPreferred(string language, IReadOnlyList<string> preferredLanguages)
        {
            if (preferredLanguages == null || preferredLanguages.Count == 0)
            {
                return false;
            }

            var canonical = Canonicalize(language);

            return preferredLanguages.Any(preferred => Canonicalize(preferred) == canonical);
        }

        public static int PreferenceRank(string language, IReadOnlyList<string> preferredLanguages)
        {
            if (preferredLanguages == null)
            {
                return int.MaxValue;
            }

            var canonical = Canonicalize(language);

            for (var index = 0; index < preferredLanguages.Count; index++)
            {
                if (Canonicalize(preferredLanguages[index]) == canonical)
                {
                    return index;
                }
            }

            return int.MaxValue;
        }
    }

    public static class AudioTrackOrdering
    {
        public static AudioTrackOrderResult Order(IEnumerable<AudioTrackInfo> sourceTracks, AudioTrackOrderOptions options)
        {
            options = options ?? new AudioTrackOrderOptions();
            var preferredLanguages = options.PreferredLanguages ?? new List<string>();

            var tracks = (sourceTracks ?? Enumerable.Empty<AudioTrackInfo>())
                .Select(track => new AudioTrackInfo
                {
                    AudioIndex = track.AudioIndex,
                    StreamIndex = track.StreamIndex,
                    Language = track.Language,
                    LanguageName = AudioLanguage.GetDisplayName(track.Language),
                    Title = track.Title,
                    Codec = track.Codec,
                    Profile = track.Profile,
                    Bitrate = track.Bitrate,
                    Channels = track.Channels,
                    SampleRate = track.SampleRate,
                    DurationSeconds = track.DurationSeconds,
                    IsDefault = track.IsDefault,
                    IsHearingImpaired = track.IsHearingImpaired,
                    IsCommentary = track.IsCommentary
                })
                .ToList();

            var defaultLanguage = options.DefaultLanguage.IsNotNullOrWhiteSpace()
                ? AudioLanguage.Canonicalize(options.DefaultLanguage)
                : null;

            // Pick the default before sorting. An explicit audio ordinal wins over language
            // preferences; otherwise an explicit DefaultLanguage wins over the preferred list. If
            // neither matches, keep the source default (or the first track) when asked to.
            var preferredLanguageTrack = tracks
                .Where(track => AudioLanguage.IsPreferred(track.Language, preferredLanguages))
                .OrderBy(track => AudioLanguage.PreferenceRank(track.Language, preferredLanguages))
                .FirstOrDefault();

            var preferred = options.DefaultAudioIndex.HasValue
                ? tracks.FirstOrDefault(track => track.AudioIndex == options.DefaultAudioIndex.Value)
                : defaultLanguage != null
                    ? tracks.FirstOrDefault(track => AudioLanguage.Canonicalize(track.Language) == defaultLanguage)
                    : preferredLanguageTrack;

            var preferredFound = preferredLanguageTrack != null;

            if (preferred == null && options.PreserveDefaultWhenNoPreferred)
            {
                preferred = tracks.FirstOrDefault(track => track.IsDefault) ?? tracks.FirstOrDefault();
            }

            preferred ??= tracks.FirstOrDefault();

            // The chosen default is pinned first; the rest are ordered by the preferred-language
            // rank and then the requested field (language name, title, bitrate or source order).
            var ranked = tracks
                .OrderByDescending(track => ReferenceEquals(track, preferred))
                .ThenBy(track => AudioLanguage.PreferenceRank(track.Language, preferredLanguages));

            var ordered = options.Descending
                ? ranked.ThenByDescending(track => OrderKey(track, options.OrderBy), StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(track => track.AudioIndex)
                    .ToList()
                : ranked.ThenBy(track => OrderKey(track, options.OrderBy), StringComparer.OrdinalIgnoreCase)
                    .ThenBy(track => track.AudioIndex)
                    .ToList();

            foreach (var track in ordered)
            {
                track.IsDefault = ReferenceEquals(track, preferred);
            }

            return new AudioTrackOrderResult
            {
                Tracks = ordered,
                DefaultAudioIndex = preferred?.AudioIndex ?? -1,
                DefaultLanguage = preferred?.Language,
                PreferredLanguageFound = preferredFound
            };
        }

        private static string OrderKey(AudioTrackInfo track, AudioTrackOrderField orderBy)
        {
            var language = track.LanguageName ?? string.Empty;
            var name = track.DisplayName ?? string.Empty;

            return orderBy switch
            {
                AudioTrackOrderField.Name => $"{name}\u0000{language}",
                AudioTrackOrderField.Bitrate => track.Bitrate.ToString("D12", CultureInfo.InvariantCulture),
                AudioTrackOrderField.Source => track.AudioIndex.ToString("D12", CultureInfo.InvariantCulture),
                _ => $"{language}\u0000{name}"
            };
        }
    }
}
