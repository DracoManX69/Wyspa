using System.Text.Json.Serialization;

namespace Wyspa.Core.Models;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8
}

public sealed record HotkeySettings(
    HotkeyModifiers Modifiers,
    string Key)
{
    public static HotkeySettings Default { get; } = new(HotkeyModifiers.Control | HotkeyModifiers.Alt, "Space");

    [JsonIgnore]
    public string DisplayText
    {
        get
        {
            var parts = new List<string>();
            if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
            if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
            if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
            if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
            parts.Add(Key);
            return string.Join("+", parts);
        }
    }
}
