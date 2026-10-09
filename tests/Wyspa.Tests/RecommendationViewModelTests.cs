using Wyspa.App.ViewModels;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class RecommendationViewModelTests
{
    private static string Sample => Path.Combine(AppContext.BaseDirectory, "BenchmarkSample.wav");
    private static SpeechPerformanceBenchmark Runner(Func<string, CancellationToken, Task>? prepare = null, Func<string, TranscriptionOptions, CancellationToken, Task<string>>? cloud = null) => new(
        prepare ?? ((_, _) => Task.CompletedTask), (_, _, _) => Task.FromResult(new SpeechResult(SpeechPerformanceBenchmark.Reference, [])),
        cloud ?? ((_, _, _) => throw new Exception("Cloud should not be called")), () => "CPU");
    [Fact]
    public async Task GroqRequiresCompletedLocalComparison_ThenUsesOnlyWinner_AndPreservesDeviceMeasurements()
    {
        var settings = new AppSettings { LocalModelId = "tiny.en" }; var localCalls = 0; var cloudCalls = 0;
        var runner = new SpeechPerformanceBenchmark((_, _) => Task.CompletedTask,
            (_, options, _) => { localCalls++; return Task.FromResult(new SpeechResult(options.LocalModelId == "tiny.en" ? SpeechPerformanceBenchmark.Reference : "bad", [])); },
            (_, _, _) => { cloudCalls++; return Task.FromResult(SpeechPerformanceBenchmark.Reference); }, () => "CPU");
        var vm = new LocalRecommendationsViewModel(() => settings, () => new(4, 8UL << 30, null), id => id is "tiny.en" or "base.en",
            () => true, () => true, _ => Task.CompletedTask, () => Task.CompletedTask, (_, _) => Task.CompletedTask, runner, Sample);
        Assert.False(vm.CompareGroqCommand.CanExecute(null)); await vm.RunAsync(true); Assert.Equal(0, cloudCalls);
        await vm.RunAsync(false); Assert.True(vm.CompareGroqCommand.CanExecute(null));
        Assert.Equal("tiny.en", vm.TopLocal!.ModelId); Assert.True(vm.Results[0].IsWinner); Assert.False(vm.Results[1].IsWinner);
        Assert.True(vm.Results[0].Score > vm.Results[1].Score); var before = localCalls;
        await vm.RunAsync(true); Assert.Equal(before, localCalls); Assert.Equal(4, cloudCalls); Assert.Equal(2, vm.Results.Count);
        Assert.Equal("tiny.en", Assert.Single(vm.Results, r => r.Provider == "local").ModelId);
        Assert.Equal(3, settings.SpeechPerformance!.Results.Count); Assert.NotNull(settings.SpeechPerformance.CloudMeasuredAt);
        Assert.Single(vm.Results, r => r.IsWinner);
    }
    [Fact]
    public void ScoreStronglyPenalizesErrors_AndNeverRanksFailedTests()
    {
        SpeechPerformanceResult Row(double time, int errors, string? error = null) => new("local", "tiny.en", "Tiny", "CPU", 0, time, time, .1, 100, errors, 22, 11, error);
        Assert.True(Row(.7, 0).Score > Row(.2, 2).Score);
        Assert.True(Row(.2, 0).Score > Row(.7, 0).Score);
        Assert.Null(Row(0, 0, "failed").Score); Assert.Equal("—", Row(0, 0, "failed").ScoreDisplay);
    }
    [Fact]
    public async Task TestsEveryInstalledModel_AndCloudAddsExactlyOneResult()
    {
        var installed = new HashSet<string> { "tiny.en", "base.en", "small.en", "large-v3-turbo-q5_0", ZipformerModel.Id };
        var prepared = new List<string>();
        var settings = new AppSettings { LocalModelId = "small.en" };
        var vm = new LocalRecommendationsViewModel(() => settings, () => new(4, 8UL << 30, null), installed.Contains,
            () => true, () => true, _ => Task.CompletedTask, () => Task.CompletedTask,
            (_, _) => Task.CompletedTask, Runner((id, _) => { prepared.Add(id); return Task.CompletedTask; }, (_, _, _) => Task.FromResult(SpeechPerformanceBenchmark.Reference)), Sample);
        await vm.RunAsync(false);
        Assert.Equal(installed.Count, vm.Results.Count); Assert.Equal(installed.Order(), prepared.Order());
        Assert.Equal("small.en", prepared[0]); Assert.All(vm.Results, row => Assert.Equal("local", row.Provider));
        prepared.Clear(); await vm.RunAsync(true);
        Assert.Equal(2, vm.Results.Count); Assert.Equal(installed.Count + 1, settings.SpeechPerformance!.Results.Count); Assert.Single(vm.Results, row => row.Provider == "groq");
        Assert.Empty(prepared); Assert.False(vm.IsBusy);
    }
    [Fact]
    public async Task ApplyingAdviceIsExplicit_Cancellable_AndReleasesListening()
    {
        var settings = new AppSettings { LocalModelId = ZipformerModel.Id }; var reserved = false; var applied = false;
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new LocalRecommendationsViewModel(() => settings, () => new(4, 8UL << 30, null), _ => true,
            () => true, () => false, value => { reserved = value; return Task.CompletedTask; }, () => Task.CompletedTask,
            async (_, token) => { began.TrySetResult(); await Task.Delay(Timeout.Infinite, token); applied = true; }, Runner(), Sample);
        await vm.RunAsync(false); Assert.False(applied); Assert.False(settings.UseLocalTranscription);
        vm.ApplyCommand.Execute(null); await began.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(reserved); Assert.True(vm.IsBusy);
        await vm.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(reserved); Assert.False(vm.IsBusy); Assert.False(applied); Assert.False(settings.UseLocalTranscription);
    }
    [Fact]
    public async Task NoKeySkipsCloud_SavesLocalMeasurements_AndReleasesReservation()
    {
        var settings = new AppSettings { LocalModelId = ZipformerModel.Id }; var reserved = false; var saves = 0;
        var vm = new LocalRecommendationsViewModel(() => settings, () => new(4, 8UL << 30, null), id => id == ZipformerModel.Id,
            () => true, () => false, value => { reserved = value; return Task.CompletedTask; }, () => { saves++; return Task.CompletedTask; },
            (_, _) => Task.CompletedTask, Runner(), Sample);
        await vm.RunAsync(true); Assert.Null(settings.SpeechPerformance); Assert.False(reserved); Assert.Equal(0, saves);
        await vm.RunAsync(false); Assert.Single(vm.Results); Assert.Single(settings.SpeechPerformance!.Results);
        Assert.Equal(1, saves); Assert.False(reserved); Assert.False(vm.IsBusy);
    }
    [Fact]
    public async Task CancelAndShutdownStopPreparation_AndReleaseReservation()
    {
        var settings = new AppSettings { LocalModelId = ZipformerModel.Id }; var reserved = false;
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var vm = new LocalRecommendationsViewModel(() => settings, () => new(4, 8UL << 30, null), _ => true,
            () => true, () => false, value => { reserved = value; return Task.CompletedTask; }, () => Task.CompletedTask,
            (_, _) => Task.CompletedTask, Runner(async (_, token) => { began.TrySetResult(); await Task.Delay(Timeout.Infinite, token); }), Sample);
        vm.TestLocalCommand.Execute(null); await began.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(reserved); Assert.True(vm.IsBusy);
        await vm.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(reserved); Assert.False(vm.IsBusy); Assert.Null(settings.SpeechPerformance);
    }
    [Fact]
    public async Task CurrentRecordingReservationFailurePreservesPreviousResults()
    {
        var settings = new AppSettings { LocalModelId = ZipformerModel.Id, SpeechPerformance = new() { Results = [new("local", ZipformerModel.Id, "old", "CPU", 0, 1, 1, .1, 100, 0, 25, 11)] } };
        var vm = new LocalRecommendationsViewModel(() => settings, () => new(4, 8UL << 30, null), _ => true,
            () => true, () => false, _ => throw new InvalidOperationException("Finish recording"), () => throw new Exception("Must not save"),
            (_, _) => Task.CompletedTask, Runner(), Sample);
        vm.Load(); await vm.RunAsync(false);
        Assert.Equal("old", Assert.Single(vm.Results).Name); Assert.Contains("Finish recording", vm.Status); Assert.False(vm.IsBusy);
    }
}
