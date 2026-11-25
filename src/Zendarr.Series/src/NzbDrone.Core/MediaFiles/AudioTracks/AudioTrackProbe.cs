using System;
using System.Collections.Generic;
using System.Linq;
using FFMpegCore;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles.MediaInfo;

namespace NzbDrone.Core.MediaFiles.AudioTracks
{
    public class AudioTrackProbeResult
    {
        public string Path { get; set; }
        public string ContainerFormat { get; set; }
        public double DurationSeconds { get; set; }
        public int PrimaryVideoStreamIndex { get; set; }
        public List<AudioTrackInfo> Tracks { get; set; } = new List<AudioTrackInfo>();
        public string Error { get; set; }
    }

    public interface IAudioTrackProbe
    {
        AudioTrackProbeResult Probe(string path);
    }

    // Reads the audio streams of a file with ffprobe. The transcode pipeline already probes media
    // through VideoFileInfoReader, but that model does not expose the audio ordinal, default flag or
    // sample rate needed to build a remux map, so audio-track management probes directly.
    public class AudioTrackProbe : IAudioTrackProbe
    {
        private readonly Logger _logger;

        public AudioTrackProbe(Logger logger)
        {
            _logger = logger;

            // The ffprobe binary is bundled next to the application (Openur.FFprobeStatic); the
            // existing VideoFileInfoReader configures the same global option. Setting it here as
            // well keeps this service usable when it is resolved before the media-info reader.
            GlobalFFOptions.Configure(options => options.BinaryFolder = AppDomain.CurrentDomain.BaseDirectory);
        }

        public AudioTrackProbeResult Probe(string path)
        {
            var result = new AudioTrackProbeResult { Path = path };

            try
            {
                var analysis = FFProbe.Analyse(path, customArguments: "-probesize 50000000");

                if (analysis == null)
                {
                    result.Error = "ffprobe returned no analysis";
                    return result;
                }

                result.ContainerFormat = analysis.Format?.FormatName;
                result.DurationSeconds = analysis.Duration.TotalSeconds;

                if (result.DurationSeconds <= 0)
                {
                    result.DurationSeconds = analysis.AudioStreams?
                        .Select(stream => stream.Duration.TotalSeconds)
                        .DefaultIfEmpty(0)
                        .Max() ?? 0;
                }

                var primaryVideo = VideoFileInfoReader.GetPrimaryVideoStream(analysis);
                result.PrimaryVideoStreamIndex = primaryVideo != null
                    ? VideoFileInfoReader.GetPrimaryVideoStreamIndex(analysis)
                    : 0;

                var audioStreams = analysis.AudioStreams?
                    .OrderBy(stream => stream.Index)
                    .ToList() ?? new List<AudioStream>();

                for (var index = 0; index < audioStreams.Count; index++)
                {
                    var stream = audioStreams[index];
                    var title = GetTag(stream, "title") ?? GetTag(stream, "name");
                    var disposition = stream.Disposition ?? new Dictionary<string, bool>();

                    result.Tracks.Add(new AudioTrackInfo
                    {
                        AudioIndex = index,
                        StreamIndex = stream.Index,
                        Language = stream.Language.IsNotNullOrWhiteSpace() ? stream.Language : "und",
                        LanguageName = AudioLanguage.GetDisplayName(stream.Language),
                        Title = title,
                        Codec = stream.CodecName,
                        Profile = stream.Profile,
                        Bitrate = stream.BitRate,
                        Channels = stream.Channels,
                        SampleRate = stream.SampleRateHz,
                        DurationSeconds = stream.Duration.TotalSeconds,
                        IsDefault = disposition.TryGetValue("default", out var isDefault) && isDefault,
                        IsHearingImpaired = disposition.TryGetValue("hearing_impaired", out var isHi) && isHi,
                        IsCommentary = (disposition.TryGetValue("comment", out var isComment) && isComment) ||
                                       (title?.IndexOf("commentary", StringComparison.OrdinalIgnoreCase) >= 0)
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to probe audio tracks in {0}", path);
                result.Error = ex.Message;
            }

            return result;
        }

        private static string GetTag(AudioStream stream, string key)
        {
            if (stream.Tags != null && stream.Tags.TryGetValue(key, out var value) && value.IsNotNullOrWhiteSpace())
            {
                return value.Trim();
            }

            return null;
        }
    }
}
