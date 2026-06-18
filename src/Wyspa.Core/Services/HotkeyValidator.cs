using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

public static class HotkeyValidator
{
    private static readonly HashSet<string> KnownKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "A","B","C","D","E","F","G","H","I","J","K","L","M",
        "N","O","P","Q","R","S","T","U","V","W","X","Y","Z",
        "0","1","2","3","4","5","6","7","8","9",
        "D0","D1","D2","D3","D4","D5","D6","D7","D8","D9",
        "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12",
        "F13","F14","F15","F16","F17","F18","F19","F20","F21","F22","F23","F24",
        "Space","Enter","Tab","Pause","Insert","Delete","Home","End","PageUp","PageDown"
    };

    public static bool TryValidate(HotkeySettings hotkey, out string? message)
    {
        var key = NormalizeKey(hotkey.Key);
        if (hotkey.Modifiers is HotkeyModifiers.None && !IsMacroFunctionKey(key))
        {
            message = "Choose at least one modifier, or use a dedicated macro key such as F13-F24.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(key) || !KnownKeys.Contains(key.Trim()))
        {
            message = "Choose a supported key such as Space, a letter, a number, F1-F24, or a navigation key.";
            return false;
        }

        message = null;
        return true;
    }

    public static bool TryParse(string displayText, out HotkeySettings hotkey, out string? message)
    {
        hotkey = HotkeySettings.Default;
        if (string.IsNullOrWhiteSpace(displayText))
        {
            message = "Enter a hotkey such as Ctrl+Alt+Space.";
            return false;
        }

        var parts = displayText.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1)
        {
            message = "Use a shortcut such as Ctrl+Alt+Space or a macro key such as F13.";
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        foreach (var part in parts[..^1])
        {
            modifiers |= part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" => HotkeyModifiers.Control,
                "ALT" => HotkeyModifiers.Alt,
                "SHIFT" => HotkeyModifiers.Shift,
                "WIN" or "WINDOWS" => HotkeyModifiers.Windows,
                _ => HotkeyModifiers.None
            };
        }

        hotkey = new HotkeySettings(modifiers, NormalizeKey(parts[^1]));
        return TryValidate(hotkey, out message);
    }

    public static string NormalizeKey(string key)
    {
        if (string.Equals(key, " ", StringComparison.Ordinal)) return "Space";
        if (key.Length == 1 && char.IsLetter(key[0])) return char.ToUpperInvariant(key[0]).ToString();
        if (key.Length == 1 && char.IsDigit(key[0])) return key;
        return key.Equals("space", StringComparison.OrdinalIgnoreCase) ? "Space" : key.Trim();
    }

    private static bool IsMacroFunctionKey(string key)
    {
        return key.StartsWith('F') && int.TryParse(key[1..], out var functionKey) && functionKey is >= 13 and <= 24;
    }
}
