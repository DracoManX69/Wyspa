using System.Text.RegularExpressions;

namespace Wyspa.Core.Services;

public sealed class TextCleanupService
{
    private static readonly string[] NonDictationResponses =
    [
        "please provide the dictated speech",
        "please provide the text",
        "please provide the transcript",
        "i will rewrite it",
        "i'll rewrite it"
    ];

    public string Clean(string input, bool spokenPunctuationEnabled = true)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var text = Regex.Replace(input.Trim(), @"\s+", " ");

        if (spokenPunctuationEnabled)
        {
            text = ConvertSpokenPunctuation(text);
        }

        if (text.Length > 0 && char.IsLetter(text[0]) && !LooksLikeCode(text))
        {
            text = char.ToUpperInvariant(text[0]) + text[1..];
        }

        return text;
    }

    public static bool HasTranscribableText(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var text = Regex.Replace(input.Trim(), @"\s+", " ");
        if (!text.Any(char.IsLetterOrDigit))
        {
            return false;
        }

        return !NonDictationResponses.Any(response => text.StartsWith(response, StringComparison.OrdinalIgnoreCase));
    }

    private static string ConvertSpokenPunctuation(string text)
    {
        var replacements = new (string Pattern, string Replacement)[]
        {
            (@"\bnew line\b", Environment.NewLine),
            (@"\bcomma\b", ","),
            (@"\b(period|full stop)\b", "."),
            (@"\bquestion mark\b", "?")
        };

        foreach (var (pattern, replacement) in replacements)
        {
            text = Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase);
        }

        text = Regex.Replace(text, @"\s+([,.?])", "$1");
        text = Regex.Replace(text, @"([,.?])(?=\S)", "$1 ");
        text = Regex.Replace(text, @"[ \t]*\r?\n[ \t]*", Environment.NewLine);
        return text.Trim();
    }

    private static bool LooksLikeCode(string text)
    {
        return text.Contains("://", StringComparison.Ordinal) ||
               text.Contains("=>", StringComparison.Ordinal) ||
               text.Contains("()", StringComparison.Ordinal) ||
               text.Contains(';') ||
               text.Contains('{') ||
               text.Contains('}');
    }
}
