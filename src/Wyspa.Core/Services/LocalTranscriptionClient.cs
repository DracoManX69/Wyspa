using System.Diagnostics;
using Whisper.net;
using Whisper.net.LibraryLoader;
using Whisper.net.Wave;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

public sealed class LocalTranscriptionClient(LocalModelStore models, string ffmpeg, Func<LocalVoiceProfile?>? profile = null,
    Func<bool>? gpuEnabled = null, Func<SpeechPerformanceReport?>? performance = null) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool _disposed;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private ManagedSpeechWorker? _worker;
    private string? _workerId;
    private bool _workerGpu, _workerActualGpu;
    public string ActualDevice { get; private set; } = "CPU";
    public double WorkerCpuSeconds => _worker?.CpuSeconds ?? 0;
    public double WorkerRamMb => _worker?.RamMb ?? 0;
    public bool SupportsGpu(string id) => LocalBackendPolicy.SupportsGpu(models.Find(id), Hardware);
    private void ReleaseWorker() { _worker?.Dispose(); _worker = null; _workerId = null; }
    private WhisperFactory? _factory;
    private ZipformerEngine? _zipformer;
    private string? _zipId, _offlineId;
    private OfflineSpeechEngine? _offline;
    private string? _loaded;
    private bool _loadedGpu;
    private static readonly Lazy<LocalHardware> Machine = new(LocalHardware.Detect);
    public LocalHardware Hardware => Machine.Value;
    public string RuntimeStatus { get; private set; } = "Local engine loads in the background when needed.";
    public event EventHandler? RuntimeChanged;
    private void Report(string message) { RuntimeStatus = message; RuntimeChanged?.Invoke(this, EventArgs.Empty); }

    private async Task EnsureEngineAsync(string id, CancellationToken token, bool? gpuOverride = null)
    {
        var model = models.Find(id);
        var allowed = gpuOverride.HasValue ? gpuOverride.Value : gpuEnabled?.Invoke() ?? true;
        var gpu = LocalBackendPolicy.UseGpu(model, Hardware, allowed, performance?.Invoke(), gpuOverride);
        if (model.Bundle?.Engine is "FasterWhisper" or "Redux")
        {
            _factory?.Dispose(); _factory = null; _loaded = null;
            _offline?.Dispose(); _offline = null; _offlineId = null;
            if (_zipformer?.InUse == false) { _zipformer.Dispose(); _zipformer = null; _zipId = null; }
            if (_workerId != id || _workerGpu != gpu || _worker?.Alive != true || gpuOverride.HasValue && _workerActualGpu != gpu)
            {
                ReleaseWorker(); Report("Preparing " + model.Name + "…");
                await models.VerifyAsync(id, token);
                var requestedWorkerGpu = gpu;
                try { _worker = await ManagedSpeechWorker.StartAsync(model, models.ModelPath(id), gpu, Hardware.WhisperThreads, new Progress<string>(Report), token); }
                catch (Exception ex) when (gpu && gpuOverride is null && ex is not OperationCanceledException)
                { _worker = await ManagedSpeechWorker.StartAsync(model, models.ModelPath(id), false, Hardware.WhisperThreads, null, token); gpu = false; }
                _workerId = id; _workerGpu = requestedWorkerGpu; _workerActualGpu = gpu;
            }
            ActualDevice = _workerActualGpu ? "GPU" : "CPU";
            Report(model.Bundle.Engine + $" · {(_workerActualGpu ? "CUDA GPU" : "CPU")} · {Hardware.WhisperThreads} threads · bounded-window transcription");
            return;
        }
        ReleaseWorker();
        if (model.Continuous)
        {
            ActualDevice = "CPU";
            _offline?.Dispose(); _offline = null; _offlineId = null;
            _factory?.Dispose(); _factory = null; _loaded = null;
            if (_zipformer is null || _zipId != id)
            {
                Report("Preparing lightweight speech recognition in the background…");
                await models.VerifyAsync(id, token);
                _zipformer?.Dispose(); _zipformer = null; _zipId = null;
                _zipformer = await LocalBackgroundWork.Run(() => new ZipformerEngine(models.ModelPath(id), Hardware.StreamingThreads, models.Files(id)), token);
                _zipId = id;
            }
            Report($"Zipformer · CPU · {Hardware.StreamingThreads} threads · continuous streaming");
            return;
        }
        if (model.Bundle is { Engine: not "Zipformer" } bundle)
        {
            ActualDevice = "CPU";
            _factory?.Dispose(); _factory = null; _loaded = null;
            if (_zipformer?.InUse == false) { _zipformer.Dispose(); _zipformer = null; _zipId = null; }
            if (_offlineId != id)
            {
                Report("Preparing " + models.Find(id).Name + "…");
                await models.VerifyAsync(id, token);
                _offline?.Dispose(); _offline = null; _offlineId = null;
                _offline = await LocalBackgroundWork.Run(() => new OfflineSpeechEngine(models.Find(id), models.ModelPath(id), Hardware.WhisperThreads), token);
                _offlineId = id;
            }
            Report(bundle.Engine + $" · CPU ONNX · {Hardware.WhisperThreads} threads · bounded-window transcription");
            return;
        }
        _offline?.Dispose(); _offline = null; _offlineId = null;
        if (_zipformer?.InUse == false) { _zipformer.Dispose(); _zipformer = null; _zipId = null; }
        var requestedGpu = gpu;
        if (_loaded == id && _loadedGpu == gpu) return;
        Report("Preparing Whisper in the background…");
        await models.VerifyAsync(id, token);
        _factory?.Dispose(); _factory = null; _loaded = null;
        // Library selection is process-wide. Keep the Vulkan runtime available even
        // for CPU-only contexts, so the per-model GPU switch works without restart.
        if (RuntimeOptions.LoadedLibrary is null)
            RuntimeOptions.RuntimeLibraryOrder = Hardware.Gpu is null ? [RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx] : [RuntimeLibrary.Vulkan, RuntimeLibrary.Cpu, RuntimeLibrary.CpuNoAvx];
        try { _factory = await LocalBackgroundWork.Run(() => WhisperFactory.FromPath(models.ModelPath(id), new() { UseGpu = gpu, UseFlashAttention = true }), token); }
        catch (Exception ex) when (gpu && ex is not OperationCanceledException)
        {
            _factory = await LocalBackgroundWork.Run(() => WhisperFactory.FromPath(models.ModelPath(id), new() { UseGpu = false }), token);
            gpu = false;
        }
        _loaded = id; _loadedGpu = requestedGpu;
        ActualDevice = gpu && RuntimeOptions.LoadedLibrary == RuntimeLibrary.Vulkan ? "GPU" : "CPU";
        Report($"Whisper · {(gpu && RuntimeOptions.LoadedLibrary == RuntimeLibrary.Vulkan ? "Vulkan GPU" : "CPU")} · up to {Hardware.WhisperThreads} CPU threads");
    }
    public async Task PrepareBackendAsync(string id, bool gpu, CancellationToken token)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token); token = cancellation.Token;
        await _gate.WaitAsync(token);
        try { await EnsureEngineAsync(id, token, gpu); }
        finally { CompleteOperation(); }
    }
    public async Task PrepareAsync(string id, CancellationToken token)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token); token = cancellation.Token;
        await _gate.WaitAsync(token);
        try { await EnsureEngineAsync(id, token); }
        finally { CompleteOperation(); }
    }
    public async Task<ILocalRecognitionStream> CreateLocalStreamAsync(TranscriptionOptions options, CancellationToken token)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token); token = cancellation.Token;
        await _gate.WaitAsync(token);
        try { await EnsureEngineAsync(options.LocalModelId, token); return _zipformer!.CreateStream(); }
        finally { CompleteOperation(); }
    }
    public async Task<SpeechResult> TranscribeAsync(string path, TranscriptionOptions options, CancellationToken token)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token); token = cancellation.Token;
        await _gate.WaitAsync(token);
        try
        {
            await EnsureEngineAsync(options.LocalModelId, token, options.LocalGpuOverride);
            var watch = Stopwatch.StartNew();
            var samples = await ReadAudioAsync(path, token);
            SpeechResult speech;
            if (models.Find(options.LocalModelId).Continuous)
            {
                using var stream = _zipformer!.CreateStream();
                var words = new List<TimedWord>(); string previous = "";
                for (var offset = 0; offset < samples.Length; offset += 8000)
                {
                    token.ThrowIfCancellationRequested();
                    var count = Math.Min(8000, samples.Length - offset);
                    var text = await stream.ProcessAsync(samples.AsSpan(offset, count).ToArray(), offset + count == samples.Length, token);
                    var delta = StreamTextReconciliation.AppendOnlyTail(previous, text);
                    if (!string.IsNullOrWhiteSpace(delta)) words.Add(new(offset / 16000d, (offset + count) / 16000d, delta.Trim()));
                    previous = text;
                }
                speech = new(previous, words);
            }
            else if (models.Find(options.LocalModelId).Bundle?.Engine is "FasterWhisper" or "Redux")
            {
                var personal = options.ApplyPersonalization && models.Find(options.LocalModelId).SupportsVocabulary ? profile?.Invoke() : null;
                speech = await _worker!.TranscribeAsync(samples, options with
                {
                    Language = models.Find(options.LocalModelId).EnglishOnly ? "en" : personal?.Language ?? options.Language,
                    Prompt = VoicePersonalization.Prompt(options.Prompt, personal?.Vocabulary)
                }, token);
            }
            else if (models.Find(options.LocalModelId).Bundle is not null)
                speech = await _offline!.TranscribeAsync(samples, token);
            else
            {
                var personal = options.ApplyPersonalization ? profile?.Invoke() : null;
                var language = personal?.Language ?? options.Language;
                var prompt = VoicePersonalization.Prompt(options.Prompt, personal?.Vocabulary);
                var builder = _factory!.CreateBuilder()
                    .WithLanguage(models.Find(options.LocalModelId).EnglishOnly ? "en" : string.IsNullOrWhiteSpace(language) ? "auto" : language)
                    .WithThreads(Hardware.WhisperThreads).WithNoContext().WithGreedySamplingStrategy().WithTemperature(0).WithTemperatureInc(0);
                if (!string.IsNullOrWhiteSpace(prompt)) builder.WithPrompt(prompt);
                using var processor = builder.Build();
                var segments = new List<TimedWord>();
                await foreach (var result in processor.ProcessAsync(samples, token))
                    if (!string.IsNullOrWhiteSpace(result.Text)) segments.Add(new(result.Start.TotalSeconds, result.End.TotalSeconds, result.Text.Trim()));
                speech = new(string.Join(" ", segments.Select(s => s.Text)), segments);
            }
            Report(RuntimeStatus.Split(" · Last:")[0] + $" · Last: {watch.Elapsed.TotalSeconds:0.00}s for {samples.Length / 16000d:0.0}s audio");
            return speech;
        }
        finally { CompleteOperation(); }
    }
    private async Task<float[]> ReadAudioAsync(string path, CancellationToken token)
    {
        // Capture and streaming already produce 16 kHz PCM. Avoid spawning FFmpeg
        // and writing a second WAV for the ordinary dictation path.
        try
        {
            await using var input = File.OpenRead(path);
            var parser = new WaveParser(input);
            await parser.InitializeAsync(token);
            if (parser.SampleRate == 16000 && parser.Channels == 1) return await parser.GetAvgSamplesAsync(token);
        }
        catch (Exception ex) when (ex is CorruptedWaveException or NotSupportedWaveException or NotSupportedException) { }
        var temporary = Path.Combine(Path.GetTempPath(), "wyspa-local-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            var start = new ProcessStartInfo(ffmpeg) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-nostdin", "-hide_banner", "-loglevel", "error", "-y", "-i", path, "-vn", "-ar", "16000", "-ac", "1", "-c:a", "pcm_s16le", temporary }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not prepare local audio.");
            var errors = process.StandardError.ReadToEndAsync(token);
            try { await process.WaitForExitAsync(token); }
            catch { if (!process.HasExited) process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); throw; }
            var error = await errors;
            if (process.ExitCode != 0) throw new InvalidOperationException("Could not decode this audio file. " + error);
            await using var audio = File.OpenRead(temporary);
            return await new WaveParser(audio).GetAvgSamplesAsync(token);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public async Task RemoveAsync(string id, CancellationToken token)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token); token = cancellation.Token;
        await _gate.WaitAsync(token);
        try
        {
            if (_workerId == id) ReleaseWorker();
            if (_zipId == id) { _zipformer?.Dispose(); _zipformer = null; _zipId = null; }
            if (_offlineId == id) { _offline?.Dispose(); _offline = null; _offlineId = null; }
            if (_loaded == id) { _factory?.Dispose(); _factory = null; _loaded = null; }
            models.Remove(id);
        }
        finally { CompleteOperation(); }
    }
    private void DisposeContexts()
    {
        ReleaseWorker(); _factory?.Dispose(); _factory = null; _zipformer?.Dispose(); _zipformer = null; _offline?.Dispose(); _offline = null;
    }
    private void CompleteOperation()
    {
        try { if (_disposed) DisposeContexts(); }
        finally { _gate.Release(); }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _lifetime.Cancel(); ReleaseWorker();
        // A native decode may still be finishing. Its gate owner releases contexts safely.
        if (_gate.Wait(0)) { try { DisposeContexts(); } finally { _gate.Release(); } }
    }
}

public sealed class TranscriptionRouter(IGroqTranscriptionClient cloud, LocalTranscriptionClient local, Func<bool> isLocal) : IGroqTranscriptionClient, IStreamProofreader, ILocalStreamingProvider
{
    public bool SupportsLocalStream(TranscriptionOptions options) => options.UseLocal && LocalModelStore.Catalog.Any(m => m.Id == options.LocalModelId && m.Continuous);
    public Task<ILocalRecognitionStream> CreateLocalStreamAsync(TranscriptionOptions options, CancellationToken token) => local.CreateLocalStreamAsync(options, token);
    public Task<ConnectionTestResult> TestConnectionAsync(string key, CancellationToken token) => cloud.TestConnectionAsync(key, token);
    public async Task<string> TranscribeAsync(string key, string path, TranscriptionOptions options, CancellationToken token)
    {
        if (!options.UseLocal) return await cloud.TranscribeAsync(key, path, options, token);
        var result = await local.TranscribeAsync(path, options, token);
        return FormatLocalResult(result, options.ResponseFormat);
    }
    public static string FormatLocalResult(SpeechResult result, string format) => format == "verbose_json"
        ? System.Text.Json.JsonSerializer.Serialize(new { text = result.Text, words = result.Words.Select(w => new { start = w.Start, end = w.End, word = w.Text }) })
        : result.Text;
    public Task<string> CleanupTranscriptAsync(string key, string text, string model, WritingCleanupTone tone, string? prompt, CancellationToken token)
        => isLocal() ? Task.FromResult(text) : cloud.CleanupTranscriptAsync(key, text, model, tone, prompt, token);
    public Task<IntentResolution> InterpretIntentAsync(string key, string text, string model, CancellationToken token)
        => isLocal() ? Task.FromResult(IntentResolution.Insert(text)) : cloud.InterpretIntentAsync(key, text, model, token);
    public Task<StreamFixResult> ProofreadStreamAsync(string key, string text, string model, CancellationToken token)
        => isLocal() ? Task.FromResult(new StreamFixResult(text, false)) : ((IStreamProofreader)cloud).ProofreadStreamAsync(key, text, model, token);
}

public sealed class NoteTranscriptionRouter(INoteIntelligence cloud, LocalTranscriptionClient local, Func<bool> isLocal) : INoteIntelligence
{
    public Task<SpeechResult> TranscribeAsync(string key, string path, TranscriptionOptions options, CancellationToken token)
        => options.UseLocal ? local.TranscribeAsync(path, options, token) : cloud.TranscribeAsync(key, path, options, token);
    public Task<string> SummariseAsync(string key, string transcript, string model, CancellationToken token)
        => isLocal() ? throw new InvalidOperationException("Summaries require Groq. Local mode keeps your transcript on this computer. Switch to Groq to send it for summarising.") : cloud.SummariseAsync(key, transcript, model, token);
}
