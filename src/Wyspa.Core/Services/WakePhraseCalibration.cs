using System.Text.RegularExpressions;
using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

public static class WakePhraseCalibration
{
    public static readonly double[] Levels = [0, .15, .3, .45, .6, .75, .9, 1];
    public static string Normalize(string text)
    {
        var phrase = Regex.Replace(text.Trim().ToLowerInvariant(), @"\s+", " ");
        if (phrase.Length > 60 || !Regex.IsMatch(phrase, @"\A[a-z]+(?: [a-z]+){1,4}\z"))
            throw new ArgumentException("Use an English wake phrase of two to five words, for example hey whisper. Letters and spaces only.");
        return phrase;
    }
    public static float Threshold(double strictness) => (float)(.10 + Math.Clamp(strictness, 0, 1) * .80);
    public static WakeCalibrationSummary Recommend(IEnumerable<WakeCalibrationTrial> trials)
    {
        var rows = trials.ToArray(); var positives = rows.Count(r => r.IsWakePhrase); var negatives = rows.Length - positives;
        return Levels.Select(level => new WakeCalibrationSummary(level, positives, negatives,
            rows.Count(r => r.IsWakePhrase && r.MaximumMatchingStrictness >= level),
            rows.Count(r => !r.IsWakePhrase && r.MaximumMatchingStrictness >= level)))
            .OrderBy(r => r.FalsePositives).ThenByDescending(r => r.TruePositives).ThenByDescending(r => r.Strictness).First();
    }
}
