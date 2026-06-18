using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using Wyspa.App.Services;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.App.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly ISettingsService _settingsService;
    private readonly ISecretStore _secretStore;
    private readonly IGroqTranscriptionClient _groqClient;
    private readonly IAudioCaptureService _audioCapture;
    private readonly IHotkeyService _hotkeyService;
    private readonly IStartupService _startupService;
    private readonly DictationOrchestrator _orchestrator;
    private string _apiKey = string.Empty;
    private string _connectionMessage = "Add a Groq API key to enable transcription.";
    private string _hotkeyText = HotkeySettings.Default.DisplayText;
    private string _scratchpadText = string.Empty;
    private string _scratchpadStatus = "Record a short clip to test Groq transcription without inserting text.";
    private bool _hasApiKey;
    private bool _isScratchpadRecording;
    private bool _startWithWindows;
    private float _microphoneLevel;
    private DictationState _status = DictationState.Idle;

    public MainViewModel(
        ISettingsService settingsService,
        ISecretStore secretStore,
        IGroqTranscriptionClient groqClient,
        IAudioCaptureService audioCapture,
        IHotkeyService hotkeyService,
        IStartupService startupService,
        DictationOrchestrator orchestrator)
    {
        _settingsService = settingsService;
        _secretStore = secretStore;
        _groqClient = groqClient;
        _audioCapture = audioCapture;
        _hotkeyService = hotkeyService;
        _startupService = startupService;
        _orchestrator = orchestrator;
        Settings = new AppSettings();
        Devices = [];
        SaveCommand = new AsyncRelayCommand(SaveHotkeyAsync);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);
        ToggleListeningCommand = new AsyncRelayCommand(ToggleListeningAsync);
        RemoveKeyCommand = new AsyncRelayCommand(RemoveKeyAsync);
        RefreshDevicesCommand = new AsyncRelayCommand(LoadDevicesAsync);
        ScratchpadCommand = new AsyncRelayCommand(ToggleScratchpadAsync);
        ClearHistoryCommand = new RelayCommand(_ => ConnectionMessage = "History is off by default. Nothing was cleared.");
        _orchestrator.StateChanged += (_, state) => RunOnUi(() => Status = state);
        _audioCapture.LevelAvailable += (_, level) => UpdateMicrophoneLevel(level);
    }

    public event EventHandler? SettingsSaved;
    public event EventHandler? SettingsChanged;
    public event EventHandler? AutoCaptureListeningChanged;
    public event EventHandler? StartupSettingChanged;

    public AppSettings Settings { get; private set; }
    public ObservableCollection<AudioDeviceInfo> Devices { get; }
    public ICommand SaveCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand ToggleListeningCommand { get; }
    public ICommand ScratchpadCommand { get; }
    public ICommand RemoveKeyCommand { get; }
    public ICommand RefreshDevicesCommand { get; }
    public ICommand ClearHistoryCommand { get; }

    public string ApiKey
    {
        get => _apiKey;
        set => SetProperty(ref _apiKey, value);
    }

    public bool HasApiKey
    {
        get => _hasApiKey;
        private set
        {
            if (SetProperty(ref _hasApiKey, value))
            {
                OnPropertyChanged(nameof(IsAutoCaptureListening));
                OnPropertyChanged(nameof(CanListen));
            }
        }
    }

    public string ConnectionMessage
    {
        get => _connectionMessage;
        set => SetProperty(ref _connectionMessage, value);
    }

    public string HotkeyText
    {
        get => _hotkeyText;
        set => SetProperty(ref _hotkeyText, value);
    }

    public string ScratchpadText
    {
        get => _scratchpadText;
        set => SetProperty(ref _scratchpadText, value);
    }

    public string ScratchpadStatus
    {
        get => _scratchpadStatus;
        set => SetProperty(ref _scratchpadStatus, value);
    }

    public bool IsScratchpadRecording
    {
        get => _isScratchpadRecording;
        private set
        {
            if (SetProperty(ref _isScratchpadRecording, value))
            {
                OnPropertyChanged(nameof(ScratchpadButtonText));
            }
        }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (SetProperty(ref _startWithWindows, value))
            {
                Settings.StartWithWindows = value;
                StartupSettingChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public float MicrophoneLevel
    {
        get => _microphoneLevel;
        private set
        {
            if (SetProperty(ref _microphoneLevel, value))
            {
                OnPropertyChanged(nameof(MicrophoneLevelText));
            }
        }
    }

    public DictationState Status
    {
        get => _status;
        private set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(ToggleText));
            }
        }
    }

    public string StatusText => Status.ToString();
    public string ToggleText => Status is DictationState.Listening ? "Stop Listening" : "Start Listening";
    public string ScratchpadButtonText => IsScratchpadRecording ? "Stop test recording" : "Start test recording";
    public string MicrophoneLevelText => $"Live input {MicrophoneLevel:P0}";
    public bool CanListen => HasApiKey;
    public bool IsAutoCaptureMode => Settings.ActivationMode is ActivationMode.AutoCapture;
    public bool IsAutoCaptureListening => HasApiKey && IsAutoCaptureMode && Settings.AutoCaptureListeningEnabled;

    public void UpdateMicrophoneLevel(float level)
    {
        RunOnUi(() => MicrophoneLevel = Math.Clamp(level, 0f, 1f));
    }

    public async Task InitializeAsync()
    {
        Settings = await _settingsService.LoadAsync(CancellationToken.None);
        StartWithWindows = _startupService.IsEnabled();
        HotkeyText = Settings.Hotkey.DisplayText;
        HasApiKey = !string.IsNullOrWhiteSpace(await _secretStore.GetApiKeyAsync(CancellationToken.None));
        if (HasApiKey)
        {
            ConnectionMessage = "Groq key is saved locally with Windows user protection.";
        }

        await LoadDevicesAsync();
        RegisterHotkey();
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(IsAutoCaptureMode));
        OnPropertyChanged(nameof(IsAutoCaptureListening));
    }

    public async Task ToggleListeningAsync()
    {
        if (!await EnsureApiKeyAvailableAsync())
        {
            return;
        }

        await _orchestrator.ToggleAsync();
    }

    public async Task HandleHotkeyPressedAsync()
    {
        if (!await EnsureApiKeyAvailableAsync())
        {
            return;
        }

        if (Settings.ActivationMode is ActivationMode.AutoCapture)
        {
            await ToggleAutoCaptureListeningAsync();
            return;
        }

        if (Settings.ActivationMode is ActivationMode.HoldToTalk)
        {
            await _orchestrator.StartListeningAsync();
        }
        else
        {
            await _orchestrator.ToggleAsync();
        }
    }

    public async Task HandleHotkeyReleasedAsync()
    {
        if (!await EnsureApiKeyAvailableAsync())
        {
            return;
        }

        if (Settings.ActivationMode is ActivationMode.HoldToTalk)
        {
            await _orchestrator.StopListeningAndTranscribeAsync();
        }
    }

    public async Task ToggleAutoCaptureListeningAsync()
    {
        if (!await EnsureApiKeyAvailableAsync())
        {
            Settings.AutoCaptureListeningEnabled = false;
            await AutoSaveSettingsAsync("Add a Groq API key before enabling AutoCapture listening.");
            return;
        }

        Settings.AutoCaptureListeningEnabled = !Settings.AutoCaptureListeningEnabled;
        await SaveSettingsCoreAsync(registerHotkey: false, updateMessage: false);
        ConnectionMessage = Settings.AutoCaptureListeningEnabled
            ? "AutoCapture listening is on."
            : "AutoCapture listening is off.";
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(IsAutoCaptureMode));
        OnPropertyChanged(nameof(IsAutoCaptureListening));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        SettingsSaved?.Invoke(this, EventArgs.Empty);
        AutoCaptureListeningChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetStartWithWindowsAsync(bool enabled)
    {
        StartWithWindows = enabled;
        _startupService.SetEnabled(enabled);
        Settings.StartWithWindows = enabled;
        Settings.FirstRunComplete = true;
        await _settingsService.SaveAsync(Settings, CancellationToken.None);
        ConnectionMessage = enabled ? "Start with Windows is on." : "Start with Windows is off.";
        OnPropertyChanged(nameof(Settings));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        SettingsSaved?.Invoke(this, EventArgs.Empty);
        StartupSettingChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task StopIfNeededAsync()
    {
        await _orchestrator.StopIfNeededAsync();
    }

    private async Task ToggleScratchpadAsync()
    {
        if (IsScratchpadRecording)
        {
            await StopScratchpadAsync();
            return;
        }

        if (_audioCapture.IsRecording)
        {
            ScratchpadStatus = "Stop the current dictation before starting a scratchpad test.";
            return;
        }

        var apiKey = await _secretStore.GetApiKeyAsync(CancellationToken.None);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            ScratchpadStatus = "Add and test your Groq API key first.";
            return;
        }

        var settings = await _settingsService.LoadAsync(CancellationToken.None);
        await _audioCapture.StartRecordingAsync(settings.MicrophoneDeviceId, CancellationToken.None);
        IsScratchpadRecording = true;
        ScratchpadStatus = "Listening for scratchpad test...";
    }

    private async Task StopScratchpadAsync()
    {
        string? recordingPath = null;
        try
        {
            ScratchpadStatus = "Transcribing scratchpad test...";
            var recording = await _audioCapture.StopRecordingAsync(CancellationToken.None);
            recordingPath = recording.FilePath;
            IsScratchpadRecording = false;

            if (recording.LooksSilent)
            {
                ScratchpadStatus = "No clear microphone audio was detected. Check the selected microphone and Windows input level.";
                return;
            }

            var settings = await _settingsService.LoadAsync(CancellationToken.None);
            var apiKey = await _secretStore.GetApiKeyAsync(CancellationToken.None);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                ScratchpadStatus = "Add and test your Groq API key first.";
                return;
            }

            var transcript = await _groqClient.TranscribeAsync(
                apiKey,
                recording.FilePath,
                new TranscriptionOptions(settings.ModelId, settings.Language, settings.CustomPrompt),
                CancellationToken.None);

            ScratchpadText = settings.CleanupEnabled
                ? new TextCleanupService().Clean(transcript, settings.SpokenPunctuationEnabled)
                : transcript.Trim();
            ScratchpadStatus = "Scratchpad transcription complete.";
        }
        catch (Exception ex)
        {
            ScratchpadStatus = ex.Message;
        }
        finally
        {
            IsScratchpadRecording = false;
            if (recordingPath is not null)
            {
                var settings = await _settingsService.LoadAsync(CancellationToken.None);
                if (!settings.RetainAudioForDebugging)
                {
                    await _audioCapture.DeleteRecordingAsync(recordingPath, CancellationToken.None);
                }
            }
        }
    }

    private async Task LoadDevicesAsync()
    {
        Devices.Clear();
        foreach (var device in await _audioCapture.GetDevicesAsync(CancellationToken.None))
        {
            Devices.Add(device);
        }
    }

    public async Task AutoSaveSettingsAsync(string? message = null)
    {
        await SaveSettingsCoreAsync(registerHotkey: false, updateMessage: false);
        if (!string.IsNullOrWhiteSpace(message))
        {
            ConnectionMessage = message;
        }
    }

    public async Task SaveHotkeyAsync()
    {
        if (!HotkeyValidator.TryParse(HotkeyText, out var parsedHotkey, out var hotkeyError))
        {
            ConnectionMessage = hotkeyError ?? "Could not read hotkey.";
            return;
        }

        Settings.Hotkey = parsedHotkey;
        HotkeyText = parsedHotkey.DisplayText;
        await SaveSettingsCoreAsync(registerHotkey: true, updateMessage: true);
    }

    public void ApplyLiveSettings()
    {
        ClampSettings();
        OnPropertyChanged(nameof(IsAutoCaptureMode));
        OnPropertyChanged(nameof(IsAutoCaptureListening));
        OnPropertyChanged(nameof(CanListen));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        AutoCaptureListeningChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task SaveSettingsCoreAsync(bool registerHotkey, bool updateMessage)
    {
        ClampSettings();

        _startupService.SetEnabled(StartWithWindows);
        Settings.StartWithWindows = StartWithWindows;
        Settings.FirstRunComplete = true;
        await _settingsService.SaveAsync(Settings, CancellationToken.None);
        if (registerHotkey)
        {
            RegisterHotkey();
        }

        if (updateMessage)
        {
            ConnectionMessage = registerHotkey ? "Hotkey saved." : "Settings saved.";
        }

        OnPropertyChanged(nameof(IsAutoCaptureMode));
        OnPropertyChanged(nameof(IsAutoCaptureListening));
        OnPropertyChanged(nameof(CanListen));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        SettingsSaved?.Invoke(this, EventArgs.Empty);
        AutoCaptureListeningChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClampSettings()
    {
        Settings.OverlayOpacity = Math.Clamp(Settings.OverlayOpacity, 0.0, 1.0);
        Settings.AutoCaptureThreshold = Math.Clamp(Settings.AutoCaptureThreshold, 0.0f, 1.0f);
        Settings.AutoCaptureSilenceMs = Math.Clamp(Settings.AutoCaptureSilenceMs, 400, 5000);
        Settings.AutoCaptureMinSpeechMs = Math.Clamp(Settings.AutoCaptureMinSpeechMs, 250, 3000);
        Settings.IntentConfidenceThreshold = Math.Clamp(Settings.IntentConfidenceThreshold, 0.1, 0.95);
    }

    private async Task TestConnectionAsync()
    {
        var key = ApiKey.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            key = await _secretStore.GetApiKeyAsync(CancellationToken.None) ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            ConnectionMessage = "Paste a Groq API key first.";
            return;
        }

        ConnectionMessage = "Testing Groq connection...";
        var result = await _groqClient.TestConnectionAsync(key, CancellationToken.None);
        ConnectionMessage = result.UserMessage;
        if (result.Success)
        {
            await _secretStore.SaveApiKeyAsync(key, CancellationToken.None);
            ApiKey = string.Empty;
            HasApiKey = true;
            Settings.FirstRunComplete = true;
            await _settingsService.SaveAsync(Settings, CancellationToken.None);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task RemoveKeyAsync()
    {
        await _orchestrator.StopIfNeededAsync();
        await _secretStore.RemoveApiKeyAsync(CancellationToken.None);
        HasApiKey = false;
        ApiKey = string.Empty;
        Settings.AutoCaptureListeningEnabled = false;
        await _settingsService.SaveAsync(Settings, CancellationToken.None);
        ConnectionMessage = "Groq API key removed from this Windows user profile.";
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(IsAutoCaptureListening));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        SettingsSaved?.Invoke(this, EventArgs.Empty);
        AutoCaptureListeningChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RegisterHotkey()
    {
        if (!_hotkeyService.TryRegister(Settings.Hotkey, out var error))
        {
            ConnectionMessage = error ?? "Could not register hotkey.";
        }
    }

    private static void RunOnUi(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(action);
    }

    private async Task<bool> EnsureApiKeyAvailableAsync()
    {
        var hasKey = !string.IsNullOrWhiteSpace(await _secretStore.GetApiKeyAsync(CancellationToken.None));
        HasApiKey = hasKey;
        if (!hasKey)
        {
            ConnectionMessage = "Add a Groq API key before listening.";
        }

        return hasKey;
    }
}
