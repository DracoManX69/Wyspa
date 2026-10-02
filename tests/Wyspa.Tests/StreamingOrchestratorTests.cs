using Wyspa.App.Services;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using Wyspa.Core.Services;
using static Wyspa.Tests.StreamingDictationTests;

namespace Wyspa.Tests;

public sealed class StreamingOrchestratorTests
{
    [Theory]
    [InlineData(ActivationMode.Toggle)]
    [InlineData(ActivationMode.HoldToTalk)]
    [InlineData(ActivationMode.AutoCapture)]
    public async Task AllModes_StreamDuringCapture_FlushOnce_AndUseStartTimeSettings(ActivationMode mode)
    {
        var settings = new Settings { Value = new AppSettings { StreamModeEnabled = true, ActivationMode = mode, IntentActionsEnabled = true, GroqWritingCleanupEnabled = true, InsertionMode = InsertionMode.Type } };
        var capture = new Capture();
        var groq = new FakeGroq();
        groq.Responses.Enqueue(Json("This is a test"));
        groq.Responses.Enqueue(Json("This is a test of Wyspa"));
        groq.Responses.Enqueue(Json("This is a test of Wyspa Stream mode"));
        var insertion = new Insertion();
        var overlay = new OverlayStatusService();
        var orchestrator = new DictationOrchestrator(settings, new Secrets(), capture, groq, new TextCleanupService(), insertion, new Keys(), overlay);
        await orchestrator.StartListeningAsync();
        capture.Emit(Pcm(1.6));
        await WaitFor(() => groq.Paths.Count == 1);
        capture.Emit(Pcm(1.1));
        await insertion.FirstUpdate.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(capture.IsRecording);
        Assert.Equal(DictationState.Listening, orchestrator.State);
        Assert.Equal("This is a test", insertion.Clipboard);
        settings.Value = new AppSettings { StreamModeEnabled = false }; // applies next time
        capture.Emit(Pcm(1));
        await orchestrator.StopListeningAndTranscribeAsync();
        Assert.Equal("This is a test of Wyspa Stream mode", insertion.Typed);
        Assert.Equal(insertion.Typed, insertion.Clipboard);
        Assert.Equal(1, capture.Stops);
        Assert.Equal(1, insertion.Begins);
        Assert.Equal(InsertionMode.Type, insertion.Mode);
        Assert.Equal(1, insertion.Ends);
        Assert.Equal(0, groq.ProofreadCalls);
        Assert.Equal(DictationState.Inserted, orchestrator.State);
        Assert.Equal(1, capture.Deleted);
        Assert.Equal(0, capture.Subscribers);
    }

    [Theory]
    [InlineData(ActivationMode.Toggle, InsertionMode.Paste)]
    [InlineData(ActivationMode.HoldToTalk, InsertionMode.Type)]
    [InlineData(ActivationMode.AutoCapture, InsertionMode.Paste)]
    public async Task StreamModeOff_InsertsOnlyOnceAfterStop_AndDoesNotRunStreamFix(ActivationMode mode, InsertionMode insertionMode)
    {
        var path = Path.GetTempFileName();
        try
        {
            var capture = new Capture { RecordingPath = path };
            var groq = new FakeGroq { ExpectedFormat = "text" };
            groq.Responses.Enqueue("  Only this dictated sentence.  ");
            var insertion = new Insertion { AllowNormalInsert = true };
            var settings = new Settings { Value = new() { StreamModeEnabled = false, StreamFixEnabled = true, ActivationMode = mode,
                InsertionMode = insertionMode, CleanupEnabled = false, IntentActionsEnabled = false, CopyInsertedTextToClipboard = true } };
            var orchestrator = new DictationOrchestrator(settings, new Secrets(), capture, groq, new(), insertion, new Keys(), new());
            await orchestrator.StartListeningAsync(); capture.Emit(Pcm(2));
            Assert.Equal(0, capture.Subscribers);
            Assert.Empty(groq.Paths);
            Assert.Equal(0, insertion.NormalInserts);
            Assert.Equal(0, insertion.Begins);
            await orchestrator.StopListeningAndTranscribeAsync();
            Assert.Equal(1, insertion.NormalInserts);
            Assert.Equal(insertionMode, insertion.Mode);
            Assert.Equal("Only this dictated sentence.", insertion.Typed);
            Assert.Equal(insertion.Typed, insertion.Clipboard);
            Assert.Empty(insertion.Finals);
            Assert.Equal(0, groq.ProofreadCalls);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task StopCancelsSlowLiveRequest_ThenRecoversTail_NoLateUpdate()
    {
        var settings = new Settings { Value = new AppSettings { StreamModeEnabled = true } };
        var capture = new Capture();
        var groq = new FakeGroq();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        groq.Handler = async token =>
        {
            if (groq.Paths.Count == 1) { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); }
            return Json("Recovered tail");
        };
        var insertion = new Insertion();
        var orchestrator = new DictationOrchestrator(settings, new Secrets(), capture, groq, new(), insertion, new Keys(), new());
        await orchestrator.StartListeningAsync(); capture.Emit(Pcm(2));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await orchestrator.StopListeningAndTranscribeAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Recovered tail", insertion.Typed);
        Assert.Equal(2, groq.Paths.Count);
        Assert.Equal(0, capture.Subscribers);
    }

    [Fact]
    public async Task InsertionUnavailable_ContinuesClipboard_ReportsRecoverableState()
    {
        var capture = new Capture(); var groq = new FakeGroq(); var insertion = new Insertion { CanType = false };
        groq.Responses.Enqueue(Json("Copied words"));
        var orchestrator = new DictationOrchestrator(new Settings { Value = new() { StreamModeEnabled = true } }, new Secrets(), capture, groq, new(), insertion, new Keys(), new());
        await orchestrator.StartListeningAsync(); capture.Emit(Pcm(2));
        await orchestrator.StopListeningAndTranscribeAsync();
        Assert.Equal("Copied words", insertion.Clipboard);
        Assert.Empty(insertion.Typed);
        Assert.Equal(DictationState.Error, orchestrator.State);
    }

    [Fact]
    public async Task StartFailure_ReleasesStreamSubscription_AndAllowsRetry()
    {
        var capture = new Capture { FailStart = true }; var groq = new FakeGroq(); var insertion = new Insertion();
        var orchestrator = new DictationOrchestrator(new Settings { Value = new() { StreamModeEnabled = true } }, new Secrets(), capture, groq, new(), insertion, new Keys(), new());
        await Assert.ThrowsAsync<InvalidOperationException>(() => orchestrator.StartListeningAsync());
        Assert.Equal(0, capture.Subscribers);
        capture.FailStart = false;
        groq.Responses.Enqueue(Json("Retry works"));
        await orchestrator.StartListeningAsync(); capture.Emit(Pcm(2));
        await orchestrator.StopListeningAndTranscribeAsync();
        Assert.Equal("Retry works", insertion.Typed);
    }

    [Fact]
    public async Task NextUtterance_CapturesDuringSlowFinalization_AndDeliversInSessionOrder()
    {
        var capture = new Capture(); var groq = new FakeGroq(); var insertion = new Insertion();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        groq.Handler = async token =>
        {
            if (groq.Paths.Count == 1) { entered.SetResult(); await release.Task.WaitAsync(token); return Json("First dictation"); }
            return Json("Second dictation");
        };
        var overlay = new OverlayStatusService();
        var orchestrator = new DictationOrchestrator(new Settings { Value = new() { StreamModeEnabled = true } }, new Secrets(), capture, groq, new(), insertion, new Keys(), overlay);
        var stopped = 0;
        orchestrator.CaptureStopped += (_, _) => stopped++;
        await orchestrator.StartListeningAsync(); capture.Emit(Pcm(2));
        var first = orchestrator.StopListeningAndTranscribeAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, stopped);
        await orchestrator.StartListeningAsync().WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(capture.IsRecording);
        capture.Emit(Pcm(2));
        var second = orchestrator.StopListeningAndTranscribeAsync();
        Assert.Single(groq.Paths); // output/network queue has a single owner
        Assert.False(first.IsCompleted); Assert.False(second.IsCompleted);
        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "First dictation", "Second dictation" }, insertion.Finals);
        Assert.Equal("Second dictation", insertion.Clipboard);
        Assert.Equal(2, capture.Deleted);
        Assert.Equal(0, capture.Subscribers);
        Assert.Equal(DictationState.Inserted, orchestrator.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StreamFix_UsesFinalAudioTranscript_AndKeepsItWhenProofreadingFails(bool failProofreading)
    {
        var capture = new Capture(); var groq = new FakeGroq(); var insertion = new Insertion { CanCorrect = true };
        groq.Responses.Enqueue(Json("um original words"));
        groq.ProofreadHandler = text =>
        {
            Assert.Equal("um original words", text);
            if (failProofreading) throw new HttpRequestException("offline");
            return Task.FromResult(new StreamFixResult("Original words.", false));
        };
        var settings = new Settings { Value = new() { StreamModeEnabled = true, StreamFixEnabled = true } };
        var overlay = new OverlayStatusService();
        var orchestrator = new DictationOrchestrator(settings, new Secrets(), capture, groq, new(), insertion, new Keys(), overlay);
        await orchestrator.StartListeningAsync(); capture.Emit(Pcm(2));
        await orchestrator.StopListeningAndTranscribeAsync();
        Assert.Equal(1, groq.ProofreadCalls);
        Assert.Equal(failProofreading ? "um original words" : "Original words.", insertion.Clipboard);
        Assert.Equal(insertion.Clipboard, insertion.Typed);
        if (failProofreading) Assert.Contains("unavailable", overlay.LastMessage);
        Assert.Equal(1, insertion.Ends);
    }

    [Fact]
    public async Task Drain_BlocksNewRecordingsUntilPendingOutputCompletes()
    {
        var capture = new Capture(); var groq = new FakeGroq(); var insertion = new Insertion();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        groq.Handler = async token => { entered.TrySetResult(); await release.Task.WaitAsync(token); return Json("Complete words"); };
        var orchestrator = new DictationOrchestrator(new Settings { Value = new() { StreamModeEnabled = true } }, new Secrets(), capture, groq, new(), insertion, new Keys(), new());
        await orchestrator.StartListeningAsync(); capture.Emit(Pcm(2));
        var drain = orchestrator.StopIfNeededAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(orchestrator.HasPendingStreamOutput);
        await orchestrator.StartListeningAsync();
        Assert.False(capture.IsRecording);
        release.SetResult(); await drain;
        Assert.False(orchestrator.HasPendingStreamOutput);
        Assert.Equal("Complete words", insertion.Clipboard);
        await orchestrator.StartListeningAsync();
        Assert.True(capture.IsRecording);
        capture.Emit(Pcm(2)); await orchestrator.StopIfNeededAsync();
    }

    private static async Task WaitFor(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate()) await Task.Delay(20, timeout.Token);
    }
    private sealed class Settings : ISettingsService
    {
        public AppSettings Value = new();
        public Task<AppSettings> LoadAsync(CancellationToken token) => Task.FromResult(Value);
        public Task SaveAsync(AppSettings settings, CancellationToken token) { Value = settings; return Task.CompletedTask; }
    }
    private sealed class Secrets : ISecretStore
    {
        public Task<string?> GetApiKeyAsync(CancellationToken token) => Task.FromResult<string?>("test");
        public Task SaveApiKeyAsync(string key, CancellationToken token) => Task.CompletedTask;
        public Task RemoveApiKeyAsync(CancellationToken token) => Task.CompletedTask;
    }
    private sealed class Keys : IKeyboardCommandService
    {
        public Task SendAsync(KeyPressCommand command, CancellationToken token) => throw new Exception("Stream Mode must not execute spoken actions.");
    }
    private sealed class Capture : IAudioCaptureService, IStreamingAudioCaptureService
    {
        private event EventHandler<ReadOnlyMemory<byte>>? _pcm;
        public event EventHandler<ReadOnlyMemory<byte>>? PcmAvailable { add { _pcm += value; Subscribers++; } remove { _pcm -= value; Subscribers--; } }
        public event EventHandler<float>? LevelAvailable { add { } remove { } }
        public int Subscribers, Stops, Deleted;
        public bool FailStart;
        public string RecordingPath = "test.wav";
        public bool IsRecording { get; private set; }
        public void Emit(byte[] pcm) => _pcm?.Invoke(this, pcm);
        public Task<IReadOnlyList<AudioDeviceInfo>> GetDevicesAsync(CancellationToken token) => throw new NotSupportedException();
        public Task StartRecordingAsync(string? id, CancellationToken token)
        { if (FailStart) throw new InvalidOperationException("Microphone unavailable"); IsRecording = true; return Task.CompletedTask; }
        public Task<RecordingResult> StopRecordingAsync(CancellationToken token)
        { IsRecording = false; Stops++; return Task.FromResult(new RecordingResult(RecordingPath, TimeSpan.FromSeconds(4), 128000, .3f)); }
        public Task DeleteRecordingAsync(string path, CancellationToken token) { Deleted++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class Insertion : ITextInsertionService, IStreamingTextInsertionService
    {
        public string Typed = "", Clipboard = "";
        public bool CanType = true;
        public bool CanCorrect;
        public int Begins, Ends, NormalInserts;
        public bool AllowNormalInsert;
        public InsertionMode Mode;
        public List<string> Finals = [];
        public TaskCompletionSource FirstUpdate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void BeginStream(InsertionMode mode = InsertionMode.Paste) { Begins++; Mode = mode; Typed = ""; }
        public void EndStream() => Ends++;
        public Task<bool> CompleteStreamAsync(string finalText, bool allowCorrection, CancellationToken token) { if (allowCorrection && CanType && CanCorrect) Typed = finalText; Clipboard = finalText; Finals.Add(finalText); return Task.FromResult(CanType && Typed == finalText); }
        public Task<bool> AppendStreamAsync(string delta, string text, CancellationToken token)
        { if (CanType) Typed += delta; Clipboard = text; FirstUpdate.TrySetResult(); return Task.FromResult(CanType); }
        public Task<bool> InsertAsync(string text, InsertionMode mode, bool success, bool failure, CancellationToken token)
        {
            if (!AllowNormalInsert) throw new Exception("Do not insert a full transcript a second time.");
            NormalInserts++; Mode = mode; Typed = text; if (success) Clipboard = text;
            return Task.FromResult(true);
        }
    }
}
