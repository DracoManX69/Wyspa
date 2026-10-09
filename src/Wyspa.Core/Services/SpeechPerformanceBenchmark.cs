using System.Diagnostics;
using System.Security.Cryptography;
using Whisper.net.Wave;
using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

public sealed class SpeechPerformanceBenchmark(
    Func<string, CancellationToken, Task> prepare,
    Func<string, TranscriptionOptions, CancellationToken, Task<SpeechResult>> local,
    Func<string, TranscriptionOptions, CancellationToken, Task<string>> cloud,
    Func<string> engine,
    Func<string, bool, CancellationToken, Task>? prepareBackend = null,
    Func<string, bool>? gpuCapable = null,
    Func<string>? actualDevice = null,
    Func<double>? workerCpu = null,
    Func<double>? workerRam = null)
{
    public const string SampleVersion = "jfk-v3-ranking";
    public const string Reference = "And so my fellow Americans ask not what your country can do for you ask what you can do for your country";
    public const string SampleSha256 = "59dfb9a4acb36fe2a2affc14bacbee2920ff435cb13cc314a08c13f66ba7860e";

    public async Task RunAsync(string path, IReadOnlyList<string> localIds, string? groqModel,
        Action<SpeechPerformanceResult> completed, IProgress<string>? progress, CancellationToken token, bool compareGpu = false)
    {
        await using var input = File.OpenRead(path);
        if (!Convert.ToHexString(await SHA256.HashDataAsync(input, token)).Equals(SampleSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The included benchmark sample is missing or damaged. Repair the app before testing.");
        input.Position = 0;
        var parser = new WaveParser(input); await parser.InitializeAsync(token);
        var duration = parser.SamplesCount / (double)parser.SampleRate;
        await RunMeasurementsAsync(path, duration, localIds, groqModel, completed, progress, token, compareGpu);
    }
    // Separate measurement seam lets tests use controlled recognizers without microphone/network access.
    public async Task RunMeasurementsAsync(string path, double duration, IReadOnlyList<string> localIds, string? groqModel,
        Action<SpeechPerformanceResult> completed, IProgress<string>? progress, CancellationToken token, bool compareGpu = false)
    {
        foreach (var id in localIds.Distinct())
        {
            await TestAsync("local", id, false, path, duration, completed, progress, token);
            if (compareGpu && gpuCapable?.Invoke(id) == true)
                await TestAsync("local", id, true, path, duration, completed, progress, token);
        }
        if (groqModel is not null) await TestAsync("groq", groqModel, false, path, duration, completed, progress, token);
    }
    private async Task TestAsync(string provider, string id, bool gpu, string path, double duration,
        Action<SpeechPerformanceResult> completed, IProgress<string>? progress, CancellationToken token)
    {
        var name = provider == "local" ? LocalModelStore.Catalog.FirstOrDefault(m => m.Id == id)?.Name ?? id : "Groq " + id;
        var requested = provider == "local" ? gpu ? "GPU" : "CPU" : "Cloud";
        var label = name + " · " + requested;
        var words = VoicePersonalization.Words(Reference).Length;
        try
        {
            progress?.Report("Preparing " + label + "…");
            var preparation = Stopwatch.StartNew();
            if (provider == "local")
            {
                if (prepareBackend is not null) await prepareBackend(id, gpu, token);
                else await prepare(id, token);
            }
            var preparationSeconds = preparation.Elapsed.TotalSeconds;
            var elapsed = new List<double>(); var cpu = new List<double>(); var errors = new List<int>(); double memory = 0;
            for (var run = 0; run < 4; run++)
            {
                token.ThrowIfCancellationRequested();
                progress?.Report($"Testing {label} · {run + 1}/4…");
                using var process = Process.GetCurrentProcess();
                var before = process.TotalProcessorTime.TotalSeconds + (provider == "local" ? workerCpu?.Invoke() ?? 0 : 0); var clock = Stopwatch.StartNew();
                var options = new TranscriptionOptions(provider == "groq" ? id : "", "en", null, UseLocal: provider == "local", LocalModelId: id, ApplyPersonalization: false, LocalGpuOverride: provider == "local" ? gpu : null);
                var text = provider == "local" ? (await local(path, options, token)).Text : await cloud(path, options, token);
                elapsed.Add(clock.Elapsed.TotalSeconds);
                process.Refresh(); cpu.Add(Math.Max(0, process.TotalProcessorTime.TotalSeconds + (provider == "local" ? workerCpu?.Invoke() ?? 0 : 0) - before));
                memory = Math.Max(memory, process.WorkingSet64 / 1_000_000d + (provider == "local" ? workerRam?.Invoke() ?? 0 : 0));
                errors.Add(VoicePersonalization.Errors(Reference, text));
            }
            completed(new(provider, id, name, provider == "local" ? engine().Split(" · Last:")[0] : "Full HTTPS request; server compute is not separately reported",
                preparationSeconds, preparationSeconds + elapsed[0], elapsed.Skip(1).Average(), cpu.Skip(1).Average(), memory, errors.Max(), words, duration, Device: provider == "local" ? actualDevice?.Invoke() ?? requested : "Cloud", RequestedDevice: requested));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            completed(new(provider, id, name, "Unavailable", 0, 0, 0, 0, 0, words, words, duration, ex.Message, "Unavailable", requested));
        }
    }
}
