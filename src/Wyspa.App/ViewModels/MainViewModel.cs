using System.Collections.ObjectModel;
using System.Diagnostics;
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
    private readonly IAudioLevelMonitorService _levelMonitor;
    private readonly IHotkeyService _hotkeyService;
    private readonly IStartupService _startupService;
    private readonly DictationOrchestrator _orchestrator;
    private readonly GitHubUpdateService _updateService;
    private readonly IAutoCaptureMediaControlService _mediaControlService;
    private string _apiKey = string.Empty;
    private string _connectionMessage = "Add a Groq API key to enable transcription.";
    private string _hotkeyText = HotkeySettings.Default.DisplayText;
    private string _scratchpadText = string.Empty;
    private string _scratchpadStatus = "Record a short clip to test Groq transcription without inserting text.";
    private bool _hasApiKey;
    private bool _isScratchpadRecording;
    private bool _isWakeVoiceRecording;
    private bool _startWithWindows;
    private bool _isCheckingForUpdates;
    private bool _isUpdateAvailable;
    private readonly SemaphoreSlim _hotkeyGate = new(1, 1);
    private readonly SemaphoreSlim _mediaGate = new(1, 1);
    private bool _hotkeyMediaActive, _holdHotkeySession;
    private string _updateStatus = "Updates have not been checked.";
    private string? _updateUrl;
    private float _microphoneLevel;
    private bool _isInputLevelPreviewVisible;
    private string _inputLevelPreviewError = string.Empty;
    private (bool Local, bool Ready)? _providerStatus;
    private DictationState _status = DictationState.Idle;

    public MainViewModel(
        ISettingsService settingsService,
        ISecretStore secretStore,
        IGroqTranscriptionClient groqClient,
        IAudioCaptureService audioCapture,
        IAudioLevelMonitorService levelMonitor,
        IHotkeyService hotkeyService,
        IStartupService startupService,
        DictationOrchestrator orchestrator,
        GitHubUpdateService updateService,
        IAutoCaptureMediaControlService mediaControlService)
    {
        _settingsService = settingsService;
        _secretStore = secretStore;
        _groqClient = groqClient;
        _audioCapture = audioCapture;
        _levelMonitor = levelMonitor;
        _hotkeyService = hotkeyService;
        _startupService = startupService;
        _orchestrator = orchestrator;
        _updateService = updateService;
        _mediaControlService = mediaControlService;
        Settings = new AppSettings();
        Models = new GroqModelsViewModel(groqClient, () => Settings);
        Devices = [];
        SaveCommand = new AsyncRelayCommand(SaveHotkeyAsync);
        TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);
        RefreshModelsCommand = new AsyncRelayCommand(RefreshModelsAsync);
        ToggleListeningCommand = new AsyncRelayCommand(ToggleListeningAsync);
        RemoveKeyCommand = new AsyncRelayCommand(RemoveKeyAsync);
        RefreshDevicesCommand = new AsyncRelayCommand(LoadDevicesAsync);
        ScratchpadCommand = new AsyncRelayCommand(ToggleScratchpadAsync);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
        OpenUpdateCommand = new RelayCommand(_ => OpenUpdate(), _ => IsUpdateAvailable && !string.IsNullOrWhiteSpace(UpdateUrl));
        _orchestrator.StateChanged += (_, state) => RunOnUi(() => { Status = state; if (state is DictationState.Error) _ = EndHotkeyMediaAsync(); });
        _orchestrator.CaptureStopped += (_, _) => { _holdHotkeySession = false; _ = EndHotkeyMediaAsync(); };
        _audioCapture.LevelAvailable += (_, level) => UpdateMicrophoneLevel(level);
        _levelMonitor.LevelAvailable += (_, level) => UpdateMicrophoneLevel(level);
        // Wake phrase calibration owns the level-monitor subscription while active.
    }

    public event EventHandler? SettingsSaved;
    public event EventHandler<bool>? InputLevelPreviewChanged;

    public string InputLevelPreviewError
    {
        get => _inputLevelPreviewError;
        set => SetProperty(ref _inputLevelPreviewError, value);
    }

    public void SetInputLevelPreviewVisible(bool visible)
    {
        if (_isInputLevelPreviewVisible == visible) return;
        _isInputLevelPreviewVisible = visible;
        InputLevelPreviewChanged?.Invoke(this, visible);
    }
    public event EventHandler? SettingsChanged;
    public event EventHandler? AutoCaptureListeningChanged;
    public event EventHandler<bool>? AutoCaptureToggleFeedbackRequested;
    public event EventHandler? StartupSettingChanged;

    public AppSettings Settings { get; private set; }
    public GroqModelsViewModel Models { get; }
    public NotesViewModel Videos { get; set; } = null!;
    public FileTranscriptionViewModel FileTranscription { get; set; } = null!;
    private NotesViewModel? _notes;
    public NotesViewModel Notes
    {
        get => _notes!;
        set
        {
            _notes = value;
            value.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(NotesViewModel.IsActive) or nameof(NotesViewModel.IsPaused)) OnPropertyChanged(nameof(NoteCaptureActive));
            };
            OnPropertyChanged();
        }
    }
    private bool _noteCaptureActive;
    public bool NoteCaptureActive
    {
        get => _noteCaptureActive;
        set { SetProperty(ref _noteCaptureActive, value); OnPropertyChanged(nameof(CanListen)); OnPropertyChanged(nameof(IsAutoCaptureListening)); }
    }
    public async Task SetNoteCaptureActiveAsync(bool active)
    {
        NoteCaptureActive = active;
        await Task.CompletedTask;
    }
    public ObservableCollection<AudioDeviceInfo> Devices { get; }
    public ICommand SaveCommand { get; }
    public ICommand TestConnectionCommand { get; }
    public ICommand RefreshModelsCommand { get; }
    public ICommand ToggleListeningCommand { get; }
    public ICommand ScratchpadCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }
    public ICommand OpenUpdateCommand { get; }
    public ICommand RemoveKeyCommand { get; }
    public ICommand RefreshDevicesCommand { get; }

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

    public string HotkeyTitle => Settings.ActivationMode switch { ActivationMode.Toggle => "Toggle Wyspa", ActivationMode.HoldToTalk => "Hold Hotkey", _ => "SmartListening On/Off" };
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

    public WakeCalibrationViewModel? WakeCalibration { get; set; }
    public void SetWakeCalibrationState(bool active) => IsWakeVoiceRecording = active;
    public bool IsWakeVoiceRecording
    {
        get => _isWakeVoiceRecording;
        private set
        {
            if (SetProperty(ref _isWakeVoiceRecording, value))
            {
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

    public bool IsCheckingForUpdates
    {
        get => _isCheckingForUpdates;
        private set => SetProperty(ref _isCheckingForUpdates, value);
    }

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        private set
        {
            if (SetProperty(ref _isUpdateAvailable, value))
            {
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string UpdateStatus
    {
        get => _updateStatus;
        private set => SetProperty(ref _updateStatus, value);
    }

    public string? UpdateUrl
    {
        get => _updateUrl;
        private set
        {
            if (SetProperty(ref _updateUrl, value))
            {
                CommandManager.InvalidateRequerySuggested();
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
    public string WakeToneText => string.IsNullOrWhiteSpace(Settings.WakeTonePath) ? "Default tone" : Settings.WakeTonePath;
    public string MicrophoneLevelText => $"Live input {MicrophoneLevel:P0}";
    public string AppVersionText => $"Current Version {GetCurrentVersionText()}";
    public LocalModelsViewModel? LocalModels { get; set; }
    public bool CanTranscribe => Settings.UseLocalTranscription ? LocalModels?.Installed == true : HasApiKey;
    public bool CanListen => CanTranscribe && !NoteCaptureActive;
    public bool IsAutoCaptureMode => Settings.ActivationMode is ActivationMode.AutoCapture;
    public bool IsAutoCaptureListening => CanTranscribe && !NoteCaptureActive && IsAutoCaptureMode && Settings.AutoCaptureListeningEnabled;
    public bool IsWakeVoiceSettingsEnabled => IsAutoCaptureMode && Settings.AutoCaptureWakeVoiceEnabled;
    public bool IsWritingCleanupSettingsEnabled => Settings.GroqWritingCleanupEnabled;
    public bool IsIntentSettingsEnabled => Settings.IntentActionsEnabled;

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

        if (Settings.UseLocalTranscription) ConnectionMessage = CanTranscribe ? "Local transcription · audio stays on this computer." : "Download a local model in Settings to begin.";

        await LoadDevicesAsync();
        RegisterHotkeys();
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(IsAutoCaptureMode));
        OnPropertyChanged(nameof(HotkeyTitle));
        OnPropertyChanged(nameof(IsAutoCaptureListening));
        OnPropertyChanged(nameof(IsWakeVoiceSettingsEnabled));
        OnPropertyChanged(nameof(IsWritingCleanupSettingsEnabled));
        OnPropertyChanged(nameof(IsIntentSettingsEnabled));
    }

    public async Task ToggleListeningAsync()
    {
        if (NoteCaptureActive) return;
        if (!await EnsureApiKeyAvailableAsync())
        {
            return;
        }

        await _orchestrator.ToggleAsync();
    }

    public async Task HandleHotkeyPressedAsync()
    {
        await _hotkeyGate.WaitAsync();
        try
        {
            if (NoteCaptureActive || IsWakeVoiceRecording || IsScratchpadRecording || !await EnsureApiKeyAvailableAsync()) return;
            if (Settings.ActivationMode == ActivationMode.AutoCapture)
            {
                if (!Settings.AutoCaptureListeningEnabled) await BeginHotkeyMediaAsync();
                await ToggleAutoCaptureListeningAsync();
                if (!IsAutoCaptureListening) await EndHotkeyMediaAsync();
            }
            else if (_audioCapture.IsRecording)
            {
                if (Settings.ActivationMode != ActivationMode.HoldToTalk) await _orchestrator.StopListeningAndTranscribeAsync();
            }
            else
            {
                _holdHotkeySession = Settings.ActivationMode == ActivationMode.HoldToTalk;
                await BeginHotkeyMediaAsync(); await _orchestrator.StartListeningAsync();
                if (!_audioCapture.IsRecording) { _holdHotkeySession = false; await EndHotkeyMediaAsync(); }
            }
        }
        catch { _holdHotkeySession = false; await EndHotkeyMediaAsync(); throw; }
        finally { _hotkeyGate.Release(); }
    }

    public async Task HandleHotkeyReleasedAsync()
    {
        await _hotkeyGate.WaitAsync();
        try
        {
            if (!_holdHotkeySession) return;
            _holdHotkeySession = false;
            await _orchestrator.StopListeningAndTranscribeAsync();
            await EndHotkeyMediaAsync();
        }
        finally { _hotkeyGate.Release(); }
    }

    private async Task BeginHotkeyMediaAsync()
    {
        await _mediaGate.WaitAsync();
        try
        {
            if (_hotkeyMediaActive || Settings.AutoCaptureMediaBehavior == AutoCaptureMediaBehavior.None) return;
            await _mediaControlService.SetListeningStateAsync(Settings.AutoCaptureMediaBehavior, true, CancellationToken.None);
            _hotkeyMediaActive = true;
        }
        catch (Exception ex) { CrashLogService.Log(ex); }
        finally { _mediaGate.Release(); }
    }
    private async Task EndHotkeyMediaAsync()
    {
        await _mediaGate.WaitAsync();
        try
        {
            if (!_hotkeyMediaActive) return;
            _hotkeyMediaActive = false;
            await _mediaControlService.RestoreAsync(CancellationToken.None);
        }
        catch (Exception ex) { CrashLogService.Log(ex); }
        finally { _mediaGate.Release(); }
    }

    public async Task ToggleAutoCaptureListeningAsync()
    {
        if (NoteCaptureActive) return;
        if (!await EnsureApiKeyAvailableAsync())
        {
            Settings.AutoCaptureListeningEnabled = false;
            await AutoSaveSettingsAsync("Add a Groq API key before enabling SmartListen listening.");
            return;
        }

        Settings.AutoCaptureListeningEnabled = !Settings.AutoCaptureListeningEnabled;
        await SaveSettingsCoreAsync(registerHotkey: false, updateMessage: false);
        ConnectionMessage = Settings.AutoCaptureListeningEnabled
            ? "SmartListen listening is on."
            : "SmartListen listening is off.";
        AutoCaptureToggleFeedbackRequested?.Invoke(this, Settings.AutoCaptureListeningEnabled);
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(IsAutoCaptureMode));
        OnPropertyChanged(nameof(HotkeyTitle));
        OnPropertyChanged(nameof(IsAutoCaptureListening));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        RefreshTranscriptionState();
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
        RefreshTranscriptionState();
        SettingsSaved?.Invoke(this, EventArgs.Empty);
        StartupSettingChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task StopIfNeededAsync()
    {
        await FileTranscription.StopAsync();
        await _orchestrator.StopIfNeededAsync();
    }

    private async Task ToggleScratchpadAsync()
    {
        if (NoteCaptureActive) { ScratchpadStatus = "Stop the notetaker session before using dictation."; return; }
        if (_orchestrator.HasPendingStreamOutput) { ScratchpadStatus = "Wait for the current dictation to finish processing."; return; }
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
        if (!Settings.UseLocalTranscription && string.IsNullOrWhiteSpace(apiKey))
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
            if (!Settings.UseLocalTranscription && string.IsNullOrWhiteSpace(apiKey))
            {
                ScratchpadStatus = "Add and test your Groq API key first.";
                return;
            }

            var transcript = await _groqClient.TranscribeAsync(
                apiKey ?? "",
                recording.FilePath,
                new TranscriptionOptions(settings.ModelId, settings.Language, settings.CustomPrompt, UseLocal: settings.UseLocalTranscription, LocalModelId: settings.LocalModelId),
                CancellationToken.None);

            var cleaned = settings.CleanupEnabled
                ? new TextCleanupService().Clean(transcript, settings.SpokenPunctuationEnabled)
                : transcript.Trim();

            if (!TextCleanupService.HasTranscribableText(cleaned))
            {
                ScratchpadText = string.Empty;
                ScratchpadStatus = "No transcribable speech was detected.";
                return;
            }

            if (settings.GroqWritingCleanupEnabled && !string.IsNullOrWhiteSpace(cleaned))
            {
                ScratchpadStatus = "Polishing scratchpad text...";
                cleaned = await _groqClient.CleanupTranscriptAsync(
                    apiKey ?? "",
                    cleaned,
                    settings.WritingCleanupModelId,
                    settings.WritingCleanupTone,
                    settings.GetWritingCleanupPrompt(),
                    CancellationToken.None);
            }

            if (!TextCleanupService.HasTranscribableText(cleaned))
            {
                ScratchpadText = string.Empty;
                ScratchpadStatus = "No transcribable speech was detected.";
                return;
            }

            ScratchpadText = cleaned;
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

    public async Task SetWakeTonePathAsync(string? path)
    {
        Settings.WakeTonePath = string.IsNullOrWhiteSpace(path) ? null : path;
        await AutoSaveSettingsAsync(Settings.WakeTonePath is null ? "Using the default wake tone." : "Custom wake tone saved.");
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(WakeToneText));
    }

    public async Task SuspendHotkeyEditingAsync()
    {
        _hotkeyService.Unregister();
        if (_holdHotkeySession) { _holdHotkeySession = false; await _orchestrator.StopIfNeededAsync(); await EndHotkeyMediaAsync(); }
    }
    public void ResumeHotkeyEditing() => RegisterDictationHotkey();

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

        var previousHotkey = Settings.Hotkey;
        Settings.Hotkey = parsedHotkey;
        HotkeyText = parsedHotkey.DisplayText;
        if (!RegisterDictationHotkey())
        {
            Settings.Hotkey = previousHotkey;
            HotkeyText = previousHotkey.DisplayText;
            RegisterDictationHotkey();
            return;
        }

        await SaveSettingsCoreAsync(registerHotkey: false, updateMessage: false);
        ConnectionMessage = "Hotkey saved.";
    }

    public void ApplyLiveSettings()
    {
        ClampSettings();
        WakeCalibration?.RefreshState();
        OnPropertyChanged(nameof(IsAutoCaptureMode));
        OnPropertyChanged(nameof(HotkeyTitle));
        OnPropertyChanged(nameof(IsAutoCaptureListening));
        OnPropertyChanged(nameof(CanListen));
        OnPropertyChanged(nameof(WakeToneText));
        OnPropertyChanged(nameof(IsWakeVoiceSettingsEnabled));
        OnPropertyChanged(nameof(IsWritingCleanupSettingsEnabled));
        OnPropertyChanged(nameof(IsIntentSettingsEnabled));
        OnPropertyChanged(nameof(Settings));
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
            RegisterHotkeys();
        }

        if (updateMessage)
        {
            ConnectionMessage = registerHotkey ? "Hotkey saved." : "Settings saved.";
        }

        OnPropertyChanged(nameof(IsAutoCaptureMode));
        OnPropertyChanged(nameof(HotkeyTitle));
        OnPropertyChanged(nameof(IsAutoCaptureListening));
        OnPropertyChanged(nameof(CanListen));
        OnPropertyChanged(nameof(WakeToneText));
        OnPropertyChanged(nameof(IsWakeVoiceSettingsEnabled));
        OnPropertyChanged(nameof(IsWritingCleanupSettingsEnabled));
        OnPropertyChanged(nameof(IsIntentSettingsEnabled));
        OnPropertyChanged(nameof(Settings));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        RefreshTranscriptionState();
        SettingsSaved?.Invoke(this, EventArgs.Empty);
        AutoCaptureListeningChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ClampSettings()
    {
        Settings.OverlayOpacity = Math.Clamp(Settings.OverlayOpacity, 0.0, 1.0);
        Settings.AutoCaptureThreshold = Math.Clamp(Settings.AutoCaptureThreshold, 0.0f, 1.0f);
        Settings.AutoCaptureSilenceMs = Math.Clamp(Settings.AutoCaptureSilenceMs, 400, 5000);
        Settings.AutoCaptureMinSpeechMs = Math.Clamp(Settings.AutoCaptureMinSpeechMs, 250, 3000);
        Settings.AutoCaptureWakeVoiceSensitivity = Math.Clamp(Settings.AutoCaptureWakeVoiceSensitivity, 0.0, 1.0);
        Settings.IntentConfidenceThreshold = Math.Clamp(Settings.IntentConfidenceThreshold, 0.1, 0.95);
        if (string.IsNullOrWhiteSpace(Settings.WritingCleanupModelId))
        {
            Settings.WritingCleanupModelId = GroqTranscriptionClient.DefaultCleanupModel;
        }
        if (string.IsNullOrWhiteSpace(Settings.FormalRewritePrompt))
        {
            Settings.FormalRewritePrompt = WritingCleanupPromptDefaults.Formal;
        }
        if (string.IsNullOrWhiteSpace(Settings.CasualRewritePrompt))
        {
            Settings.CasualRewritePrompt = WritingCleanupPromptDefaults.Casual;
        }
        if (string.IsNullOrWhiteSpace(Settings.TechnicalRewritePrompt))
        {
            Settings.TechnicalRewritePrompt = WritingCleanupPromptDefaults.Technical;
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        IsUpdateAvailable = false;
        UpdateUrl = null;
        UpdateStatus = "Checking GitHub for updates...";
        try
        {
            var result = await _updateService.CheckLatestAsync(GetCurrentVersion(), CancellationToken.None);
            UpdateStatus = result.UserMessage;
            UpdateUrl = result.InstallerUrl ?? result.ReleaseUrl;
            IsUpdateAvailable = result.UpdateAvailable;
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private void OpenUpdate()
    {
        if (string.IsNullOrWhiteSpace(UpdateUrl))
        {
            return;
        }

        Process.Start(new ProcessStartInfo(UpdateUrl)
        {
            UseShellExecute = true
        });
    }

    private async Task TestConnectionAsync()
    {
        if (Models.IsRefreshing) return;
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
        var result = await Models.RefreshAsync(key);
        ConnectionMessage = result.UserMessage;
        if (result.Success)
        {
            await _secretStore.SaveApiKeyAsync(key, CancellationToken.None);
            ApiKey = string.Empty;
            HasApiKey = true;
            Settings.FirstRunComplete = true;
            await _settingsService.SaveAsync(Settings, CancellationToken.None);
            SettingsChanged?.Invoke(this, EventArgs.Empty);
            RefreshTranscriptionState();
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task RefreshModelsAsync()
    {
        await Models.RefreshAsync(await _secretStore.GetApiKeyAsync(CancellationToken.None) ?? "");
    }

    private async Task RemoveKeyAsync()
    {
        if (Models.IsRefreshing) return;
        await FileTranscription.StopAsync();
        await _orchestrator.StopIfNeededAsync();
        await _secretStore.RemoveApiKeyAsync(CancellationToken.None);
        HasApiKey = false;
        ApiKey = string.Empty;
        Models.Clear();
        Settings.AutoCaptureListeningEnabled = false;
        await EndHotkeyMediaAsync();
        await _settingsService.SaveAsync(Settings, CancellationToken.None);
        ConnectionMessage = "Groq API key removed from this Windows user profile.";
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(IsAutoCaptureListening));
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        RefreshTranscriptionState();
        SettingsSaved?.Invoke(this, EventArgs.Empty);
        AutoCaptureListeningChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RegisterHotkeys()
    {
        RegisterDictationHotkey();
    }

    private bool RegisterDictationHotkey()
    {
        if (!_hotkeyService.TryRegister(Settings.Hotkey, out var error))
        {
            ConnectionMessage = error ?? "Could not register hotkey.";
            return false;
        }

        return true;
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
        if (Settings.UseLocalTranscription)
        {
            if (LocalModels?.Installed == true) return true;
            ConnectionMessage = "Install a local model in Settings before listening.";
            return false;
        }
        var hasKey = !string.IsNullOrWhiteSpace(await _secretStore.GetApiKeyAsync(CancellationToken.None));
        HasApiKey = hasKey;
        if (!hasKey)
        {
            ConnectionMessage = "Add a Groq API key before listening.";
        }

        return hasKey;
    }

    private static Version GetCurrentVersion()
    {
        var version = typeof(MainViewModel).Assembly.GetName().Version;
        return version is null ? new Version(0, 0, 0) : new Version(version.Major, Math.Max(version.Minor, 0), Math.Max(version.Build, 0));
    }

    private static string GetCurrentVersionText()
    {
        var version = GetCurrentVersion();
        return $"{version.Major}.{version.Minor}.{version.Build}";
    }
    private void RefreshTranscriptionState()
    {
        var state = (Local: Settings.UseLocalTranscription, Ready: CanTranscribe);
        if (_providerStatus != state)
        {
            _providerStatus = state;
            ConnectionMessage = state.Local ? state.Ready ? "Local transcription · audio stays on this computer." : "Install a local model in Settings to begin." : HasApiKey ? "Groq is connected." : "Add a Groq API key to enable transcription.";
        }
        OnPropertyChanged(nameof(CanTranscribe)); OnPropertyChanged(nameof(CanListen)); OnPropertyChanged(nameof(IsAutoCaptureListening));
        WakeCalibration?.RefreshState();
    }

}
