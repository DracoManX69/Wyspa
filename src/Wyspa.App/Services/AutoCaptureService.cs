using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using Wyspa.Core.Services;
using System.Windows.Threading;

namespace Wyspa.App.Services;

public sealed class AutoCaptureService : IDisposable
{
    public bool Suspended { get; set; }
    public bool LevelPreviewEnabled { get; private set; }
    private readonly ISettingsService _settingsService;
    private readonly ISecretStore _secretStore;
    private readonly IAudioLevelMonitorService _monitor;
    private readonly IAudioCaptureService _audioCapture;
    private readonly DictationOrchestrator _orchestrator;
    private readonly OverlayStatusService _overlay;
    private readonly WakeToneService _wakeTone;
    private readonly WakeVoiceMatcher _wakeVoiceMatcher = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _settingsLock = new();
    private readonly List<float> _wakeVoiceBuffer = [];
    private readonly Queue<float> _preRoll = new();
    private readonly Dispatcher _dispatcher;
    private AppSettings _settings = new();
    private DateTimeOffset _lastVoiceAt;
    private DateTimeOffset _recordingStartedAt;
    private DateTimeOffset _cooldownUntil;
    private string? _monitorDeviceId;
    private bool _hasApiKey;
    private bool _isStarting;
    private bool _isStopping;
    private DateTimeOffset _lastWakeVoiceCheckAt;
    private DateTimeOffset _wakeVoiceAcceptedUntil;

    public AutoCaptureService(
        ISettingsService settingsService,
        ISecretStore secretStore,
        IAudioLevelMonitorService monitor,
        IAudioCaptureService audioCapture,
        DictationOrchestrator orchestrator,
        OverlayStatusService overlay,
        WakeToneService wakeTone)
    {
        _settingsService = settingsService;
        _secretStore = secretStore;
        _monitor = monitor;
        _audioCapture = audioCapture;
        _orchestrator = orchestrator;
        _overlay = overlay;
        _wakeTone = wakeTone;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _monitor.LevelAvailable += OnMonitorLevel;
        _monitor.AudioAvailable += OnMonitorAudio;
        _audioCapture.LevelAvailable += OnRecordingLevel;
        _orchestrator.ListeningStarting += OnListeningStarting;
        _orchestrator.CaptureStopped += OnCaptureStopped;
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
            await ApplyMonitorStateAsync(settings, hasApiKey, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    // A visible threshold meter can use local levels without enabling speech-triggered
    // capture, requiring a key, changing saved settings, or creating an audio file.
    public async Task SetLevelPreviewAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            LevelPreviewEnabled = enabled;
            await ApplyMonitorStateAsync(GetSettingsSnapshot(), _hasApiKey, cancellationToken);
        }
        finally { _gate.Release(); }
    }

    private async Task ApplyMonitorStateAsync(AppSettings settings, bool hasApiKey, CancellationToken cancellationToken)
    {
        if (_audioCapture.IsRecording || !ShouldMonitorRun(settings, hasApiKey))
        {
            _monitor.Stop();
            return;
        }

        if (!_monitor.IsRunning || !string.Equals(_monitorDeviceId, settings.MicrophoneDeviceId, StringComparison.Ordinal))
        {
            _monitor.Stop();
            _monitorDeviceId = settings.MicrophoneDeviceId;
            await _monitor.StartAsync(settings.MicrophoneDeviceId, cancellationToken);
        }
    }

    public void Dispose()
    {
        _monitor.LevelAvailable -= OnMonitorLevel;
        _monitor.AudioAvailable -= OnMonitorAudio;
        _audioCapture.LevelAvailable -= OnRecordingLevel;
        _orchestrator.ListeningStarting -= OnListeningStarting;
        _orchestrator.CaptureStopped -= OnCaptureStopped;
        _orchestrator.StateChanged -= OnOrchestratorStateChanged;
        _monitor.Dispose();
        _gate.Dispose();
    }

    private void OnMonitorLevel(object? sender, float level)
    {
        _overlay.UpdateLevel(level);
        var settings = GetSettingsSnapshot();
        var now = DateTimeOffset.UtcNow;
        if (Suspended || settings.ActivationMode is not ActivationMode.AutoCapture ||
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

        if (settings.AutoCaptureWakeVoiceEnabled)
        {
            return;
        }

        RunOnAppDispatcher(StartCaptureAsync);
    }

    private void OnMonitorAudio(object? sender, IReadOnlyList<float> samples)
    {
        var settings = GetSettingsSnapshot();
        lock (_preRoll)
        {
            if (!Suspended && _hasApiKey && settings.ActivationMode is ActivationMode.AutoCapture &&
                settings.AutoCaptureListeningEnabled && !_audioCapture.IsRecording)
            {
                foreach (var sample in samples) _preRoll.Enqueue(sample);
                while (_preRoll.Count > 8000) _preRoll.Dequeue();
            }
            else _preRoll.Clear();
        }
        if (Suspended || settings.ActivationMode is not ActivationMode.AutoCapture ||
            !settings.AutoCaptureListeningEnabled ||
            !settings.AutoCaptureWakeVoiceEnabled ||
            settings.AutoCaptureWakeVoiceProfile is null ||
            !_hasApiKey ||
            _audioCapture.IsRecording ||
            _isStarting ||
            _isStopping ||
            DateTimeOffset.UtcNow < _cooldownUntil)
        {
            ClearWakeVoiceBuffer();
            return;
        }

        AppendWakeVoiceSamples(samples);
        var now = DateTimeOffset.UtcNow;
        if (now - _lastWakeVoiceCheckAt < TimeSpan.FromMilliseconds(180))
        {
            return;
        }

        _lastWakeVoiceCheckAt = now;
        if (Peak(samples) < Math.Max(0.015f, settings.AutoCaptureThreshold * 0.45f))
        {
            return;
        }

        var score = ScoreWakeVoice(settings.AutoCaptureWakeVoiceProfile);
        if (score >= settings.AutoCaptureWakeVoiceSensitivity)
        {
            _wakeVoiceAcceptedUntil = DateTimeOffset.UtcNow.AddMilliseconds(900);
            ClearWakeVoiceBuffer();
            RunOnAppDispatcher(StartCaptureAsync);
        }
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
            var startedAt = _recordingStartedAt;
            RunOnAppDispatcher(() => StopCaptureAsync(startedAt));
        }
    }

    private async Task StartCaptureAsync()
    {
        await _gate.WaitAsync();
        try
        {
            var settings = GetSettingsSnapshot();
            if (Suspended || _audioCapture.IsRecording ||
                _isStarting ||
                _isStopping ||
                settings.ActivationMode is not ActivationMode.AutoCapture ||
                !settings.AutoCaptureListeningEnabled ||
                !_hasApiKey ||
                !WakeVoiceGateSatisfied(settings, DateTimeOffset.UtcNow) ||
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
            _wakeVoiceAcceptedUntil = DateTimeOffset.MinValue;
            _monitor.Stop();
            byte[] preRoll;
            lock (_preRoll)
            {
                preRoll = new byte[_preRoll.Count * 2];
                var index = 0;
                foreach (var sample in _preRoll)
                {
                    System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(preRoll.AsSpan(index, 2),
                        (short)Math.Clamp((int)(sample * 32768), short.MinValue, short.MaxValue));
                    index += 2;
                }
                _preRoll.Clear();
            }
            if (settings.AutoCaptureWakeVoiceEnabled)
            {
                _wakeTone.Play(settings);
            }

            _recordingStartedAt = DateTimeOffset.UtcNow;
            _lastVoiceAt = _recordingStartedAt;
            await _orchestrator.StartListeningAsync(preRoll: preRoll);
        }
        finally
        {
            _isStarting = false;
            _gate.Release();
        }
    }

    private async Task StopCaptureAsync(DateTimeOffset startedAt)
    {
        bool streaming;
        await _gate.WaitAsync();
        try
        {
            if (!_audioCapture.IsRecording || _isStopping || _recordingStartedAt != startedAt)
            {
                return;
            }

            _isStopping = true;
            streaming = _orchestrator.IsStreaming;
        }
        finally { _gate.Release(); }
        try
        {
            await _orchestrator.StopListeningAndTranscribeAsync();
            if (!streaming)
            {
                _cooldownUntil = DateTimeOffset.UtcNow.AddMilliseconds(700);
                await RestartMonitorIfNeededAsync();
            }
        }
        finally
        {
            if (!streaming) _isStopping = false;
        }
    }

    private async Task RestartMonitorIfNeededAsync()
    {
        var settings = GetSettingsSnapshot();
        if (_audioCapture.IsRecording)
        {
            return;
        }

        if (!settings.StreamModeEnabled) await Task.Delay(200);
        settings = GetSettingsSnapshot();
        if (_audioCapture.IsRecording) return;
        if (!ShouldMonitorRun(settings))
        {
            _monitor.Stop();
            return;
        }

        if (!_monitor.IsRunning)
        {
            _monitorDeviceId = settings.MicrophoneDeviceId;
            await _monitor.StartAsync(settings.MicrophoneDeviceId, CancellationToken.None);
        }
    }

    private void OnListeningStarting(object? sender, EventArgs e)
    {
        _monitor.Stop();
        lock (_preRoll) _preRoll.Clear();
    }

    private void OnCaptureStopped(object? sender, EventArgs e)
    {
        _isStopping = false;
        // Network processing continues independently; resume the local monitor
        // now, without a network-length gap or a post-processing cooldown.
        RunOnAppDispatcher(RestartMonitorIfNeededAsync);
    }

    private void OnOrchestratorStateChanged(object? sender, DictationState state)
    {
        if (state is DictationState.Listening or DictationState.Transcribing)
        {
            return;
        }

        RunOnAppDispatcher(RestartMonitorIfNeededAsync);
    }

    private bool WakeVoiceGateSatisfied(AppSettings settings, DateTimeOffset now) =>
        !settings.AutoCaptureWakeVoiceEnabled || now <= _wakeVoiceAcceptedUntil;

    private bool ShouldMonitorRun(AppSettings settings) => ShouldMonitorRun(settings, _hasApiKey);

    private bool ShouldMonitorRun(AppSettings settings, bool hasApiKey) =>
        !Suspended && (LevelPreviewEnabled || (hasApiKey &&
        settings.ActivationMode is ActivationMode.AutoCapture &&
        settings.AutoCaptureListeningEnabled));

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

        if (!settings.AutoCaptureWakeVoiceEnabled || settings.AutoCaptureWakeVoiceProfile is null)
        {
            ClearWakeVoiceBuffer();
            _wakeVoiceAcceptedUntil = DateTimeOffset.MinValue;
        }
    }

    private void AppendWakeVoiceSamples(IReadOnlyList<float> samples)
    {
        lock (_wakeVoiceBuffer)
        {
            _wakeVoiceBuffer.AddRange(samples);
            var maxSampleCount = WakeVoiceMatcher.SampleRate * 4;
            if (_wakeVoiceBuffer.Count > maxSampleCount)
            {
                _wakeVoiceBuffer.RemoveRange(0, _wakeVoiceBuffer.Count - maxSampleCount);
            }
        }
    }

    private double ScoreWakeVoice(WakeVoiceProfile profile)
    {
        lock (_wakeVoiceBuffer)
        {
            return _wakeVoiceMatcher.Score(_wakeVoiceBuffer, profile);
        }
    }

    private void ClearWakeVoiceBuffer()
    {
        lock (_wakeVoiceBuffer)
        {
            _wakeVoiceBuffer.Clear();
        }
    }

    private static float Peak(IReadOnlyList<float> samples)
    {
        var peak = 0f;
        for (var index = 0; index < samples.Count; index++)
        {
            peak = Math.Max(peak, Math.Abs(samples[index]));
        }

        return peak;
    }

    private static AppSettings CopySettings(AppSettings settings) => new()
    {
        FirstRunComplete = settings.FirstRunComplete,
        MicrophoneDeviceId = settings.MicrophoneDeviceId,
        Hotkey = settings.Hotkey,
        AutoCaptureHotkey = settings.AutoCaptureHotkey,
        ActivationMode = settings.ActivationMode,
        StreamModeEnabled = settings.StreamModeEnabled,
        StreamFixEnabled = settings.StreamFixEnabled,
        ModelId = settings.ModelId,
        Language = settings.Language,
        CustomPrompt = settings.CustomPrompt,
        StartMinimized = settings.StartMinimized,
        StartWithWindows = settings.StartWithWindows,
        InsertionMode = settings.InsertionMode,
        CopyInsertedTextToClipboard = settings.CopyInsertedTextToClipboard,
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
        AutoCaptureListeningEnabled = settings.AutoCaptureListeningEnabled,
        AutoCaptureMediaBehavior = settings.AutoCaptureMediaBehavior,
        AutoCaptureWakeVoiceEnabled = settings.AutoCaptureWakeVoiceEnabled,
        AutoCaptureWakeVoiceSensitivity = settings.AutoCaptureWakeVoiceSensitivity,
        AutoCaptureWakeVoiceProfile = settings.AutoCaptureWakeVoiceProfile,
        WakeToneEnabled = settings.WakeToneEnabled,
        WakeTonePath = settings.WakeTonePath
    };
}
