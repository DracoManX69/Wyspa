using System.Text.RegularExpressions;
using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

public static class SpokenKeyCommandParser
{
    private static readonly Dictionary<string, string> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enter"] = "Enter",
        ["return"] = "Enter",
        ["tab"] = "Tab",
        ["escape"] = "Escape",
        ["esc"] = "Escape",
        ["space"] = "Space",
        ["backspace"] = "Backspace",
        ["delete"] = "Delete",
        ["up"] = "Up",
        ["down"] = "Down",
        ["left"] = "Left",
        ["right"] = "Right",
        ["home"] = "Home",
        ["end"] = "End",
        ["page up"] = "PageUp",
        ["page down"] = "PageDown"
    };

    public static bool TryParse(string text, out KeyPressCommand command)
    {
        command = new KeyPressCommand(string.Empty, []);
        var normalized = Regex.Replace(text.Trim().TrimEnd('.', '!', '?'), @"\s+", " ").ToLowerInvariant();
        if (!normalized.StartsWith("press ", StringComparison.Ordinal))
        {
            return false;
        }

        var phrase = normalized["press ".Length..];
        var modifiers = new List<string>();
        foreach (var modifier in new[] { "control", "ctrl", "shift", "alt", "windows", "win" })
        {
            var token = modifier + " ";
            if (phrase.StartsWith(token, StringComparison.Ordinal))
            {
                modifiers.Add(modifier is "control" ? "Ctrl" : modifier is "windows" ? "Win" : char.ToUpperInvariant(modifier[0]) + modifier[1..]);
                phrase = phrase[token.Length..];
            }
        }

        if (Keys.TryGetValue(phrase, out var key))
        {
            command = new KeyPressCommand(key, modifiers);
            return true;
        }

        if (phrase.Length == 1 && char.IsLetterOrDigit(phrase[0]))
        {
            command = new KeyPressCommand(phrase.ToUpperInvariant(), modifiers);
            return true;
        }

        if (Regex.IsMatch(phrase, @"^f([1-9]|1[0-9]|2[0-4])$"))
        {
            command = new KeyPressCommand(phrase.ToUpperInvariant(), modifiers);
            return true;
        }

        return false;
    }
}
