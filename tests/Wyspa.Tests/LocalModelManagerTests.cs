using System.Net;
using System.Security.Cryptography;
using Wyspa.App.ViewModels;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class LocalModelManagerTests
{
    private static readonly byte[] Data = [1, 2, 3, 4];
    private sealed class Fixture : IDisposable
    {
        public readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        private readonly HttpClient _http;
        private readonly LocalTranscriptionClient _client;
        public LocalModelStore Store { get; }
        public LocalModelsViewModel Vm { get; }
        public AppSettings Settings { get; } = new() { UseLocalTranscription = true, LocalModelId = "tiny.en" };
        public int Saves;
        public Fixture(HttpMessageHandler handler)
        {
            _http = new(handler);
            Store = new(_http, DirectoryPath, new[] { "tiny.en", "base.en", "small.en", "tiny", "base" }
                .Select(id => new LocalModel(id, id, Data.Length, Convert.ToHexString(SHA256.HashData(Data)))).ToArray());
            _client = new(Store, "unused");
            Vm = new(Store, _client, () => Settings, () => { Interlocked.Increment(ref Saves); return Task.CompletedTask; });
        }
        public void Install(string id) { Directory.CreateDirectory(DirectoryPath); File.WriteAllBytes(Store.ModelPath(id), Data); Vm.Refresh(); }
        public void Dispose() { _client.Dispose(); _http.Dispose(); if (Directory.Exists(DirectoryPath)) Directory.Delete(DirectoryPath, true); }
    }
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private static HttpResponseMessage Response(byte[]? data = null) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(data ?? Data) };
    private static Handler Good() => new((_, _) => Task.FromResult(Response()));

    [Fact]
    public void OffDisablesManagerAndGpu_ButCanTurnOnWithNoInstalledModels()
    {
        using var f = new Fixture(Good());
        f.Vm.Enabled = false;
        f.Vm.Items[0].IsSelected = true;
        Assert.False(f.Vm.CanConfigure); Assert.False(f.Vm.InstallSelectedCommand.CanExecute(null));
        Assert.False(f.Vm.Items[0].ManageCommand.CanExecute(null)); Assert.False(f.Vm.CanUseGpu);
        Assert.True(f.Vm.CanEnable); f.Vm.Enabled = true;
        Assert.True(f.Vm.CanConfigure); Assert.False(f.Vm.Installed);
        Assert.Equal(5, f.Vm.Items.Count);
        if (!f.Vm.GpuAvailable)
        {
            f.Settings.LocalGpuEnabled = false; f.Vm.GpuEnabled = true;
            Assert.False(f.Vm.GpuEnabled); Assert.False(f.Vm.CanUseGpu);
        }
    }
    [Fact]
    public async Task BatchDownloadsHaveSeparateProgress_TwoAtOnce_AndKeepInstalledSelections()
    {
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var live = 0; var peak = 0; var requests = 0;
        using var f = new Fixture(new Handler(async (_, token) =>
        {
            Interlocked.Increment(ref requests); var count = Interlocked.Increment(ref live);
            peak = Math.Max(peak, count); if (count == 2) began.TrySetResult();
            try { await release.Task.WaitAsync(token); return Response(); }
            finally { Interlocked.Decrement(ref live); }
        }));
        f.Install("tiny.en");
        foreach (var row in f.Vm.Items) row.IsSelected = true;
        f.Vm.InstallSelectedCommand.Execute(null); await began.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(f.Vm.IsBusy); Assert.False(f.Vm.CanEnable);
        Assert.Equal(2, f.Vm.Items.Count(row => row.IsWorking)); Assert.Equal(2, f.Vm.Items.Count(row => row.State == "Queued"));
        release.SetResult();
        // Await without cancelling: completed state is signalled by IsBusy notifications.
        await WaitForIdle(f.Vm);
        Assert.Equal(4, requests); Assert.Equal(2, peak);
        Assert.All(f.Vm.Items, row => Assert.True(row.Installed));
        Assert.All(f.Vm.Items.Skip(1), row => { Assert.Equal(100, row.Progress); Assert.False(row.IsSelected); Assert.Equal("Remove", row.ActionLabel); });
        Assert.True(f.Vm.Items[0].IsSelected); Assert.Equal("tiny.en", f.Vm.SelectedModelId);
    }
    [Fact]
    public async Task FailedDownloadDoesNotStopOthers_AndRetainsItsSelectionForRetry()
    {
        using var f = new Fixture(new Handler((request, _) => Task.FromResult(Response(request.RequestUri!.AbsolutePath.Contains("base.en") ? [9] : null))));
        f.Vm.Items[0].IsSelected = f.Vm.Items[1].IsSelected = true;
        f.Vm.InstallSelectedCommand.Execute(null); await WaitForIdle(f.Vm);
        Assert.True(f.Vm.Items[0].Installed); Assert.False(f.Vm.Items[0].IsSelected);
        Assert.False(f.Vm.Items[1].Installed); Assert.True(f.Vm.Items[1].IsSelected);
        Assert.Contains("Failed", f.Vm.Items[1].State); Assert.Contains("1 failed", f.Vm.Status);
        Assert.Empty(Directory.GetFiles(f.DirectoryPath, "*.partial"));
    }
    [Fact]
    public async Task ShutdownCancelsActiveAndQueuedDownloads_AndCleansTemporaryFiles()
    {
        var began = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var f = new Fixture(new Handler(async (_, token) => { began.TrySetResult(); await Task.Delay(Timeout.Infinite, token); return Response(); }));
        foreach (var row in f.Vm.Items) row.IsSelected = true;
        f.Vm.InstallSelectedCommand.Execute(null); await began.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await f.Vm.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(f.Vm.IsBusy); Assert.All(f.Vm.Items, row => { Assert.False(row.IsWorking); Assert.True(row.IsSelected); Assert.False(row.Installed); });
        Assert.Contains("cancelled", f.Vm.Status); Assert.Empty(Directory.GetFiles(f.DirectoryPath, "*.partial"));
    }
    [Fact]
    public async Task BulkRemovalReservesListening_ChoosesRemainingModel_AndNeverSwitchesToCloud()
    {
        using var f = new Fixture(Good()); f.Install("tiny.en"); f.Install("base.en"); f.Install("small.en");
        var reservations = new List<bool>(); f.Vm.ReserveRemovalAsync = value => { reservations.Add(value); return Task.CompletedTask; };
        f.Vm.Items[0].IsSelected = f.Vm.Items[1].IsSelected = f.Vm.Items[3].IsSelected = true;
        f.Vm.RemoveSelectedCommand.Execute(null); await WaitForIdle(f.Vm);
        Assert.Equal(new[] { true, false }, reservations); Assert.Equal("small.en", f.Vm.SelectedModelId);
        Assert.False(f.Vm.Items[0].Installed); Assert.False(f.Vm.Items[1].Installed); Assert.True(f.Vm.Items[2].Installed);
        Assert.True(f.Vm.Items[3].IsSelected); Assert.True(f.Settings.UseLocalTranscription);
        f.Vm.Items[2].ManageCommand.Execute(null); await WaitForIdle(f.Vm);
        Assert.False(f.Vm.Installed); Assert.True(f.Settings.UseLocalTranscription); Assert.True(f.Vm.CanConfigure);
    }
    [Fact]
    public async Task BusyCaptureRefusesRemoval_WithoutDeletingOrChangingSelection()
    {
        using var f = new Fixture(Good()); f.Install("tiny.en");
        f.Vm.ReserveRemovalAsync = _ => throw new InvalidOperationException("Finish recording");
        f.Vm.Items[0].ManageCommand.Execute(null); await WaitForIdle(f.Vm);
        Assert.True(f.Vm.Installed); Assert.Equal("tiny.en", f.Vm.SelectedModelId); Assert.Contains("Finish recording", f.Vm.Status);
    }
    [Fact]
    public async Task UseInstalledModelPersistsChoiceWithoutRetrainingOrCloud()
    {
        using var f = new Fixture(Good()); f.Install("base.en");
        Assert.False(f.Vm.Items[0].UseCommand.CanExecute(null));
        f.Vm.Items[1].UseCommand.Execute(null);
        Assert.Equal("base.en", f.Vm.SelectedModelId); Assert.Equal(1, f.Saves); Assert.Equal("Active", f.Vm.Items[1].UseLabel);
        Assert.True(f.Settings.UseLocalTranscription); Assert.Null(f.Settings.LocalVoiceProfile);
        await Task.CompletedTask;
    }
    private static async Task WaitForIdle(LocalModelsViewModel vm)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (vm.IsBusy && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.False(vm.IsBusy);
    }
}
