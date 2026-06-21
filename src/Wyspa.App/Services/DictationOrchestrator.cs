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

    public DictationState State { get; private set; } = DictationState.Idle;
    public event EventHandler? ListeningStarting;
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
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_audioCapture.IsRecording)
            {
                await StopTranscribeAndInsertAsync(cancellationToken);
            }
            else
            {
                await StartAsync(cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StartListeningAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!_audioCapture.IsRecording)
            {
                await StartAsync(cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopListeningAndTranscribeAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_audioCapture.IsRecording)
            {
                await StopTranscribeAndInsertAsync(cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopIfNeededAsync(CancellationToken cancellationToken = default)
    {
        if (_audioCapture.IsRecording)
        {
            await ToggleAsync(cancellationToken);
        }
    }

    private async Task StartAsync(CancellationToken cancellationToken)
    {
        var settings = await _settingsService.LoadAsync(cancellationToken);
        var apiKey = await _secretStore.GetApiKeyAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            SetState(DictationState.Error, "Add your Groq API key before listening.");
            return;
        }

        ListeningStarting?.Invoke(this, EventArgs.Empty);
        _overlay.SetOpacity(settings.OverlayOpacity);
        await _audioCapture.StartRecordingAsync(settings.MicrophoneDeviceId, cancellationToken);
        SetState(DictationState.Listening, "Listening");
    }

    private async Task StopTranscribeAndInsertAsync(CancellationToken cancellationToken)
    {
        string? recordingPath = null;
        try
        {
            SetState(DictationState.Transcribing, "Transcribing");
            var recording = await _audioCapture.StopRecordingAsync(cancellationToken);
            recordingPath = recording.FilePath;
            if (recording.LooksSilent)
            {
                throw new InvalidOperationException("No clear microphone audio was detected. Check the selected microphone and Windows input level.");
            }

            var settings = await _settingsService.LoadAsync(cancellationToken);
            var apiKey = await _secretStore.GetApiKeyAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException("Add your Groq API key in Settings before dictating.");
            }

            var transcript = await _groqClient.TranscribeAsync(
                apiKey,
                recording.FilePath,
                new TranscriptionOptions(settings.ModelId, settings.Language, settings.CustomPrompt),
                cancellationToken);

            var cleaned = settings.CleanupEnabled
                ? _textCleanup.Clean(transcript, settings.SpokenPunctuationEnabled)
                : transcript.Trim();

            if (settings.IntentActionsEnabled && SpokenKeyCommandParser.TryParse(cleaned, out var keyCommand))
            {
                await _keyboardCommand.SendAsync(keyCommand, cancellationToken);
                CompleteAndHide();
                return;
            }

            if (settings.IntentActionsEnabled)
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

            if (settings.GroqWritingCleanupEnabled && !string.IsNullOrWhiteSpace(cleaned))
            {
                SetState(DictationState.Transcribing, "Polishing");
                cleaned = await _groqClient.CleanupTranscriptAsync(
                    apiKey,
                    cleaned,
                    settings.WritingCleanupModelId,
                    settings.WritingCleanupTone,
                    cancellationToken);
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
            else if (settings.IntentActionsEnabled)
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
