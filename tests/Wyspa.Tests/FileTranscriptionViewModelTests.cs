using Wyspa.App.ViewModels;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;

namespace Wyspa.Tests;

public sealed class FileTranscriptionViewModelTests
{
    [Fact]
    public async Task TranscribesPartsInOrder_AndSkipsCompletedFilesOnRetry()
    {
        var client = new FakeClient();
        var settings = new AppSettings { ModelId = "whisper-large-v3-turbo", Language = "en", CustomPrompt = "Wyspa", GroqWritingCleanupEnabled = true };
        var preparation = new FakePreparation();
        var viewModel = new FileTranscriptionViewModel(preparation, client, new FakeSecrets("test-key"), () => settings);
        viewModel.AddFiles(["first.wav", "second.wav", "first.wav"]);
        Assert.Equal(2, viewModel.Files.Count);
        await viewModel.StartAsync();
        Assert.Equal(4, client.Calls);
        Assert.All(viewModel.Files, item => { Assert.True(item.IsComplete); Assert.Equal("part one" + Environment.NewLine + Environment.NewLine + "part two", item.Transcript); });
        Assert.Equal("en", client.Options!.Language);
        Assert.Equal("Wyspa", client.Options.Prompt);
        await viewModel.StartAsync();
        Assert.Equal(4, client.Calls);
        Assert.All(preparation.Directories, directory => Assert.False(Directory.Exists(directory)));
    }

    [Fact]
    public async Task Cancellation_KeepsPartialResult_CleansTemp_AndStopsQueue()
    {
        var preparation = new FakePreparation();
        var client = new FakeClient { BlockSecondPart = true };
        var vm = new FileTranscriptionViewModel(preparation, client, new FakeSecrets("test-key"), () => new AppSettings());
        vm.AddFiles(["first.wav", "second.wav"]);
        var task = vm.StartAsync();
        await client.SecondPartStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        vm.Cancel();
        await task;
        Assert.Equal(2, client.Calls);
        Assert.False(vm.IsBusy);
        Assert.Equal("part one", vm.Files[0].Transcript);
        Assert.False(vm.Files[0].IsComplete);
        Assert.Equal("Ready", vm.Files[1].Status);
        Assert.All(preparation.Directories, directory => Assert.False(Directory.Exists(directory)));
    }

    [Fact]
    public async Task MissingKey_DoesNotPrepareOrUpload()
    {
        var preparation = new FakePreparation();
        var client = new FakeClient();
        var vm = new FileTranscriptionViewModel(preparation, client, new FakeSecrets(null), () => new AppSettings());
        vm.AddFiles(["file.wav"]);
        await vm.StartAsync();
        Assert.Empty(preparation.Directories);
        Assert.Equal(0, client.Calls);
        Assert.Contains("API key", vm.Status);
        Assert.False(vm.IsBusy);
    }

    private sealed class FakePreparation : IAudioFilePreparationService
    {
        public List<string> Directories { get; } = [];
        public Task<PreparedAudio> PrepareAsync(string path, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            var directory = Directory.CreateTempSubdirectory("wyspa-queue-test-").FullName;
            Directories.Add(directory);
            var paths = new[] { Path.Combine(directory, "one.flac"), Path.Combine(directory, "two.flac") };
            foreach (var file in paths) File.WriteAllBytes(file, [1]);
            return Task.FromResult(new PreparedAudio(paths, 10, 2, "Lossless", directory));
        }
    }

    private sealed class FakeSecrets(string? key) : ISecretStore
    {
        public Task<string?> GetApiKeyAsync(CancellationToken cancellationToken) => Task.FromResult(key);
        public Task SaveApiKeyAsync(string apiKey, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task RemoveApiKeyAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeClient : IGroqTranscriptionClient
    {
        public int Calls { get; private set; }
        public bool BlockSecondPart { get; init; }
        public TaskCompletionSource SecondPartStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TranscriptionOptions? Options { get; private set; }
        public async Task<string> TranscribeAsync(string apiKey, string audioFilePath, TranscriptionOptions options, CancellationToken cancellationToken)
        {
            Calls++; Options = options;
            if (BlockSecondPart && Calls == 2)
            {
                SecondPartStarted.SetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            return Calls % 2 == 1 ? "part one" : "part two";
        }
        // File transcription must never call the extra token-consuming models.
        public Task<string> CleanupTranscriptAsync(string apiKey, string transcript, string modelId, WritingCleanupTone tone, string? prompt, CancellationToken cancellationToken) => throw new Xunit.Sdk.XunitException("Unexpected rewrite request");
        public Task<IntentResolution> InterpretIntentAsync(string apiKey, string transcript, string modelId, CancellationToken cancellationToken) => throw new Xunit.Sdk.XunitException("Unexpected intent request");
        public Task<ConnectionTestResult> TestConnectionAsync(string apiKey, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
}
