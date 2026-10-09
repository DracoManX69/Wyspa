using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class WakeEnrollmentTests
{
    [Fact]
    public void GenericAccentMissesDoNotProduceAnArbitrarilyStrictStartingThreshold()
    {
        var readings = Enumerable.Range(0, 4).Select(i => new WakeSetupReading("wake-" + i, true, false, true, null, "hey whisba", .03, .2, 0)).ToArray();
        var profile = WakeEnrollmentTrainer.Build("hey whisper", "mic", readings);
        Assert.Contains("hey whisba", profile.PronunciationVariants);
        Assert.Equal(.3, WakeEnrollmentTrainer.RecommendStrictness(readings, profile));
        profile.TunedStrictness = .15; Assert.Equal(.15, WakeEnrollmentTrainer.RecommendStrictness(readings, profile));
    }
    [Fact]
    public void SuiteIncludesRoomVariedPositivesNearMatchesAndIndependentChecks()
    {
        var steps = WakeSetupSuite.Create("hey whisper");
        Assert.Equal(14, steps.Count); Assert.True(steps[0].IsRoom);
        Assert.Equal(3, steps.Count(s => s.IsWake && s.IsPhraseOnly && !s.IsValidation));
        Assert.Equal(4, steps.Count(s => s.IsValidation));
        Assert.Contains(steps, s => s.Id == "wake-sentence" && s.Prompt.Contains("hey whisper"));
        Assert.Contains(steps, s => s.Id == "near" && s.Prompt.Contains("hey mister"));
        Assert.All(steps.Where(s => !s.IsWake && !s.IsRoom), s => Assert.DoesNotContain("hey whisper", s.Prompt));
        Assert.All(steps.Zip(steps.Skip(1)), pair => Assert.False(pair.First.IsWake && pair.Second.IsWake));
        Assert.NotEqual(steps.Single(s => s.Id == "ordinary").Prompt, WakeSetupSuite.Create("hey whisper", 1).Single(s => s.Id == "ordinary").Prompt);
    }
    [Fact]
    public void QualityFeedbackDistinguishesQuietClippingAndRoomSamples()
    {
        var step = WakeSetupSuite.Create("hey whisper")[1];
        WakeSampleAnalysis Row(double rms, double peak, double clipping) => new(.5, "hey whisper", rms, peak, clipping, []);
        Assert.Contains("quiet", WakeSetupSuite.QualityProblem(step, Row(.0001, .002, 0), 16000));
        Assert.Contains("clipping", WakeSetupSuite.QualityProblem(step, Row(.1, 1, .03), 16000));
        Assert.Null(WakeSetupSuite.QualityProblem(step, Row(.03, .2, 0), 16000));
        Assert.Null(WakeSetupSuite.QualityProblem(WakeSetupSuite.Create("hey whisper")[0], Row(0, 0, 0), 16000));
    }
    [Fact]
    public void PronunciationAliasesRequireRepeatExamplesAndCannotLearnNearPhraseNegatives()
    {
        WakeSetupReading Row(string text, bool wake, bool validation = false) => new("sample", wake, validation, true, .5, text, .03, .2, 0);
        var profile = WakeEnrollmentTrainer.Build("hey whisper", "mic", [Row("hey whisba", true), Row("hey whisba", true), Row("hey mister", false), Row("hey mister", true, true)]);
        Assert.Contains("hey whisba", profile.PronunciationVariants); Assert.DoesNotContain("hey mister", profile.PronunciationVariants);
        Assert.Empty(WakeEnrollmentTrainer.Build("hey whisper", "mic", [Row("hey whisba", true), Row("hey whisba", true), Row("hey whisba", false)]).PronunciationVariants);
        Assert.False(WakeSetupSuite.PlausibleVariant("hey mister", "hey whisper"));
        Assert.True(WakeSetupSuite.PlausibleVariant("hey whisper", "hey wyspa"));
        Assert.Empty(WakeEnrollmentTrainer.Build("hey whisper", "mic", [Row("hey whispa", true)]).PronunciationVariants);
    }
    [Fact]
    public void AcousticCandidateNeverBypassesLexicalVerification()
    {
        var frames = Frames(0); var profile = new WakeVoiceProfile { AcousticTemplates = [frames], AcousticThreshold = .3 };
        var valid = new WakeSampleAnalysis(null, "hey whisper", .03, .2, 0, frames);
        Assert.True(WakeEnrollmentTrainer.Predict(valid, "hey whisper", profile, .5));
        Assert.False(WakeEnrollmentTrainer.Predict(valid with { RecognizedText = "hey mister" }, "hey whisper", profile, .5));
    }
    [Fact]
    public void FeatureMatchingIsGainIndependentBoundedAndRejectsInvalidVectors()
    {
        var audio = Enumerable.Range(0, 24000).Select(i => (float)(.2 * Math.Sin(i * (.02 + .000003 * i)))).ToArray();
        var reference = WakeAcoustics.Extract(audio); var quiet = WakeAcoustics.Extract(audio.Select(v => v * .4f).ToArray());
        Assert.InRange(reference.Length, 10, 320); Assert.All(reference, row => Assert.Equal(12, row.Length));
        Assert.True(WakeAcoustics.Distance(reference, quiet) < .02);
        Assert.Equal(double.PositiveInfinity, WakeAcoustics.Distance([[float.NaN]], reference));
    }
    [Fact]
    public void ValidationReadingsNeverBecomeTrainingTemplatesOrAliases()
    {
        var training = Enumerable.Range(0, 4).Select(i => new WakeSetupReading("wake", true, false, true, .5, "hey whisper", .03, .2, 0, Frames(i * .001))).ToList();
        training.Add(new("validation", true, true, true, .5, "hey whispa", .03, .2, 0, Frames(1)));
        var profile = WakeEnrollmentTrainer.Build("hey whisper", "mic", training);
        Assert.Equal(4, profile.AcousticTemplates.Count); Assert.Empty(profile.PronunciationVariants);
        Assert.All(profile.SetupReadings, row => Assert.Null(row.Features));
    }
    private static float[][] Frames(double delta) => Enumerable.Range(0, 30).Select(i => Enumerable.Range(0, 12).Select(c => (float)(Math.Sin(i * .1 + c) + delta)).ToArray()).ToArray();
}
