using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using Wyspa.Core.Services;
using System.Windows.Threading;
using System.Diagnostics;

namespace Wyspa.App.Services;

public sealed class AutoCaptureService : IDisposable
{
    private bool _suspended;
    private CancellationTokenSource? _wakePreparation;
    public bool Suspended
    {
        get => _suspended;
        set
        {
            if (_suspended == value) return;
            _suspended = value;
            if (value) { _wakePreparation?.Cancel(); _monitor.Stop(); }
            Interlocked.Increment(ref _wakeGeneration);
        }
    }
    public bool LevelPreviewEnabled { get; private set; }
    private readonly ISettingsService _settingsService;
    private readonly ISecretStore _secretStore;
    private readonly IAudioLevelMonitorService _monitor;
    private readonly IAudioCaptureService _audioCapture;
    private readonly DictationOrchestrator _orchestrator;
    private readonly OverlayStatusService _overlay;
    private readonly WakeToneService _wakeTone;
    private readonly IWakePhraseDetector _wakeDetector;
    private readonly bool _ownsWakeDetector;
    private readonly CancellationTokenSource _wakeShutdown = new();
    private readonly System.Threading.Channels.Channel<(float[] Samples, int Generation)> _wakeQueue = System.Threading.Channels.Channel.CreateBounded<(float[], int)>(new System.Threading.Channels.BoundedChannelOptions(8) { SingleReader = true, FullMode = System.Threading.Channels.BoundedChannelFullMode.Wait });
    private readonly Task _wakeWorker;
    private readonly ISpeechActivityDetector _speechDetector;
    private readonly bool _ownsSpeechDetector;
    private readonly IStreamingAudioCaptureService? _pcmSource;
    private readonly SpeechEndpoint _endpoint = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Task _speechWorker, _endpointWorker;
    private readonly System.Threading.Channels.Channel<(float[] Samples, long Generation, TimeSpan At)> _speechQueue = System.Threading.Channels.Channel.CreateBounded<(float[], long, TimeSpan)>(32);
    private volatile bool _speechReady, _automaticSession;
    private long _sessionGeneration;
    private int _wakeGeneration;
    private bool _wakeReady;
    private string? _appliedWakeConfiguration;
    private string _wakeStatus = "Wake phrase detection is off.";
    public string WakeStatus => _wakeStatus;
    public event EventHandler<string>? WakeStatusChanged;
    private void ReportWake(string message) { _wakeStatus = message; WakeStatusChanged?.Invoke(this, message); }

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _settingsLock = new();
    private readonly Queue<float> _preRoll = new();
    private readonly Dispatcher _dispatcher;
    private AppSettings _settings = new();
    private DateTimeOffset _cooldownUntil;
    private string? _monitorDeviceId;
    private bool _hasApiKey;
    private bool _isStarting;
    private bool _isStopping;
    private DateTimeOffset _wakeVoiceAcceptedUntil;

    public AutoCaptureService(
        ISettingsService settingsService,
        ISecretStore secretStore,
        IAudioLevelMonitorService monitor,
        IAudioCaptureService audioCapture,
        DictationOrchestrator orchestrator,
        OverlayStatusService overlay,
        WakeToneService wakeTone, IWakePhraseDetector? wakeDetector = null, ISpeechActivityDetector? speechDetector = null)
    {
        _settingsService = settingsService;
        _secretStore = secretStore;
        _monitor = monitor;
        _audioCapture = audioCapture;
        _orchestrator = orchestrator;
        _overlay = overlay;
        _wakeTone = wakeTone;
        _wakeDetector = wakeDetector ?? new WakeKeywordEngine(); _ownsWakeDetector = wakeDetector is null;
        _speechDetector = speechDetector ?? new LocalSpeechActivityDetector(); _ownsSpeechDetector = speechDetector is null;
        _pcmSource = audioCapture as IStreamingAudioCaptureService;
        if (_pcmSource is not null) _pcmSource.PcmAvailable += OnRecordingPcm;
        _wakeWorker = Task.Run(RunWakeWorkerAsync);
        _speechWorker = Task.Run(RunSpeechWorkerAsync);
        _endpointWorker = Task.Run(RunEndpointWorkerAsync);
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
        await ApplySettingsAsync(settings, settings.UseLocalTranscription || hasApiKey, cancellationToken);
    }

    public async Task ApplySettingsAsync(AppSettings settings, bool hasApiKey, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var configuration = $"{settings.ActivationMode}|{settings.AutoCaptureListeningEnabled}|{settings.AutoCaptureWakeVoiceEnabled}|{settings.AutoCaptureWakePhrase}|{settings.AutoCaptureWakeVoiceSensitivity:R}|{settings.MicrophoneDeviceId}|{hasApiKey}";
            if (configuration != _appliedWakeConfiguration) { _appliedWakeConfiguration = configuration; Interlocked.Increment(ref _wakeGeneration); }
            if (_wakeDetector is IWakeEnrollmentDetector enrolled) enrolled.ConfigurePersonalization(settings.AutoCaptureWakeVoiceProfile, settings.MicrophoneDeviceId);
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
        // Preparation owns the shared level monitor until it releases its reservation.
        if (Suspended) return;
        if (settings.ActivationMode == ActivationMode.AutoCapture && settings.AutoCaptureListeningEnabled && hasApiKey && _pcmSource is not null && !_speechReady)
        {
            try { await _speechDetector.PrepareAsync(new Progress<string>(ReportWake), cancellationToken); _speechReady = true; }
            catch (Exception ex) when (ex is not OperationCanceledException) { ReportWake("Speech detection unavailable; using the bounded level fallback. " + ex.Message); }
        }
        if (_audioCapture.IsRecording || !ShouldMonitorRun(settings, hasApiKey))
        {
            _monitor.Stop();
            return;
        }

        if (settings.AutoCaptureWakeVoiceEnabled && settings.ActivationMode == ActivationMode.AutoCapture && settings.AutoCaptureListeningEnabled && hasApiKey)
        {
            try
            {
                WakePhraseCalibration.Normalize(settings.AutoCaptureWakePhrase);
                using var preparation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _wakeShutdown.Token);
                _wakePreparation = preparation;
                try { await _wakeDetector.PrepareAsync(new Progress<string>(ReportWake), preparation.Token); }
                finally { _wakePreparation = null; }
                if (Suspended || !ShouldMonitorRun(settings, hasApiKey)) { _monitor.Stop(); return; }
                _wakeReady = true; ReportWake("Ready · waiting for “" + settings.AutoCaptureWakePhrase + "”. Wait for the tone before dictating.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { _wakeReady = false; _monitor.Stop(); return; }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _wakeReady = false; _monitor.Stop(); ReportWake("Wake detector unavailable: " + ex.Message); return;
            }
        }
        else if (!settings.AutoCaptureWakeVoiceEnabled || settings.ActivationMode != ActivationMode.AutoCapture || !settings.AutoCaptureListeningEnabled || !hasApiKey) { _wakeReady = false; ReportWake("Wake phrase detection is off."); }
        if (!_monitor.IsRunning || !string.Equals(_monitorDeviceId, settings.MicrophoneDeviceId, StringComparison.Ordinal))
        {
            _monitor.Stop();
            _monitorDeviceId = settings.MicrophoneDeviceId;
            await _monitor.StartAsync(settings.MicrophoneDeviceId, cancellationToken);
        }
    }

    public async Task StopWakeWorkerAsync() { _wakeShutdown.Cancel(); _wakeQueue.Writer.TryComplete(); _speechQueue.Writer.TryComplete(); await Task.WhenAll(_wakeWorker, _speechWorker, _endpointWorker); }

    public void Dispose()
    {
        _monitor.LevelAvailable -= OnMonitorLevel;
        _monitor.AudioAvailable -= OnMonitorAudio;
        _audioCapture.LevelAvailable -= OnRecordingLevel;
        _orchestrator.ListeningStarting -= OnListeningStarting;
        _orchestrator.CaptureStopped -= OnCaptureStopped;
        _orchestrator.StateChanged -= OnOrchestratorStateChanged;
        if (_pcmSource is not null) _pcmSource.PcmAvailable -= OnRecordingPcm;
        _endpoint.End(); _wakeShutdown.Cancel(); _wakeQueue.Writer.TryComplete(); _speechQueue.Writer.TryComplete();
        if (_ownsSpeechDetector) _ = _speechWorker.ContinueWith(_ => _speechDetector.Dispose());
        if (_ownsWakeDetector) _ = _wakeWorker.ContinueWith(_ => _wakeDetector.Dispose());
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
                settings.AutoCaptureListeningEnabled && !_audioCapture.IsRecording && (!settings.AutoCaptureWakeVoiceEnabled || DateTimeOffset.UtcNow <= _wakeVoiceAcceptedUntil))
            {
                foreach (var sample in samples) _preRoll.Enqueue(sample);
                while (_preRoll.Count > 8000) _preRoll.Dequeue();
            }
            else _preRoll.Clear();
        }
        if (Suspended || settings.ActivationMode is not ActivationMode.AutoCapture ||
            !settings.AutoCaptureListeningEnabled ||
            !settings.AutoCaptureWakeVoiceEnabled ||
            !_wakeReady ||
            !_hasApiKey ||
            _audioCapture.IsRecording ||
            _isStarting ||
            _isStopping ||
            DateTimeOffset.UtcNow < _cooldownUntil)
        {
            ClearWakeVoiceBuffer();
            return;
        }

        if (!_wakeQueue.Writer.TryWrite((samples.ToArray(), Volatile.Read(ref _wakeGeneration))))
            Interlocked.Increment(ref _wakeGeneration); // discontinuity: stale partial phrases cannot trigger
    }

    private async Task RunWakeWorkerAsync()
    {
        var generation = -1; long samplesSinceReset = 0; long silent = 0;
        try
        {
            await foreach (var entry in _wakeQueue.Reader.ReadAllAsync(_wakeShutdown.Token))
            {
                var settings = GetSettingsSnapshot();
                if (entry.Generation != Volatile.Read(ref _wakeGeneration) || Suspended || !_wakeReady || !settings.AutoCaptureWakeVoiceEnabled ||
                    settings.ActivationMode != ActivationMode.AutoCapture || !settings.AutoCaptureListeningEnabled || _audioCapture.IsRecording || _isStarting || _isStopping)
                    continue;
                if (generation != entry.Generation) { _wakeDetector.Reset(); generation = entry.Generation; samplesSinceReset = 0; silent = 0; }
                try
                {
                    var found = await _wakeDetector.ProcessAsync(entry.Samples, settings.AutoCaptureWakePhrase, settings.AutoCaptureWakeVoiceSensitivity, _wakeShutdown.Token);
                    samplesSinceReset += entry.Samples.Length;
                    silent = Peak(entry.Samples) < .008f ? silent + entry.Samples.Length : 0;
                    if (samplesSinceReset > 16000 * 20 && silent > 16000) { _wakeDetector.Reset(); samplesSinceReset = 0; }
                    else if (samplesSinceReset > 16000 * 120) { _wakeDetector.Reset(); samplesSinceReset = 0; }
                    if (found && generation == Volatile.Read(ref _wakeGeneration) && !Suspended && !_audioCapture.IsRecording)
                    {
                        Interlocked.Increment(ref _wakeGeneration);
                        lock (_preRoll) _preRoll.Clear();
                        _wakeVoiceAcceptedUntil = DateTimeOffset.UtcNow.AddMilliseconds(900);
                        ReportWake("Wake phrase accepted. Dictation starts after the tone.");
                        RunOnAppDispatcher(StartCaptureAsync);
                    }
                }
                catch (OperationCanceledException) when (_wakeShutdown.IsCancellationRequested) { break; }
                catch (Exception ex) { _wakeReady = false; ReportWake("Wake detection paused: " + ex.Message); }
            }
        }
        catch (OperationCanceledException) when (_wakeShutdown.IsCancellationRequested) { }
    }

    private void OnRecordingLevel(object? sender, float level)
    {
        if (Suspended || !_automaticSession || !_audioCapture.IsRecording) return;
        // A peak is not speech: PCM-capable capture uses the neural detector instead.
        if ((!_speechReady || _pcmSource is null) && SpeechEndpoint.FallbackSpeech(level, GetSettingsSnapshot().AutoCaptureThreshold))
            _endpoint.Speech(Volatile.Read(ref _sessionGeneration), _clock.Elapsed);
    }
    private void OnRecordingPcm(object? sender, ReadOnlyMemory<byte> pcm)
    {
        if (Suspended || !_automaticSession || !_speechReady || !_audioCapture.IsRecording || pcm.Length < 2) return;
        var samples = new float[pcm.Length / 2];
        for (var i = 0; i < samples.Length; i++) samples[i] = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(pcm.Span.Slice(i * 2, 2)) / 32768f;
        _speechQueue.Writer.TryWrite((samples, Volatile.Read(ref _sessionGeneration), _clock.Elapsed));
    }
    private async Task RunSpeechWorkerAsync()
    {
        long generation = -1;
        try
        {
            await foreach (var entry in _speechQueue.Reader.ReadAllAsync(_wakeShutdown.Token))
            {
                if (entry.Generation != Volatile.Read(ref _sessionGeneration) || !_automaticSession || Suspended) continue;
                if (generation != entry.Generation) { _speechDetector.Reset(); generation = entry.Generation; }
                try
                {
                    if (await _speechDetector.ProcessAsync(entry.Samples, _wakeShutdown.Token)) _endpoint.Speech(entry.Generation, entry.At);
                }
                catch (OperationCanceledException) when (_wakeShutdown.IsCancellationRequested) { break; }
                catch (Exception ex) { _speechReady = false; ReportWake("Speech detection fell back to audio levels: " + ex.Message); }
            }
        }
        catch (OperationCanceledException) when (_wakeShutdown.IsCancellationRequested) { }
    }
    private async Task RunEndpointWorkerAsync()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        try
        {
            while (await timer.WaitForNextTickAsync(_wakeShutdown.Token))
            {
                var settings = GetSettingsSnapshot();
                if (!Suspended && _automaticSession && _audioCapture.IsRecording && !_isStarting && !_isStopping &&
                    _endpoint.ShouldStop(_clock.Elapsed, settings.AutoCaptureSilenceMs, settings.AutoCaptureMinSpeechMs))
                {
                    var generation = Volatile.Read(ref _sessionGeneration);
                    RunOnAppDispatcher(() => StopCaptureAsync(generation));
                }
            }
        }
        catch (OperationCanceledException) when (_wakeShutdown.IsCancellationRequested) { }
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
            if (!settings.UseLocalTranscription && string.IsNullOrWhiteSpace(apiKey))
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
            await _orchestrator.StartListeningAsync(preRoll: preRoll);
            if (settings.AutoCaptureWakeVoiceEnabled && _audioCapture.IsRecording) _wakeTone.Play(settings);
        }
        finally
        {
            _isStarting = false;
            _gate.Release();
        }
    }

    private async Task StopCaptureAsync(long generation)
    {
        bool streaming;
        await _gate.WaitAsync();
        try
        {
            if (Suspended || !_audioCapture.IsRecording || _isStopping || Volatile.Read(ref _sessionGeneration) != generation)
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
        if (Suspended) return;
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
        Interlocked.Increment(ref _wakeGeneration);
        _monitor.Stop();
        _automaticSession = GetSettingsSnapshot().ActivationMode == ActivationMode.AutoCapture;
        Volatile.Write(ref _sessionGeneration, _endpoint.Begin(_clock.Elapsed));
        lock (_preRoll) _preRoll.Clear();
    }

    private void OnCaptureStopped(object? sender, EventArgs e)
    {
        _isStopping = false; _automaticSession = false; _endpoint.End();
        Interlocked.Increment(ref _wakeGeneration);
        _wakeVoiceAcceptedUntil = DateTimeOffset.MinValue;
        var settings = GetSettingsSnapshot();
        if (settings.AutoCaptureWakeVoiceEnabled && ShouldMonitorRun(settings)) ReportWake("Ready · waiting for “" + settings.AutoCaptureWakePhrase + "”.");
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

        if (!settings.AutoCaptureWakeVoiceEnabled)
        {
            ClearWakeVoiceBuffer();
            _wakeVoiceAcceptedUntil = DateTimeOffset.MinValue;
        }
    }

    private void ClearWakeVoiceBuffer() { Interlocked.Increment(ref _wakeGeneration); }

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
        UseLocalTranscription = settings.UseLocalTranscription,
        LocalModelId = settings.LocalModelId,
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
        AutoCaptureWakePhrase = settings.AutoCaptureWakePhrase,
        AutoCaptureWakeVoiceEnabled = settings.AutoCaptureWakeVoiceEnabled,
        AutoCaptureWakeVoiceSensitivity = settings.AutoCaptureWakeVoiceSensitivity,
        AutoCaptureWakeVoiceProfile = settings.AutoCaptureWakeVoiceProfile,
        WakeToneEnabled = settings.WakeToneEnabled,
        WakeTonePath = settings.WakeTonePath
    };
}
