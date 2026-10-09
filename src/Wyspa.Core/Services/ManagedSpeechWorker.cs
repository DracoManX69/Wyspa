using System.Diagnostics;
using System.Text.Json;
using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

internal sealed class ManagedSpeechWorker : IDisposable
{
    private readonly Process _process;
    private readonly Task _errors;
    private bool _disposed;
    private string? _pcmPath;
    private ManagedSpeechWorker(Process process)
    {
        _process = process;
        // Drain warnings without persisting user audio, prompts, transcripts or worker output.
        _errors = Task.Run(async () => { try { while (await process.StandardError.ReadLineAsync() is not null) { } } catch (Exception ex) when (ex is ObjectDisposedException or IOException or InvalidOperationException) { } });
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (System.ComponentModel.Win32Exception) { }
    }
    public bool Alive => !_disposed && !_process.HasExited;
    public double CpuSeconds { get { if (_disposed) return 0; try { _process.Refresh(); return _process.TotalProcessorTime.TotalSeconds; } catch (InvalidOperationException) { return 0; } } }
    public double RamMb { get { if (_disposed) return 0; try { _process.Refresh(); return _process.WorkingSet64 / 1_000_000d; } catch (InvalidOperationException) { return 0; } } }
    public static async Task<ManagedSpeechWorker> StartAsync(LocalModel model, string directory, bool gpu, int threads, IProgress<string>? progress, CancellationToken token)
    {
        var runtime = await ManagedSpeechRuntime.Default.EnsureAsync(gpu, progress, token);
        var start = new ProcessStartInfo(runtime.Python) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(runtime.Python)! };
        foreach (var arg in new[] { "-I", "-u", "-X", "utf8", "-B", runtime.Script }) start.ArgumentList.Add(arg);
        start.Environment["WYSPA_CUDA_PATHS"] = string.Join(Path.PathSeparator, runtime.CudaPaths);
        // CUDA providers also use the child-only PATH. The user's/system PATH is untouched.
        start.Environment["PATH"] = string.Join(Path.PathSeparator, runtime.CudaPaths.Append(start.Environment.TryGetValue("PATH", out var inheritedPath) ? inheritedPath ?? "" : ""));
        start.Environment["OMP_NUM_THREADS"] = threads.ToString();
        var worker = new ManagedSpeechWorker(Process.Start(start) ?? throw new InvalidOperationException("Could not start the local speech runtime."));
        try
        {
            using var response = await worker.ExchangeAsync(new { op = "prepare", engine = model.Bundle!.Engine, directory, gpu, threads }, token);
            return worker;
        }
        catch { worker.Dispose(); throw; }
    }
    public async Task<SpeechResult> TranscribeAsync(float[] samples, TranscriptionOptions options, CancellationToken token)
    {
        var pcm = _pcmPath = Path.Combine(Path.GetTempPath(), "wyspa-worker-" + Guid.NewGuid().ToString("N") + ".pcm");
        try
        {
            var bytes = new byte[samples.Length * sizeof(float)]; Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
            using (var file = new FileStream(pcm, FileMode.CreateNew, FileAccess.Write, FileShare.Read | FileShare.Delete, 131072, FileOptions.Asynchronous))
                await file.WriteAsync(bytes, token);
            using var result = await ExchangeAsync(new { op = "transcribe", pcm, language = options.Language == "auto" ? null : options.Language, prompt = options.Prompt }, token);
            var root = result.RootElement;
            return new(root.GetProperty("text").GetString()!, root.GetProperty("segments").EnumerateArray()
                .Select(row => new TimedWord(row.GetProperty("start").GetDouble(), row.GetProperty("end").GetDouble(), row.GetProperty("text").GetString()!)).ToList());
        }
        finally { File.Delete(pcm); _pcmPath = null; }
    }
    private async Task<JsonDocument> ExchangeAsync(object request, CancellationToken token)
    {
        try
        {
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), token);
            await _process.StandardInput.FlushAsync(token);
            var line = await _process.StandardOutput.ReadLineAsync(token);
            if (line is null) throw new InvalidOperationException("The local speech worker stopped. Check Windows runtime support and retry.");
            var result = JsonDocument.Parse(line);
            if (result.RootElement.TryGetProperty("error", out var error))
            { var message = error.GetString(); result.Dispose(); throw new InvalidOperationException("Local engine: " + message); }
            return result;
        }
        catch { Dispose(); throw; }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { if (!_process.HasExited) { _process.Kill(true); _process.WaitForExit(1000); } }
        catch (InvalidOperationException) { }
        finally
        {
            _process.Dispose();
            if (_pcmPath is { } pcm) { try { File.Delete(pcm); } catch (IOException) { } _pcmPath = null; }
        }
    }
}
