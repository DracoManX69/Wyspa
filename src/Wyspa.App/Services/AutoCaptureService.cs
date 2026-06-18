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
    private readonly Dispatcher _dispatcher;
    private AppSettings _settings = new();
    private DateTimeOffset _lastVoiceAt;
    private DateTimeOffset _recordingStartedAt;
    private DateTimeOffset _cooldownUntil;
    private string? _monitorDeviceId;
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
        _settings = await _settingsService.LoadAsync(cancellationToken);
        if (!_audioCapture.IsRecording)
        {
            if (!_monitor.IsRunning || !string.Equals(_monitorDeviceId, _settings.MicrophoneDeviceId, StringComparison.Ordinal))
            {
                _monitor.Stop();
                _monitorDeviceId = _settings.MicrophoneDeviceId;
                await _monitor.StartAsync(_settings.MicrophoneDeviceId, cancellationToken);
            }
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
        var now = DateTimeOffset.UtcNow;
        if (_settings.ActivationMode is not ActivationMode.AutoCapture ||
            !_settings.AutoCaptureListeningEnabled ||
            _audioCapture.IsRecording ||
            _isStarting ||
            _isStopping ||
            now < _cooldownUntil ||
            level < _settings.AutoCaptureThreshold)
        {
            return;
        }

        RunOnAppDispatcher(StartCaptureAsync);
    }

    private void OnRecordingLevel(object? sender, float level)
    {
        if (_settings.ActivationMode is not ActivationMode.AutoCapture || !_audioCapture.IsRecording)
        {
            return;
        }

        if (level >= _settings.AutoCaptureThreshold * 0.65f)
        {
            _lastVoiceAt = DateTimeOffset.UtcNow;
            return;
        }

        var now = DateTimeOffset.UtcNow;
        if (!_isStopping &&
            now - _lastVoiceAt >= TimeSpan.FromMilliseconds(_settings.AutoCaptureSilenceMs) &&
            now - _recordingStartedAt >= TimeSpan.FromMilliseconds(_settings.AutoCaptureMinSpeechMs))
        {
            RunOnAppDispatcher(StopCaptureAsync);
        }
    }

    private async Task StartCaptureAsync()
    {
        await _gate.WaitAsync();
        try
        {
            if (_audioCapture.IsRecording ||
                _isStarting ||
                _isStopping ||
                _settings.ActivationMode is not ActivationMode.AutoCapture ||
                !_settings.AutoCaptureListeningEnabled ||
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
        _settings = await _settingsService.LoadAsync(CancellationToken.None);
        if (_audioCapture.IsRecording)
        {
            return;
        }

        await Task.Delay(200);
        if (!_monitor.IsRunning)
        {
            _monitorDeviceId = _settings.MicrophoneDeviceId;
            await _monitor.StartAsync(_settings.MicrophoneDeviceId, CancellationToken.None);
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
}
