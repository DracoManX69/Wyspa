using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using Wyspa.Core.Services;
using Wyspa.App.ViewModels;

namespace Wyspa.Tests;

public sealed class ContinuousLocalTests
{
    [Fact]
    public async Task LocalWhisperStopUsesBoundedWindows_NotACompleteFinalPass()
    {
        var provider = new StreamingDictationTests.FakeGroq();
        for (var i = 0; i < 10; i++) provider.Responses.Enqueue(StreamingDictationTests.Json(string.Join(" ", Enumerable.Range(0, 20).Select(n => "word" + n)), 0, 1));
        using var session = new StreamingDictationSession(provider, "", new("", "en", null, UseLocal: true, LocalModelId: "tiny.en"),
            (_, _, _) => Task.CompletedTask);
        session.AddAudio(null, StreamingDictationTests.Pcm(30.5));
        await session.ProcessAsync(true, default);
        Assert.All(provider.UploadLengths, length => Assert.InRange(length, 44, 640044));
        Assert.All(provider.Paths, path => Assert.DoesNotContain("stream-final", path));
        Assert.True(provider.Paths.Count >= 2);
    }
    [Fact]
    public async Task StreamFeedsEachSampleOnce_AndFlushesTailWithoutFullRetranscription()
    {
        var provider = new Provider(); var output = new List<string>();
        using var session = new StreamingDictationSession(provider, "", new("", "en", null, UseLocal: true, LocalModelId: ZipformerModel.Id),
            (delta, text, token) => { output.Add(text); return Task.CompletedTask; });
        session.AddAudio(null, new byte[6400]);
        await session.ProcessAsync(false, default);
        await session.ProcessAsync(false, default);
        session.AddAudio(null, new byte[3200]);
        await session.ProcessAsync(true, default);
        await session.ProcessAsync(true, default);
        Assert.Equal(4800, provider.Stream.Samples);
        Assert.Equal(1, provider.Stream.Finishes);
        Assert.Equal(1, provider.Creates);
        Assert.Equal("very very good", session.Text);
        Assert.Equal(["very", "very very", "very very good"], output);
    }
    [Fact]
    public async Task FailedDeliveryRetriesWithoutFeedingPcmTwice()
    {
        var provider = new Provider(); var fail = true;
        using var session = new StreamingDictationSession(provider, "", new("", "en", null, UseLocal: true, LocalModelId: ZipformerModel.Id),
            (delta, text, token) => { if (fail) { fail = false; throw new IOException("Clipboard busy"); } return Task.CompletedTask; });
        session.AddAudio(null, new byte[6400]);
        await Assert.ThrowsAsync<IOException>(() => session.ProcessAsync(false, default));
        await session.ProcessAsync(true, default);
        Assert.Equal(3200, provider.Stream.Samples);
        Assert.Equal(1, provider.Stream.Finishes);
    }
    [Fact]
    public async Task FreshInstallDefaultsToLocalStreaming_ButExistingCloudPreferenceIsPreserved()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var settings = new JsonSettingsService(path);
            var fresh = await settings.LoadAsync(default);
            Assert.True(fresh.UseLocalTranscription); Assert.True(fresh.StreamModeEnabled); Assert.Equal(ZipformerModel.Id, fresh.LocalModelId);
            await File.WriteAllTextAsync(path, "{}");
            Assert.False((await settings.LoadAsync(default)).UseLocalTranscription);
            await settings.SaveAsync(new() { UseLocalTranscription = false, LocalModelId = "small.en" }, default);
            var existing = await settings.LoadAsync(default);
            Assert.False(existing.UseLocalTranscription); Assert.Equal("small.en", existing.LocalModelId);
        }
        finally { File.Delete(path); }
    }
    [Fact]
    public void LocalModeCanEnableManagementWithoutModels_ButRequiresInstalledModelForDictation()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(directory);
        try
        {
            using var http = new HttpClient();
            var store = new LocalModelStore(http, directory, [new("tiny.en", "Test", 1, "")]);
            using var client = new LocalTranscriptionClient(store, "unused");
            var settings = new AppSettings { LocalModelId = "tiny.en" };
            var vm = new LocalModelsViewModel(store, client, () => settings, () => Task.CompletedTask);
            vm.Enabled = true; Assert.True(vm.Enabled); Assert.False(vm.Installed); Assert.True(vm.CanConfigure);
            File.WriteAllBytes(store.ModelPath("tiny.en"), [0]);
            vm.Enabled = true; Assert.True(vm.Enabled); Assert.Null(settings.LocalVoiceProfile);
        }
        finally { Directory.Delete(directory, true); }
    }
    [Theory]
    [InlineData(2, 4, 1, 1)]
    [InlineData(8, 32, 4, 2)]
    [InlineData(64, 4, 2, 2)]
    public void HardwareBudgetsLeaveHeadroom(int cores, int gb, int whisper, int streaming)
    {
        var hardware = new LocalHardware(cores, (ulong)gb * 1024 * 1024 * 1024, null);
        Assert.Equal(whisper, hardware.WhisperThreads); Assert.Equal(streaming, hardware.StreamingThreads);
    }
    private sealed class Recognition : ILocalRecognitionStream
    {
        public int Samples, Finishes, Calls;
        public Task<string> ProcessAsync(float[] samples, bool finished, CancellationToken token)
        {
            Samples += samples.Length;
            if (finished) { Finishes++; return Task.FromResult("very very good"); }
            return Task.FromResult(++Calls == 1 ? "very" : "very very");
        }
        public void Dispose() { }
    }
    private sealed class Provider : IGroqTranscriptionClient, ILocalStreamingProvider
    {
        public Recognition Stream = new(); public int Creates;
        public bool SupportsLocalStream(TranscriptionOptions options) => options.UseLocal;
        public Task<ILocalRecognitionStream> CreateLocalStreamAsync(TranscriptionOptions options, CancellationToken token)
        { Creates++; return Task.FromResult<ILocalRecognitionStream>(Stream); }
        public Task<string> TranscribeAsync(string key, string path, TranscriptionOptions options, CancellationToken token) => throw new Exception("Must not transcribe snapshots or complete audio.");
        public Task<ConnectionTestResult> TestConnectionAsync(string key, CancellationToken token) => throw new NotSupportedException();
        public Task<string> CleanupTranscriptAsync(string key, string text, string model, WritingCleanupTone tone, string? prompt, CancellationToken token) => throw new NotSupportedException();
        public Task<IntentResolution> InterpretIntentAsync(string key, string text, string model, CancellationToken token) => throw new NotSupportedException();
    }
}
