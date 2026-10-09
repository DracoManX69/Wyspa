using System.Text.RegularExpressions;
using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

public static partial class VoicePersonalization
{
    public static IReadOnlyList<string> Passages { get; } =
    [
        "Please make a note of my ideas for tomorrow. I want the first draft to be clear, useful, and easy to read.",
        "Before we finish the meeting, let us review the changes. I will check the details and send a short update after lunch.",
        "Sometimes I speak quickly, and sometimes I pause to think. Please keep the words I say, including names and unfamiliar expressions."
    ];
    public static string[] Words(string text) => WordPattern().Matches(text.ToLowerInvariant()).Select(m => m.Value.Replace("’", "'")).ToArray();
    [GeneratedRegex(@"[\p{L}\p{N}]+(?:['’][\p{L}\p{N}]+)*")]
    private static partial Regex WordPattern();
    public static int CharacterDistance(string left, string right)
    {
        var previous = Enumerable.Range(0, right.Length + 1).ToArray();
        for (var i = 1; i <= left.Length; i++)
        {
            var current = new int[right.Length + 1]; current[0] = i;
            for (var j = 1; j <= right.Length; j++) current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[^1];
    }
    public static int Errors(string reference, string actual)
    {
        var expected = Words(reference); var heard = Words(actual);
        var previous = Enumerable.Range(0, heard.Length + 1).ToArray();
        for (var i = 1; i <= expected.Length; i++)
        {
            var current = new int[heard.Length + 1]; current[0] = i;
            for (var j = 1; j <= heard.Length; j++) current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), previous[j - 1] + (expected[i - 1] == heard[j - 1] ? 0 : 1));
            previous = current;
        }
        return previous[^1];
    }
    public static string? Prompt(string? original, string? vocabulary)
    {
        // Vocabulary is a hint, never the reference passage or a replacement transcript.
        var parts = new[] { original?.Trim(), vocabulary?.Trim() }.Where(s => !string.IsNullOrWhiteSpace(s));
        var text = string.Join(". ", parts);
        return text.Length == 0 ? null : text[..Math.Min(text.Length, 1000)];
    }
    public static int SuggestedSilence(IEnumerable<VoiceTestScore> scores)
    {
        var items = scores.ToArray();
        var seconds = items.Sum(s => s.AudioSeconds);
        var wpm = seconds <= 0 ? 0 : items.Sum(s => s.ReferenceWords) * 60 / seconds;
        // Conservative pace-based suggestion, not an acoustic pause detector.
        return wpm < 100 ? 1800 : wpm < 150 ? 1400 : 1200;
    }
}
