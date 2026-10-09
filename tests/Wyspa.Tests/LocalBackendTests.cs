using System.Text.Json;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class LocalBackendTests
{
    private static readonly LocalHardware Pc = new(8, 16UL << 30, "NVIDIA test GPU");
    private static LocalModel Model(string id = "tiny.en") => LocalModelStore.Catalog.Single(m => m.Id == id);
    private static SpeechPerformanceResult Row(string device, double seconds, int errors = 0, string? actual = null, string? failure = null) =>
        new("local", "tiny.en", "Tiny", "test", 0, seconds, seconds, .1, 100, errors, 25, 11, failure, actual ?? device, device);
    private static SpeechPerformanceReport Report(params SpeechPerformanceResult[] rows) => new() { HardwareFingerprint = Pc.Fingerprint, GpuEnabled = true, Results = rows.ToList() };
    [Fact]
    public void GpuPermissionAndCapabilitiesRespectEngineAndHardware()
    {
        Assert.True(LocalBackendPolicy.SupportsGpu(Model(), Pc));
        Assert.True(LocalBackendPolicy.SupportsGpu(Model("faster-whisper-tiny.en"), Pc));
        Assert.False(LocalBackendPolicy.SupportsGpu(Model("faster-whisper-tiny.en"), Pc with { Gpu = "Intel GPU" }));
        Assert.False(LocalBackendPolicy.SupportsGpu(Model(), Pc with { Gpu = null }));
        foreach (var id in new[] { "parakeet-redux-onnx", "sensevoice-small-int8", ZipformerModel.Id, "parakeet-v3-int8" })
            Assert.False(LocalBackendPolicy.SupportsGpu(Model(id), Pc));
        Assert.False(LocalBackendPolicy.UseGpu(Model(), Pc, false, null));
        Assert.False(LocalBackendPolicy.UseGpu(Model(), Pc, true, null, false));
    }
    [Theory]
    [InlineData(1, .5, 0, true)]
    [InlineData(1, 1.2, 0, false)]
    [InlineData(1, .95, 0, false)]
    [InlineData(1, .5, 4, false)]
    public void UsesFasterComparableBackend_AndCpuForNearTies(double cpu, double gpu, int gpuErrors, bool expected) =>
        Assert.Equal(expected, LocalBackendPolicy.UseGpu(Model(), Pc, true, Report(Row("CPU", cpu), Row("GPU", gpu, gpuErrors))));
    [Fact]
    public void FailedOrFallbackGpuMeasurementChoosesCpu_StaleDataIsIgnored()
    {
        Assert.False(LocalBackendPolicy.UseGpu(Model(), Pc, true, Report(Row("CPU", 1), Row("GPU", .1, actual: "CPU"))));
        Assert.False(LocalBackendPolicy.UseGpu(Model(), Pc, true, Report(Row("CPU", 1), Row("GPU", 0, failure: "GPU failed"))));
        var stale = Report(Row("CPU", 1), Row("GPU", 2)); stale.HardwareFingerprint = "other PC";
        Assert.True(LocalBackendPolicy.UseGpu(Model(), Pc, true, stale));
        stale.HardwareFingerprint = Pc.Fingerprint; stale.SampleVersion = "jfk-v1";
        Assert.True(LocalBackendPolicy.UseGpu(Model(), Pc, true, stale));
        Assert.Equal("CPU fallback", Row("GPU", .1, actual: "CPU").DeviceDisplay);
    }
    [Fact]
    public async Task BenchmarkSeparatesCpuGpu_ForSupportedModelsOnly_AndAddsCloudOnce()
    {
        var prepared = new List<(string Id, bool Gpu)>(); var requests = new List<TranscriptionOptions>();
        var device = "CPU";
        var runner = new SpeechPerformanceBenchmark((_, _) => throw new Exception("Use explicit backend preparation"),
            (_, options, _) => { requests.Add(options); return Task.FromResult(new SpeechResult(SpeechPerformanceBenchmark.Reference, [])); },
            (_, _, _) => Task.FromResult(SpeechPerformanceBenchmark.Reference), () => "test",
            (id, gpu, _) => { prepared.Add((id, gpu)); device = gpu ? "GPU" : "CPU"; return Task.CompletedTask; },
            id => id == "tiny.en", () => device);
        var results = new List<SpeechPerformanceResult>();
        await runner.RunMeasurementsAsync("sample", 11, ["tiny.en", ZipformerModel.Id], "cloud", results.Add, null, default, true);
        Assert.Equal(new[] { ("tiny.en", false), ("tiny.en", true), (ZipformerModel.Id, false) }, prepared);
        Assert.Equal(new[] { "CPU", "GPU", "CPU", "Cloud" }, results.Select(r => r.DeviceDisplay));
        Assert.Equal(12, requests.Count); Assert.Equal(4, requests.Count(r => r.LocalGpuOverride == true));
        Assert.Single(results, row => row.Provider == "groq");
    }
    [Fact]
    public async Task GpuFailureIsRetainedWithoutStoppingOtherModels_AndFallbackIsLabelled()
    {
        var runner = new SpeechPerformanceBenchmark((_, _) => Task.CompletedTask,
            (_, _, _) => Task.FromResult(new SpeechResult(SpeechPerformanceBenchmark.Reference, [])), (_, _, _) => throw new Exception(), () => "test",
            (_, gpu, _) => gpu ? throw new InvalidOperationException("missing GPU runtime") : Task.CompletedTask, _ => true, () => "CPU");
        var rows = new List<SpeechPerformanceResult>();
        await runner.RunMeasurementsAsync("sample", 11, ["tiny.en", "base.en"], null, rows.Add, null, default, true);
        Assert.Equal(4, rows.Count); Assert.Equal(2, rows.Count(r => r.Error is null));
        Assert.All(rows.Where(r => r.RequestedDevice == "GPU"), row => Assert.Contains("missing GPU", row.Error));
        var fallback = new SpeechPerformanceBenchmark((_, _) => Task.CompletedTask,
            (_, _, _) => Task.FromResult(new SpeechResult(SpeechPerformanceBenchmark.Reference, [])), (_, _, _) => throw new Exception(), () => "CPU fallback",
            (_, _, _) => Task.CompletedTask, _ => true, () => "CPU");
        rows.Clear(); await fallback.RunMeasurementsAsync("sample", 11, ["tiny.en"], null, rows.Add, null, default, true);
        Assert.Equal("CPU fallback", rows[1].DeviceDisplay);
    }
    [Fact]
    public void ModelInfoAndManagedDownloadsArePinnedAndUnderModelCap()
    {
        Assert.Equal(4, ExtraSpeechModels.Catalog.Count);
        Assert.All(ExtraSpeechModels.Catalog, model =>
        {
            Assert.InRange(model.Bytes, 1, 1_500_000_000); Assert.Equal(model.Bytes, model.Bundle!.Files.Sum(f => f.Bytes));
            Assert.True(Uri.TryCreate(model.InfoUrl, UriKind.Absolute, out var uri)); Assert.Equal("https", uri!.Scheme);
            Assert.All(model.Bundle.Files, file => Assert.Equal(64, file.Hash.Length));
        });
        Assert.InRange(ManagedSpeechRuntime.Files.Where(f => !f.Gpu).Sum(f => f.Bytes), 1, 100_000_000);
        Assert.All(ManagedSpeechRuntime.Files, file => { Assert.Equal(64, file.Hash.Length); Assert.StartsWith("https://", file.Url); Assert.True(file.Bytes > 0); });
        Assert.Equal(2, ManagedSpeechRuntime.Files.Count(f => f.Gpu));
        Assert.Contains("large-v3", Model("large-v3-q5_0").InfoUrl);
        Assert.DoesNotContain("q5", Model("large-v3-q5_0").InfoUrl);
    }
    [Fact]
    public void OldSavedMeasurementsLoadWithoutInventingDeviceEvidence()
    {
        var row = JsonSerializer.Deserialize<SpeechPerformanceResult>("""{"Provider":"local","ModelId":"tiny.en","Name":"Tiny","Engine":"CPU","PreparationSeconds":0,"FirstSeconds":1,"TypicalSeconds":1,"CpuSeconds":0,"AppRamMb":100,"WordErrors":0,"ReferenceWords":25,"AudioSeconds":11}""");
        Assert.NotNull(row); Assert.Equal("Not recorded", row.DeviceDisplay); Assert.Null(row.RequestedDevice);
    }
}
