using Wyspa.Core.Models;

namespace Wyspa.App.Services;

// UI boundary only. Tests compile the real orchestrator and all core stream logic.
public sealed class OverlayStatusService
{
    public string LastMessage { get; private set; } = "";
    public void SetOpacity(double opacity) { }
    public void Show(string message, DictationState state) { LastMessage = message; Hidden = false; }
    public bool Hidden { get; private set; }
    public void SetCaptureActive(bool active, float speechThreshold = .012f) { }
    public void Hide() => Hidden = true;
}
