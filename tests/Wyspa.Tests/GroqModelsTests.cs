using System.Net;
using System.Text.Json;
using Wyspa.App.ViewModels;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class GroqModelsTests
{
    [Fact]
    public async Task RefreshFetchesActiveModelsAndSeparatesSpeechTextAndSpecialistTasks()
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.groq.com/openai/v1/models", request.RequestUri!.AbsoluteUri);
            Assert.Equal("test-key", request.Headers.Authorization!.Parameter);
            return Json("""{"data":[{"id":"whisper-large-v3"},{"id":"whisper-large-v3-turbo"},{"id":"whisper-retired","active":false},{"id":"openai/gpt-oss-20b"},{"id":"llama-3.1-8b-instant"},{"id":"openai/gpt-oss-safeguard-20b"},{"id":"meta-llama/llama-prompt-guard-2-22m"},{"id":"canopylabs/orpheus-v1-english"},{"id":"playai-tts"},{"id":"unknown-task"},{"id":"whisper-large-v3"},{"id":42},null]}""");
        }));
        var settings = new AppSettings();
        var vm = new GroqModelsViewModel(new GroqTranscriptionClient(http), () => settings);
        await vm.RefreshAsync("test-key");
        Assert.Equal(new[] { "whisper-large-v3", "whisper-large-v3-turbo" }, vm.TranscriptionModels);
        Assert.Equal(new[] { "llama-3.1-8b-instant", "openai/gpt-oss-20b" }, vm.TextModels);
        vm.TranscriptionModel = "openai/gpt-oss-20b";
        Assert.Equal("whisper-large-v3-turbo", settings.ModelId);
        vm.TranscriptionModel = "whisper-large-v3";
        vm.SummaryModel = "llama-3.1-8b-instant";
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        try
        {
            var store = new JsonSettingsService(path);
            await store.SaveAsync(settings, default);
            var reopened = await store.LoadAsync(default);
            Assert.Equal("whisper-large-v3", reopened.ModelId);
            Assert.Equal("llama-3.1-8b-instant", reopened.SummaryModelId);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"data\":null}")]
    public async Task InvalidResponseIsRecoverableAndDoesNotReplaceSavedSettings(string response)
    {
        using var http = new HttpClient(new Handler(_ => Json(response)));
        var settings = new AppSettings();
        var vm = new GroqModelsViewModel(new GroqTranscriptionClient(http), () => settings);
        Assert.False((await vm.RefreshAsync("test-key")).Success);
        Assert.Contains("unreadable", vm.Status);
        Assert.False(vm.IsRefreshing);
        Assert.Equal("whisper-large-v3-turbo", settings.ModelId);
    }

    [Fact]
    public async Task RefreshUpdatesChoicesWithoutSilentlyReplacingRetiredSelectionAndKeepsListOnFailure()
    {
        var response = Json("""{"data":[{"id":"whisper-large-v3-turbo"},{"id":"llama-3.1-8b-instant"}]}""");
        using var http = new HttpClient(new Handler(_ => response));
        var settings = new AppSettings();
        var vm = new GroqModelsViewModel(new GroqTranscriptionClient(http), () => settings);
        await vm.RefreshAsync("key");
        response = Json("""{"data":[{"id":"whisper-large-v3"},{"id":"openai/gpt-oss-120b"}]}""");
        await vm.RefreshAsync("key");
        Assert.Equal("whisper-large-v3", Assert.Single(vm.TranscriptionModels));
        vm.TranscriptionModel = null!;
        Assert.Equal("whisper-large-v3-turbo", settings.ModelId);
        Assert.Contains("not available", vm.TranscriptionSelection);
        response = new HttpResponseMessage(HttpStatusCode.Unauthorized);
        await vm.RefreshAsync("key");
        Assert.Contains("rejected", vm.Status);
        Assert.Equal("whisper-large-v3", Assert.Single(vm.TranscriptionModels));
        vm.Clear(); Assert.Empty(vm.TranscriptionModels); Assert.Empty(vm.TextModels);
        Assert.Equal("whisper-large-v3-turbo", settings.ModelId);
    }

    [Fact]
    public async Task MissingKeyDoesNotRequestModelsAndNetworkFailureKeepsSettings()
    {
        var count = 0;
        using var http = new HttpClient(new Handler(_ => { count++; throw new HttpRequestException(); }));
        var settings = new AppSettings();
        var vm = new GroqModelsViewModel(new GroqTranscriptionClient(http), () => settings);
        await vm.RefreshAsync(""); Assert.Equal(0, count);
        await vm.RefreshAsync("key"); Assert.Equal(1, count);
        Assert.Contains("network", vm.Status); Assert.True(vm.CanRefresh);
    }

    [Fact]
    public async Task EmptySuccessfulListDoesNotEraseAnySavedModel()
    {
        using var http = new HttpClient(new Handler(_ => Json("""{"data":[]}""")));
        var settings = new AppSettings(); var before = JsonSerializer.Serialize(settings);
        var vm = new GroqModelsViewModel(new GroqTranscriptionClient(http), () => settings);
        Assert.True((await vm.RefreshAsync("key")).Success);
        Assert.Empty(vm.TranscriptionModels); Assert.Empty(vm.TextModels);
        Assert.Equal(before, JsonSerializer.Serialize(settings));
        Assert.Contains("No compatible models", vm.Status);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(handler(request));
    }
}
