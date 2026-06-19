namespace Wyspa.Core.Models;

public sealed class AppSettings
{
    public bool FirstRunComplete { get; set; }
    public string? MicrophoneDeviceId { get; set; }
    public HotkeySettings Hotkey { get; set; } = HotkeySettings.Default;
    public HotkeySettings AutoCaptureHotkey { get; set; } = HotkeySettings.DefaultAutoCapture;
    public ActivationMode ActivationMode { get; set; } = ActivationMode.Toggle;
    public string ModelId { get; set; } = "whisper-large-v3-turbo";
    public string? Language { get; set; }
    public string? CustomPrompt { get; set; }
    public bool StartMinimized { get; set; }
    public bool StartWithWindows { get; set; }
    public InsertionMode InsertionMode { get; set; } = InsertionMode.Paste;
    public bool CleanupEnabled { get; set; } = true;
    public bool SpokenPunctuationEnabled { get; set; } = true;
    public bool IntentActionsEnabled { get; set; } = true;
    public string IntentModelId { get; set; } = "llama-3.3-70b-versatile";
    public double IntentConfidenceThreshold { get; set; } = 0.62;
    public bool HistoryEnabled { get; set; }
    public bool RetainAudioForDebugging { get; set; }
    public double OverlayOpacity { get; set; } = 0.82;
    public float AutoCaptureThreshold { get; set; } = 0.08f;
    public int AutoCaptureSilenceMs { get; set; } = 1200;
    public int AutoCaptureMinSpeechMs { get; set; } = 650;
    public bool AutoCaptureListeningEnabled { get; set; } = true;
}
