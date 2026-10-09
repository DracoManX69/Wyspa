using System.Net;
using System.Security.Cryptography;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class LocalModelTests
{
    [Fact]
    public async Task DownloadVerifiesAndPublishesAtomically()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            byte[] data = [1, 2, 3, 4];
            using var http = new HttpClient(new Handler(data));
            var store = new LocalModelStore(http, dir, [new("test", "test", data.Length, Convert.ToHexString(SHA256.HashData(data)))]);
            Assert.False(store.IsInstalled("test"));
            await store.DownloadAsync("test", new Progress<double>(), default);
            await store.VerifyAsync("test", default);
            Assert.True(store.IsInstalled("test")); Assert.Single(Directory.GetFiles(dir));
            store.Remove("test"); Assert.False(store.IsInstalled("test"));
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task InvalidDownloadNeverReplacesInstalledModel_AndCleansPartial()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var http = new HttpClient(new Handler([9, 9]));
            var store = new LocalModelStore(http, dir, [new("test", "test", 2, new string('0', 64))]);
            File.WriteAllBytes(store.ModelPath("test"), [1, 2]);
            await Assert.ThrowsAsync<InvalidDataException>(() => store.DownloadAsync("test", new Progress<double>(), default));
            Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(store.ModelPath("test")));
            Assert.Single(Directory.GetFiles(dir));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.VerifyAsync("test", default));
            Assert.Throws<InvalidOperationException>(() => store.ModelPath("../escape"));
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task CancelledDownloadLeavesNoInstalledOrPartialFile()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var http = new HttpClient(new Handler([1]));
            var store = new LocalModelStore(http, dir);
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.DownloadAsync("tiny", new Progress<double>(), cancellation.Token));
            Assert.Empty(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task LocalSelectionPersists_AndOlderSettingsKeepGroq()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var path = Path.Combine(dir, "settings.json"); var settings = new JsonSettingsService(path);
            File.WriteAllText(path, "{}"); Assert.False((await settings.LoadAsync(default)).UseLocalTranscription);
            await settings.SaveAsync(new AppSettings { UseLocalTranscription = true, LocalModelId = "tiny.en" }, default);
            var loaded = await settings.LoadAsync(default); Assert.True(loaded.UseLocalTranscription); Assert.Equal("tiny.en", loaded.LocalModelId);
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task LocalRouterNeverFallsBackToCloud_WhenModelMissing()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            using var http = new HttpClient(new RejectNetwork());
            using var local = new LocalTranscriptionClient(new LocalModelStore(http, dir), "unused");
            var router = new TranscriptionRouter(new GroqTranscriptionClient(http), local, () => true);
            await Assert.ThrowsAsync<InvalidOperationException>(() => router.TranscribeAsync("", "unused", new("cloud", null, null, UseLocal: true), default));
            Assert.Equal("private", await router.CleanupTranscriptAsync("", "private", "cloud", WritingCleanupTone.Casual, null, default));
            Assert.Equal("private", (await router.ProofreadStreamAsync("", "private", "cloud", default)).Text);
            var notes = new NoteTranscriptionRouter(new GroqNoteIntelligence(http), local, () => true);
            await Assert.ThrowsAsync<InvalidOperationException>(() => notes.SummariseAsync("", "private", "cloud", default));
        }
        finally { Directory.Delete(dir, true); }
    }
    [Fact]
    public async Task TransientDownloadFailureRetriesAndVerifies()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            byte[] data = [1, 2, 3]; var handler = new RetryHandler(data);
            using var http = new HttpClient(handler);
            var store = new LocalModelStore(http, dir, [new("test", "test", data.Length, Convert.ToHexString(SHA256.HashData(data)))]);
            await store.DownloadAsync("test", new Progress<double>(), default);
            Assert.Equal(2, handler.Calls); await store.VerifyAsync("test", default); Assert.Single(Directory.GetFiles(dir));
        }
        finally { Directory.Delete(dir, true); }
    }
    private sealed class RetryHandler(byte[] data) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            => Task.FromResult(++Calls == 1 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(data) });
    }
    private sealed class RejectNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new Exception("Local mode attempted a network request");
    }
    private sealed class Handler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }); }
    }
}
