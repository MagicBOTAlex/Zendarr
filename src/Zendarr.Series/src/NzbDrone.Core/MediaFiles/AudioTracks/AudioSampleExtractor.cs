using System;
using System.IO;
using NLog;
using NzbDrone.Core.MediaFiles.Transcoding;

namespace NzbDrone.Core.MediaFiles.AudioTracks
{
    public class AudioSampleExtractionResult
    {
        public short[] Samples { get; set; }
        public string Error { get; set; }
        public bool Success => Samples != null && Samples.Length > 0;
    }

    public interface IAudioSampleExtractor
    {
        AudioSampleExtractionResult Extract(string path, int audioIndex, double startSeconds, double durationSeconds, int sampleRate);
    }

    // Decodes one audio stream to mono 16-bit PCM at a fixed sample rate. The audio is written to a
    // temporary raw file (ffmpeg's stdout is not safe for binary through the process wrapper) and
    // read back as shorts for the cross-correlator.
    public class AudioSampleExtractor : IAudioSampleExtractor
    {
        private readonly IAudioCommandRunner _runner;
        private readonly Logger _logger;

        public AudioSampleExtractor(IAudioCommandRunner runner, Logger logger)
        {
            _runner = runner;
            _logger = logger;
        }

        public AudioSampleExtractionResult Extract(string path, int audioIndex, double startSeconds, double durationSeconds, int sampleRate)
        {
            var folder = Path.Combine(Path.GetTempPath(), "zendarr-audio-tracks");
            var rawPath = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".raw");

            try
            {
                Directory.CreateDirectory(folder);

                var seek = startSeconds > 0 ? $"-ss {FormatSeconds(startSeconds)} " : string.Empty;
                var duration = durationSeconds > 0 ? $"-t {FormatSeconds(durationSeconds)} " : string.Empty;
                var arguments = $"{seek}{duration}-i {TranscodeArgumentBuilder.Quote(path)} -map 0:a:{audioIndex} -ac 1 -ar {sampleRate} -f s16le -acodec pcm_s16le {TranscodeArgumentBuilder.Quote(rawPath)}";

                var result = _runner.Run(arguments, 1800);

                if (!result.Success)
                {
                    return new AudioSampleExtractionResult
                    {
                        Error = result.TimedOut ? "ffmpeg timed out extracting audio" : TrimError(result.StandardError)
                    };
                }

                if (!File.Exists(rawPath))
                {
                    return new AudioSampleExtractionResult { Error = "ffmpeg did not create the PCM sample" };
                }

                var bytes = File.ReadAllBytes(rawPath);

                if (bytes.Length < 2)
                {
                    return new AudioSampleExtractionResult { Error = "ffmpeg produced an empty PCM sample" };
                }

                var samples = new short[bytes.Length / 2];
                Buffer.BlockCopy(bytes, 0, samples, 0, samples.Length * 2);

                return new AudioSampleExtractionResult { Samples = samples };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to extract audio samples from {0}", path);
                return new AudioSampleExtractionResult { Error = ex.Message };
            }
            finally
            {
                try
                {
                    if (File.Exists(rawPath))
                    {
                        File.Delete(rawPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Unable to delete temporary PCM sample {0}", rawPath);
                }
            }
        }

        private static string FormatSeconds(double seconds)
        {
            return seconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        private static string TrimError(string error)
        {
            if (error == null)
            {
                return "ffmpeg failed";
            }

            var trimmed = error.Trim();
            return trimmed.Length <= 2000 ? trimmed : trimmed.Substring(trimmed.Length - 2000);
        }
    }
}
