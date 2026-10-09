namespace Wyspa.Core.Models;

public sealed record WakeSetupStep(string Id, string Title, string Prompt, string Hint, bool IsWake, bool IsValidation, bool IsPhraseOnly, int Seconds, bool IsRoom = false);
public sealed record WakeSetupReading(string StepId, bool IsWake, bool IsValidation, bool IsPhraseOnly, double? KeywordStrictness,
    string RecognizedText, double Rms, double Peak, double ClippedFraction, float[][]? Features = null);
public sealed record WakeSampleAnalysis(double? KeywordStrictness, string RecognizedText, double Rms, double Peak, double ClippedFraction, float[][] Features);
