using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

public static class WakeEnrollmentTrainer
{
    public static WakeVoiceProfile Build(string phrase, string? microphone, IEnumerable<WakeSetupReading> readings)
    {
        var rows = readings.ToArray(); var training = rows.Where(r => !r.IsValidation).ToArray();
        var templates = training.Where(r => r.IsWake && r.IsPhraseOnly && r.Features is { Length: >= 10 }).Select(r => r.Features!).Take(6).ToList();
        var variants = training.Where(r => r.IsWake && r.IsPhraseOnly && WakeSetupSuite.PlausibleVariant(r.RecognizedText, phrase))
            .GroupBy(r => string.Join(" ", VoicePersonalization.Words(r.RecognizedText)))
            .Where(g => g.Count() >= 2).Select(g => g.Key).Where(v => v != phrase &&
                !training.Any(r => !r.IsWake && WakePhraseVerifier.Matches(r.RecognizedText, v))).Take(3).ToList();
        var positives = templates.Select((sample, index) => WakeAcoustics.Best(sample, templates.Where((_, i) => i != index))).Where(double.IsFinite).ToArray();
        var negativeDistance = training.Where(r => !r.IsWake && r.Features is { Length: >= 10 }).Select(r => WakeAcoustics.Best(r.Features!, templates)).DefaultIfEmpty(1).Min();
        var threshold = positives.Length >= 3 ? Math.Min(.75, Math.Min(positives.Max() + .08, negativeDistance - .08)) : 0;
        if (threshold <= 0 || positives.Any(d => d > threshold)) threshold = 0;
        return new() { DetectorVersion = 2, EnrollmentVersion = 1, SetupSuiteVersion = WakeSetupSuite.Version, Phrase = phrase, MicrophoneId = microphone,
            AcousticTemplates = threshold > 0 ? templates : [], AcousticThreshold = threshold, PronunciationVariants = variants,
            NoiseRms = training.FirstOrDefault(r => r.StepId == "room")?.Rms ?? .003,
            SetupReadings = rows.Select(r => r with { Features = null }).ToList(), TrainingSampleCount = training.Count(r => r.IsWake), VoiceTrainingSampleCount = training.Count(r => !r.IsWake) };
    }
    public static double RecommendStrictness(IEnumerable<WakeSetupReading> readings, WakeVoiceProfile profile)
    {
        var trials = readings.Where(r => !r.IsValidation && r.StepId != "room").Select(r => new WakeCalibrationTrial(r.IsWake,
            WakePhraseVerifier.Matches(r.RecognizedText, profile.Phrase) || profile.PronunciationVariants.Any(v => WakePhraseVerifier.Matches(r.RecognizedText, v))
                ? r.KeywordStrictness : null, DateTimeOffset.UtcNow)).ToArray();
        // No canonical hits means no evidence for a high threshold. New pronunciation keywords
        // start at a recall-friendly value; live positive/negative checks then tune it.
        return profile.TunedStrictness ?? (trials.Any(r => r.IsWakePhrase && r.MaximumMatchingStrictness.HasValue)
            ? Math.Max(0, WakePhraseCalibration.Recommend(trials).Strictness - .15) : .3);
    }
    public static bool Predict(WakeSampleAnalysis sample, string phrase, WakeVoiceProfile profile, double strictness)
    {
        var lexical = WakePhraseVerifier.Matches(sample.RecognizedText, phrase) || profile.PronunciationVariants.Any(v => WakePhraseVerifier.Matches(sample.RecognizedText, v));
        var acoustic = profile.AcousticThreshold > 0 && WakeAcoustics.Best(sample.Features, profile.AcousticTemplates) <= profile.AcousticThreshold;
        return lexical && (sample.KeywordStrictness >= strictness || acoustic);
    }
}
