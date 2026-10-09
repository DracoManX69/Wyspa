using Wyspa.App.ViewModels;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class VoiceSetupTests
{
    [Theory]
    [InlineData("Hello, WORLD!", "hello world", 0)]
    [InlineData("one two three", "one four three five", 2)]
    [InlineData("one two", "", 2)]
    [InlineData("Café résumé", "café résumé", 0)]
    public void WordErrorsUseEditDistance(string reference, string actual, int expected) => Assert.Equal(expected, VoicePersonalization.Errors(reference, actual));

    [Fact]
    public void CatalogHasPinnedSourcesAndHardSizeLimit()
    {
        Assert.Equal(25, LocalModelStore.Catalog.Count);
        Assert.All(LocalModelStore.Catalog, m =>
        {
            Assert.InRange(m.Bytes, 1, 1_500_000_000);
            if (m.Id == ZipformerModel.Id) { Assert.All(ZipformerModel.Files, f => Assert.Equal(64, f.Hash.Length)); Assert.Contains(ZipformerModel.Revision, m.SourceUrl); return; }
            if (m.Bundle is { } bundle)
            {
                Assert.Equal(m.Bytes, bundle.Files.Sum(f => f.Bytes)); Assert.All(bundle.Files, f => Assert.Equal(64, f.Hash.Length));
                Assert.All(bundle.Files, f => Assert.StartsWith("https://huggingface.co/", f.Url)); return;
            }
            Assert.Equal(64, m.Sha256.Length);
            Assert.Contains(LocalModelStore.SourceRevision, m.SourceUrl); Assert.Contains(m.Size, m.DisplayName);
        });
        Assert.True(LocalModelStore.Catalog.Single(m => m.Id == "medium.en-q5_0").EnglishOnly);
        Assert.False(LocalModelStore.Catalog.Single(m => m.Id == "large-v3-turbo-q5_0").EnglishOnly);
    }
    [Fact]
    public void LocalStreamFormatPreservesTimingContract()
    {
        var speech = new SpeechResult("hello there", [new(0, 1.2, "hello there")]);
        var parsed = GroqNoteIntelligence.Parse(TranscriptionRouter.FormatLocalResult(speech, "verbose_json"));
        Assert.Equal(speech.Text, parsed.Text); Assert.Equal(speech.Words, parsed.Words);
        Assert.Equal("hello there", TranscriptionRouter.FormatLocalResult(speech, "text"));
    }
    [Theory]
    [InlineData("base.en")]
    [InlineData("faster-whisper-tiny.en")]
    public async Task ThreeReadingsSaveProfile_WithoutKeepingAudioOrLeakingReferenceIntoPrompt(string modelId)
    {
        var settings = new AppSettings { LocalModelId = modelId }; var capture = new Capture(); var reservations = new List<bool>();
        VoiceSetupViewModel vm = null!; var calls = 0; var saves = 0;
        vm = new(capture, () => settings, (_, options, _) =>
        {
            calls++; Assert.False(options.ApplyPersonalization);
            Assert.DoesNotContain(vm.Reference, options.Prompt ?? "");
            return Task.FromResult(new SpeechResult(vm.Reference, []));
        }, _ => true, value => { reservations.Add(value); return Task.CompletedTask; }, () => { saves++; return Task.CompletedTask; });
        vm.Vocabulary = "Wyspa, Deakin";
        for (var i = 0; i < 3; i++) { await vm.StartAsync(); Assert.True(vm.IsRecording); await vm.StopAndTestAsync(); }
        Assert.Equal(6, calls); Assert.Equal(3, capture.Deleted); Assert.False(capture.IsRecording);
        Assert.Equal(new[] { true, false, true, false, true, false }, reservations);
        await vm.SaveAsync();
        Assert.Equal(1, saves); Assert.True(settings.UseLocalTranscription); Assert.NotNull(settings.LocalVoiceProfile);
        Assert.Equal("Wyspa, Deakin", settings.LocalVoiceProfile.Vocabulary); Assert.Equal(3, settings.LocalVoiceProfile.Scores.Count);
        Assert.Equal(settings.LocalVoiceProfile.SuggestedSilenceMs, settings.AutoCaptureSilenceMs);
        await vm.ShutdownAsync();
    }
    [Fact]
    public async Task CancelRecordingDeletesAudioAndReleasesReservation()
    {
        var capture = new Capture(); var released = false;
        var vm = new VoiceSetupViewModel(capture, () => new AppSettings(), (_, _, _) => throw new Exception("Must not transcribe"), _ => true,
            r => { released = !r; return Task.CompletedTask; }, () => Task.CompletedTask);
        await vm.StartAsync(); await vm.CancelAsync();
        Assert.False(vm.IsRecording); Assert.Equal(1, capture.Deleted); Assert.True(released); Assert.Empty(vm.Results);
        await vm.ShutdownAsync();
    }
    [Fact]
    public async Task FailedInferenceCleansRecordingAndDoesNotCompletePassage()
    {
        var capture = new Capture(); var settings = new AppSettings();
        var vm = new VoiceSetupViewModel(capture, () => settings, (_, _, _) => throw new InvalidOperationException("broken model"), _ => true, _ => Task.CompletedTask, () => Task.CompletedTask);
        await vm.StartAsync(); await vm.StopAndTestAsync(); await vm.SaveAsync();
        Assert.Equal(1, capture.Deleted); Assert.Empty(vm.Results); Assert.Null(settings.LocalVoiceProfile); Assert.False(vm.IsBusy);
        Assert.Contains("broken model", vm.Status); await vm.ShutdownAsync();
    }
    [Fact]
    public async Task ChangingHintsMidSetupRequiresRestart()
    {
        var capture = new Capture(); var settings = new AppSettings(); VoiceSetupViewModel vm = null!;
        vm = new(capture, () => settings, (_, _, _) => Task.FromResult(new SpeechResult(vm.Reference, [])), _ => true, _ => Task.CompletedTask, () => Task.CompletedTask);
        await vm.StartAsync(); await vm.StopAndTestAsync(); vm.Vocabulary = "changed"; await vm.StartAsync();
        Assert.False(vm.IsRecording); Assert.Contains("Restart", vm.Status); await vm.ShutdownAsync();
    }
    [Fact]
    public async Task ProfileRoundTripsWithSettings()
    {
        var path = Path.GetTempFileName();
        try
        {
            var service = new JsonSettingsService(path);
            await service.SaveAsync(new AppSettings { LocalVoiceProfile = new() { Language = "fr", Vocabulary = "Deakin", ModelId = "small", CompletedAt = DateTimeOffset.UtcNow } }, default);
            Assert.Equal("Deakin", (await service.LoadAsync(default)).LocalVoiceProfile!.Vocabulary);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public async Task HintsThatWorsenRecognitionAreNotApplied()
    {
        var settings = new AppSettings(); var capture = new Capture(); VoiceSetupViewModel vm = null!;
        vm = new(capture, () => settings, (_, options, _) => Task.FromResult(new SpeechResult(
            options.Prompt is null ? vm.Reference : vm.Reference + " invented extra words", [])), _ => true, _ => Task.CompletedTask, () => Task.CompletedTask);
        vm.Vocabulary = "unhelpful words";
        for (var i = 0; i < 3; i++) { await vm.StartAsync(); await vm.StopAndTestAsync(); }
        await vm.SaveAsync(); Assert.Equal("", settings.LocalVoiceProfile!.Vocabulary); Assert.Contains("increased errors", vm.Status);
        await vm.ShutdownAsync();
    }
    [Fact]
    public async Task FailedProfileSaveRestoresPreviousSettings()
    {
        var old = new LocalVoiceProfile { Vocabulary = "old" }; var settings = new AppSettings { LocalVoiceProfile = old, AutoCaptureSilenceMs = 900 };
        VoiceSetupViewModel vm = null!;
        vm = new(new Capture(), () => settings, (_, _, _) => Task.FromResult(new SpeechResult(vm.Reference, [])), _ => true, _ => Task.CompletedTask,
            () => throw new IOException("disk full"));
        for (var i = 0; i < 3; i++) { await vm.StartAsync(); await vm.StopAndTestAsync(); }
        await vm.SaveAsync(); Assert.Same(old, settings.LocalVoiceProfile); Assert.Equal(900, settings.AutoCaptureSilenceMs); Assert.False(settings.UseLocalTranscription);
        await vm.ShutdownAsync();
    }
    private sealed class Capture : IAudioCaptureService
    {
        public event EventHandler<float>? LevelAvailable;
        public bool IsRecording { get; private set; }
        public int Deleted { get; private set; }
        public Task<IReadOnlyList<AudioDeviceInfo>> GetDevicesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<AudioDeviceInfo>>([]);
        public Task StartRecordingAsync(string? device, CancellationToken token) { IsRecording = true; LevelAvailable?.Invoke(this, .2f); return Task.CompletedTask; }
        public Task<RecordingResult> StopRecordingAsync(CancellationToken token) { IsRecording = false; return Task.FromResult(new RecordingResult("test.wav", TimeSpan.FromSeconds(12), 384000, .4f)); }
        public Task DeleteRecordingAsync(string path, CancellationToken token) { Deleted++; return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
