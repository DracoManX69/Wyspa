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
            Features = features,
            FeatureSets = [features],
            TrainingSampleCount = 1
        };
    }

    public WakeVoiceProfile AddTrainingSample(WakeVoiceProfile? existingProfile, IReadOnlyList<float> samples)
    {
        var sampleProfile = CreateProfile(samples);
        if (existingProfile is null ||
            existingProfile.SampleRate != SampleRate ||
            existingProfile.SegmentCount != SegmentCount)
        {
            return sampleProfile;
        }

        var featureSets = GetFeatureSets(existingProfile).Append(sampleProfile.Features).TakeLast(8).Select(features => features.ToArray()).ToList();
        return new WakeVoiceProfile
        {
            SampleRate = SampleRate,
            DurationMs = (int)Math.Round((existingProfile.DurationMs * Math.Max(1, existingProfile.TrainingSampleCount) + sampleProfile.DurationMs) /
                (double)(Math.Max(1, existingProfile.TrainingSampleCount) + 1)),
            SegmentCount = SegmentCount,
            Features = AverageFeatures(featureSets),
            FeatureSets = featureSets,
            TrainingSampleCount = Math.Min(8, Math.Max(1, existingProfile.TrainingSampleCount) + 1),
            VoiceFeatures = existingProfile.VoiceFeatures,
            VoiceFeatureSets = existingProfile.VoiceFeatureSets,
            VoiceTrainingSampleCount = existingProfile.VoiceTrainingSampleCount
        };
    }

    public WakeVoiceProfile AddVoiceTrainingSample(WakeVoiceProfile? existingProfile, IReadOnlyList<float> samples)
    {
        var trimmed = TrimSilence(samples);
        if (trimmed.Length < MinSampleCount)
        {
            throw new InvalidOperationException("Record a slightly longer training sentence.");
        }

        if (trimmed.Length > MaxSampleCount)
        {
            trimmed = trimmed[^MaxSampleCount..];
        }

        var voiceFeatures = BuildVoiceFeatures(trimmed);
        var voiceFeatureSets = (existingProfile?.VoiceFeatureSets.Count > 0
                ? existingProfile.VoiceFeatureSets
                : existingProfile?.VoiceFeatures.Length > 0 ? [existingProfile.VoiceFeatures] : new List<double[]>())
            .Append(voiceFeatures)
            .TakeLast(8)
            .Select(features => features.ToArray())
            .ToList();

        return new WakeVoiceProfile
        {
            SampleRate = SampleRate,
            DurationMs = existingProfile?.DurationMs ?? 0,
            SegmentCount = SegmentCount,
            Features = existingProfile?.Features ?? [],
            FeatureSets = existingProfile?.FeatureSets ?? [],
            TrainingSampleCount = existingProfile?.TrainingSampleCount ?? 0,
            VoiceFeatures = AverageFeatures(voiceFeatureSets),
            VoiceFeatureSets = voiceFeatureSets,
            VoiceTrainingSampleCount = Math.Min(8, (existingProfile?.VoiceTrainingSampleCount ?? 0) + 1)
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
        var best = 0d;
        foreach (var featureSet in GetFeatureSets(profile))
        {
            best = Math.Max(best, BestRecentWindowScore(samples, featureSet, targetSampleCount));
            if (best >= 0.995)
            {
                return best;
            }
        }

        var voiceScore = VoiceScore(samples, profile);
        return voiceScore <= 0 ? best : Math.Clamp(best * (0.85d + 0.15d * voiceScore), 0, 1);
    }

    public double VoiceScore(IReadOnlyList<float> samples, WakeVoiceProfile? profile)
    {
        if (profile is null || samples.Count < MinSampleCount)
        {
            return 0;
        }

        var featureSets = GetVoiceFeatureSets(profile).ToList();
        if (featureSets.Count == 0)
        {
            return 0;
        }

        var trimmed = TrimSilence(samples);
        if (trimmed.Length < MinSampleCount)
        {
            return 0;
        }

        if (trimmed.Length > MaxSampleCount)
        {
            trimmed = trimmed[^MaxSampleCount..];
        }

        var features = BuildVoiceFeatures(trimmed);
        return featureSets.Max(featureSet => DistanceSimilarity(features, featureSet));
    }

    private static IEnumerable<double[]> GetFeatureSets(WakeVoiceProfile profile)
    {
        if (profile.FeatureSets.Count > 0)
        {
            return profile.FeatureSets.Where(features => features.Length > 0);
        }

        return profile.Features.Length > 0 ? [profile.Features] : [];
    }

    private static IEnumerable<double[]> GetVoiceFeatureSets(WakeVoiceProfile profile)
    {
        if (profile.VoiceFeatureSets.Count > 0)
        {
            return profile.VoiceFeatureSets.Where(features => features.Length > 0);
        }

        return profile.VoiceFeatures.Length > 0 ? [profile.VoiceFeatures] : [];
    }

    private static double[] AverageFeatures(IReadOnlyList<double[]> featureSets)
    {
        if (featureSets.Count == 0)
        {
            return [];
        }

        var length = featureSets.Min(features => features.Length);
        var average = new double[length];
        foreach (var features in featureSets)
        {
            for (var index = 0; index < length; index++)
            {
                average[index] += features[index];
            }
        }

        for (var index = 0; index < average.Length; index++)
        {
            average[index] /= featureSets.Count;
        }

        return average;
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

    private static double[] BuildVoiceFeatures(IReadOnlyList<float> samples)
    {
        var features = new double[BandCenters.Length + 4];
        var rms = 0d;
        var crossings = 0;
        var previousFrameRms = 0d;
        var frameDelta = 0d;
        var frameCount = 0;
        var frameSize = Math.Max(1, SampleRate / 20);

        for (var index = 0; index < samples.Count; index++)
        {
            rms += samples[index] * samples[index];
            if (index > 0 && Math.Sign(samples[index]) != Math.Sign(samples[index - 1]))
            {
                crossings++;
            }
        }

        for (var start = 0; start < samples.Count; start += frameSize)
        {
            var end = Math.Min(samples.Count, start + frameSize);
            var frameRms = 0d;
            for (var index = start; index < end; index++)
            {
                frameRms += samples[index] * samples[index];
            }

            frameRms = Math.Sqrt(frameRms / Math.Max(1, end - start));
            if (frameCount > 0)
            {
                frameDelta += Math.Abs(frameRms - previousFrameRms);
            }

            previousFrameRms = frameRms;
            frameCount++;
        }

        features[0] = Math.Sqrt(rms / samples.Count);
        features[1] = crossings / (double)samples.Count * 8d;
        features[2] = frameCount <= 1 ? 0 : frameDelta / (frameCount - 1);
        features[3] = samples.Count / (double)MaxSampleCount;
        var bandTotal = 0d;
        for (var band = 0; band < BandCenters.Length; band++)
        {
            features[band + 4] = GoertzelMagnitude(samples, 0, samples.Count, BandCenters[band]);
            bandTotal += features[band + 4];
        }

        if (bandTotal > double.Epsilon)
        {
            for (var band = 0; band < BandCenters.Length; band++)
            {
                features[band + 4] /= bandTotal;
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
