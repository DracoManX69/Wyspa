using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Channels;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.App.ViewModels;

public enum NoteLibrary { Conversations, YouTube }

public sealed class NoteBubble(NoteEntry entry, string? mySpeaker) : ViewModelBase
{
    public NoteEntry Entry { get; } = entry;
    public string Text => Entry.Text;
    public string Speaker => Entry.Speaker == "You" || Entry.Speaker == mySpeaker ? "You" : Entry.Speaker;
    public bool IsMine => Entry.Speaker == "You" || Entry.Speaker == mySpeaker;
    public bool IsError => Entry.IsError;
    public string Time => TimeSpan.FromSeconds(Entry.Start).ToString(@"hh\:mm\:ss");
}

public sealed class NotesViewModel : ViewModelBase
{
    private readonly IConversationCapture _capture;
    private readonly ISpeakerIdentifier _speakers;
    private readonly INoteIntelligence _groq;
    private readonly ISecretStore _secrets;
    private readonly INoteStore _store;
    private readonly IVideoImporter _video;
    private readonly Func<AppSettings> _settings;
    private readonly Func<Func<Task>, Task> _ui;
    private readonly Func<bool, Task> _reserveMicrophone;
    private readonly Func<Task> _saveSettings;
    private readonly NoteLibrary _library;
    private readonly Stopwatch _clock = new();
    private Channel<AudioChunk>? _queue;
    private Task[] _workers = [];
    private CancellationTokenSource? _sessionCancellation;
    private CancellationTokenSource? _importCancellation;
    private CancellationTokenSource? _summaryCancellation;
    private Task? _importTask;
    private Task? _summaryTask;
    private string _key = "";
    private TranscriptionOptions _options = new("whisper-large-v3-turbo", null, null);
    private ConversationMode _sessionMode;
    private NoteSession? _activeNote;
    private NoteSession? _selectedNote;
    private bool _active, _paused, _transition, _importing, _summarising;
    private bool _finishing;
    private Task? _finishingTask;
    private Task? _startingTask;
    private Task? _pauseTask;
    private bool _shuttingDown;
    private ConversationCaptureOptions? _captureOptions;
    private string _status = "Ready to start. Capture sources are configured in Settings. Conversations save automatically on this PC.";
    private string _error = "";
    private string _url = "";
    private string _videoStatus = "Paste a public, single YouTube video URL. No login needed.";
    private CaptureTarget? _output, _application;
    private int _pending;
    private bool _rebuilding;

    public NotesViewModel(IConversationCapture capture, ISpeakerIdentifier speakers, INoteIntelligence groq,
        ISecretStore secrets, INoteStore store, IVideoImporter video, Func<AppSettings> settings,
        Func<Func<Task>, Task> ui, Func<bool, Task> reserveMicrophone, Func<Task> saveSettings,
        NoteLibrary library = NoteLibrary.Conversations)
    {
        _capture = capture; _speakers = speakers; _groq = groq; _secrets = secrets; _store = store;
        _video = video; _settings = settings; _ui = ui; _reserveMicrophone = reserveMicrophone; _saveSettings = saveSettings;
        _library = library;
        StartCommand = new AsyncRelayCommand(StartAsync, () => CanConfigure && !IsVideoLibrary);
        PauseCommand = new AsyncRelayCommand(PauseResumeAsync, () => IsActive && !_transition);
        StopCommand = new AsyncRelayCommand(StopAsync, () => IsActive && !_transition);
        CancelPendingCommand = new AsyncRelayCommand(() => { _sessionCancellation?.Cancel(); return Task.CompletedTask; }, () => _finishing);
        SummariseCommand = new AsyncRelayCommand(() => _summaryTask = SummariseAsync(), () => SelectedNote?.Entries.Any(e => !e.IsError) == true && !_summarising && !_importing);
        CancelSummaryCommand = new AsyncRelayCommand(() => { _summaryCancellation?.Cancel(); return Task.CompletedTask; }, () => _summarising);
        ImportCommand = new AsyncRelayCommand(() => _importTask = ImportAsync(), () => IsVideoLibrary && CanConfigure && !string.IsNullOrWhiteSpace(Url));
        CancelImportCommand = new AsyncRelayCommand(() => { _importCancellation?.Cancel(); return Task.CompletedTask; }, () => _importing);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync, () => CanConfigure);
        SaveCommand = new AsyncRelayCommand(async () => { if (SelectedNote is { } note) await SaveAsync(note); });
        DeleteCommand = new AsyncRelayCommand(DeleteAsync, () => CanConfigure && SelectedNote is not null);
        _capture.ChunkAvailable += OnChunk;
        _capture.Failed += (_, error) => _ = _ui(async () =>
        {
            Error = error;
            if (IsActive && !IsPaused && !_transition) await PauseResumeAsync();
        });
    }

    public ObservableCollection<NoteSession> Notes { get; } = [];
    public ObservableCollection<NoteBubble> Bubbles { get; } = [];
    public ObservableCollection<CaptureTarget> Outputs { get; } = [];
    public ObservableCollection<CaptureTarget> Applications { get; } = [];
    public ObservableCollection<string> SpeakerChoices { get; } = ["Not chosen"];
    public IReadOnlyList<string> Modes { get; } = ["Computer call", "In-person · identify speakers"];
    public IReadOnlyList<string> AudioModes { get; } = ["Output device", "Selected app"];
    public AsyncRelayCommand StartCommand { get; }
    public AsyncRelayCommand PauseCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand CancelPendingCommand { get; }
    public AsyncRelayCommand SummariseCommand { get; }
    public AsyncRelayCommand CancelSummaryCommand { get; }
    public AsyncRelayCommand ImportCommand { get; }
    public AsyncRelayCommand CancelImportCommand { get; }
    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public bool IsActive { get => _active; private set { _active = value; Changed(); } }
    public bool IsPaused { get => _paused; private set { _paused = value; Changed(); } }
    public bool CanConfigure => !_shuttingDown && !_active && !_transition && !_importing && !_summarising;
    public bool CanBrowse => CanConfigure;
    public bool IsImporting => _importing;
    public bool IsSummarising => _summarising;
    public bool IsVideoLibrary => _library == NoteLibrary.YouTube;
    public string TranscriptEmptyText => IsVideoLibrary ? "Your video transcript will appear here."
        : "Your conversation will appear here.\nOther voices on the left · you on the right";
    public string CaptureDescription => IsCallMode
        ? $"Computer call · {(IsDeviceMode ? SelectedOutput?.Name ?? "Default output" : SelectedApplication?.Name ?? "Choose a calling app in Settings")}" : "In-person · speaker identification";
    public string PauseText => IsPaused ? "Resume" : "Pause";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Error { get => _error; private set => SetProperty(ref _error, value); }
    public string VideoStatus { get => _videoStatus; private set => SetProperty(ref _videoStatus, value); }
    public string Url { get => _url; set { SetProperty(ref _url, value); RefreshCommands(); } }
    public string FolderPath => _store.DirectoryPath;
    public AppSettings Options => _settings();
    public bool SupportsApplicationCapture => _capture.SupportsApplicationCapture;
    public bool IsCallMode => ModeIndex == 0;
    public bool IsRoomNote => SelectedNote?.Kind == "In-person";
    public bool IsDeviceMode => AudioModeIndex == 0;
    public int ModeIndex
    {
        get => (int)_settings().NoteMode;
        set { if (!CanConfigure || value < 0) return; _settings().NoteMode = (ConversationMode)value; OnPropertyChanged(); OnPropertyChanged(nameof(IsCallMode)); OnPropertyChanged(nameof(CaptureDescription)); }
    }
    public int AudioModeIndex
    {
        get => (int)_settings().NoteAudioMode;
        set { if (!CanConfigure || value < 0) return; _settings().NoteAudioMode = (CallAudioMode)value; OnPropertyChanged(); OnPropertyChanged(nameof(IsDeviceMode)); OnPropertyChanged(nameof(CaptureDescription)); }
    }
    public CaptureTarget? SelectedOutput
    {
        get => _output;
        set { if (CanConfigure && value is not null && SetProperty(ref _output, value)) { _settings().NoteOutputDeviceId = value.Id; OnPropertyChanged(nameof(CaptureDescription)); } }
    }
    public CaptureTarget? SelectedApplication { get => _application; set { if (CanConfigure) { SetProperty(ref _application, value); OnPropertyChanged(nameof(CaptureDescription)); } } }
    public string SummaryModel => _settings().SummaryModelId;
    public string Title
    {
        get => SelectedNote?.Title ?? "";
        set { if (SelectedNote is { } note) { note.Title = value; OnPropertyChanged(); } }
    }
    public string MySpeaker
    {
        get => SelectedNote?.MySpeaker ?? "Not chosen";
        set
        {
            if (_rebuilding || string.IsNullOrWhiteSpace(value) || value == MySpeaker) return;
            if (SelectedNote is not { } note) return;
            note.MySpeaker = value == "Not chosen" ? null : value;
            OnPropertyChanged(); RebuildBubbles(); _ = SaveAsync(note);
        }
    }
    public string Summary => SelectedNote?.Summary ?? "";
    public string SummaryStatus => SelectedNote is { Summary.Length: > 0 } note
        ? $"{note.SummaryModel} · covers {note.SummaryEntryCount} passages. {(note.Entries.Count != note.SummaryEntryCount ? "Transcript changed; summarise again to update." : "")}" : "";
    public NoteSession? SelectedNote
    {
        get => _selectedNote;
        set { if (!CanBrowse || (value is not null && !BelongsToLibrary(value))) return; Select(value); }
    }
    private bool BelongsToLibrary(NoteSession note) => string.Equals(note.Kind, "YouTube", StringComparison.OrdinalIgnoreCase) == IsVideoLibrary;
    private void Select(NoteSession? note)
    {
        _selectedNote = note; OnPropertyChanged(nameof(SelectedNote)); OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(MySpeaker)); OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(SummaryStatus)); OnPropertyChanged(nameof(IsRoomNote));
        RebuildBubbles(); RefreshCommands();
    }
    public async Task RefreshAsync()
    {
        if (!CanConfigure) return;
        try
        {
            var id = SelectedNote?.Id;
            Notes.Clear(); foreach (var note in (await _store.LoadAsync()).Where(BelongsToLibrary)) Notes.Add(note);
            Select(Notes.FirstOrDefault(n => n.Id == id) ?? Notes.FirstOrDefault());
            if (IsVideoLibrary) return;
            var outputId = _settings().NoteOutputDeviceId;
            Outputs.Clear(); foreach (var item in _capture.GetOutputs()) Outputs.Add(item);
            SelectedOutput = Outputs.FirstOrDefault(o => o.Id == outputId) ?? Outputs.FirstOrDefault();
            var applicationId = SelectedApplication?.Id;
            Applications.Clear(); foreach (var app in _capture.GetApplications()) Applications.Add(app);
            SelectedApplication = Applications.FirstOrDefault(a => a.Id == applicationId);
        }
        catch (Exception ex) { Error = Friendly(ex); }
    }

    public Task StartAsync()
    {
        if (_startingTask is { IsCompleted: false }) return _startingTask;
        return _startingTask = BeginAsync();
    }
    private async Task BeginAsync()
    {
        if (!CanConfigure || IsVideoLibrary) return;
        _transition = true; Changed(); Error = "";
        var reserved = false;
        try
        {
            _key = await RequireKeyAsync(CancellationToken.None);
            var settings = _settings();
            if (settings.NoteMode == ConversationMode.ComputerCall && settings.NoteAudioMode == CallAudioMode.Application &&
                (!SupportsApplicationCapture || SelectedApplication is null))
                throw new InvalidOperationException(SupportsApplicationCapture ? "Choose the calling app, or switch to Output device." : "App capture requires Windows build 20348 or newer. Choose Output device.");
            _options = new(settings.ModelId, settings.Language, settings.CustomPrompt);
            _sessionMode = settings.NoteMode;
            _captureOptions = CaptureOptions();
            if (_sessionMode == ConversationMode.InPerson)
            {
                Status = "Loading local speaker models…";
                await _speakers.InitializeAsync(CancellationToken.None); _speakers.Reset();
            }
            await _saveSettings();
            await _reserveMicrophone(true); reserved = true;
            _activeNote = new NoteSession { Title = "Conversation " + DateTime.Now.ToString("dd MMM HH:mm"), Kind = _sessionMode == ConversationMode.ComputerCall ? "Computer call" : "In-person", MySpeaker = _sessionMode == ConversationMode.ComputerCall ? "You" : null };
            await _store.SaveAsync(_activeNote);
            Notes.Insert(0, _activeNote); Select(_activeNote);
            _sessionCancellation = new CancellationTokenSource();
            _queue = Channel.CreateBounded<AudioChunk>(new BoundedChannelOptions(64) { SingleWriter = false, SingleReader = false, FullMode = BoundedChannelFullMode.Wait });
            _workers = [ProcessQueueAsync(_sessionCancellation.Token), ProcessQueueAsync(_sessionCancellation.Token)];
            IsActive = true; IsPaused = false; _clock.Restart();
            await _capture.StartAsync(_captureOptions, 0, _sessionCancellation.Token);
            Status = "Listening · live passages appear as Groq returns them.";
        }
        catch (Exception ex)
        {
            Error = Friendly(ex);
            try { await _capture.StopAsync(); } catch (Exception stopError) { Error += " " + Friendly(stopError); }
            _queue?.Writer.TryComplete();
            if (_workers.Length > 0) await Task.WhenAll(_workers);
            _sessionCancellation?.Dispose(); _sessionCancellation = null;
            IsActive = false; _clock.Stop();
            if (reserved) await _reserveMicrophone(false);
            Status = "Could not start. Check the message above and try again.";
        }
        finally { _transition = false; Changed(); }
    }
    private ConversationCaptureOptions CaptureOptions()
    {
        var settings = _settings();
        return new(_sessionMode, settings.MicrophoneDeviceId, settings.NoteAudioMode,
            SelectedOutput?.Id, int.TryParse(SelectedApplication?.Id, out var pid) ? pid : 0);
    }
    public Task PauseResumeAsync()
    {
        if (_pauseTask is { IsCompleted: false }) return _pauseTask;
        return _pauseTask = ChangePauseAsync();
    }
    private async Task ChangePauseAsync()
    {
        if (!IsActive || _transition) return;
        _transition = true; Changed();
        try
        {
            if (IsPaused)
            {
                await _capture.StartAsync(_captureOptions!, _clock.Elapsed.TotalSeconds, _sessionCancellation!.Token);
                IsPaused = false; Status = "Listening · notes save automatically.";
            }
            else
            {
                await _capture.StopAsync(); IsPaused = true;
                Status = "Paused · both audio sources are stopped. Captured speech is still being processed.";
            }
        }
        catch (Exception ex) { Error = Friendly(ex); IsPaused = true; }
        finally { _transition = false; Changed(); }
    }
    public Task StopAsync()
    {
        if (_finishingTask is { IsCompleted: false }) return _finishingTask;
        return _finishingTask = FinishAsync();
    }
    private async Task FinishAsync()
    {
        if (!IsActive || _transition) return;
        _transition = true; _finishing = true; Changed(); Status = "Finishing captured speech… Use Cancel pending to skip unfinished passages.";
        try
        {
            try { await _capture.StopAsync(); } catch (Exception ex) { Error = Friendly(ex); }
            _queue!.Writer.TryComplete();
            await Task.WhenAll(_workers);
            _speakers.Reset();
            if (_activeNote is not null) await SaveAsync(_activeNote);
            Status = "Stopped · transcript saved on this PC. You can summarise, copy, or reopen it later.";
        }
        catch (Exception ex) { Error = Friendly(ex); }
        finally
        {
            _queue?.Writer.TryComplete(); _clock.Stop(); IsActive = false; IsPaused = false;
            _sessionCancellation?.Dispose(); _sessionCancellation = null;
            try { await _reserveMicrophone(false); } catch (Exception ex) { Error = Friendly(ex); }
            _transition = false; _finishing = false; Changed();
        }
    }
    private void OnChunk(object? sender, AudioChunk chunk)
    {
        if (_queue?.Writer.TryWrite(chunk) == true) { Interlocked.Increment(ref _pending); return; }
        _ = _ui(async () =>
        {
            Error = "Transcription could not keep up. Capture has been paused; a missing passage is marked below.";
            if (_activeNote is not null)
            {
                _activeNote.Entries.Add(new(Guid.NewGuid(), chunk.Sequence, chunk.Start, chunk.Start + chunk.Duration, chunk.Source, "[Audio could not be queued for transcription]", true));
                RebuildBubbles(); await SaveAsync(_activeNote);
            }
            if (IsActive && !IsPaused && !_transition) await PauseResumeAsync();
        });
    }
    private async Task ProcessQueueAsync(CancellationToken token)
    {
        await foreach (var chunk in _queue!.Reader.ReadAllAsync())
        {
            var path = Path.Combine(Path.GetTempPath(), "Wyspa", "note-" + Guid.NewGuid().ToString("N") + ".wav");
            Task<IReadOnlyList<SpeakerTurn>>? identification = null;
            try
            {
                token.ThrowIfCancellationRequested();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                PcmWave.Write(path, chunk.Samples);
                if (_sessionMode == ConversationMode.InPerson) identification = _speakers.IdentifyAsync(chunk.Samples, token);
                var speech = await _groq.TranscribeAsync(_key, path, _options, token);
                await PublishAsync(chunk, TranscriptAssembler.Assemble(chunk, speech));
                if (identification is not null)
                {
                    IReadOnlyList<SpeakerTurn> turns;
                    try { turns = await identification; }
                    catch (Exception ex)
                    {
                        turns = [];
                        await _ui(() => { Error = "Speaker detection failed; text is retained. " + Friendly(ex); return Task.CompletedTask; });
                    }
                    await PublishAsync(chunk, TranscriptAssembler.Assemble(chunk, speech, turns));
                }
            }
            catch (Exception ex)
            {
                await PublishAsync(chunk, [new(Guid.NewGuid(), chunk.Sequence, chunk.Start, chunk.Start + chunk.Duration, chunk.Source,
                    token.IsCancellationRequested ? "[Pending transcription cancelled]" : "[Transcription failed: " + Friendly(ex) + "]", true)]);
                await _ui(() => { Error = "Some speech could not be transcribed; the gap is marked in the notes. " + Friendly(ex); return Task.CompletedTask; });
            }
            finally
            {
                if (identification is not null) try { await identification; } catch { /* Reported above or transcript already marked failed. */ }
                try { File.Delete(path); } catch (IOException) { }
                Interlocked.Decrement(ref _pending);
            }
        }
    }
    private Task PublishAsync(AudioChunk chunk, IReadOnlyList<NoteEntry> entries) => _ui(async () =>
    {
        if (_activeNote is not { } note) return;
        note.Entries.RemoveAll(e => e.Chunk == chunk.Sequence);
        foreach (var entry in entries)
        {
            // Only suppress a repeated boundary passage when its audio timestamps overlap.
            if (!note.Entries.Any(e => e.Speaker == entry.Speaker && Math.Abs(e.Start - entry.Start) < .35 && e.Text == entry.Text))
                note.Entries.Add(entry);
        }
        note.Entries.Sort((a, b) => a.Start != b.Start ? a.Start.CompareTo(b.Start) : a.Chunk.CompareTo(b.Chunk));
        RebuildBubbles(); OnPropertyChanged(nameof(SummaryStatus)); RefreshCommands(); await SaveAsync(note);
    });

    private void RebuildBubbles()
    {
        _rebuilding = true;
        try
        {
            // Preserve unaffected bubbles and scroll position when a late result is inserted earlier.
            var desired = SelectedNote?.Entries.OrderBy(e => e.Start).ThenBy(e => e.Chunk).Select(e => new NoteBubble(e, SelectedNote.MySpeaker)).ToArray() ?? [];
            for (var index = 0; index < desired.Length; index++)
            {
                if (index < Bubbles.Count && Bubbles[index].Entry == desired[index].Entry && Bubbles[index].IsMine == desired[index].IsMine) continue;
                var found = Bubbles.Skip(index).FirstOrDefault(b => b.Entry.Id == desired[index].Entry.Id);
                if (found is not null) Bubbles.Move(Bubbles.IndexOf(found), index);
                if (index < Bubbles.Count && Bubbles[index].Entry.Id == desired[index].Entry.Id) Bubbles[index] = desired[index];
                else Bubbles.Insert(index, desired[index]);
            }
            while (Bubbles.Count > desired.Length) Bubbles.RemoveAt(Bubbles.Count - 1);
            SpeakerChoices.Clear(); SpeakerChoices.Add("Not chosen");
            foreach (var speaker in desired.Select(e => e.Entry.Speaker).Where(s => s.StartsWith("Speaker ", StringComparison.Ordinal)).Distinct()) SpeakerChoices.Add(speaker);
            OnPropertyChanged(nameof(MySpeaker));
        }
        finally { _rebuilding = false; }
    }
    public async Task SaveSelectedAsync() { if (SelectedNote is { } note) await SaveAsync(note); }
    private async Task SaveAsync(NoteSession note)
    {
        try { await _store.SaveAsync(note); }
        catch (Exception ex) { Error = "Notes could not be saved. Keep Wyspa open, free disk space, and use Save again. " + Friendly(ex); }
    }
    private async Task SummariseAsync()
    {
        if (SelectedNote is not { } note || _summarising) return;
        _summarising = true; Changed(); Error = "";
        using var cancellation = new CancellationTokenSource(); _summaryCancellation = cancellation;
        var count = note.Entries.Count;
        var model = SummaryModel;
        try
        {
            await _saveSettings();
            var key = await RequireKeyAsync(cancellation.Token);
            var snapshot = new NoteSession { Title = note.Title, CreatedAt = note.CreatedAt, Kind = note.Kind, MySpeaker = note.MySpeaker, Entries = [.. note.Entries] };
            var result = await _groq.SummariseAsync(key, NoteStore.Export(snapshot), model, cancellation.Token);
            note.Summary = result; note.SummaryModel = model; note.SummaryEntryCount = count;
            OnPropertyChanged(nameof(Summary)); OnPropertyChanged(nameof(SummaryStatus)); await SaveAsync(note);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Error = "Summary cancelled. Your transcript is saved."; }
        catch (Exception ex) { Error = Friendly(ex); }
        finally { _summaryCancellation = null; _summarising = false; Changed(); }
    }
    public async Task ImportAsync()
    {
        if (!CanConfigure || !IsVideoLibrary) return;
        _importing = true; Changed(); Error = "";
        using var cancellation = new CancellationTokenSource(); _importCancellation = cancellation;
        NoteSession? note = null;
        try
        {
            var url = VideoImporter.NormalizeUrl(Url);
            var key = await RequireKeyAsync(cancellation.Token);
            var settings = _settings();
            var options = new TranscriptionOptions(settings.ModelId, settings.Language, settings.CustomPrompt);
            using var video = await _video.PrepareAsync(url, new Progress<string>(message => { if (_importing) VideoStatus = message; }), cancellation.Token);
            note = new NoteSession { Title = video.Title, Kind = "YouTube", SourceUrl = url };
            await _store.SaveAsync(note);
            Notes.Insert(0, note); Select(note);
            for (var index = 0; index < video.Parts.Count; index++)
            {
                VideoStatus = $"Transcribing part {index + 1} of {video.Parts.Count}…";
                var speech = await _groq.TranscribeAsync(key, video.Parts[index], options, cancellation.Token);
                // Video parts have a fixed offset; keep Groq's precise intra-part timestamps.
                var words = speech.Words;
                var text = words.Count > 0 ? string.Join(" ", words.Select(w => w.Text.Trim())) : speech.Text;
                if (!string.IsNullOrWhiteSpace(text)) note.Entries.Add(new(Guid.NewGuid(), index, index * 300d, index * 300d + (words.LastOrDefault()?.End ?? 300), "Video", text));
                File.Delete(video.Parts[index]);
                RebuildBubbles(); await SaveAsync(note);
            }
            VideoStatus = "Complete · transcript saved. Copy, open the notes folder, or Summarise.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        { VideoStatus = "Cancelled · completed passages remain saved; temporary audio was deleted."; }
        catch (Exception ex) { VideoStatus = "Import stopped · any completed passages remain saved."; Error = Friendly(ex); }
        finally { _importCancellation = null; _importing = false; Changed(); }
    }
    private async Task DeleteAsync()
    {
        if (SelectedNote is not { } note || !CanConfigure) return;
        try { await _store.DeleteAsync(note.Id); Notes.Remove(note); Select(Notes.FirstOrDefault()); }
        catch (Exception ex) { Error = Friendly(ex); }
    }
    private async Task<string> RequireKeyAsync(CancellationToken token)
        => await _secrets.GetApiKeyAsync(token) is { Length: > 0 } key ? key : throw new InvalidOperationException("Save and test your Groq key in Settings → Groq first.");
    public string ExportSelected() => SelectedNote is { } note ? NoteStore.Export(note) : "";
    public async Task ShutdownAsync()
    {
        _shuttingDown = true;
        _importCancellation?.Cancel(); _summaryCancellation?.Cancel();
        if (_startingTask is not null) await _startingTask;
        if (_pauseTask is not null) await _pauseTask;
        if (_importTask is not null) await _importTask;
        if (_summaryTask is not null) await _summaryTask;
        // Bound shutdown if the network is unavailable; cancelled passages remain explicitly marked.
        _sessionCancellation?.CancelAfter(TimeSpan.FromSeconds(12));
        await StopAsync(); await SaveSelectedAsync();
        _capture.ChunkAvailable -= OnChunk;
        await _capture.DisposeAsync(); _speakers.Dispose();
    }
    private static string Friendly(Exception ex) => ex switch
    {
        HttpRequestException => "Could not reach Groq. Check the internet connection.",
        OperationCanceledException => "The request timed out. Check the connection and try again.",
        _ => ex.Message
    };
    private void Changed()
    {
        foreach (var name in new[] { nameof(IsActive), nameof(IsPaused), nameof(CanConfigure), nameof(CanBrowse), nameof(IsImporting), nameof(IsSummarising), nameof(PauseText) }) OnPropertyChanged(name);
        RefreshCommands();
    }
    private void RefreshCommands()
    {
        foreach (var command in new[] { StartCommand, PauseCommand, StopCommand, CancelPendingCommand, SummariseCommand, CancelSummaryCommand, ImportCommand, CancelImportCommand, RefreshCommand, DeleteCommand }) command?.RaiseCanExecuteChanged();
    }
}
