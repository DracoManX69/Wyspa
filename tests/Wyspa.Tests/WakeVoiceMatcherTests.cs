using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class WakeVoiceMatcherTests
{
    [Fact]
    public void Score_ReturnsHighMatch_ForSamePhraseShape()
    {
        var matcher = new WakeVoiceMatcher();
        var samples = BuildPhrase([220, 440, 330]);
        var profile = matcher.CreateProfile(samples);

        var score = matcher.Score(samples, profile);

        Assert.True(score > 0.9);
    }

    [Fact]
    public void Score_ReturnsLowerMatch_ForDifferentPhraseShape()
    {
        var matcher = new WakeVoiceMatcher();
        var profile = matcher.CreateProfile(BuildPhrase([220, 440, 330]));

        var score = matcher.Score(BuildPhrase([900, 1200, 700]), profile);

        Assert.True(score < 0.8);
    }

    [Fact]
    public void Score_ReturnsHighMatch_WhenPhraseIsInsideRecentAudio()
    {
        var matcher = new WakeVoiceMatcher();
        var phrase = BuildPhrase([220, 440, 330]);
        var profile = matcher.CreateProfile(phrase);
        var samples = new float[WakeVoiceMatcher.SampleRate + phrase.Length + WakeVoiceMatcher.SampleRate / 2];
        Array.Copy(phrase, 0, samples, WakeVoiceMatcher.SampleRate, phrase.Length);

        var score = matcher.Score(samples, profile);

        Assert.True(score > 0.8);
    }

    [Fact]
    public void AddTrainingSample_AccumulatesLocalWakeExamples()
    {
        var matcher = new WakeVoiceMatcher();
        var profile = matcher.AddTrainingSample(null, BuildPhrase([220, 440, 330]));

        profile = matcher.AddTrainingSample(profile, BuildPhrase([240, 460, 350]));

        Assert.Equal(2, profile.TrainingSampleCount);
        Assert.Equal(2, profile.FeatureSets.Count);
        Assert.True(matcher.Score(BuildPhrase([240, 460, 350]), profile) > 0.8);
    }

    private static float[] BuildPhrase(double[] frequencies)
    {
        var segmentLength = WakeVoiceMatcher.SampleRate / 2;
        var samples = new float[segmentLength * frequencies.Length];
        for (var segment = 0; segment < frequencies.Length; segment++)
        {
            for (var index = 0; index < segmentLength; index++)
            {
                var sampleIndex = segment * segmentLength + index;
                var envelope = Math.Sin(Math.PI * index / segmentLength);
                samples[sampleIndex] = (float)(0.35 * envelope * Math.Sin(2 * Math.PI * frequencies[segment] * index / WakeVoiceMatcher.SampleRate));
            }
        }

        return samples;
    }
}
