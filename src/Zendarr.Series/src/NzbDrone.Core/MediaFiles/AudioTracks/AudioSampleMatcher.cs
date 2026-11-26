using System;

namespace NzbDrone.Core.MediaFiles.AudioTracks
{
    public class AudioSampleMatchOptions
    {
        public double MinScore { get; set; } = 0.55;

        // Restrict the search to a window of the candidate. Zero duration means the whole file.
        public double SearchStartSeconds { get; set; }
        public double SearchDurationSeconds { get; set; }

        // 200 Hz with a one-sample step keeps the first pass cheap on feature-length tracks while
        // still landing within 2.5 ms of the true lag. The 1 kHz and original-rate refinements then
        // recover sample precision. A 40 ms step skips over the narrow peak of noise-like audio.
        public int CoarseSampleRate { get; set; } = 200;
        public int CoarseStepSamples { get; set; } = 1;
    }

    public class AudioSampleMatch
    {
        public bool Matched { get; set; }
        public double Score { get; set; }
        public double LagSeconds { get; set; }
        public long LagSamples { get; set; }
        public string Error { get; set; }
    }

    // Finds a short reference sample inside a longer candidate audio track using normalised
    // cross-correlation. The search is coarse-to-fine: a 1 kHz mono signal is scanned at 40 ms
    // steps, then the best region is refined at 1 kHz and finally at the original sample rate.
    // This is deliberately sample comparison (not a perceptual fingerprint) so the offset is
    // reported in the same timebase as the muxer's -itsoffset.
    public static class AudioSampleMatcher
    {
        public static AudioSampleMatch Match(short[] reference, short[] candidate, int sampleRate, AudioSampleMatchOptions options = null)
        {
            options = options ?? new AudioSampleMatchOptions();

            if (reference == null || reference.Length == 0)
            {
                return new AudioSampleMatch { Error = "reference sample is empty" };
            }

            if (candidate == null || candidate.Length == 0)
            {
                return new AudioSampleMatch { Error = "candidate audio is empty" };
            }

            if (sampleRate <= 0)
            {
                return new AudioSampleMatch { Error = "sample rate must be positive" };
            }

            var factor = Math.Max(1, sampleRate / Math.Max(1, options.CoarseSampleRate));
            var referenceCoarse = Downsample(reference, factor);
            var candidateCoarse = Downsample(candidate, factor);

            var searchStart = Math.Max(0, (int)Math.Round(options.SearchStartSeconds * sampleRate));
            var searchEnd = candidate.Length;

            if (options.SearchDurationSeconds > 0)
            {
                searchEnd = Math.Min(candidate.Length, searchStart + (int)Math.Round(options.SearchDurationSeconds * sampleRate));
            }

            var coarseStart = searchStart / factor;
            var coarseEnd = Math.Min(candidateCoarse.Length, searchEnd / factor);

            if (referenceCoarse.Length > coarseEnd - coarseStart)
            {
                return new AudioSampleMatch { Error = "candidate is shorter than the reference sample" };
            }

            var best = FindBest(referenceCoarse, candidateCoarse, coarseStart, coarseEnd, options.CoarseStepSamples);

            if (best == null)
            {
                return new AudioSampleMatch { Error = "no candidate audio energy found" };
            }

            // Refine around the coarse peak at 1 kHz on a small slice (the full 1 kHz signal can be
            // hundreds of megabytes for a feature-length track), then at the original sample rate.
            var fineStart = Math.Max(0, best.Lag - options.CoarseStepSamples);
            var fineLength = Math.Min(candidateCoarse.Length - fineStart, referenceCoarse.Length + (options.CoarseStepSamples * 2));
            var fineSlice = Slice(candidateCoarse, fineStart, fineLength);
            var fine = FindBest(referenceCoarse, fineSlice, 0, fineSlice.Length, 1);
            var bestCoarse = fine != null ? fine.Lag + fineStart : best.Lag;

            var originalStart = Math.Max(0, (bestCoarse * factor) - factor);
            var originalLength = Math.Min(candidate.Length - originalStart, reference.Length + (factor * 2));
            var originalSlice = Slice(candidate, originalStart, originalLength);
            var original = FindBest(reference, originalSlice, 0, originalSlice.Length, 1);

            var lagSamples = original != null ? original.Lag + originalStart : (long)bestCoarse * factor;
            var score = original?.Score ?? best.Score;

            var matched = score >= options.MinScore;

            return new AudioSampleMatch
            {
                Matched = matched,
                Score = score,
                LagSamples = lagSamples,
                LagSeconds = lagSamples / (double)sampleRate,
                Error = matched ? null : "correlation score below threshold"
            };
        }

        private static short[] Slice(short[] source, int start, int length)
        {
            if (length <= 0)
            {
                return Array.Empty<short>();
            }

            var slice = new short[length];
            Array.Copy(source, start, slice, 0, length);
            return slice;
        }

        private class Peak
        {
            public int Lag { get; set; }
            public double Score { get; set; }
        }

        private static Peak FindBest(short[] reference, short[] candidate, int start, int end, int step)
        {
            if (reference == null || candidate == null || reference.Length == 0)
            {
                return null;
            }

            var maxStart = candidate.Length - reference.Length;

            if (maxStart < 0)
            {
                return null;
            }

            start = Math.Max(0, Math.Min(start, maxStart));
            end = Math.Max(start + 1, Math.Min(end, maxStart + 1));

            // A coarse step must still scan the true lag. Aligning the first lag to the step keeps
            // the scan on the step grid instead of starting mid-grid and skipping the peak.
            if (step > 1)
            {
                start = Math.Max(0, start - (start % step));
            }

            var referenceMean = Mean(reference, 0, reference.Length);
            var referenceEnergy = 0.0;

            for (var index = 0; index < reference.Length; index++)
            {
                var value = reference[index] - referenceMean;
                referenceEnergy += value * value;
            }

            if (referenceEnergy <= 0)
            {
                return null;
            }

            var prefix = new double[candidate.Length + 1];
            var prefixSquares = new double[candidate.Length + 1];

            for (var index = 0; index < candidate.Length; index++)
            {
                var value = candidate[index];
                prefix[index + 1] = prefix[index] + value;
                prefixSquares[index + 1] = prefixSquares[index] + (value * value);
            }

            var best = new Peak { Lag = start, Score = double.NegativeInfinity };

            for (var lag = start; lag < end; lag += step)
            {
                var dot = 0.0;

                for (var index = 0; index < reference.Length; index++)
                {
                    dot += (reference[index] - referenceMean) * candidate[lag + index];
                }

                var sum = prefix[lag + reference.Length] - prefix[lag];
                var sumSquares = prefixSquares[lag + reference.Length] - prefixSquares[lag];
                var mean = sum / reference.Length;
                var variance = sumSquares - (sum * mean);

                if (variance <= 0)
                {
                    continue;
                }

                var score = dot / Math.Sqrt(referenceEnergy * variance);

                if (score > best.Score)
                {
                    best.Lag = lag;
                    best.Score = score;
                }
            }

            return double.IsNegativeInfinity(best.Score) ? null : best;
        }

        private static double Mean(short[] values, int start, int length)
        {
            var sum = 0.0;

            for (var index = start; index < start + length; index++)
            {
                sum += values[index];
            }

            return sum / length;
        }

        private static short[] Downsample(short[] source, int factor)
        {
            if (factor <= 1)
            {
                return source;
            }

            var length = source.Length / factor;
            var result = new short[length];

            for (var index = 0; index < length; index++)
            {
                var start = index * factor;
                var end = Math.Min(source.Length, start + factor);
                var sum = 0L;

                for (var sample = start; sample < end; sample++)
                {
                    sum += source[sample];
                }

                result[index] = (short)(sum / Math.Max(1, end - start));
            }

            return result;
        }
    }
}
