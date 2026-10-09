using System.Diagnostics;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.App.Services;

public sealed class DictationOrchestrator
{
    private readonly ISettingsService _settingsService;
    private readonly ISecretStore _secretStore;
    private readonly IAudioCaptureService _audioCapture;
    private readonly IGroqTranscriptionClient _groqClient;
    private readonly TextCleanupService _textCleanup;
    private readonly ITextInsertionService _textInsertion;
    private readonly IKeyboardCommandService _keyboardCommand;
    private readonly OverlayStatusService _overlay;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _reservedForNotes;
    private StreamRun? _stream;
    private Task _streamQueue = Task.CompletedTask;
    private int _pendingStreams;
    private int _finalizingStreams;
    private int _draining;

    private sealed class StreamRun(AppSettings settings, string key, Task previous)
    {
        public AppSettings Settings { get; } = settings;
        public string Key { get; } = key;
        public Task Previous { get; } = previous;
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenSource Cancellation { get; } = new();
        public StreamingDictationSession Session { get; set; } = null!;
        public Task Worker { get; set; } = Task.CompletedTask;
        public bool Begun, ClipboardOnly, Busy, Finalizing;
        public string? Notice;
    }

    public async Task ReserveForNotesAsync(bool reserved)
    {
        await _gate.WaitAsync();
        try
        {
            if (reserved && (_audioCapture.IsRecording || _pendingStreams > 0))
                throw new InvalidOperationException("Finish the current dictation or scratchpad recording before starting notes.");
            _reservedForNotes = reserved;
        }
        finally { _gate.Release(); }
    }

    public DictationState State { get; private set; } = DictationState.Idle;
    public bool IsStreaming => _stream is not null;
    public bool HasPendingStreamOutput => _pendingStreams > 0;
    public event EventHandler? ListeningStarting;
    public event EventHandler? CaptureStopped;
    public event EventHandler<DictationState>? StateChanged;

    public DictationOrchestrator(
        ISettingsService settingsService,
        ISecretStore secretStore,
        IAudioCaptureService audioCapture,
        IGroqTranscriptionClient groqClient,
        TextCleanupService textCleanup,
        ITextInsertionService textInsertion,
        IKeyboardCommandService keyboardCommand,
        OverlayStatusService overlay)
    {
        _settingsService = settingsService;
        _secretStore = secretStore;
        _audioCapture = audioCapture;
        _groqClient = groqClient;
        _textCleanup = textCleanup;
        _textInsertion = textInsertion;
        _keyboardCommand = keyboardCommand;
        _overlay = overlay;
    }

    public async Task ToggleAsync(CancellationToken cancellationToken = default)
    {
        Task finish = Task.CompletedTask;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_reservedForNotes) return;
            if (_audioCapture.IsRecording) finish = await StopUnderGateAsync(cancellationToken);
            else await StartAsync(cancellationToken);
        }
        finally { _gate.Release(); }
        await finish;
    }

    public async Task StartListeningAsync(CancellationToken cancellationToken = default, ReadOnlyMemory<byte> preRoll = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { if (!_audioCapture.IsRecording) await StartAsync(cancellationToken, preRoll); }
        finally { _gate.Release(); }
    }

    public async Task StopListeningAndTranscribeAsync(CancellationToken cancellationToken = default)
    {
        Task finish = Task.CompletedTask;
        await _gate.WaitAsync(cancellationToken);
        try { if (!_reservedForNotes && _audioCapture.IsRecording) finish = await StopUnderGateAsync(cancellationToken); }
        finally { _gate.Release(); }
        await finish;
    }

    public async Task StopIfNeededAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _draining);
        try
        {
            await StopListeningAndTranscribeAsync(cancellationToken);
            await _streamQueue.WaitAsync(cancellationToken);
        }
        finally { Interlocked.Decrement(ref _draining); }
    }

    private async Task<Task> StopUnderGateAsync(CancellationToken token)
    {
        if (_stream is not { } run)
        {
            await _streamQueue.WaitAsync(token);
            await StopTranscribeAndInsertAsync(token);
            return Task.CompletedTask;
        }
        run.Finalizing = true;
        _finalizingStreams++;
        RefreshStreamStatus();
        run.Cancellation.Cancel();
        // Capture ends immediately, independent of provider or insertion latency.
        // Draining completes before detaching the last PCM callback.
        string? path = null;
        Exception? captureError = null;
        try { path = (await _audioCapture.StopRecordingAsync(CancellationToken.None)).FilePath; }
        catch (Exception ex) { captureError = ex; }
        _overlay.SetCaptureActive(false);
        ((IStreamingAudioCaptureService)_audioCapture).PcmAvailable -= run.Session.AddAudio;
        _stream = null;
        CaptureStopped?.Invoke(this, EventArgs.Empty);
        RefreshStreamStatus();
        return FinishStreamAsync(run, path, captureError, token);
    }

    private async Task StartAsync(CancellationToken cancellationToken, ReadOnlyMemory<byte> preRoll = default)
    {
        if (_reservedForNotes || _draining > 0) return;
        var settings = await _settingsService.LoadAsync(cancellationToken);
        var apiKey = await _secretStore.GetApiKeyAsync(cancellationToken) ?? "";
        if (!settings.UseLocalTranscription && string.IsNullOrWhiteSpace(apiKey))
        {
            SetState(DictationState.Error, "Add your Groq API key before listening.");
            return;
        }

        ListeningStarting?.Invoke(this, EventArgs.Empty);
        _overlay.SetOpacity(settings.OverlayOpacity);
        try
        {
            if (settings.StreamModeEnabled)
            {
                if (_audioCapture is not IStreamingAudioCaptureService source || _textInsertion is not IStreamingTextInsertionService insertion)
                    throw new InvalidOperationException("This capture device does not support Stream Mode.");
                var run = new StreamRun(settings, apiKey, _streamQueue);
                run.Session = new StreamingDictationSession(_groqClient, apiKey,
                    new TranscriptionOptions(settings.ModelId, settings.Language, settings.CustomPrompt, UseLocal: settings.UseLocalTranscription, LocalModelId: settings.LocalModelId), async (delta, cumulative, token) =>
                    {
                        if (!await insertion.AppendStreamAsync(delta, cumulative, token)) run.ClipboardOnly = true;
                    });
                _stream = run;
                _streamQueue = run.Completed.Task;
                _pendingStreams++;
                source.PcmAvailable += run.Session.AddAudio;
            }
            if (_audioCapture is IPreRollAudioCaptureService bufferedCapture) bufferedCapture.SetPreRoll(preRoll);
            await _audioCapture.StartRecordingAsync(settings.MicrophoneDeviceId, cancellationToken);
            _overlay.SetCaptureActive(true, settings.ActivationMode == ActivationMode.AutoCapture ? Math.Max(.012f, settings.AutoCaptureThreshold * .65f) : .012f);
            SetState(DictationState.Listening, _stream is null ? "Listening" : "Streaming");
            if (_stream is { } activeRun) activeRun.Worker = RunStreamAsync(activeRun);
        }
        catch
        {
            if (_stream is { } run)
            {
                ((IStreamingAudioCaptureService)_audioCapture).PcmAvailable -= run.Session.AddAudio;
                run.Session.Dispose(); run.Cancellation.Dispose();
                _stream = null;
                _pendingStreams--;
                // A failed queued capture must not release the next session before
                // its predecessor has finished using the clipboard/insertion service.
                _ = CompleteAfterPreviousAsync(run);
            }
            throw;
        }
    }

    private static async Task CompleteAfterPreviousAsync(StreamRun run)
    {
        await run.Previous;
        run.Completed.TrySetResult();
    }

    private async Task BeginOutputAsync(StreamRun run, CancellationToken token)
    {
        await run.Previous.WaitAsync(token);
        if (run.Begun) return;
        ((IStreamingTextInsertionService)_textInsertion).BeginStream(run.Settings.InsertionMode);
        run.Begun = true;
    }

    private async Task RunStreamAsync(StreamRun run)
    {
        var token = run.Cancellation.Token;
        try
        {
            await BeginOutputAsync(run, token);
            var delay = run.Session.IsContinuousLocal ? TimeSpan.FromMilliseconds(100) : StreamingCadence.Interval;
            while (true)
            {
                await Task.Delay(delay, token);
                var started = Stopwatch.GetTimestamp();
                run.Busy = true;
                RefreshStreamStatus();
                try { await run.Session.ProcessAsync(stopped: false, token); }
                finally { run.Busy = false; RefreshStreamStatus(); }
                delay = run.Session.IsContinuousLocal ? TimeSpan.FromMilliseconds(100) : StreamingCadence.After(Stopwatch.GetElapsedTime(started));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception)
        {
            run.Notice = "Live updates paused · audio retained";
            RefreshStreamStatus();
        }
    }

    private async Task FinishStreamAsync(StreamRun run, string? path, Exception? captureError, CancellationToken token)
    {
        string? notice = null;
        try
        {
            await run.Worker;
            await BeginOutputAsync(run, token);
            if (captureError is not null) throw captureError;
            await run.Session.ProcessAsync(stopped: true, token);
            var finalText = run.Session.Text;
            if (!run.Settings.UseLocalTranscription && run.Settings.StreamFixEnabled && !string.IsNullOrWhiteSpace(finalText))
            {
                try
                {
                    _overlay.Show("Transcribing · checking text", DictationState.Transcribing);
                    if (_groqClient is not IStreamProofreader proofreader)
                        throw new InvalidOperationException("Stream Fix is unavailable.");
                    var proofread = await proofreader.ProofreadStreamAsync(run.Key, finalText, run.Settings.WritingCleanupModelId, token);
                    finalText = proofread.Text;
                    if (proofread.RejectedEdits) notice = "Finished · uncertain edits skipped";
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { notice = "Stream Fix unavailable · transcript retained"; }
            }
            if (!string.IsNullOrEmpty(run.Session.Text))
            {
                _overlay.Show("Transcribing · delivering text", DictationState.Transcribing);
                // Clipboard fallback is a normal backup, not a review or approval step.
                await ((IStreamingTextInsertionService)_textInsertion).CompleteStreamAsync(finalText, !run.Settings.UseLocalTranscription && run.Settings.StreamFixEnabled, token);
            }
        }
        catch (OperationCanceledException) { notice = "Cancelled · completed words on clipboard"; }
        catch (Exception ex) { notice = $"Stream incomplete: {ex.Message}"; }
        finally
        {
            // Delivery is finished. UIA unsubscription and file deletion can block;
            // neither should leave a completed dictation looking like a request.
            _pendingStreams--; _finalizingStreams--;
            if (_pendingStreams > 0) RefreshStreamStatus();
            else if (notice is not null) SetState(DictationState.Error, notice);
            else CompleteAndHide();
            try
            {
                if (run.Begun) ((IStreamingTextInsertionService)_textInsertion).EndStream();
                if (path is not null && !run.Settings.RetainAudioForDebugging)
                    await _audioCapture.DeleteRecordingAsync(path, CancellationToken.None);
            }
            catch
            {
                if (_pendingStreams == 0) SetState(DictationState.Error, "Finished · temporary audio cleanup failed");
            }
            finally
            {
                try { run.Session.Dispose(); }
                catch
                {
                    if (_pendingStreams == 0) SetState(DictationState.Error, "Finished · temporary stream cleanup failed");
                }
                finally { run.Cancellation.Dispose(); run.Completed.TrySetResult(); }
            }
        }
    }

    private void RefreshStreamStatus()
    {
        if (_pendingStreams == 0) return;
        var state = _stream is null ? DictationState.Transcribing : DictationState.Listening;
        if (State != state) { State = state; StateChanged?.Invoke(this, state); }
        if (_finalizingStreams > 0 || _stream?.Busy == true)
            _overlay.Show(_stream is null ? "Transcribing" : "Transcribing · microphone active", DictationState.Transcribing);
        else
            _overlay.Show(_stream?.Notice ?? (_stream?.ClipboardOnly == true ? "Streaming to clipboard" : "Streaming"), DictationState.Listening);
    }

    private async Task StopTranscribeAndInsertAsync(CancellationToken cancellationToken)
    {
        string? recordingPath = null;
        try
        {
            SetState(DictationState.Transcribing, "Transcribing");
            var recording = await _audioCapture.StopRecordingAsync(cancellationToken);
            _overlay.SetCaptureActive(false);
            recordingPath = recording.FilePath;
            CaptureStopped?.Invoke(this, EventArgs.Empty);
            if (recording.LooksSilent)
            {
                throw new InvalidOperationException("No clear microphone audio was detected. Check the selected microphone and Windows input level.");
            }

            var settings = await _settingsService.LoadAsync(cancellationToken);
            var apiKey = await _secretStore.GetApiKeyAsync(cancellationToken) ?? "";
            if (!settings.UseLocalTranscription && string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Add your Groq API key in Settings before dictating.");
            }

            var transcript = await _groqClient.TranscribeAsync(
                apiKey,
                recording.FilePath,
                new TranscriptionOptions(settings.ModelId, settings.Language, settings.CustomPrompt, UseLocal: settings.UseLocalTranscription, LocalModelId: settings.LocalModelId),
                cancellationToken);

            var cleaned = settings.CleanupEnabled
                ? _textCleanup.Clean(transcript, settings.SpokenPunctuationEnabled)
                : transcript.Trim();

            if (!TextCleanupService.HasTranscribableText(cleaned))
            {
                CompleteAndHide();
                return;
            }

            if (settings.IntentActionsEnabled && SpokenKeyCommandParser.TryParse(cleaned, out var keyCommand))
            {
                await _keyboardCommand.SendAsync(keyCommand, cancellationToken);
                CompleteAndHide();
                return;
            }

            if (!settings.UseLocalTranscription && settings.IntentActionsEnabled)
            {
                SetState(DictationState.Transcribing, "Interpreting");
                var intent = await _groqClient.InterpretIntentAsync(
                    apiKey,
                    cleaned,
                    settings.IntentModelId,
                    cancellationToken);

                if (intent.Confidence >= settings.IntentConfidenceThreshold)
                {
                    if (intent.Kind is IntentDecisionKind.Ignore)
                    {
                        CompleteAndHide();
                        return;
                    }

                    if (intent.Kind is IntentDecisionKind.Action && intent.Action is not null)
                    {
                        await _keyboardCommand.SendAsync(ToKeyCommand(intent.Action.Value), cancellationToken);
                        CompleteAndHide();
                        return;
                    }

                    if (intent.Kind is IntentDecisionKind.InsertText && !string.IsNullOrWhiteSpace(intent.Text))
                    {
                        cleaned = intent.Text.Trim();
                    }
                }
            }

            if (!settings.UseLocalTranscription && settings.GroqWritingCleanupEnabled && !string.IsNullOrWhiteSpace(cleaned))
            {
                SetState(DictationState.Transcribing, "Polishing");
                cleaned = await _groqClient.CleanupTranscriptAsync(
                    apiKey,
                    cleaned,
                    settings.WritingCleanupModelId,
                    settings.WritingCleanupTone,
                    settings.GetWritingCleanupPrompt(),
                    cancellationToken);
            }

            if (!TextCleanupService.HasTranscribableText(cleaned))
            {
                CompleteAndHide();
                return;
            }

            var inserted = await _textInsertion.InsertAsync(
                cleaned,
                settings.InsertionMode,
                copyToClipboardOnSuccess: settings.CopyInsertedTextToClipboard,
                copyToClipboardOnFailure: settings.IntentActionsEnabled,
                cancellationToken);
            if (inserted)
            {
                CompleteAndHide();
            }
            else if (!settings.UseLocalTranscription && settings.IntentActionsEnabled)
            {
                SetState(DictationState.Error, "Copied to clipboard");
            }
            else
            {
                SetState(DictationState.Error, "Could not insert text");
            }

            if (!settings.RetainAudioForDebugging)
            {
                await _audioCapture.DeleteRecordingAsync(recording.FilePath, cancellationToken);
                recordingPath = null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SetState(DictationState.Error, ex.Message);
        }
        finally
        {
            if (recordingPath is not null)
            {
                try
                {
                    var settings = await _settingsService.LoadAsync(CancellationToken.None);
                    if (!settings.RetainAudioForDebugging)
                    {
                        await _audioCapture.DeleteRecordingAsync(recordingPath, CancellationToken.None);
                    }
                }
                catch
                {
                }
            }
        }
    }

    private void SetState(DictationState state, string message)
    {
        State = state;
        StateChanged?.Invoke(this, state);
        _overlay.Show(message, state);
    }

    private void CompleteAndHide()
    {
        State = DictationState.Inserted;
        StateChanged?.Invoke(this, State);
        _overlay.Hide();
    }

    private static KeyPressCommand ToKeyCommand(VoxAction action) => action switch
    {
        VoxAction.Copy => new KeyPressCommand("C", ["Ctrl"]),
        VoxAction.Paste => new KeyPressCommand("V", ["Ctrl"]),
        VoxAction.Cut => new KeyPressCommand("X", ["Ctrl"]),
        VoxAction.SelectAll => new KeyPressCommand("A", ["Ctrl"]),
        VoxAction.Undo => new KeyPressCommand("Z", ["Ctrl"]),
        VoxAction.Redo => new KeyPressCommand("Y", ["Ctrl"]),
        VoxAction.Enter => new KeyPressCommand("Enter", []),
        VoxAction.Tab => new KeyPressCommand("Tab", []),
        VoxAction.Escape => new KeyPressCommand("Escape", []),
        VoxAction.Backspace => new KeyPressCommand("Backspace", []),
        VoxAction.Delete => new KeyPressCommand("Delete", []),
        VoxAction.TaskView => new KeyPressCommand("Tab", ["Win"]),
        _ => new KeyPressCommand(string.Empty, [])
    };

}
