using Wyspa.App.ViewModels;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class NotesViewModelTests
{
    [Fact]
    public async Task CancelPendingMarksGapsAndReleasesAudioAndMicrophone()
    {
        using var setup = new Setup();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        setup.Groq.Action = async (_, token) => { started.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return Speech(); };
        await setup.Vm.StartAsync(); setup.Capture.Emit(1, "You", 0); setup.Capture.Emit(2, "Other side", 2);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var stop = setup.Vm.StopAsync();
        setup.Vm.CancelPendingCommand.Execute(null);
        await stop.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, setup.Vm.Bubbles.Count);
        Assert.All(setup.Vm.Bubbles, b => { Assert.True(b.IsError); Assert.Contains("cancelled", b.Text); });
        Assert.False(setup.Reserved); Assert.All(setup.Groq.Paths, p => Assert.False(File.Exists(p)));
    }

    [Fact]
    public async Task CaptureStopFailureStillDrainsRequestsAndReleasesReservation()
    {
        using var setup = new Setup();
        await setup.Vm.StartAsync(); setup.Capture.Emit(1, "You", 0);
        setup.Capture.FailStop = true;
        await setup.Vm.StopAsync();
        Assert.False(setup.Reserved); Assert.False(setup.Vm.IsActive);
        Assert.Single(setup.Vm.Bubbles); Assert.Contains("device", setup.Vm.Error);
    }
    [Fact]
    public async Task ResultsArrivingOutOfOrderAreSortedAndStopDrainsBothSources()
    {
        using var setup = new Setup();
        var slow = new TaskCompletionSource<SpeechResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        setup.Groq.Action = (i, _) => { if (i == 1) { started.SetResult(); return slow.Task; } return Task.FromResult(Speech("Later")); };
        await setup.Vm.StartAsync();
        setup.Capture.Emit(1, "Other side", 1);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        setup.Capture.Emit(2, "You", 5);
        await Until(() => setup.Vm.Bubbles.Any(b => b.Text == "Later"));
        var stop = setup.Vm.StopAsync(); Assert.False(stop.IsCompleted);
        slow.SetResult(Speech("Earlier")); await stop;
        Assert.Equal(new[] { "Earlier", "Later" }, setup.Vm.Bubbles.Select(b => b.Text));
        Assert.False(setup.Vm.Bubbles[0].IsMine); Assert.True(setup.Vm.Bubbles[1].IsMine);
        Assert.False(setup.Reserved); Assert.False(setup.Vm.IsActive);
        Assert.All(setup.Groq.Paths, p => Assert.False(File.Exists(p)));
        Assert.Equal(2, Assert.Single(await setup.Store.LoadAsync()).Entries.Count);
    }

    [Fact]
    public async Task PauseStopsCaptureResumeKeepsSameNoteAndModeCannotChangeWhileActive()
    {
        using var setup = new Setup();
        await setup.Vm.StartAsync(); var noteId = setup.Vm.SelectedNote!.Id;
        setup.Vm.ModeIndex = 1; Assert.Equal(ConversationMode.ComputerCall, setup.Settings.NoteMode);
        await setup.Vm.PauseResumeAsync(); Assert.True(setup.Vm.IsPaused); Assert.False(setup.Capture.Running);
        Assert.True(setup.Reserved);
        await setup.Vm.PauseResumeAsync(); Assert.True(setup.Capture.Running); Assert.Equal(noteId, setup.Vm.SelectedNote.Id);
        await setup.Vm.StopAsync(); Assert.False(setup.Reserved);
    }

    [Fact]
    public async Task RoomTextAppearsBeforeSpeakerDetectionFinishesAndIdentityPersists()
    {
        using var setup = new Setup(); setup.Settings.NoteMode = ConversationMode.InPerson;
        var identification = new TaskCompletionSource<IReadOnlyList<SpeakerTurn>>(TaskCreationOptions.RunContinuationsAsynchronously);
        setup.Speakers.Result = identification.Task;
        await setup.Vm.StartAsync(); setup.Capture.Emit(1, "Identifying speaker…", 0);
        await Until(() => setup.Vm.Bubbles.Count == 1);
        Assert.Equal("Identifying speaker…", setup.Vm.Bubbles[0].Speaker);
        identification.SetResult([new(0, 2, "Speaker 1")]);
        await setup.Vm.StopAsync(); setup.Vm.MySpeaker = "Speaker 1";
        await setup.Vm.SaveSelectedAsync();
        Assert.True(Assert.Single(setup.Vm.Bubbles).IsMine);
        await setup.Vm.RefreshAsync(); Assert.Equal("Speaker 1", setup.Vm.MySpeaker);
        Assert.True(Assert.Single(setup.Vm.Bubbles).IsMine);
    }

    [Fact]
    public async Task FailedUploadLeavesVisibleGapAndDeletesTemporaryAudio()
    {
        using var setup = new Setup(); setup.Groq.Action = (_, _) => throw new HttpRequestException("offline");
        await setup.Vm.StartAsync(); setup.Capture.Emit(1, "You", 0); await setup.Vm.StopAsync();
        Assert.True(Assert.Single(setup.Vm.Bubbles).IsError);
        Assert.Contains("could not be transcribed", setup.Vm.Error);
        Assert.All(setup.Groq.Paths, p => Assert.False(File.Exists(p)));
    }

    [Fact]
    public async Task MissingApiKeyDoesNotStartCaptureOrReserveMicrophone()
    {
        using var setup = new Setup(); setup.Secrets.Key = null;
        await setup.Vm.StartAsync();
        Assert.False(setup.Capture.Running); Assert.False(setup.Reserved); Assert.Empty(setup.Vm.Notes);
        Assert.Contains("Groq key", setup.Vm.Error);
    }

    [Fact]
    public async Task ImportSavesTextAndDeletesDownloadedAudio()
    {
        using var setup = new Setup(NoteLibrary.YouTube); setup.Vm.Url = "https://youtu.be/jNQXAC9IVRw";
        await setup.Vm.ImportAsync();
        Assert.Equal("YouTube", setup.Vm.SelectedNote!.Kind); Assert.Equal(2, setup.Vm.Bubbles.Count);
        Assert.False(Directory.Exists(setup.Video.Directory));
        Assert.Equal(2, Assert.Single(await setup.Store.LoadAsync()).Entries.Count);
        Assert.Equal(300, setup.Vm.Bubbles[1].Entry.Start);
    }

    [Fact]
    public async Task LibrariesFilterExistingFilesAndKeepIndependentSelectionSummaryAndDeletion()
    {
        using var conversation = new Setup();
        var call = new NoteSession { Title = "Call", Kind = "Computer call", Summary = "Call summary" };
        var room = new NoteSession { Title = "Room", Kind = "In-person" };
        var video = new NoteSession { Title = "Video", Kind = "YouTube", Summary = "Video summary" };
        foreach (var note in new[] { call, room, video }) await conversation.Store.SaveAsync(note);
        var videos = new NotesViewModel(new Capture(), new Speakers(), new Groq(), new Secrets(), conversation.Store,
            new Video(), () => conversation.Settings, action => action(), _ => Task.CompletedTask, () => Task.CompletedTask, NoteLibrary.YouTube);
        await conversation.Vm.RefreshAsync(); await videos.RefreshAsync();
        Assert.Equal(2, conversation.Vm.Notes.Count);
        Assert.DoesNotContain(conversation.Vm.Notes, n => n.Kind == "YouTube");
        Assert.Equal(video.Id, Assert.Single(videos.Notes).Id);
        conversation.Vm.SelectedNote = conversation.Vm.Notes.Single(n => n.Id == call.Id);
        videos.SelectedNote = call; // Cross-library selection is rejected.
        Assert.Equal(video.Id, videos.SelectedNote!.Id);
        Assert.Equal("Call summary", conversation.Vm.Summary);
        Assert.Equal("Video summary", videos.Summary);
        await conversation.Vm.StartAsync();
        Assert.True(videos.CanBrowse); Assert.True(videos.CanConfigure);
        await conversation.Vm.StopAsync();
        videos.DeleteCommand.Execute(null);
        await Until(() => videos.Notes.Count == 0);
        Assert.Contains(await conversation.Store.LoadAsync(), n => n.Id == call.Id);
        Assert.DoesNotContain(await conversation.Store.LoadAsync(), n => n.Id == video.Id);
        Assert.False(videos.StartCommand.CanExecute(null));
        Assert.False(conversation.Vm.ImportCommand.CanExecute(null));
        await videos.ShutdownAsync();
    }

    private static SpeechResult Speech(string text = "Hello") => new(text, [new(.1, .8, text)]);
    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
    private sealed class Setup : IDisposable
    {
        public AppSettings Settings { get; } = new();
        public Capture Capture { get; } = new();
        public Speakers Speakers { get; } = new();
        public Groq Groq { get; } = new();
        public Secrets Secrets { get; } = new();
        public Video Video { get; } = new();
        public NoteStore Store { get; } = new(Directory.CreateTempSubdirectory("wyspa-vm-notes-").FullName);
        public NotesViewModel Vm { get; }
        public bool Reserved { get; private set; }
        private readonly SemaphoreSlim _ui = new(1);
        public Setup(NoteLibrary library = NoteLibrary.Conversations) => Vm = new(Capture, Speakers, Groq, Secrets, Store, Video, () => Settings,
            async action => { await _ui.WaitAsync(); try { await action(); } finally { _ui.Release(); } },
            reserved => { Reserved = reserved; return Task.CompletedTask; }, () => Task.CompletedTask, library);
        public void Dispose() { Directory.Delete(Store.DirectoryPath, true); if (Directory.Exists(Video.Directory)) Directory.Delete(Video.Directory, true); }
    }
    private sealed class Capture : IConversationCapture
    {
        public event EventHandler<AudioChunk>? ChunkAvailable;
        public event EventHandler<string>? Failed { add { } remove { } }
        public bool SupportsApplicationCapture => true;
        public bool Running { get; private set; }
        public bool FailStop { get; set; }
        public IReadOnlyList<CaptureTarget> GetOutputs() => [new("", "Default")];
        public IReadOnlyList<CaptureTarget> GetApplications() => [];
        public Task StartAsync(ConversationCaptureOptions options, double offset, CancellationToken token) { Running = true; return Task.CompletedTask; }
        public Task StopAsync() { Running = false; if (FailStop) throw new InvalidOperationException("device stopped unexpectedly"); return Task.CompletedTask; }
        public void Emit(int id, string source, double time) => ChunkAvailable?.Invoke(this, new(id, source, time, new float[32000]));
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Speakers : ISpeakerIdentifier
    {
        public Task<IReadOnlyList<SpeakerTurn>> Result { get; set; } = Task.FromResult<IReadOnlyList<SpeakerTurn>>([]);
        public Task InitializeAsync(CancellationToken token) => Task.CompletedTask;
        public Task<IReadOnlyList<SpeakerTurn>> IdentifyAsync(float[] samples, CancellationToken token) => Result;
        public void Reset() { }
        public void Dispose() { }
    }
    private sealed class Groq : INoteIntelligence
    {
        private int _calls;
        public List<string> Paths { get; } = [];
        public Func<int, CancellationToken, Task<SpeechResult>> Action { get; set; } = (_, _) => Task.FromResult(Speech());
        public Task<SpeechResult> TranscribeAsync(string key, string path, TranscriptionOptions options, CancellationToken token)
        {
            lock (Paths) Paths.Add(path);
            return Action(Interlocked.Increment(ref _calls), token);
        }
        public Task<string> SummariseAsync(string key, string transcript, string model, CancellationToken token) => Task.FromResult("Summary");
    }
    private sealed class Secrets : ISecretStore
    {
        public string? Key { get; set; } = "fake-test-key";
        public Task<string?> GetApiKeyAsync(CancellationToken cancellationToken) => Task.FromResult(Key);
        public Task SaveApiKeyAsync(string apiKey, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveApiKeyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
    private sealed class Video : IVideoImporter
    {
        public string Directory { get; } = System.IO.Directory.CreateTempSubdirectory("wyspa-video-test-").FullName;
        public Task<PreparedVideo> PrepareAsync(string url, IProgress<string> progress, CancellationToken token)
        {
            var parts = new[] { Path.Combine(Directory, "one.wav"), Path.Combine(Directory, "two.wav") };
            foreach (var part in parts) File.WriteAllBytes(part, [1]);
            return Task.FromResult(new PreparedVideo("Test video", Directory, parts));
        }
    }
}
