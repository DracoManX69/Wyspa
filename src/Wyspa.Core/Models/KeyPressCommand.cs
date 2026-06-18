namespace Wyspa.Core.Models;

public sealed record KeyPressCommand(string Key, IReadOnlyList<string> Modifiers);
