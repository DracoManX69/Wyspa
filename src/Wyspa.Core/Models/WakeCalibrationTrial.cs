namespace Wyspa.Core.Models;

public sealed record WakeCalibrationTrial(bool IsWakePhrase, double? MaximumMatchingStrictness, DateTimeOffset RecordedAt);
public sealed record WakeCalibrationSummary(double Strictness, int Positives, int Negatives, int TruePositives, int FalsePositives)
{
    public bool Ready => Positives >= 3 && Negatives >= 3 && TruePositives >= Math.Ceiling(Positives * .8) && FalsePositives == 0;
    public string Description => $"Wake detections: {TruePositives}/{Positives}. Ordinary-speech false triggers: {FalsePositives}/{Negatives}. Suggested strictness: {Strictness:P0}. " +
        (Ready ? "These samples separate cleanly. Keep testing different distances and background noise." : "Collect at least three wake and three ordinary-speech examples. If they overlap, try a more distinct phrase or microphone position.");
}
