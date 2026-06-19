using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

public sealed class WakeVoiceMatcher
{
    public const int SampleRate = 16000;
    private const int SegmentCount = 14;
    private const int MinSampleCount = SampleRate / 2;
    private const int MaxSampleCount = SampleRate * 3;
    private static readonly double[] BandCenters = [160, 250, 400, 630, 1000, 1600, 2500, 3600];

    public WakeVoiceProfile CreateProfile(IReadOnlyList<float> samples)
    {
        var trimmed = TrimSilence(samples);
        if (trimmed.Length < MinSampleCount)
        {
            throw new InvalidOperationException("Record a slightly longer wake phrase.");
        }

        if (trimmed.Length > MaxSampleCount)
        {
            trimmed = trimmed[^MaxSampleCount..];
        }

        var features = BuildFeatures(trimmed);
        return new WakeVoiceProfile
        {
            SampleRate = SampleRate,
            DurationMs = (int)Math.Round(trimmed.Length * 1000d / SampleRate),
            SegmentCount = SegmentCount,
            Features = features
        };
    }

    public double Score(IReadOnlyList<float> samples, WakeVoiceProfile? profile)
    {
        if (profile?.Features is null ||
            profile.Features.Length == 0 ||
            profile.SampleRate != SampleRate ||
            profile.SegmentCount != SegmentCount ||
            samples.Count < MinSampleCount)
        {
            return 0;
        }

        var targetSampleCount = Math.Clamp(profile.DurationMs * SampleRate / 1000, MinSampleCount, MaxSampleCount);
        return BestRecentWindowScore(samples, profile.Features, targetSampleCount);
    }

    private static double BestRecentWindowScore(IReadOnlyList<float> samples, IReadOnlyList<double> profileFeatures, int targetSampleCount)
    {
        var best = ScoreCandidate(TrimSilence(samples), profileFeatures);
        var windowSizes = new[]
        {
            Math.Clamp((int)(targetSampleCount * 0.72), MinSampleCount, MaxSampleCount),
            targetSampleCount,
            Math.Clamp((int)(targetSampleCount * 1.32), MinSampleCount, MaxSampleCount)
        };

        foreach (var windowSize in windowSizes.Distinct())
        {
            if (samples.Count < windowSize)
            {
                continue;
            }

            var step = Math.Max(SampleRate / 12, windowSize / 8);
            for (var end = samples.Count; end >= windowSize; end -= step)
            {
                var candidate = new float[windowSize];
                for (var index = 0; index < windowSize; index++)
                {
                    candidate[index] = samples[end - windowSize + index];
                }

                best = Math.Max(best, ScoreCandidate(TrimSilence(candidate), profileFeatures));
                if (best >= 0.995)
                {
                    return best;
                }
            }
        }

        return best;
    }

    private static double ScoreCandidate(IReadOnlyList<float> samples, IReadOnlyList<double> profileFeatures)
    {
        if (samples.Count < MinSampleCount)
        {
            return 0;
        }

        return DistanceSimilarity(BuildFeatures(samples), profileFeatures);
    }

    private static float[] TrimSilence(IReadOnlyList<float> samples)
    {
        if (samples.Count == 0)
        {
            return [];
        }

        var peak = samples.Max(sample => Math.Abs(sample));
        var threshold = Math.Max(0.018f, peak * 0.16f);
        var start = 0;
        while (start < samples.Count && Math.Abs(samples[start]) < threshold)
        {
            start++;
        }

        var end = samples.Count - 1;
        while (end > start && Math.Abs(samples[end]) < threshold)
        {
            end--;
        }

        if (end <= start)
        {
            return samples.ToArray();
        }

        var padding = SampleRate / 10;
        start = Math.Max(0, start - padding);
        end = Math.Min(samples.Count - 1, end + padding);
        var trimmed = new float[end - start + 1];
        for (var index = 0; index < trimmed.Length; index++)
        {
            trimmed[index] = samples[start + index];
        }

        return trimmed;
    }

    private static double[] BuildFeatures(IReadOnlyList<float> samples)
    {
        var features = new double[SegmentCount * (BandCenters.Length + 2)];
        var maxRms = 0d;
        for (var segment = 0; segment < SegmentCount; segment++)
        {
            var start = segment * samples.Count / SegmentCount;
            var end = Math.Max(start + 1, (segment + 1) * samples.Count / SegmentCount);
            var length = end - start;
            var offset = segment * (BandCenters.Length + 2);
            var rms = 0d;
            var crossings = 0;
            for (var index = start; index < end; index++)
            {
                rms += samples[index] * samples[index];
                if (index > start && Math.Sign(samples[index]) != Math.Sign(samples[index - 1]))
                {
                    crossings++;
                }
            }

            features[offset] = Math.Sqrt(rms / length);
            maxRms = Math.Max(maxRms, features[offset]);
            features[offset + 1] = crossings / (double)length * 8d;
            for (var band = 0; band < BandCenters.Length; band++)
            {
                features[offset + band + 2] = GoertzelMagnitude(samples, start, length, BandCenters[band]);
            }
        }

        for (var segment = 0; segment < SegmentCount; segment++)
        {
            var offset = segment * (BandCenters.Length + 2);
            features[offset] = maxRms <= double.Epsilon ? 0 : features[offset] / maxRms;
            var bandTotal = 0d;
            for (var band = 0; band < BandCenters.Length; band++)
            {
                bandTotal += features[offset + band + 2];
            }

            if (bandTotal <= double.Epsilon)
            {
                continue;
            }

            for (var band = 0; band < BandCenters.Length; band++)
            {
                features[offset + band + 2] /= bandTotal;
            }
        }

        return features;
    }

    private static double GoertzelMagnitude(IReadOnlyList<float> samples, int start, int length, double frequency)
    {
        var omega = 2d * Math.PI * frequency / SampleRate;
        var coefficient = 2d * Math.Cos(omega);
        var previous = 0d;
        var previous2 = 0d;
        for (var index = 0; index < length; index++)
        {
            var window = 0.5d - 0.5d * Math.Cos(2d * Math.PI * index / Math.Max(1, length - 1));
            var current = samples[start + index] * window + coefficient * previous - previous2;
            previous2 = previous;
            previous = current;
        }

        var power = previous2 * previous2 + previous * previous - coefficient * previous * previous2;
        return Math.Sqrt(Math.Max(0, power)) / length;
    }

    private static double DistanceSimilarity(IReadOnlyList<double> left, IReadOnlyList<double> right)
    {
        var length = Math.Min(left.Count, right.Count);
        var distance = 0d;
        for (var index = 0; index < length; index++)
        {
            var delta = left[index] - right[index];
            distance += delta * delta;
        }

        var rmse = Math.Sqrt(distance / Math.Max(1, length));
        return Math.Clamp(Math.Exp(-2.6d * rmse), 0, 1);
    }
}
