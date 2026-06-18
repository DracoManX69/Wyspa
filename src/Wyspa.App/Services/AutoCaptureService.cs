using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using System.Windows.Threading;

namespace Wyspa.App.Services;

public sealed class AutoCaptureService : IDisposable
{
    private readonly ISettingsService _settingsService;
    private readonly ISecretStore _secretStore;
    private readonly IAudioLevelMonitorService _monitor;
    private readonly IAudioCaptureService _audioCapture;
    private readonly DictationOrchestrator _orchestrator;
    private readonly OverlayStatusService _overlay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _settingsLock = new();
    private readonly Dispatcher _dispatcher;
    private AppSettings _settings = new();
    private DateTimeOffset _lastVoiceAt;
    private DateTimeOffset _recordingStartedAt;
    private DateTimeOffset _cooldownUntil;
    private string? _monitorDeviceId;
    private bool _hasApiKey;
    private bool _isStarting;
    private bool _isStopping;

    public AutoCaptureService(
        ISettingsService settingsService,
        ISecretStore secretStore,
        IAudioLevelMonitorService monitor,
        IAudioCaptureService audioCapture,
        DictationOrchestrator orchestrator,
        OverlayStatusService overlay)
    {
        _settingsService = settingsService;
        _secretStore = secretStore;
        _monitor = monitor;
        _audioCapture = audioCapture;
        _orchestrator = orchestrator;
        _overlay = overlay;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _monitor.LevelAvailable += OnMonitorLevel;
        _audioCapture.LevelAvailable += OnRecordingLevel;
        _orchestrator.ListeningStarting += OnListeningStarting;
        _orchestrator.StateChanged += OnOrchestratorStateChanged;
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var settings = await _settingsService.LoadAsync(cancellationToken);
        var hasApiKey = !string.IsNullOrWhiteSpace(await _secretStore.GetApiKeyAsync(cancellationToken));
        await ApplySettingsAsync(settings, hasApiKey, cancellationToken);
    }

    public async Task ApplySettingsAsync(AppSettings settings, bool hasApiKey, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            SetSettings(settings, hasApiKey);
            _overlay.SetOpacity(settings.OverlayOpacity);
            if (!_audioCapture.IsRecording)
            {
                if (!_monitor.IsRunning || !string.Equals(_monitorDeviceId, settings.MicrophoneDeviceId, StringComparison.Ordinal))
                {
                    _monitor.Stop();
                    _monitorDeviceId = settings.MicrophoneDeviceId;
                    await _monitor.StartAsync(settings.MicrophoneDeviceId, cancellationToken);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _monitor.LevelAvailable -= OnMonitorLevel;
        _audioCapture.LevelAvailable -= OnRecordingLevel;
        _orchestrator.ListeningStarting -= OnListeningStarting;
        _orchestrator.StateChanged -= OnOrchestratorStateChanged;
        _monitor.Dispose();
        _gate.Dispose();
    }

    private void OnMonitorLevel(object? sender, float level)
    {
        _overlay.UpdateLevel(level);
        var settings = GetSettingsSnapshot();
        var now = DateTimeOffset.UtcNow;
        if (settings.ActivationMode is not ActivationMode.AutoCapture ||
            !settings.AutoCaptureListeningEnabled ||
            !_hasApiKey ||
            _audioCapture.IsRecording ||
            _isStarting ||
            _isStopping ||
            now < _cooldownUntil ||
            level < settings.AutoCaptureThreshold)
        {
            return;
        }

        RunOnAppDispatcher(StartCaptureAsync);
    }

    private void OnRecordingLevel(object? sender, float level)
    {
        var settings = GetSettingsSnapshot();
        if (settings.ActivationMode is not ActivationMode.AutoCapture || !_audioCapture.IsRecording)
        {
            return;
        }

        if (level >= settings.AutoCaptureThreshold * 0.65f)
        {
            _lastVoiceAt = DateTimeOffset.UtcNow;
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!_isStopping &&
            now - _lastVoiceAt >= TimeSpan.FromMilliseconds(settings.AutoCaptureSilenceMs) &&
            now - _recordingStartedAt >= TimeSpan.FromMilliseconds(settings.AutoCaptureMinSpeechMs))
        {
            RunOnAppDispatcher(StopCaptureAsync);
        }
    }

    private async Task StartCaptureAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var settings = GetSettingsSnapshot();
            if (_audioCapture.IsRecording ||
                _isStarting ||
                _isStopping ||
                settings.ActivationMode is not ActivationMode.AutoCapture ||
                !settings.AutoCaptureListeningEnabled ||
                !_hasApiKey ||
                DateTimeOffset.UtcNow < _cooldownUntil)
            {
                return;
            }

            var apiKey = await _secretStore.GetApiKeyAsync(CancellationToken.None);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                _cooldownUntil = DateTimeOffset.UtcNow.AddMilliseconds(1200);
                return;
            }

            _isStarting = true;
            _monitor.Stop();
            _recordingStartedAt = DateTimeOffset.UtcNow;
            _lastVoiceAt = _recordingStartedAt;
            await _orchestrator.StartListeningAsync();
        }
        finally
        {
            _isStarting = false;
            _gate.Release();
        }
    }

    private async Task StopCaptureAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (!_audioCapture.IsRecording || _isStopping)
            {
                return;
            }

            _isStopping = true;
            await _orchestrator.StopListeningAndTranscribeAsync();
            _cooldownUntil = DateTimeOffset.UtcNow.AddMilliseconds(700);
            await RestartMonitorIfNeededAsync();
        }
        finally
        {
            _isStopping = false;
            _gate.Release();
        }
    }

    private async Task RestartMonitorIfNeededAsync()
    {
        var settings = GetSettingsSnapshot();
        if (_audioCapture.IsRecording)
        {
            return;
        }

        await Task.Delay(200);
        if (!_monitor.IsRunning)
        {
            _monitorDeviceId = settings.MicrophoneDeviceId;
            await _monitor.StartAsync(settings.MicrophoneDeviceId, CancellationToken.None);
        }
    }

    private void OnListeningStarting(object? sender, EventArgs e)
    {
        _monitor.Stop();
    }

    private void OnOrchestratorStateChanged(object? sender, DictationState state)
    {
        if (state is DictationState.Listening or DictationState.Transcribing)
        {
            return;
        }

        RunOnAppDispatcher(RestartMonitorIfNeededAsync);
    }

    private void RunOnAppDispatcher(Func<Task> action)
    {
        if (_dispatcher.CheckAccess())
        {
            _ = ExecuteSafelyAsync(action);
            return;
        }

        _dispatcher.BeginInvoke(() => _ = ExecuteSafelyAsync(action));
    }

    private static async Task ExecuteSafelyAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            CrashLogService.Log(ex);
        }
    }

    private AppSettings GetSettingsSnapshot()
    {
        lock (_settingsLock)
        {
            return _settings;
        }
    }

    private void SetSettings(AppSettings settings, bool hasApiKey)
    {
        lock (_settingsLock)
        {
            _settings = CopySettings(settings);
            _hasApiKey = hasApiKey;
        }
    }

    private static AppSettings CopySettings(AppSettings settings) => new()
    {
        FirstRunComplete = settings.FirstRunComplete,
        MicrophoneDeviceId = settings.MicrophoneDeviceId,
        Hotkey = settings.Hotkey,
        ActivationMode = settings.ActivationMode,
        ModelId = settings.ModelId,
        Language = settings.Language,
        CustomPrompt = settings.CustomPrompt,
        StartMinimized = settings.StartMinimized,
        StartWithWindows = settings.StartWithWindows,
        InsertionMode = settings.InsertionMode,
        CleanupEnabled = settings.CleanupEnabled,
        SpokenPunctuationEnabled = settings.SpokenPunctuationEnabled,
        IntentActionsEnabled = settings.IntentActionsEnabled,
        IntentModelId = settings.IntentModelId,
        IntentConfidenceThreshold = settings.IntentConfidenceThreshold,
        HistoryEnabled = settings.HistoryEnabled,
        RetainAudioForDebugging = settings.RetainAudioForDebugging,
        OverlayOpacity = settings.OverlayOpacity,
        AutoCaptureThreshold = settings.AutoCaptureThreshold,
        AutoCaptureSilenceMs = settings.AutoCaptureSilenceMs,
        AutoCaptureMinSpeechMs = settings.AutoCaptureMinSpeechMs,
        AutoCaptureListeningEnabled = settings.AutoCaptureListeningEnabled
    };
}
