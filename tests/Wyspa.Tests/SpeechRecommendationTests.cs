using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class SpeechRecommendationTests
{
    private static LocalHardware Pc => new(8, 32UL << 30, "Test GPU") { CpuName = "Test CPU", GpuMemoryBytes = 12UL << 30, DiscreteGpu = true };
    private static AppSettings Settings => new() { LocalModelId = ZipformerModel.Id, StreamModeEnabled = true };
    private static SpeechPerformanceResult Result(string provider, string id, double seconds, int errors = 0) => new(provider, id, id, "test", 0, 1, seconds, .1, 150, errors, 25, 11);
    private static AppSettings Measured(params SpeechPerformanceResult[] rows)
    {
        var settings = Settings; settings.SpeechPerformance = new() { MeasuredAt = DateTimeOffset.UtcNow, HardwareFingerprint = Pc.Fingerprint, GpuEnabled = true, Results = rows.ToList() }; return settings;
    }
    [Fact]
    public void GpuHardwareSuggestsCompactWhisper_AndKeepsLatencyUnmeasured()
    {
        var rec = SpeechModelAdvisor.Hardware(Pc, Settings);
        Assert.Equal("tiny.en-q5_1", rec.ModelId); Assert.False(rec.Measured);
        Assert.Contains("unmeasured", rec.Explanation); Assert.Contains("Turbo", rec.Explanation);
    }
    [Fact]
    public void CpuStreamingAndLowMemoryPreferIncludedModel()
    {
        Assert.Equal(ZipformerModel.Id, SpeechModelAdvisor.Hardware(Pc with { Gpu = null }, Settings).ModelId);
        Assert.Equal(ZipformerModel.Id, SpeechModelAdvisor.Hardware(new(2, 2UL << 30, null), Settings).ModelId);
        var settings = Settings; settings.Language = "fr";
        Assert.Equal("tiny-q5_1", SpeechModelAdvisor.Hardware(Pc, settings).ModelId);
    }
    [Fact]
    public void SampleQualityPreventsFastBadTranscriptFromWinning()
    {
        var settings = Measured(Result("local", ZipformerModel.Id, .1, 7), Result("local", "tiny.en-q5_1", .3));
        var rec = SpeechModelAdvisor.Recommend(Pc, settings, _ => true);
        Assert.Equal("tiny.en-q5_1", rec.ModelId); Assert.Equal("local", rec.Provider); Assert.True(rec.Measured);
        Assert.Contains("no current successful", rec.Explanation);
    }
    [Fact]
    public void MeasuredLocalOrCloudWinnerUsesLatencyAndSampleQuality()
    {
        var localWins = Measured(Result("local", "tiny.en-q5_1", .3), Result("groq", "whisper-large-v3-turbo", 1));
        Assert.Equal("local", SpeechModelAdvisor.Recommend(Pc, localWins, _ => true).Provider);
        var cloudWins = Measured(Result("local", "tiny.en-q5_1", 3), Result("groq", "whisper-large-v3-turbo", .6));
        Assert.Equal("groq", SpeechModelAdvisor.Recommend(Pc, cloudWins, _ => true).Provider);
        var qualityWins = Measured(Result("local", ZipformerModel.Id, .1, 6), Result("groq", "whisper-large-v3-turbo", .8));
        Assert.Equal("groq", SpeechModelAdvisor.Recommend(Pc, qualityWins, _ => true).Provider);
    }
    [Fact]
    public void StaleHardwareGpuOrCloudSelectionDoesNotDriveProviderChoice()
    {
        var settings = Measured(Result("local", "tiny.en-q5_1", 3), Result("groq", "whisper-large-v3-turbo", .1));
        settings.LocalGpuEnabled = false;
        Assert.False(SpeechModelAdvisor.Recommend(Pc, settings, _ => true).Measured);
        settings.LocalGpuEnabled = true; settings.ModelId = "whisper-large-v3";
        Assert.Equal("local", SpeechModelAdvisor.Recommend(Pc, settings, _ => true).Provider);
        Assert.False(SpeechModelAdvisor.Current(settings.SpeechPerformance, Pc with { GpuDriverVersion = 100 }, true));
        Assert.False(SpeechModelAdvisor.Recommend(Pc, settings, _ => false).Measured);
        settings.ModelId = "whisper-large-v3-turbo"; settings.SpeechPerformance!.MeasuredAt = DateTimeOffset.UtcNow.AddDays(-2);
        Assert.Equal("local", SpeechModelAdvisor.Recommend(Pc, settings, _ => true).Provider);
    }
    [Fact]
    public async Task LocalOnlyTestNeverCallsCloud_UsesNeutralPrompts_AndKeepsErrorsOutOfSettingsText()
    {
        var localCalls = 0; var cloudCalls = 0; var rows = new List<SpeechPerformanceResult>();
        var runner = new SpeechPerformanceBenchmark((_, _) => Task.CompletedTask,
            (_, options, _) => { Assert.False(options.ApplyPersonalization); Assert.Null(options.Prompt); Assert.True(options.UseLocal); localCalls++; return Task.FromResult(new SpeechResult(SpeechPerformanceBenchmark.Reference, [])); },
            (_, _, _) => { cloudCalls++; throw new Exception("No cloud expected."); }, () => "CPU");
        await runner.RunMeasurementsAsync("fixture", 11, [ZipformerModel.Id, ZipformerModel.Id], null, rows.Add, null, default);
        Assert.Equal(4, localCalls); Assert.Equal(0, cloudCalls); Assert.Single(rows); Assert.Equal(0, rows[0].WordErrors);
        Assert.DoesNotContain(SpeechPerformanceBenchmark.Reference, System.Text.Json.JsonSerializer.Serialize(rows));
    }
    [Fact]
    public async Task CloudComparisonUsesSameFile_AndFailurePreservesLocalResults()
    {
        var rows = new List<SpeechPerformanceResult>(); var paths = new List<string>();
        var runner = new SpeechPerformanceBenchmark((_, _) => Task.CompletedTask,
            (path, _, _) => { paths.Add(path); return Task.FromResult(new SpeechResult(SpeechPerformanceBenchmark.Reference, [])); },
            (path, options, _) => { paths.Add(path); Assert.False(options.UseLocal); Assert.Equal("whisper-large-v3-turbo", options.ModelId); throw new HttpRequestException("offline"); }, () => "CPU");
        await runner.RunMeasurementsAsync("same-file", 11, [ZipformerModel.Id], "whisper-large-v3-turbo", rows.Add, null, default);
        Assert.Equal(2, rows.Count); Assert.Null(rows[0].Error); Assert.Contains("offline", rows[1].Error);
        Assert.All(paths, path => Assert.Equal("same-file", path));
    }
    [Fact]
    public async Task TamperedIncludedSampleIsRejectedBeforeAnyProviderCall()
    {
        var path = Path.GetTempFileName(); await File.WriteAllTextAsync(path, "not the included sample");
        try
        {
            var runner = new SpeechPerformanceBenchmark((_, _) => throw new Exception("must not run"), (_, _, _) => throw new Exception("must not run"), (_, _, _) => throw new Exception("must not run"), () => "CPU");
            await Assert.ThrowsAsync<InvalidDataException>(() => runner.RunAsync(path, [ZipformerModel.Id], "whisper-large-v3-turbo", _ => { }, null, default));
        }
        finally { File.Delete(path); }
    }
}
