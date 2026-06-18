using Wyspa.Core.Models;

namespace Wyspa.Core.Abstractions;

public interface IHotkeyService : IDisposable
{
    event EventHandler? Pressed;
    event EventHandler? Released;
    bool IsRegistered { get; }
    bool TryRegister(HotkeySettings hotkey, out string? errorMessage);
    void Unregister();
}
