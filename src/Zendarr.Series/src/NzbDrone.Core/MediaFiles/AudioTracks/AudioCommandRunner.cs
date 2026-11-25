using System;
using System.Text;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Processes;
using NzbDrone.Core.MediaFiles.Transcoding;

namespace NzbDrone.Core.MediaFiles.AudioTracks
{
    public class AudioCommandResult
    {
        public int ExitCode { get; set; }
        public bool TimedOut { get; set; }
        public string StandardOutput { get; set; }
        public string StandardError { get; set; }
        public string Command { get; set; }

        public bool Success => !TimedOut && ExitCode == 0;
    }

    public interface IAudioCommandRunner
    {
        AudioCommandResult Run(string arguments, int timeoutSeconds = 600);
    }

    // Small, synchronous ffmpeg runner for audio-track remux/merge work. The transcode runner is
    // built around progress parsing and a job row; this slice only needs "run this command and give
    // me the exit code and stderr", so it stays independent of the transcode queue.
    public class AudioCommandRunner : IAudioCommandRunner
    {
        private readonly IFFmpegProvider _ffmpegProvider;
        private readonly IProcessProvider _processProvider;
        private readonly Logger _logger;

        public AudioCommandRunner(IFFmpegProvider ffmpegProvider, IProcessProvider processProvider, Logger logger)
        {
            _ffmpegProvider = ffmpegProvider;
            _processProvider = processProvider;
            _logger = logger;
        }

        public AudioCommandResult Run(string arguments, int timeoutSeconds = 600)
        {
            var ffmpeg = _ffmpegProvider.GetFFmpegPath();
            var result = new AudioCommandResult
            {
                Command = arguments,
                ExitCode = -1
            };

            if (ffmpeg.IsNullOrWhiteSpace())
            {
                result.StandardError = "ffmpeg was not found";
                return result;
            }

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            try
            {
                var process = _processProvider.Start(
                    ffmpeg,
                    "-hide_banner -nostdin -y " + arguments,
                    null,
                    line =>
                    {
                        lock (stdout)
                        {
                            stdout.AppendLine(line);
                        }
                    },
                    line =>
                    {
                        lock (stderr)
                        {
                            stderr.AppendLine(line);
                        }
                    },
                    logOutput: false);

                var exited = process.WaitForExit(Math.Max(1, timeoutSeconds) * 1000);

                if (!exited)
                {
                    result.TimedOut = true;

                    try
                    {
                        process.Kill();
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Unable to kill timed-out ffmpeg process");
                    }

                    result.StandardError = "ffmpeg timed out";
                    return result;
                }

                // Flush the asynchronous output callbacks before reading the buffers.
                process.WaitForExit();

                result.ExitCode = process.ExitCode;
                result.StandardOutput = stdout.ToString();
                result.StandardError = stderr.ToString();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to run ffmpeg audio command");
                result.StandardError = ex.Message;
            }

            return result;
        }
    }
}
