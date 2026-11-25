using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.MediaFiles.AudioTracks
{
    public class AudioTrackListResult
    {
        public string Path { get; set; }
        public string ContainerFormat { get; set; }
        public double DurationSeconds { get; set; }
        public List<AudioTrackInfo> Tracks { get; set; } = new List<AudioTrackInfo>();
        public AudioTrackOrderResult Order { get; set; }
        public string Error { get; set; }
        public bool Success => Error.IsNullOrWhiteSpace();
    }

    public class AudioTrackReorderRequest
    {
        public string SourcePath { get; set; }
        public string OutputPath { get; set; }
        public AudioTrackOrderOptions OrderOptions { get; set; } = new AudioTrackOrderOptions();
        public bool DryRun { get; set; }
        public bool IncludeCopiedStreams { get; set; } = true;
        public bool ReplaceOriginal { get; set; }
    }

    public class AudioTrackReorderResult
    {
        public string SourcePath { get; set; }
        public string OutputPath { get; set; }
        public string BackupPath { get; set; }
        public string Command { get; set; }
        public bool Executed { get; set; }
        public bool ReplacedOriginal { get; set; }
        public long OutputSize { get; set; }
        public AudioTrackOrderResult Order { get; set; }
        public string Error { get; set; }
        public bool Success => Error.IsNullOrWhiteSpace();
    }

    public class AudioSamplePointResult
    {
        public double PositionSeconds { get; set; }
        public bool Matched { get; set; }
        public double OffsetSeconds { get; set; }
        public double Score { get; set; }
        public string Error { get; set; }
    }

    public class AudioTrackMatchResult
    {
        public string Path { get; set; }
        public string Language { get; set; }
        public string Title { get; set; }
        public int AudioIndex { get; set; }
        public AudioTrackInfo Track { get; set; }
        public List<AudioTrackInfo> CandidateTracks { get; set; } = new List<AudioTrackInfo>();
        public List<AudioTrackInfo> MergedTracks { get; set; } = new List<AudioTrackInfo>();
        public bool Matched { get; set; }
        public double OffsetSeconds { get; set; }
        public double Score { get; set; }
        public int SampleCount { get; set; }
        public int MatchedSampleCount { get; set; }
        public string Error { get; set; }
        public List<AudioSamplePointResult> Samples { get; set; } = new List<AudioSamplePointResult>();
    }

    public class AudioTrackMergeRequest
    {
        public string BasePath { get; set; }
        public List<string> AdditionalPaths { get; set; } = new List<string>();
        public string OutputPath { get; set; }
        public AudioTrackOrderOptions OrderOptions { get; set; } = new AudioTrackOrderOptions();
        public bool IncludeBaseAudio { get; set; } = true;
        public bool IncludeCandidateAudio { get; set; } = true;
        public bool IncludeCopiedStreams { get; set; } = true;
        public bool SkipDuplicateTracks { get; set; } = true;
        public bool DryRun { get; set; }
        public bool ReplaceOriginal { get; set; }
        public double SampleSeconds { get; set; } = 2.0;
        public double MinMatchScore { get; set; } = 0.55;
        public double MaxSampleOffsetSeconds { get; set; } = 0.5;
        public int SampleRate { get; set; } = 8000;
        public int MaxSamples { get; set; } = 3;
    }

    public class AudioTrackMergeResult
    {
        public string BasePath { get; set; }
        public string OutputPath { get; set; }
        public string BackupPath { get; set; }
        public string Command { get; set; }
        public bool Executed { get; set; }
        public bool ReplacedOriginal { get; set; }
        public long OutputSize { get; set; }
        public List<AudioTrackMatchResult> Matches { get; set; } = new List<AudioTrackMatchResult>();
        public List<AudioTrackInfo> OutputTracks { get; set; } = new List<AudioTrackInfo>();
        public AudioTrackOrderResult BaseOrder { get; set; }
        public string Error { get; set; }
        public bool Success => Error.IsNullOrWhiteSpace();
    }

    public interface IAudioTrackService
    {
        AudioTrackListResult GetTracks(string path, AudioTrackOrderOptions options = null);
        AudioTrackReorderResult Reorder(AudioTrackReorderRequest request);
        AudioTrackMergeResult Merge(AudioTrackMergeRequest request);
    }

    public class AudioTrackService : IAudioTrackService
    {
        private const string TempSuffix = ".zendarr-audio-tracks.tmp";
        private const string BackupSuffix = ".zendarr-audio-tracks.bak";

        private readonly IAudioTrackProbe _probe;
        private readonly IAudioSampleExtractor _extractor;
        private readonly IAudioTrackCommandBuilder _commandBuilder;
        private readonly IAudioCommandRunner _runner;
        private readonly Logger _logger;

        public AudioTrackService(IAudioTrackProbe probe,
                                 IAudioSampleExtractor extractor,
                                 IAudioTrackCommandBuilder commandBuilder,
                                 IAudioCommandRunner runner,
                                 Logger logger)
        {
            _probe = probe;
            _extractor = extractor;
            _commandBuilder = commandBuilder;
            _runner = runner;
            _logger = logger;
        }

        public AudioTrackListResult GetTracks(string path, AudioTrackOrderOptions options = null)
        {
            var result = new AudioTrackListResult { Path = path };

            if (path.IsNullOrWhiteSpace())
            {
                result.Error = "A file path is required";
                return result;
            }

            if (!File.Exists(path))
            {
                result.Error = $"File not found: {path}";
                return result;
            }

            var probe = _probe.Probe(path);

            if (probe.Error.IsNotNullOrWhiteSpace())
            {
                result.Error = probe.Error;
                return result;
            }

            result.ContainerFormat = probe.ContainerFormat;
            result.DurationSeconds = probe.DurationSeconds;
            result.Tracks = probe.Tracks;
            result.Order = AudioTrackOrdering.Order(probe.Tracks, options ?? new AudioTrackOrderOptions());

            return result;
        }

        public AudioTrackReorderResult Reorder(AudioTrackReorderRequest request)
        {
            var result = new AudioTrackReorderResult();

            if (request == null)
            {
                result.Error = "A request is required";
                return result;
            }

            result.SourcePath = request.SourcePath;

            if (request.SourcePath.IsNullOrWhiteSpace())
            {
                result.Error = "A source path is required";
                return result;
            }

            if (!File.Exists(request.SourcePath))
            {
                result.Error = $"File not found: {request.SourcePath}";
                return result;
            }

            var probe = _probe.Probe(request.SourcePath);

            if (probe.Error.IsNotNullOrWhiteSpace())
            {
                result.Error = probe.Error;
                return result;
            }

            if (probe.Tracks.Count == 0)
            {
                result.Error = "The source file has no audio tracks";
                return result;
            }

            var order = AudioTrackOrdering.Order(probe.Tracks, request.OrderOptions ?? new AudioTrackOrderOptions());
            result.Order = order;

            var outputPath = request.OutputPath.IsNotNullOrWhiteSpace()
                ? request.OutputPath
                : DefaultOutputPath(request.SourcePath, "audio-tracks");

            var replace = request.ReplaceOriginal;

            if (replace)
            {
                if (!CanReplaceInPlace(request.SourcePath))
                {
                    result.Error = "In-place replacement is only supported for .mkv files";
                    return result;
                }

                outputPath = request.SourcePath + TempSuffix + ".mkv";
            }

            var command = _commandBuilder.BuildReorder(new AudioReorderCommandInput
            {
                SourcePath = request.SourcePath,
                OutputPath = outputPath,
                PrimaryVideoStreamIndex = probe.PrimaryVideoStreamIndex,
                Tracks = order.Tracks,
                DefaultAudioIndex = order.DefaultAudioIndex,
                IncludeCopiedStreams = request.IncludeCopiedStreams
            });

            result.Command = command;
            result.OutputPath = replace ? request.SourcePath : outputPath;

            if (request.DryRun)
            {
                return result;
            }

            var run = _runner.Run(command, 1800);

            if (!run.Success)
            {
                result.Error = run.TimedOut ? "ffmpeg timed out" : TrimError(run.StandardError);
                Cleanup(outputPath);
                return result;
            }

            result.Executed = true;

            if (replace)
            {
                var backup = ReplaceFile(request.SourcePath, outputPath, out var replaceError);

                if (replaceError.IsNotNullOrWhiteSpace())
                {
                    result.Error = replaceError;
                    return result;
                }

                result.BackupPath = backup;
                result.ReplacedOriginal = true;
            }

            var finalPath = result.OutputPath;
            result.OutputSize = File.Exists(finalPath) ? new FileInfo(finalPath).Length : 0;

            if (result.OutputSize <= 0)
            {
                result.Error = "ffmpeg produced an empty output file";
            }

            return result;
        }

        public AudioTrackMergeResult Merge(AudioTrackMergeRequest request)
        {
            var result = new AudioTrackMergeResult();

            if (request == null)
            {
                result.Error = "A request is required";
                return result;
            }

            request.SampleRate = request.SampleRate > 0 ? request.SampleRate : 8000;
            request.SampleSeconds = request.SampleSeconds > 0 ? request.SampleSeconds : 2.0;
            request.MaxSamples = request.MaxSamples > 0 ? request.MaxSamples : 3;
            request.MinMatchScore = request.MinMatchScore > 0 ? request.MinMatchScore : 0.55;
            request.MaxSampleOffsetSeconds = request.MaxSampleOffsetSeconds > 0 ? request.MaxSampleOffsetSeconds : 0.5;

            result.BasePath = request.BasePath;

            if (request.BasePath.IsNullOrWhiteSpace())
            {
                result.Error = "A base path is required";
                return result;
            }

            if (!File.Exists(request.BasePath))
            {
                result.Error = $"Base file not found: {request.BasePath}";
                return result;
            }

            var baseProbe = _probe.Probe(request.BasePath);

            if (baseProbe.Error.IsNotNullOrWhiteSpace())
            {
                result.Error = baseProbe.Error;
                return result;
            }

            if (baseProbe.Tracks.Count == 0)
            {
                result.Error = "The base file has no audio tracks";
                return result;
            }

            var options = request.OrderOptions ?? new AudioTrackOrderOptions();
            var baseOrder = AudioTrackOrdering.Order(baseProbe.Tracks, options);
            result.BaseOrder = baseOrder;

            var referenceTrack = baseOrder.Tracks.FirstOrDefault(track => track.IsDefault) ?? baseOrder.Tracks.First();
            var baseDuration = referenceTrack.DurationSeconds > 0 ? referenceTrack.DurationSeconds : baseProbe.DurationSeconds;
            var baseSamples = ExtractBaseSamples(request.BasePath, baseDuration, referenceTrack, request);

            if (baseSamples.Count == 0)
            {
                result.Error = "Could not extract reference samples from the base audio track";
                return result;
            }

            var candidates = new List<AudioMergeCandidateInput>();
            var mergedTracks = new List<AudioTrackInfo>();
            var candidatePaths = (request.AdditionalPaths ?? new List<string>())
                .Where(path => path.IsNotNullOrWhiteSpace())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            foreach (var candidatePath in candidatePaths)
            {
                var match = MatchCandidate(candidatePath, baseSamples, request, options);
                result.Matches.Add(match);

                if (!match.Matched)
                {
                    continue;
                }

                var tracks = request.IncludeCandidateAudio
                    ? match.CandidateTracks ?? new List<AudioTrackInfo>()
                    : new List<AudioTrackInfo>();

                if (request.SkipDuplicateTracks)
                {
                    tracks = tracks
                        .Where(track => !IsDuplicate(baseOrder.Tracks, track) &&
                                        !IsDuplicate(mergedTracks, track))
                        .ToList();
                }

                if (tracks.Count == 0)
                {
                    match.Error = "Every candidate audio track duplicates a track already present";
                    continue;
                }

                foreach (var track in tracks)
                {
                    track.IsDefault = false;
                }

                match.MergedTracks = tracks;
                mergedTracks.AddRange(tracks);

                candidates.Add(new AudioMergeCandidateInput
                {
                    Path = candidatePath,
                    ReferenceTrack = match.Track,
                    Tracks = tracks,
                    OffsetSeconds = match.OffsetSeconds
                });
            }

            if (candidates.Count == 0)
            {
                var matched = result.Matches.Any(match => match.Matched);
                result.Error = matched
                    ? "No new candidate audio tracks to merge"
                    : "No candidate audio tracks matched the base audio";
                return result;
            }

            var outputPath = request.OutputPath.IsNotNullOrWhiteSpace()
                ? request.OutputPath
                : DefaultOutputPath(request.BasePath, "merged-audio");

            var replace = request.ReplaceOriginal;

            if (replace)
            {
                if (!CanReplaceInPlace(request.BasePath))
                {
                    result.Error = "In-place replacement is only supported for .mkv files";
                    return result;
                }

                outputPath = request.BasePath + TempSuffix + ".mkv";
            }

            var command = _commandBuilder.BuildMerge(new AudioMergeCommandInput
            {
                BasePath = request.BasePath,
                OutputPath = outputPath,
                PrimaryVideoStreamIndex = baseProbe.PrimaryVideoStreamIndex,
                BaseTracks = baseOrder.Tracks,
                Candidates = candidates,
                IncludeBaseAudio = request.IncludeBaseAudio,
                IncludeCopiedStreams = request.IncludeCopiedStreams,
                DurationSeconds = baseProbe.DurationSeconds
            });

            result.Command = command;
            result.OutputPath = replace ? request.BasePath : outputPath;

            if (request.DryRun)
            {
                return result;
            }

            var run = _runner.Run(command, 1800);

            if (!run.Success)
            {
                result.Error = run.TimedOut ? "ffmpeg timed out" : TrimError(run.StandardError);
                Cleanup(outputPath);
                return result;
            }

            result.Executed = true;

            if (replace)
            {
                var backup = ReplaceFile(request.BasePath, outputPath, out var replaceError);

                if (replaceError.IsNotNullOrWhiteSpace())
                {
                    result.Error = replaceError;
                    return result;
                }

                result.BackupPath = backup;
                result.ReplacedOriginal = true;
            }

            var finalPath = result.OutputPath;
            result.OutputSize = File.Exists(finalPath) ? new FileInfo(finalPath).Length : 0;

            if (result.OutputSize <= 0)
            {
                result.Error = "ffmpeg produced an empty output file";
                return result;
            }

            var outputProbe = _probe.Probe(finalPath);
            result.OutputTracks = outputProbe.Tracks;

            return result;
        }

        private List<BaseSample> ExtractBaseSamples(string path, double durationSeconds, AudioTrackInfo track, AudioTrackMergeRequest request)
        {
            var samples = new List<BaseSample>();
            var sampleSeconds = Math.Max(0.25, request.SampleSeconds);
            var positions = GetSamplePositions(durationSeconds, sampleSeconds, request.MaxSamples);

            foreach (var position in positions)
            {
                var extraction = _extractor.Extract(path, track.AudioIndex, position, sampleSeconds, request.SampleRate);

                if (extraction.Success)
                {
                    samples.Add(new BaseSample
                    {
                        PositionSeconds = position,
                        Samples = extraction.Samples
                    });
                }
            }

            return samples;
        }

        private AudioTrackMatchResult MatchCandidate(string path, List<BaseSample> baseSamples, AudioTrackMergeRequest request, AudioTrackOrderOptions options)
        {
            var result = new AudioTrackMatchResult { Path = path };

            if (!File.Exists(path))
            {
                result.Error = $"File not found: {path}";
                return result;
            }

            var probe = _probe.Probe(path);

            if (probe.Error.IsNotNullOrWhiteSpace())
            {
                result.Error = probe.Error;
                return result;
            }

            if (probe.Tracks.Count == 0)
            {
                result.Error = "The candidate file has no audio tracks";
                return result;
            }

            var order = AudioTrackOrdering.Order(probe.Tracks, options);
            var bestFailure = new AudioTrackMatchResult
            {
                Path = path,
                CandidateTracks = order.Tracks,
                Error = "No audio track matched"
            };
            var bestScore = double.NegativeInfinity;

            foreach (var track in order.Tracks)
            {
                var candidateDuration = track.DurationSeconds > 0 ? track.DurationSeconds : probe.DurationSeconds;
                var extraction = _extractor.Extract(path, track.AudioIndex, 0, candidateDuration, request.SampleRate);

                if (!extraction.Success)
                {
                    bestFailure = new AudioTrackMatchResult
                    {
                        Path = path,
                        Error = extraction.Error,
                        CandidateTracks = order.Tracks
                    };
                    continue;
                }

                var match = MatchSamples(baseSamples, extraction.Samples, request);
                match.Path = path;
                match.AudioIndex = track.AudioIndex;
                match.Language = track.Language;
                match.Title = track.Title;
                match.Track = track;
                match.CandidateTracks = order.Tracks;

                if (match.Matched)
                {
                    return match;
                }

                if (match.Score > bestScore)
                {
                    bestScore = match.Score;
                    bestFailure = match;
                }
            }

            return bestFailure;
        }

        private AudioTrackMatchResult MatchSamples(List<BaseSample> baseSamples, short[] candidateSamples, AudioTrackMergeRequest request)
        {
            var result = new AudioTrackMatchResult { SampleCount = baseSamples.Count };
            var offsets = new List<double>();
            var scores = new List<double>();
            var firstOffset = (double?)null;

            foreach (var sample in baseSamples)
            {
                var options = new AudioSampleMatchOptions
                {
                    MinScore = request.MinMatchScore
                };

                if (firstOffset.HasValue)
                {
                    var expectedLag = sample.PositionSeconds - firstOffset.Value;
                    var searchStart = Math.Max(0, expectedLag - 10);
                    options.SearchStartSeconds = searchStart;
                    options.SearchDurationSeconds = 20;
                }

                var match = AudioSampleMatcher.Match(sample.Samples, candidateSamples, request.SampleRate, options);
                var point = new AudioSamplePointResult
                {
                    PositionSeconds = sample.PositionSeconds,
                    Matched = match.Matched,
                    Score = match.Score,
                    Error = match.Error
                };

                if (match.Matched)
                {
                    var offset = sample.PositionSeconds - match.LagSeconds;
                    point.OffsetSeconds = offset;
                    offsets.Add(offset);
                    scores.Add(match.Score);
                    firstOffset ??= offset;
                }

                result.Samples.Add(point);
            }

            result.MatchedSampleCount = offsets.Count;

            if (offsets.Count == 0)
            {
                result.Error = "No sample matched";
                return result;
            }

            var required = Math.Min(2, baseSamples.Count);

            if (offsets.Count < required)
            {
                result.Error = $"Only {offsets.Count} of {baseSamples.Count} samples matched";
                return result;
            }

            var sorted = offsets.OrderBy(value => value).ToList();
            var median = sorted.Count % 2 == 1
                ? sorted[sorted.Count / 2]
                : (sorted[(sorted.Count / 2) - 1] + sorted[sorted.Count / 2]) / 2.0;

            var maxDeviation = offsets.Max(offset => Math.Abs(offset - median));

            if (maxDeviation > request.MaxSampleOffsetSeconds)
            {
                result.Error = $"Sample offsets disagree by {maxDeviation:0.###}s";
                return result;
            }

            result.OffsetSeconds = median;
            result.Score = scores.Average();
            result.Matched = result.Score >= request.MinMatchScore;

            if (!result.Matched)
            {
                result.Error = $"Average correlation score {result.Score:0.###} is below {request.MinMatchScore:0.###}";
            }

            return result;
        }

        private static List<double> GetSamplePositions(double durationSeconds, double sampleSeconds, int maxSamples)
        {
            var positions = new List<double>();

            if (durationSeconds <= 0)
            {
                positions.Add(0);
                return positions;
            }

            var usable = Math.Max(0, durationSeconds - sampleSeconds);

            if (usable <= 0)
            {
                positions.Add(0);
                return positions;
            }

            var fractions = new[] { 0.1, 0.35, 0.6, 0.85 };
            var count = Math.Max(1, Math.Min(maxSamples, fractions.Length));

            for (var index = 0; index < count; index++)
            {
                var position = Math.Min(usable, usable * fractions[index]);
                position = Math.Round(position, 3);

                if (!positions.Any(existing => Math.Abs(existing - position) < 0.01))
                {
                    positions.Add(position);
                }
            }

            if (positions.Count == 0)
            {
                positions.Add(0);
            }

            return positions;
        }

        private static bool IsDuplicate(IEnumerable<AudioTrackInfo> existing, AudioTrackInfo candidate)
        {
            if (existing == null || candidate == null)
            {
                return false;
            }

            var language = AudioLanguage.Canonicalize(candidate.Language);
            var title = (candidate.Title ?? string.Empty).Trim();

            return existing.Any(track =>
                AudioLanguage.Canonicalize(track.Language) == language &&
                string.Equals((track.Title ?? string.Empty).Trim(), title, StringComparison.OrdinalIgnoreCase) &&
                track.Channels == candidate.Channels);
        }

        private static bool CanReplaceInPlace(string path)
        {
            return string.Equals(Path.GetExtension(path), ".mkv", StringComparison.OrdinalIgnoreCase);
        }

        private static string ReplaceFile(string sourcePath, string replacementPath, out string error)
        {
            error = null;
            var backupPath = sourcePath + BackupSuffix;

            try
            {
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }

                File.Move(sourcePath, backupPath);
                File.Move(replacementPath, sourcePath);

                return backupPath;
            }
            catch (Exception ex)
            {
                error = ex.Message;

                try
                {
                    if (!File.Exists(sourcePath) && File.Exists(backupPath))
                    {
                        File.Move(backupPath, sourcePath);
                    }
                }
                catch
                {
                    // The original error is more useful than a restore error.
                }

                return null;
            }
        }

        private static string DefaultOutputPath(string sourcePath, string suffix)
        {
            var directory = Path.GetDirectoryName(sourcePath);
            var name = Path.GetFileNameWithoutExtension(sourcePath);
            var candidate = Path.Combine(directory ?? string.Empty, $"{name}.{suffix}.mkv");

            if (!File.Exists(candidate))
            {
                return candidate;
            }

            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
            return Path.Combine(directory ?? string.Empty, $"{name}.{suffix}.{timestamp}.mkv");
        }

        private static void Cleanup(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best effort; the caller gets the ffmpeg error, not a cleanup error.
            }
        }

        private static string TrimError(string error)
        {
            if (error.IsNullOrWhiteSpace())
            {
                return "ffmpeg failed";
            }

            var trimmed = error.Trim();
            return trimmed.Length <= 4000 ? trimmed : trimmed.Substring(trimmed.Length - 4000);
        }

        private class BaseSample
        {
            public double PositionSeconds { get; set; }
            public short[] Samples { get; set; }
        }
    }
}
