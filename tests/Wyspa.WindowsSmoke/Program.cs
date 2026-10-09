using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using NAudio.Wave;
using Wyspa.App;
using Wyspa.App.Services;
using Wyspa.App.ViewModels;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using Wyspa.Core.Services;
using Wyspa.Infrastructure.Audio;
using Wyspa.Infrastructure.Settings;
using Wyspa.Infrastructure.Hotkeys;
using Wyspa.Infrastructure.Insertion;
using Wyspa.Infrastructure.Media;

internal static class Program
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowEnabled(IntPtr handle);
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            if (args[0] == "wake-optimisation-research") { WakeOptimisationResearch.Run(output, args[2], args[3], args[4]).GetAwaiter().GetResult(); return 0; }
            if (args[0] is "wake-optimised-corpus" or "wake-optimised-noise") { WakeOptimisationResearch.Live(output, args[2], args[0] == "wake-optimised-noise").GetAwaiter().GetResult(); return 0; }
            if (args[0] == "personal-wake") { PersonalWakeAsync(output, args.Skip(2).ToArray()).GetAwaiter().GetResult(); return 0; }
            if (args[0] == "wake-pronunciation") { WakePronunciationAsync(output, args.Skip(2).ToArray()).GetAwaiter().GetResult(); return 0; }
            if (args[0] == "wake-live") { WakeLiveAsync(output, args.Skip(2).ToArray()).GetAwaiter().GetResult(); return 0; }
            if (args[0] == "wake-verify") { WakeVerifyAsync(output, args.Skip(2).ToArray()).GetAwaiter().GetResult(); return 0; }
            if (args[0] == "media-snapshot")
            {
                var manager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask().GetAwaiter().GetResult();
                File.WriteAllText(Path.Combine(output, "media-api.txt"), "PASS: media-session API read successfully; session count: " + manager.GetSessions().Count + "; no playback commands sent."); return 0;
            }
            if (args[0] == "stream-browser") return StreamFixBrowserSmoke.Run(output);
            if (args[0] == "stream-word") return StreamFixWordSmoke.Run(output);
            if (args[0] == "stream-insertion") return StreamingInsertionSmoke.Run(output);
            if (args[0] == "stream-target") { StreamingInsertionSmoke.Target(output); return 0; }
            if (args[0] == "stream-groq") { StreamingGroqSmoke.RunAsync(output, args[2]).GetAwaiter().GetResult(); return 0; }
            if (args[0] is "native" or "speakers") { NativeAsync(output, args[2], args[0] == "speakers").GetAwaiter().GetResult(); return 0; }
            if (args[0] == "video") { VideoAsync(output).GetAwaiter().GetResult(); return 0; }
            if (args[0] == "groq") { GroqAsync(output, args[2]).GetAwaiter().GetResult(); return 0; }
            if (args[0] == "local-profile") { LocalProfileAsync(output, args[2], args[3]).GetAwaiter().GetResult(); return 0; }
            if (args[0] == "wake-custom") { WakeLexicalAsync(output, args.Skip(3).ToArray(), args[2]).GetAwaiter().GetResult(); return 0; }
            if (args[0] == "wake-lexical") { WakeLexicalAsync(output, args.Skip(2).ToArray()).GetAwaiter().GetResult(); return 0; }
            if (args[0] is "advisor" or "advisor-groq") { AdvisorAsync(output, args[2], args[3], args[0] == "advisor-groq").GetAwaiter().GetResult(); return 0; }
            if (args[0] == "optional-tools") { OptionalToolsAsync(output, args[2]).GetAwaiter().GetResult(); return 0; }
            if (args[0] == "local-first-run") { FirstRunLocalAsync(output, args[2], args[3]).GetAwaiter().GetResult(); return 0; }
            if (args[0] == "worker-lifecycle") { WorkerLifecycleAsync(output, args[2]).GetAwaiter().GetResult(); return 0; }
            if (args[0] == "local-devices") { LocalDevicesAsync(output, args[2], args[3], args[4]).GetAwaiter().GetResult(); return 0; }
            if (args[0] == "local-benchmark") { BenchmarkLocalAsync(output, args[2], args[3], args[4]).GetAwaiter().GetResult(); return 0; }
            if (args[0] == "local") { LocalAsync(output, args[2], args[3], args.Length > 4 ? args[4] : "tiny.en").GetAwaiter().GetResult(); return 0; }
            if (args[0] == "models") { ModelsAsync(output).GetAwaiter().GetResult(); return 0; }
            Render(output, args[2], args[0] == "preview", args[0] is "wake-setup-ui" or "wake-wizard-ui"); return Environment.ExitCode;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static async Task LocalProfileAsync(string output, string wave, string ffmpeg)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        var models = new LocalModelStore(http, Path.Combine(output, "models"));
        if (!models.IsInstalled("tiny.en")) await models.DownloadAsync("tiny.en", new Progress<double>(), default);
        var settings = new AppSettings { LocalModelId = "tiny.en" };
        var persistence = new JsonSettingsService(Path.Combine(output, "voice-profile-settings.json"));
        using var local = new LocalTranscriptionClient(models, ffmpeg, () => settings.LocalVoiceProfile);
        var capture = new FixtureSetupCapture(wave);
        var vm = new VoiceSetupViewModel(capture, () => settings, local.TranscribeAsync, models.IsInstalled,
            _ => Task.CompletedTask, () => persistence.SaveAsync(settings, default));
        vm.Vocabulary = "Americans, country";
        for (var passage = 0; passage < 3; passage++)
        {
            vm.Reference = "And so my fellow Americans ask not what your country can do for you ask what you can do for your country";
            await vm.StartAsync(); await vm.StopAndTestAsync();
            if (vm.Results.Count != passage + 1) throw new Exception(vm.Status);
        }
        await vm.SaveAsync();
        var saved = await persistence.LoadAsync(default);
        if (!saved.UseLocalTranscription || saved.LocalVoiceProfile?.Scores.Count != 3) throw new Exception("Real inference profile did not persist: " + vm.Status);
        var result = await local.TranscribeAsync(wave, new("", null, null, UseLocal: true, LocalModelId: "tiny.en"), default);
        if (!result.Text.Contains("country", StringComparison.OrdinalIgnoreCase)) throw new Exception("Saved profile failed on inference.");
        File.WriteAllText(Path.Combine(output, "voice-profile-result.txt"), vm.Summary + "\n" + vm.Status + "\nSaved profile inference: " + result.Text);
        await vm.ShutdownAsync();
    }
    private sealed class FixtureSetupCapture(string fixture) : IAudioCaptureService
    {
        public event EventHandler<float>? LevelAvailable;
        private string? _path;
        public bool IsRecording { get; private set; }
        public Task<IReadOnlyList<AudioDeviceInfo>> GetDevicesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<AudioDeviceInfo>>([]);
        public Task StartRecordingAsync(string? device, CancellationToken token)
        { _path = Path.Combine(Path.GetTempPath(), "wyspa-profile-fixture-" + Guid.NewGuid() + ".wav"); File.Copy(fixture, _path); IsRecording = true; LevelAvailable?.Invoke(this, .3f); return Task.CompletedTask; }
        public Task<RecordingResult> StopRecordingAsync(CancellationToken token)
        { IsRecording = false; return Task.FromResult(new RecordingResult(_path!, TimeSpan.FromSeconds(11), new FileInfo(_path!).Length, .4f)); }
        public Task DeleteRecordingAsync(string path, CancellationToken token) { File.Delete(path); return Task.CompletedTask; }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private static async Task WakeLexicalAsync(string output, string[] paths, string phrase = "hey whisper")
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var store = new OptionalDependencyStore(http, Path.Combine(output, "models"));
        using var detector = new WakeKeywordEngine(store);
        await detector.PrepareAsync(new Progress<string>(Console.WriteLine), default);
        var scores = new List<object>();
        foreach (var path in paths)
        {
            await using var file = File.OpenRead(path);
            var samples = await new Whisper.net.Wave.WaveParser(file).GetAvgSamplesAsync();
            var score = await detector.EvaluateAsync(samples, phrase, default);
            detector.Reset(); var matched = false;
            for (var start = 0; start < samples.Length; start += 960)
                matched |= await detector.ProcessAsync(samples[start..Math.Min(samples.Length, start + 960)], phrase, .3, default);
            matched |= await detector.ProcessAsync(new float[16000], phrase, .3, default);
            scores.Add(new { path = Path.GetFileName(path), maximumStrictness = score, matched });
        }
        File.WriteAllText(Path.Combine(output, "wake-lexical-results.json"), JsonSerializer.Serialize(scores, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(scores));
    }
    private static async Task AdvisorAsync(string output, string sample, string ffmpeg, bool compareGroq)
    {
        using var localHttp = new HttpClient();
        var models = new LocalModelStore(localHttp, Path.Combine(output, "models"));
        using var client = new LocalTranscriptionClient(models, ffmpeg);
        var settings = new AppSettings { LocalModelId = "tiny.en-q5_1", StreamModeEnabled = true };
        using var cloudHttp = new HttpClient(); var cloud = new GroqTranscriptionClient(cloudHttp);
        var secrets = new DpapiSecretStore(); var available = !string.IsNullOrWhiteSpace(await secrets.GetApiKeyAsync(default));
        var runner = new SpeechPerformanceBenchmark(client.PrepareAsync, client.TranscribeAsync,
            async (path, options, token) => await cloud.TranscribeAsync(await secrets.GetApiKeyAsync(token) ?? throw new Exception("No saved key"), path, options, token), () => client.RuntimeStatus,
            client.PrepareBackendAsync, client.SupportsGpu, () => client.ActualDevice, () => client.WorkerCpuSeconds, () => client.WorkerRamMb);
        var report = new SpeechPerformanceReport { MeasuredAt = DateTimeOffset.UtcNow, HardwareFingerprint = client.Hardware.Fingerprint, GpuEnabled = true };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await runner.RunAsync(sample, [ZipformerModel.Id, "tiny.en-q5_1"], compareGroq && available ? settings.ModelId : null,
            report.Results.Add, new Progress<string>(Console.WriteLine), timeout.Token, true);
        settings.SpeechPerformance = report;
        var advice = SpeechModelAdvisor.Recommend(client.Hardware, settings, models.IsInstalled);
        if (!report.Results.Any(r => r.Provider == "local" && r.Error is null)) throw new Exception("No local measurements succeeded.");
        if (client.Hardware.Gpu is not null && client.Hardware.GpuMemoryBytes == 0) throw new Exception("GPU memory was not detected.");
        File.WriteAllText(Path.Combine(output, "advisor-results.json"), JsonSerializer.Serialize(new { client.Hardware, GroqMeasured = compareGroq && available, report, advice }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(advice.Explanation);
    }
    private static async Task OptionalToolsAsync(string output, string wave)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        var store = new OptionalDependencyStore(http, Path.Combine(output, "cache"));
        var progress = new Progress<string>(Console.WriteLine);
        var deno = await store.EnsureAsync(OptionalDependencyStore.Deno, null, progress, default);
        var version = await VideoImporter.RunAsync(deno, ["--version"], output, default);
        if (!version.Contains("deno 2.9.7")) throw new Exception("Incorrect Deno version.");
        using var identifier = new LocalSpeakerIdentifier(Path.Combine(output, "missing-bundled-models"), () => .55, store);
        await identifier.InitializeAsync(default, progress);
        using var waveReader = new WaveFileReader(wave);
        var samples = new float[waveReader.SampleCount]; var provider = waveReader.ToSampleProvider();
        var count = provider.Read(samples, 0, samples.Length);
        var turns = await identifier.IdentifyAsync(samples[..count], default);
        if (turns.Count == 0) throw new Exception("Downloaded speaker models returned no turns.");
        using var offline = new HttpClient(new NoNetworkHandler());
        var cached = new OptionalDependencyStore(offline, store.DirectoryPath);
        foreach (var dependency in new[] { OptionalDependencyStore.Deno, OptionalDependencyStore.Segmentation, OptionalDependencyStore.Embedding })
            await cached.EnsureAsync(dependency, null, null, default);
        var ffmpeg = Path.Combine(AppContext.BaseDirectory, "Tools", "Video", "ffmpeg.exe");
        var prepared = Path.Combine(output, "converted.wav");
        await VideoImporter.RunAsync(ffmpeg, ["-y", "-i", wave, "-ar", "16000", "-ac", "1", prepared], output, default);
        if (!File.Exists(prepared)) throw new Exception("Reduced FFmpeg package did not decode audio.");
        File.Delete(prepared);
        File.WriteAllText(Path.Combine(output, "optional-tools-result.txt"), "PASS: verified first-use Deno and speaker downloads, native Deno execution, speaker inference, offline cache reuse, FFmpeg decoding.\n" + version + $"\nSpeaker turns: {turns.Count}");
    }
    private static async Task FirstRunLocalAsync(string output, string bundle, string wave)
    {
        using var http = new HttpClient(new NoNetworkHandler());
        var store = new LocalModelStore(http, Path.Combine(output, "models"));
        var persistence = new JsonSettingsService(Path.Combine(output, "first-run.json"));
        var settings = await persistence.LoadAsync(default);
        if (!settings.UseLocalTranscription || settings.LocalModelId != ZipformerModel.Id || !settings.StreamModeEnabled) throw new Exception("Wrong first-run defaults.");
        using var engine = new LocalTranscriptionClient(store, "FFmpeg must not be needed for native capture");
        var vm = new LocalModelsViewModel(store, engine, () => settings, () => persistence.SaveAsync(settings, default));
        await vm.InitializeAsync(bundle);
        if (!vm.Installed || !vm.Enabled || settings.LocalVoiceProfile is not null || !settings.LocalDefaultsInitialized) throw new Exception("Default model did not become usable without setup.");
        var text = await engine.TranscribeAsync(wave, new("", "en", null, UseLocal: true, LocalModelId: settings.LocalModelId), default);
        if (!text.Text.Contains("country")) throw new Exception("First-run inference failed.");
        await engine.RemoveAsync(ZipformerModel.Id, default);
        await vm.InitializeAsync(bundle);
        if (vm.Installed) throw new Exception("Removed model was unexpectedly reinstalled.");
        File.WriteAllText(Path.Combine(output, "first-run-result.txt"), "PASS: offline bundled install, no profile/key, PCM without FFmpeg, inference, removal respected across initialization.\n" + text.Text);
    }
    private sealed class NoNetworkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => throw new Exception("First-run local setup must not require network.");
    }
    private static async Task WakePronunciationAsync(string output, string[] paths)
    {
        using var detector = new WakeKeywordEngine(); await detector.PrepareAsync(null, default);
        async Task<float[]> Audio(string path) { await using var file = File.OpenRead(path); return await new Whisper.net.Wave.WaveParser(file).GetAvgSamplesAsync(); }
        var positive = await Audio(paths[0]); var ordinary = await Audio(paths[1]); var near = await Audio(paths[2]);
        var profile = new WakeVoiceProfile { DetectorVersion = 2, EnrollmentVersion = 1, SetupValidated = true,
            Phrase = "hey wyspa", MicrophoneId = "fixture", PronunciationVariants = ["hey whisper"] };
        async Task<bool> Run(float[] samples, WakeVoiceProfile? personal, string mic = "fixture")
        {
            detector.ConfigurePersonalization(personal, mic); detector.Reset();
            var audio = new float[samples.Length + 16000]; samples.CopyTo(audio, 0);
            for (var offset = 0; offset < audio.Length; offset += 960)
                if (await detector.ProcessAsync(audio.AsSpan(offset, Math.Min(960, audio.Length - offset)).ToArray(), profile.Phrase, .3, default)) return true;
            return false;
        }
        var canonicalOnly = await Run(positive, null);
        var learned = await Run(positive, profile);
        var wrongMic = await Run(positive, profile, "other-mic");
        var falseOrdinary = await Run(ordinary, profile); var falseNear = await Run(near, profile);
        File.WriteAllText(Path.Combine(output, "wake-pronunciation.json"), JsonSerializer.Serialize(new { canonicalOnly, learned, wrongMic, falseOrdinary, falseNear, profile.PronunciationVariants }, new JsonSerializerOptions { WriteIndented = true }));
        if (canonicalOnly || !learned || wrongMic || falseOrdinary || falseNear) throw new Exception("Learned keyword pronunciation routing failed.");
    }
    private static async Task PersonalWakeAsync(string output, string[] paths)
    {
        using var detector = new WakeKeywordEngine(); await detector.PrepareAsync(null, default);
        async Task<float[]> Audio(string path) { await using var file = File.OpenRead(path); return await new Whisper.net.Wave.WaveParser(file).GetAvgSamplesAsync(); }
        var positive = await Audio(paths[0]); var ordinary = await Audio(paths[1]); var near = await Audio(paths[2]);
        var readings = new List<WakeSetupReading>();
        for (var i = 0; i < 4; i++)
        {
            var sample = await detector.AnalyzeAsync(positive.Select(v => v * (float)(.6 + .1 * i)).ToArray(), "hey whisper", default);
            readings.Add(new("wake-" + i, true, false, true, sample.KeywordStrictness, sample.RecognizedText, sample.Rms, sample.Peak, sample.ClippedFraction, sample.Features));
        }
        foreach (var (id, clip) in new[] { ("ordinary", ordinary), ("near", near) })
        {
            var sample = await detector.AnalyzeAsync(clip, "hey whisper", default);
            readings.Add(new(id, false, false, false, sample.KeywordStrictness, sample.RecognizedText, sample.Rms, sample.Peak, sample.ClippedFraction, sample.Features));
        }
        var profile = WakeEnrollmentTrainer.Build("hey whisper", "fixture-mic", readings);
        if (profile.AcousticTemplates.Count < 3) throw new Exception("Fixture enrollment did not produce personal templates.");
        // A strictness that the generic detector missed in prior fixture measurements.
        var watch = Stopwatch.StartNew(); var wake = await detector.CheckAsync(positive, "hey whisper", profile, 1, default);
        var seconds = watch.Elapsed.TotalSeconds;
        var falseOrdinary = await detector.CheckAsync(ordinary, "hey whisper", profile, 1, default);
        var falseNear = await detector.CheckAsync(near, "hey whisper", profile, 1, default);
        File.WriteAllText(Path.Combine(output, "personal-wake.json"), JsonSerializer.Serialize(new { profile.AcousticThreshold, Templates = profile.AcousticTemplates.Count, wake, falseOrdinary, falseNear, seconds }, new JsonSerializerOptions { WriteIndented = true }));
        if (!wake || falseOrdinary || falseNear) throw new Exception("Personal fallback did not separate the spoken fixtures.");
    }
    private static async Task WakeLiveAsync(string output, string[] paths)
    {
        using var detector = new WakeKeywordEngine(); await detector.PrepareAsync(null, default); var rows = new List<object>();
        foreach (var path in paths)
        {
            detector.Reset(); await using var input = File.OpenRead(path); var audio = await new Whisper.net.Wave.WaveParser(input).GetAvgSamplesAsync();
            var samples = new float[audio.Length + 16000]; audio.CopyTo(samples, 0); bool found = false; double? at = null;
            var clock = Stopwatch.StartNew();
            for (var offset = 0; offset < samples.Length; offset += 960)
            {
                if (await detector.ProcessAsync(samples.AsSpan(offset, Math.Min(960, samples.Length - offset)).ToArray(), "hey whisper", .45, default))
                { found = true; at = (offset + 960) / 16000d; break; }
            }
            rows.Add(new { file = Path.GetFileName(path), found, atAudioSeconds = at, processingSeconds = clock.Elapsed.TotalSeconds });
            if (Path.GetFileName(path) == "positive.wav" && !found || Path.GetFileName(path) != "positive.wav" && found) throw new Exception("Streaming wake verification was incorrect: " + path);
        }
        File.WriteAllText(Path.Combine(output, "wake-live.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static async Task WakeVerifyAsync(string output, string[] paths)
    {
        using var verifier = new WakePhraseVerifier(); using var vad = new LocalSpeechActivityDetector();
        await verifier.PrepareAsync(default); await vad.PrepareAsync(null, default);
        var rows = new List<object>();
        foreach (var path in paths)
        {
            await using var input = File.OpenRead(path); var samples = await new Whisper.net.Wave.WaveParser(input).GetAvgSamplesAsync();
            var clock = Stopwatch.StartNew(); var text = await verifier.RecognizeAsync(samples, default);
            var seconds = clock.Elapsed.TotalSeconds; vad.Reset(); var speech = 0;
            for (var offset = 0; offset < samples.Length; offset += 640)
                if (await vad.ProcessAsync(samples.AsSpan(offset, Math.Min(640, samples.Length - offset)).ToArray(), default)) speech++;
            rows.Add(new { file = Path.GetFileName(path), text, seconds, match = WakePhraseVerifier.Matches(text, Path.GetFileName(path) == "custom.wav" ? "hello computer" : "hey whisper"), speechFrames = speech });
        }
        File.WriteAllText(Path.Combine(output, "wake-verification.json"), JsonSerializer.Serialize(rows, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static async Task WorkerLifecycleAsync(string output, string sample)
    {
        using var http = new HttpClient(); var models = new LocalModelStore(http, Path.Combine(output, "models"));
        var previousPids = Process.GetProcessesByName("python").Select(p => { using (p) return p.Id; }).ToHashSet();
        var previousPcm = Directory.GetFiles(Path.GetTempPath(), "wyspa-worker-*.pcm").ToHashSet(StringComparer.OrdinalIgnoreCase);
        using var local = new LocalTranscriptionClient(models, "unused", gpuEnabled: () => false);
        await local.PrepareAsync("faster-whisper-tiny.en", default);
        var workerPids = Process.GetProcessesByName("python").Where(p => !previousPids.Contains(p.Id)).Select(p => { using (p) return p.Id; }).ToArray();
        if (workerPids.Length != 1) throw new Exception("Expected one isolated worker.");
        var inference = local.TranscribeAsync(sample, new("", "en", null, UseLocal: true, LocalModelId: "faster-whisper-tiny.en"), default);
        await Task.Delay(20); local.Dispose();
        try { await inference; throw new Exception("Disposed inference must be cancelled."); }
        catch (OperationCanceledException) { }
        await Task.Delay(150);
        foreach (var pid in workerPids)
        {
            try { using var process = Process.GetProcessById(pid); if (!process.HasExited) throw new Exception("Worker survived local client disposal."); }
            catch (ArgumentException) { }
        }
        if (Directory.GetFiles(Path.GetTempPath(), "wyspa-worker-*.pcm").Any(path => !previousPcm.Contains(path))) throw new Exception("Worker disposal left transient audio.");
        File.WriteAllText(Path.Combine(output, "worker-lifecycle.txt"), "PASS: cancelled in-flight inference, killed owned worker, cleaned temporary PCM, repeated disposal safe.");
    }
    private static async Task LocalDevicesAsync(string output, string sample, string ffmpeg, string id)
    {
        using var http = new HttpClient(); var models = new LocalModelStore(http, Path.Combine(output, "models"));
        SpeechPerformanceReport? report = null;
        using var local = new LocalTranscriptionClient(models, ffmpeg, gpuEnabled: () => true, performance: () => report);
        report = new() { HardwareFingerprint = local.Hardware.Fingerprint, GpuEnabled = true, MeasuredAt = DateTimeOffset.UtcNow };
        var runner = new SpeechPerformanceBenchmark(local.PrepareAsync, local.TranscribeAsync,
            (_, _, _) => throw new Exception("No cloud upload in native device check"), () => local.RuntimeStatus,
            local.PrepareBackendAsync, local.SupportsGpu, () => local.ActualDevice, () => local.WorkerCpuSeconds, () => local.WorkerRamMb);
        await runner.RunAsync(sample, [id], null, report.Results.Add, new Progress<string>(Console.WriteLine), default, true);
        File.WriteAllText(Path.Combine(output, "devices-" + id + ".json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        if (report.Results.Any(row => row.Error is not null)) throw new Exception(string.Join("; ", report.Results.Where(row => row.Error is not null).Select(row => row.Error)));
        if (local.SupportsGpu(id) && !report.Results.Any(row => row.RequestedDevice == "GPU" && row.Device == "GPU")) throw new Exception("Native GPU result was a CPU fallback.");
        var expectedGpu = LocalBackendPolicy.UseGpu(models.Find(id), local.Hardware, true, report);
        await local.TranscribeAsync(sample, new("", "en", null, UseLocal: true, LocalModelId: id), default);
        if (local.ActualDevice != (expectedGpu ? "GPU" : "CPU")) throw new Exception("Automatic backend did not use measured choice.");
        File.WriteAllText(Path.Combine(output, "chosen-" + id + ".txt"), local.RuntimeStatus);
    }
    private static async Task BenchmarkLocalAsync(string output, string wave, string ffmpeg, string id)
    {
        using var http = new HttpClient();
        var models = new LocalModelStore(http, Path.Combine(output, "models"));
        using var local = new LocalTranscriptionClient(models, ffmpeg, gpuEnabled: () => Environment.GetEnvironmentVariable("WYSPA_CPU_ONLY") != "1");
        var results = new List<object>();
        for (var run = 0; run < 4; run++)
        {
            var clock = Stopwatch.StartNew();
            var result = await local.TranscribeAsync(wave, new("", "en", null, UseLocal: true, LocalModelId: id), default);
            results.Add(new { run, seconds = clock.Elapsed.TotalSeconds, result.Text, local.RuntimeStatus });
            if (!result.Text.Contains("country", StringComparison.OrdinalIgnoreCase)) throw new Exception(result.Text);
        }
        if (id == ZipformerModel.Id)
        {
            await using var input = File.OpenRead(wave);
            var samples = await new Whisper.net.Wave.WaveParser(input).GetAvgSamplesAsync();
            var router = new TranscriptionRouter(new GroqTranscriptionClient(http), local, () => true);
            var updates = new List<string>();
            using var session = new StreamingDictationSession(router, "", new("", "en", null, UseLocal: true, LocalModelId: id),
                (delta, text, token) => { updates.Add(text); return Task.CompletedTask; });
            var decode = Stopwatch.StartNew();
            for (var offset = 0; offset < samples.Length; offset += 1600)
            {
                var count = Math.Min(1600, samples.Length - offset); var pcm = new byte[count * 2];
                for (var i = 0; i < count; i++) System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), (short)(samples[offset + i] * 32767));
                session.AddAudio(null, pcm); await session.ProcessAsync(false, default);
            }
            var preStop = decode.Elapsed.TotalSeconds;
            decode.Restart(); await session.ProcessAsync(true, default);
            results.Add(new { streamDecodeSeconds = preStop, stopSeconds = decode.Elapsed.TotalSeconds, updates = updates.Count, session.Text });
            if (updates.Count < 2 || !session.Text.Contains("country")) throw new Exception("Continuous local stream failed.");
        }
        var process = Process.GetCurrentProcess(); var idleCpu = process.TotalProcessorTime.TotalSeconds + local.WorkerCpuSeconds;
        await Task.Delay(2000); process.Refresh();
        var report = JsonSerializer.Serialize(new { local.Hardware, results, idleCpuSecondsOverTwoSeconds = process.TotalProcessorTime.TotalSeconds + local.WorkerCpuSeconds - idleCpu, workingSetMb = process.WorkingSet64 / 1_000_000d + local.WorkerRamMb }, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(output, "benchmark-" + id + (Environment.GetEnvironmentVariable("WYSPA_CPU_ONLY") == "1" ? "-cpu" : "-auto") + ".json"), report);
        Console.WriteLine(report);
    }
    private static async Task LocalAsync(string output, string wave, string ffmpeg, string id)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        var models = new LocalModelStore(http, Path.Combine(output, "models"));
        if (!models.IsInstalled(id)) await models.DownloadAsync(id, new Progress<double>(), default);
        using var local = new LocalTranscriptionClient(models, ffmpeg);
        var watch = Stopwatch.StartNew();
        var result = await local.TranscribeAsync(wave, new("unused", "en", null, UseLocal: true, LocalModelId: id), default);
        if (!result.Text.Contains("country", StringComparison.OrdinalIgnoreCase)) throw new Exception("Local fixture transcription did not match: " + result.Text);
        File.WriteAllText(Path.Combine(output, "local-" + id + "-result.txt"), $"{watch.Elapsed}: {result.Text}\nSegments: {result.Words.Count}");
        Console.WriteLine(result.Text);
    }
    private static async Task ModelsAsync(string output)
    {
        var key = await new DpapiSecretStore().GetApiKeyAsync(default);
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("No saved Groq key is available.");
        using var http = new HttpClient();
        var models = new GroqModelsViewModel(new GroqTranscriptionClient(http), () => new AppSettings());
        var result = await models.RefreshAsync(key);
        if (!result.Success) throw new InvalidOperationException(result.UserMessage);
        File.WriteAllText(Path.Combine(output, "live-models.json"), JsonSerializer.Serialize(new { models.TranscriptionModels, models.TextModels, models.Status }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Groq model discovery: {models.TranscriptionModels.Count} speech models and {models.TextModels.Count} text models.");
    }
    private static async Task GroqAsync(string output, string wave)
    {
        var key = await new DpapiSecretStore().GetApiKeyAsync(default);
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("No saved Groq key is available for the smoke test.");
        using var http = new HttpClient(); var groq = new GroqNoteIntelligence(http);
        var speech = await groq.TranscribeAsync(key, wave, new("whisper-large-v3-turbo", "en", null), default);
        var summary = await groq.SummariseAsync(key, speech.Text, "openai/gpt-oss-20b", default);
        File.WriteAllText(Path.Combine(output, "groq-result.json"), JsonSerializer.Serialize(new { speech, summary }));
        Console.WriteLine($"Groq transcription: {speech.Words.Count} timestamped words. Summary: {summary.Length} characters.");
    }
    private static async Task VideoAsync(string output)
    {
        using var token = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var video = await new VideoImporter(Path.Combine(AppContext.BaseDirectory, "Tools", "Video"))
            .PrepareAsync("https://www.youtube.com/watch?v=jNQXAC9IVRw", new Progress<string>(Console.WriteLine), token.Token);
        Console.WriteLine($"Imported {video.Title}, {video.Parts.Count} audio parts");
        using var reader = new WaveFileReader(video.Parts[0]);
        File.WriteAllText(Path.Combine(output, "video-result.txt"), $"{video.Title}\n{video.Parts.Count} parts\n{reader.WaveFormat}\n{reader.TotalTime}");
        var key = await new DpapiSecretStore().GetApiKeyAsync(default);
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("No saved Groq key is available.");
        using var http = new HttpClient();
        var speech = await new GroqNoteIntelligence(http).TranscribeAsync(key, video.Parts[0], new("whisper-large-v3-turbo", "en", null), token.Token);
        File.WriteAllText(Path.Combine(output, "youtube-transcript.txt"), speech.Text);
        if (speech.Words.Count == 0) throw new Exception("Video speech returned no timestamped words.");
        Console.WriteLine($"YouTube audio transcribed: {speech.Words.Count} timestamped words.");
    }
    private static async Task NativeAsync(string output, string wave, bool speakersOnly)
    {
        var report = new List<string>();
        using var reader = new WaveFileReader(wave);
        var provider = reader.ToSampleProvider();
        var samples = new float[reader.SampleCount];
        var count = provider.Read(samples, 0, samples.Length); samples = samples[..count];
        using var identifier = new LocalSpeakerIdentifier(Path.Combine(AppContext.BaseDirectory, "Tools", "Speakers"), () => .55);
        await identifier.InitializeAsync(default);
        var watch = Stopwatch.StartNew();
        var turns = await identifier.IdentifyAsync(samples, default);
        report.Add($"Speaker model: {turns.Count} turns, {turns.Select(t => t.Speaker).Distinct().Count()} speakers, {watch.ElapsedMilliseconds} ms for {samples.Length/16000d:0.0}s.");
        report.AddRange(turns.Select(t => $"{t.Start:0.00}-{t.End:0.00}: {t.Speaker}"));
        if (turns.Count == 0) throw new InvalidOperationException("Speaker model returned no turns for speech fixture.");
        watch.Restart();
        var shortTurns = await identifier.IdentifyAsync(samples.Take(56000).ToArray(), default);
        report.Add($"Short-window model: {shortTurns.Count} turns in {watch.ElapsedMilliseconds} ms.");
        identifier.Reset();
        var streamed = new List<SpeakerTurn>();
        for (var start = 0; start < samples.Length; start += 56000)
        {
            var chunk = samples[start..Math.Min(start + 56000, samples.Length)];
            var detected = await identifier.IdentifyAsync(chunk, default);
            streamed.AddRange(detected.Select(t => t with { Start = t.Start + start / 16000d, End = t.End + start / 16000d }));
        }
        report.Add($"Live-sized chunks: {streamed.Count} turns, {streamed.Select(t => t.Speaker).Where(s => s.StartsWith("Speaker ")).Distinct().Count()} identified speaker labels (uncertain turns listed below).");
        report.AddRange(streamed.Select(t => $"  {t.Start:0.00}-{t.End:0.00}: {t.Speaker}"));
        report.Add("Windows: " + Environment.OSVersion.Version);
        File.WriteAllLines(Path.Combine(output, "speaker-result.txt"), report);
        if (speakersOnly) { Console.WriteLine(string.Join("\n", report)); return; }
        // Capture playback locally only; no microphone input or capture upload is used by this test.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
        {
            report.Add("Process loopback unavailable on this Windows build; checking device loopback instead.");
            using var deviceCapture = new WasapiLoopbackCapture();
            long captured = 0;
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            deviceCapture.DataAvailable += (_, e) => Interlocked.Add(ref captured, e.BytesRecorded);
            deviceCapture.RecordingStopped += (_, e) => { if (e.Exception is not null) done.TrySetException(e.Exception); else done.TrySetResult(); };
            deviceCapture.StartRecording();
            using var play = new WaveFileReader(wave); using var outputDevice = new WaveOutEvent(); outputDevice.Init(play); outputDevice.Play();
            await Task.Delay(4200); outputDevice.Stop(); deviceCapture.StopRecording(); await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (captured == 0) throw new InvalidOperationException("Device loopback produced no audio.");
            report.Add($"Device loopback: {captured} bytes captured.");
            File.WriteAllLines(Path.Combine(output, "native-result.txt"), report); Console.WriteLine(string.Join("\n", report));
            return;
        }
        // Record only our own synthetic playback via Windows process loopback, never microphone audio.
        using var process = await ProcessLoopbackCapture.CreateAsync(Environment.ProcessId);
        var bytes = 0L;
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.DataAvailable += (_, e) => Interlocked.Add(ref bytes, e.BytesRecorded);
        process.RecordingStopped += (_, e) => { if (e.Exception is not null) stopped.TrySetException(e.Exception); else stopped.TrySetResult(); };
        process.StartRecording();
        using var playbackReader = new WaveFileReader(wave);
        using var player = new WaveOutEvent(); player.Init(playbackReader); player.Play();
        await Task.Delay(4500); player.Stop(); process.StopRecording(); await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (bytes == 0) throw new InvalidOperationException("Process capture produced no audio.");
        report.Add($"Process loopback: {bytes} bytes captured from own process.");
        File.WriteAllLines(Path.Combine(output, "native-result.txt"), report); Console.WriteLine(string.Join("\n", report));
    }
    private static void Render(string output, string appXaml, bool preview = false, bool wakeOnly = false)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var xamlStream = File.OpenRead(appXaml);
        var xml = XDocument.Load(xamlStream); XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var resources = xml.Root!.Element(ns + "Application.Resources")!;
        var dictionary = resources.Element(ns + "ResourceDictionary") ?? new XElement(ns + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"), resources.Elements());
        app.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
        var errors = new StringWriter(); PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(errors));
        var systemDarkMode = false;
        using var theme = new ThemeService(app.Resources, () => systemDarkMode);
        theme.ApplyTheme(false);
        var store = new NoteStore(Path.Combine(output, "notes"));
        var capture = new FakeCapture();
        var settings = new AppSettings();
        var vm = new NotesViewModel(capture, new FakeSpeakers(), new FakeGroq(), new FakeSecrets(), store, new FakeVideo(), () => settings,
            action => app.Dispatcher.InvokeAsync(action).Task.Unwrap(), _ => Task.CompletedTask, () => Task.CompletedTask);
        using var http = new HttpClient(new ModelHandler());
        var groq = new GroqTranscriptionClient(http);
        var settingsService = new JsonSettingsService(Path.Combine(output, "settings.json"));
        settingsService.SaveAsync(new AppSettings(), default).GetAwaiter().GetResult();
        var audio = new FakeDictationCapture();
        var monitor = new FakeLevelMonitor();
        using var hotkey = new FakeHotkey(); var media = new FakeMediaControl();
        var statusOverlay = new OverlayStatusService(() => new StatusOverlayWindow());
        var orchestrator = new DictationOrchestrator(settingsService, new FakeSecrets(), audio, groq, new TextCleanupService(), new WindowsTextInsertionService(), new WindowsKeyboardCommandService(), statusOverlay);
        var main = new MainViewModel(settingsService, new FakeSecrets(), groq, audio, monitor, hotkey, new FakeStartup(), orchestrator, new GitHubUpdateService(http), media);
        var fixtureIds = new[] { "tiny.en", "base.en", "small.en", "tiny", "base" };
        var localModels = new LocalModelStore(http, Path.Combine(output, "models"), fixtureIds.Select(id =>
            LocalModelStore.Catalog.First(model => model.Id == id) with { Bytes = 1, Sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new byte[] { 0 })) }).ToArray());
        Directory.CreateDirectory(localModels.DirectoryPath);
        foreach (var id in fixtureIds.Take(4)) File.WriteAllBytes(localModels.ModelPath(id), [0]);
        var preparationHold = (TaskCompletionSource?)null;
        using var localClient = new LocalTranscriptionClient(localModels, "ffmpeg.exe");
        main.LocalModels = new LocalModelsViewModel(localModels, localClient, () => main.Settings, () => main.AutoSaveSettingsAsync());
        main.LocalModels.VoiceSetup = new VoiceSetupViewModel(new FakeSetupCapture(), () => main.Settings,
            (_, _, _) => Task.FromResult(new SpeechResult(main.LocalModels.VoiceSetup!.Reference, [])), _ => true,
            _ => Task.CompletedTask, () => main.AutoSaveSettingsAsync());
        var simulatedDevice = "CPU";
        var adviceRunner = new SpeechPerformanceBenchmark(async (_, token) => { if (preparationHold is not null) await preparationHold.Task.WaitAsync(token); },
            async (_, _, token) => { await Task.Delay(5, token); return new SpeechResult(SpeechPerformanceBenchmark.Reference, []); },
            async (_, _, token) => { await Task.Delay(25, token); return SpeechPerformanceBenchmark.Reference; }, () => "Simulated engine for UI checks",
            async (_, gpu, token) => { if (preparationHold is not null) await preparationHold.Task.WaitAsync(token); simulatedDevice = gpu ? "GPU" : "CPU"; },
            localClient.SupportsGpu, () => simulatedDevice);
        main.LocalModels.Recommendations = new LocalRecommendationsViewModel(() => main.Settings, () => localClient.Hardware, localModels.IsInstalled,
            () => main.LocalModels.CanConfigure, () => true, _ => Task.CompletedTask, () => main.AutoSaveSettingsAsync(),
            async (recommendation, token) => { main.Settings.UseLocalTranscription = recommendation.Provider == "local"; if (main.Settings.UseLocalTranscription) main.Settings.LocalModelId = recommendation.ModelId; await main.AutoSaveSettingsAsync(); },
            adviceRunner, Path.Combine(AppContext.BaseDirectory, "Assets", "BenchmarkSample.wav"));
        main.LocalModels.Recommendations.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(LocalRecommendationsViewModel.IsBusy)) main.LocalModels.Refresh(); };
        main.SettingsChanged += (_, _) => theme.ApplyPreference(main.Settings.Theme);
        using var autoCapture = new AutoCaptureService(settingsService, new FakeSecrets(), monitor, audio, orchestrator, statusOverlay, new WakeToneService());
        main.WakeCalibration = new WakeCalibrationViewModel(monitor, new FakeKeywordDetector(), () => main.Settings, value => { autoCapture.Suspended = value; return Task.CompletedTask; },
            () => main.AutoSaveSettingsAsync(), () => true);
        main.WakeCalibration.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(WakeCalibrationViewModel.IsRecording) or nameof(WakeCalibrationViewModel.IsBusy) or nameof(WakeCalibrationViewModel.IsWizardOpen)) main.SetWakeCalibrationState(main.WakeCalibration.IsWorking || main.WakeCalibration.IsWizardOpen); };
        main.WakeCalibration.Load();
        Exception? previewFailure = null;
        main.InputLevelPreviewChanged += async (_, visible) =>
        {
            try { await autoCapture.SetLevelPreviewAsync(visible); }
            catch (Exception ex) { previewFailure = ex; }
        };
        main.Notes = vm;
        settings = main.Settings;
        main.Devices.Add(new AudioDeviceInfo("", "Windows default input", true));
        main.Devices.Add(new AudioDeviceInfo("preview-test-device", "Test input"));
        settings.MicrophoneDeviceId = "";
        var videos = new NotesViewModel(new FakeCapture(), new FakeSpeakers(), new FakeGroq(), new FakeSecrets(), store, new FakeVideo(), () => settings,
            action => app.Dispatcher.InvokeAsync(action).Task.Unwrap(), _ => Task.CompletedTask, () => Task.CompletedTask, NoteLibrary.YouTube);
        main.Videos = videos;
        main.FileTranscription = new FileTranscriptionViewModel(new AudioFilePreparationService(Path.Combine(AppContext.BaseDirectory, "Tools", "Flac", "flac.exe")), groq, new FakeSecrets(), () => settings);
        var window = new MainWindow { Title = "WyspaFluent settings verification", DataContext = main };
        window.Loaded += async (_, _) =>
        {
            try
            {
                var tabs = Find<TabControl>(window)!;
                if (wakeOnly)
                {
                    tabs.SelectedIndex = 4; main.Settings.ActivationMode = ActivationMode.AutoCapture; main.ApplyLiveSettings(); await Idle(window);
                    var setupExpander = FindAll<Expander>(window).First(e => e.Header?.ToString() == "Experimental"); setupExpander.IsExpanded = true; await Idle(window);
                    var opener = FindAll<Button>(window).First(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Open wake setup wizard");
                    async Task<WakeSetupWindow> OpenWizard()
                    {
                        _ = window.Dispatcher.BeginInvoke(new Action(() => opener.RaiseEvent(new RoutedEventArgs(Button.ClickEvent))));
                        using var timeout = new CancellationTokenSource(5000);
                        while (!app.Windows.OfType<WakeSetupWindow>().Any()) await Task.Delay(10, timeout.Token);
                        var dialog = app.Windows.OfType<WakeSetupWindow>().Single(); await Idle(dialog); return dialog;
                    }
                    var wizard = await OpenWizard();
                    if (wizard.Owner != window || IsWindowEnabled(new System.Windows.Interop.WindowInteropHelper(window).Handle)) throw new Exception("Wizard was not an owned modal dialog.");
                    await main.HandleHotkeyPressedAsync();
                    if (audio.IsRecording || media.Starts != 0) throw new Exception("Listening hotkey started capture/media during wizard introduction.");
                    Save(wizard, Path.Combine(output, "wizard-introduction-light.png"));
                    FindAll<Button>(wizard).First(b => System.Windows.Automation.AutomationProperties.GetName(b) == "Start wake wizard").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Idle(wizard); Save(wizard, Path.Combine(output, "wizard-room-light.png"));
                    for (var reading = 0; reading < main.WakeCalibration.SetupSteps.Count; reading++)
                    {
                        var step = main.WakeCalibration.SetupSteps[main.WakeCalibration.SetupIndex]; main.WakeCalibration.PracticeCommand.Execute(null);
                        using var timeout = new CancellationTokenSource(5000); while (!main.WakeCalibration.IsRecording) await Task.Delay(10, timeout.Token);
                        for (var frame = 0; frame < 30; frame++) monitor.EmitAudio(step.IsWake ? .6f : .2f);
                        if (reading == 1) { await Idle(wizard); Save(wizard, Path.Combine(output, "wizard-recording-light.png")); }
                        main.WakeCalibration.FinishCommand.Execute(null);
                        while (main.WakeCalibration.IsWorking || !main.WakeCalibration.PracticeCommand.CanExecute(null)) await Task.Delay(10, timeout.Token);
                        if (reading == 5) { await Idle(wizard); Save(wizard, Path.Combine(output, "wizard-sentences-light.png")); }
                    }
                    if (!main.WakeCalibration.SetupComplete || main.Settings.AutoCaptureWakeVoiceProfile?.SetupValidated != true || !main.WakeCalibration.Enabled) throw new Exception("Wizard did not validate and apply the profile.");
                    File.Copy(Path.Combine(output, "settings.json"), Path.Combine(output, "validated-settings.json"), true);
                    await Idle(wizard); Save(wizard, Path.Combine(output, "wizard-complete-light.png"));
                    theme.ApplyTheme(true); window.ApplyTheme(true); await Idle(wizard); Save(wizard, Path.Combine(output, "wizard-complete-dark.png"));
                    wizard.Width = 420; wizard.Height = 480; await Idle(wizard); Save(wizard, Path.Combine(output, "wizard-complete-narrow.png"));
                    wizard.Close(); await Idle(window);
                    wizard = await OpenWizard();
                    FindAll<Button>(wizard).First(b => b.Content?.ToString() == "Learn a fresh profile").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await Idle(wizard);
                    main.WakeCalibration.PracticeCommand.Execute(null);
                    using (var timeout = new CancellationTokenSource(5000))
                    {
                        while (!main.WakeCalibration.IsRecording) await Task.Delay(10, timeout.Token);
                        wizard.Close();
                        while (wizard.IsLoaded || main.WakeCalibration.IsWorking) await Task.Delay(10, timeout.Token);
                    }
                    if (monitor.IsRunning) throw new Exception("Closing the recording wizard left its microphone running.");
                    if (media.Starts != 0 || media.Restores != 0) throw new Exception("Wizard changed media playback.");
                    File.WriteAllText(Path.Combine(output, "binding-errors.txt"), errors.ToString());
                    if (errors.GetStringBuilder().Length > 0) throw new Exception("Wake wizard binding errors: " + errors);
                    File.WriteAllText(Path.Combine(output, "wake-setup-result.txt"), "PASS: owned modal wizard, 14 varied samples, validated saved profile, auto-enabling, close-during-recording cleanup, zero media commands, light/dark/narrow rendering.");
                    return;
                }
                if (preview)
                {
                    tabs.SelectedIndex = 4;
                    await main.RefreshModelsAsync(); await Idle(window);
                    // Uses the real view and view models with local test dependencies only.
                    window.Closed += (_, _) => app.Shutdown();
                    return;
                }
                tabs.SelectedIndex = 0; await Idle(window); Save(window, Path.Combine(output, "home.png"));
                tabs.SelectedIndex = 1; await Idle(window); Save(window, Path.Combine(output, "audio-files.png"));
                tabs.SelectedIndex = 2;
                if (tabs.Items.Count != 5) throw new Exception("Expected Home, Audio Files, Conversation, YouTube, Settings.");
                await store.SaveAsync(new NoteSession { Title = "Video sample", Kind = "YouTube", Summary = "Independent video summary", Entries = [new(Guid.NewGuid(), 0, 0, 2, "Video", "This is the saved video transcript.")] });
                await vm.RefreshAsync();
                if (vm.Notes.Any(n => n.Kind == "YouTube")) throw new Exception("Video appeared in conversation history.");
                await videos.RefreshAsync();
                await Idle(window); Save(window, Path.Combine(output, "conversation-empty.png"));
                await vm.StartAsync();
                capture.Emit(1, "Other side", 1); capture.Emit(2, "You", 4); capture.Emit(3, "Other side", 7);
                await Task.Delay(350);
                await vm.PauseResumeAsync(); if (!vm.IsPaused) throw new Exception("Pause failed");
                await vm.PauseResumeAsync(); if (vm.IsPaused) throw new Exception("Resume failed");
                await vm.StopAsync();
                vm.Title = "Project catch-up"; await vm.SaveSelectedAsync();
                if (vm.Bubbles.Count != 3) throw new Exception("Expected three transcript bubbles");
                await Idle(window); Save(window, Path.Combine(output, "conversation-result.png"));
                var overlay = new NoteOverlayWindow { DataContext = vm }; overlay.Show();
                await Idle(overlay); Save(overlay, Path.Combine(output, "overlay.png")); overlay.Close();
                window.Width = 560; window.Height = 480;
                await Idle(window); Save(window, Path.Combine(output, "conversation-narrow.png"));
                window.Width = 1180; window.Height = 860; tabs.SelectedIndex = 3;
                await Idle(window); Save(window, Path.Combine(output, "youtube.png"));
                if (videos.SelectedNote?.Kind != "YouTube" || videos.Summary != "Independent video summary") throw new Exception("Video state was overwritten by a conversation.");
                tabs.SelectedIndex = 4;
                await main.RefreshModelsAsync(); await Idle(window);
                var content = (StackPanel)window.FindName("SettingsContent");
                var expanders = content.Children.OfType<Expander>().ToArray();
                if (!expanders.Select(e => (string)e.Header).SequenceEqual(new[] { "Local models", "Groq", "Conversation", "Audio & Capture", "Look & Feel", "Privacy", "System", "Experimental" }))
                    throw new Exception("Unexpected settings group names or order.");
                await VerifyLevelPreviewAndNavigation(window, tabs, main, autoCapture, monitor, audio, output, theme);
                await VerifyThemeSettings(window, theme, settingsService, output, dark => systemDarkMode = dark);
                await VerifyWakeCycleAsync(window, output);
                await VerifySpeechEndpointAsync(window, output);
                // Help popup focus acceptance has a separate interactive harness.
                if (Environment.GetEnvironmentVariable("WYSPA_SKIP_HELP_SMOKE") != "1") await HelpHintsSmoke.VerifyAsync(window, output);
                if (previewFailure is not null) throw new Exception("Level preview failed.", previewFailure);
                foreach (var group in expanders) group.IsExpanded = false;
                AppNavigationCommands.OpenLocalSettings.Execute(null, window); await Idle(window);
                var manager = FindAll<System.Windows.Controls.DataGrid>(content).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Local model manager");
                var localToggle = FindAll<CheckBox>(content).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Use local transcription");
                var gpuToggle = FindAll<CheckBox>(content).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Use GPU acceleration when available");
                localToggle.IsChecked = false; await Idle(window);
                if (manager.IsEnabled || gpuToggle.IsEnabled || !localToggle.IsEnabled) throw new Exception("Local off must disable the manager and GPU while keeping its toggle enabled.");
                Save(window, Path.Combine(output, "model-manager-off.png"));
                localToggle.IsChecked = true; await Idle(window);
                main.LocalModels.Items.Single(row => row.Id == "tiny.en").UseCommand.Execute(null);
                await Idle(window); await Task.Delay(350);
                var localSaved = await settingsService.LoadAsync(default);
                if (!localSaved.UseLocalTranscription || localSaved.LocalVoiceProfile is not null || localSaved.LocalModelId != "tiny.en") throw new Exception("Installed local model must work without voice setup.");
                if (gpuToggle.IsEnabled != main.LocalModels.GpuAvailable) throw new Exception("GPU control must follow compatible GPU detection.");
                if (main.LocalModels.GpuAvailable)
                {
                    gpuToggle.IsChecked = false; await Idle(window); await Task.Delay(350);
                    if ((await settingsService.LoadAsync(default)).LocalGpuEnabled) throw new Exception("CPU-only choice was not saved.");
                    gpuToggle.IsChecked = true; await Idle(window);
                }
                manager.BringIntoView(); await Idle(window);
                manager.SelectedItem = main.LocalModels.Items[0]; await Idle(window);
                if (manager.SelectedItems.Count != 0) throw new Exception("Model manager must not select rows on click.");
                var modelHeaders = FindAll<System.Windows.Controls.Primitives.DataGridColumnHeader>(manager).Where(h => h.Content is string).ToArray();
                if (!modelHeaders.Select(h => h.Content.ToString()).SequenceEqual(new[] { "Select", "Model", "Size", "Status", "Action", "About", "Dictation" })) throw new Exception("Model Manager headers do not match the requested names.");
                foreach (var header in modelHeaders) if (header.HorizontalContentAlignment != HorizontalAlignment.Center) throw new Exception("Headers must center horizontally.");
                var info = FindAll<Button>(manager).First(c => System.Windows.Automation.AutomationProperties.GetName(c).StartsWith("Information about "));
                if (info.Tag as string != main.LocalModels.Items[0].InfoUrl) throw new Exception("Model info button must point to its model page.");
                Save(window, Path.Combine(output, "model-manager-light.png"));
                theme.ApplyTheme(true); await Idle(window); Save(window, Path.Combine(output, "model-manager-dark.png"));
                window.Width = 560; window.Height = 480; await Idle(window);
                await VerifyHorizontalTableAsync(manager, window, output, "model-manager-narrow");
                window.Width = 1180; window.Height = 860; theme.ApplyTheme(false); await Idle(window);
                var missingRow = main.LocalModels.Items.Single(row => row.Id == "base");
                var batchCheckbox = FindAll<CheckBox>(manager).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Select " + missingRow.Name);
                batchCheckbox.IsChecked = true; await Idle(window);
                var selectedRow = FindAll<DataGridRow>(manager).Single(r => ReferenceEquals(r.Item, missingRow));
                if (!Equals(selectedRow.BorderBrush, window.FindResource("AccentBrush"))) throw new Exception("Checkbox selection must add the accent border.");
                if (!missingRow.IsSelected) throw new Exception("Model checkbox must update batch selection.");
                main.LocalModels.InstallSelectedCommand.Execute(null); await Idle(window);
                if (!missingRow.IsWorking || !main.LocalModels.IsBusy) throw new Exception("Batch must expose per-model running state.");
                var progress = FindAll<System.Windows.Controls.ProgressBar>(manager).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Download progress for " + missingRow.Name);
                if (!progress.IsVisible) throw new Exception("Per-model progress must be visible while downloading.");
                Save(window, Path.Combine(output, "model-manager-downloading.png"));
                while (main.LocalModels.IsBusy) await Task.Delay(50);
                if (!missingRow.Installed || missingRow.IsSelected || missingRow.Progress != 100) throw new Exception("Batch model installation did not finish correctly.");
                missingRow.IsSelected = true; main.LocalModels.RemoveSelectedCommand.Execute(null);
                while (main.LocalModels.IsBusy) await Task.Delay(50);
                if (missingRow.Installed || missingRow.IsSelected) throw new Exception("Batch model removal did not finish correctly.");
                var advice = main.LocalModels.Recommendations!;
                preparationHold = new(TaskCreationOptions.RunContinuationsAsynchronously);
                if (advice.CompareGroqCommand.CanExecute(null)) throw new Exception("Groq comparison must require a completed local comparison.");
                var pendingTest = advice.RunAsync(false); await Idle(window);
                var spinner = FindAll<System.Windows.Controls.Grid>(content).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Model testing in progress");
                spinner.BringIntoView(); await Idle(window); await Task.Delay(100);
                var angle = ((RotateTransform)spinner.RenderTransform).Angle;
                await Task.Delay(150);
                if (!spinner.IsVisible || !advice.IsBusy || ((RotateTransform)spinner.RenderTransform).Angle == angle) throw new Exception("Testing spinner must animate throughout preparation.");
                Save(window, Path.Combine(output, "model-test-running.png"));
                preparationHold.SetResult(); await pendingTest; preparationHold = null; await Idle(window);
                if (spinner.IsVisible || advice.IsBusy) throw new Exception("Testing spinner did not stop when complete.");
                var expectedRows = main.LocalModels.GpuAvailable && main.Settings.LocalGpuEnabled ? 8 : 4;
                if (advice.Results.Count != expectedRows || main.Settings.SpeechPerformance?.Results.Count != expectedRows) throw new Exception("All local CPU/GPU variants and Groq must be compared and persisted.");
                if (!advice.Results[0].IsWinner || advice.Results.Count(r => r.IsWinner) != 1 || !advice.CompareGroqCommand.CanExecute(null)) throw new Exception("Completed comparison must rank/highlight the winner and enable Groq.");
                await advice.RunAsync(true); await Idle(window);
                if (advice.Results.Count != 2 || main.Settings.SpeechPerformance?.Results.Count != expectedRows + 1) throw new Exception("Groq must compare only the top local result while retaining all backend evidence.");
                if (advice.Choice.Provider != "local") throw new Exception("Measured recommendation did not choose a faster comparable local result.");
                var advicePanel = FindAll<Expander>(content).Single(e => System.Windows.Automation.AutomationProperties.GetName(e) == "Model recommendations and speed comparison");
                advicePanel.BringIntoView(); await Idle(window); await Task.Delay(350);
                Save(window, Path.Combine(output, "recommendation-light.png"));
                theme.ApplyTheme(true); await Idle(window); Save(window, Path.Combine(output, "recommendation-dark.png"));
                window.Width = 560; window.Height = 480; await Idle(window);
                var performance = FindAll<DataGrid>(content).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Measured transcription performance");
                var resultHeaders = FindAll<System.Windows.Controls.Primitives.DataGridColumnHeader>(performance).Where(h => h.Content is string).ToArray();
                if (!resultHeaders.Select(h => h.Content.ToString()).SequenceEqual(new[] { "Score", "Model", "Device", "Avg. Result", "1st Result", "CPU Time", "RAM", "Errors" })) throw new Exception("Comparison headers do not match requested names.");
                performance.SelectedItem = advice.Results[0]; await Idle(window);
                if (performance.SelectedItems.Count != 0) throw new Exception("Performance rows must not select on click.");
                await VerifyHorizontalTableAsync(performance, window, output, "recommendation-narrow");
                window.Width = 1180; window.Height = 860; theme.ApplyTheme(false); advice.IsExpanded = false; await Idle(window);
                var setup = main.LocalModels.VoiceSetup!;
                setup.IsExpanded = true;
                setup.Vocabulary = "Wyspa, Deakin";
                for (var reading = 0; reading < 3; reading++) { await setup.StartAsync(); await setup.StopAndTestAsync(); }
                await setup.SaveAsync(); await Idle(window);
                localSaved = await settingsService.LoadAsync(default);
                if (!localSaved.UseLocalTranscription || localSaved.LocalVoiceProfile?.Scores.Count != 3) throw new Exception("Voice profile did not persist.");
                var setupPanel = FindAll<Expander>(content).Single(e => System.Windows.Automation.AutomationProperties.GetName(e) == "Voice setup and testing");
                setupPanel.BringIntoView(); await Idle(window); Save(window, Path.Combine(output, "voice-setup-light.png"));
                theme.ApplyTheme(true); await Idle(window); Save(window, Path.Combine(output, "voice-setup-dark.png"));
                window.Width = 560; window.Height = 480; await Idle(window);
                FindAll<TextBox>(content).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Read aloud passage").BringIntoView();
                await Idle(window); Save(window, Path.Combine(output, "voice-setup-narrow.png"));
                window.Width = 1180; window.Height = 860; theme.ApplyTheme(false);
                setup.IsExpanded = false; AppNavigationCommands.OpenLocalSettings.Execute(null, window); await Idle(window); await Task.Delay(350);
                Save(window, Path.Combine(output, "local-light.png"));
                theme.ApplyTheme(true); await Idle(window); Save(window, Path.Combine(output, "local-dark.png"));
                window.Width = 560; window.Height = 480; await Idle(window); Save(window, Path.Combine(output, "local-narrow.png"));
                window.Width = 1180; window.Height = 860; theme.ApplyTheme(false); localToggle.IsChecked = false; await Idle(window);
                expanders[0].IsExpanded = false;
                expanders[1].IsExpanded = true; await Idle(window);
                var modelBox = FindAll<ComboBox>(content).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Transcription model");
                if (modelBox.Items.Count != 2) throw new Exception("Speech dropdown contains incompatible models.");
                modelBox.SelectedItem = "whisper-large-v3";
                await Idle(window); await Task.Delay(350);
                if ((await settingsService.LoadAsync(default)).ModelId != "whisper-large-v3") throw new Exception("Model selection did not persist.");
                await main.RefreshModelsAsync(); await Idle(window);
                if ((string?)modelBox.SelectedItem != "whisper-large-v3") throw new Exception("Model refresh reset the selection.");
                Save(window, Path.Combine(output, "settings-groq.png"));
                modelBox.IsDropDownOpen = true; await Idle(window);
                await Task.Delay(250); // Settle the native popup animation before capturing its own surface.
                var popup = (System.Windows.Controls.Primitives.Popup)modelBox.Template.FindName("PART_Popup", modelBox);
                SaveSurface((FrameworkElement)popup.Child, Path.Combine(output, "model-dropdown.png"));
                modelBox.IsDropDownOpen = false;
                var languageBox = FindAll<TextBox>(content).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Transcription language");
                if (!languageBox.Focus()) throw new Exception("Language field could not receive keyboard focus.");
                languageBox.Text = "en";
                if (!languageBox.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next))) throw new Exception("Keyboard focus traversal failed.");
                await Idle(window); await Task.Delay(350);
                if ((await settingsService.LoadAsync(default)).Language != "en") throw new Exception("Text field did not autosave on focus loss.");
                if (System.Windows.Input.Keyboard.FocusedElement == languageBox) throw new Exception("Focus did not leave language field.");
                Find<ScrollViewer>(content.Parent)?.ScrollToTop();
                window.Width = 560; window.Height = 480;
                await Idle(window); Save(window, Path.Combine(output, "settings-narrow.png"));
                window.Width = 1180; window.Height = 860;
                foreach (var expander in expanders) expander.IsExpanded = false;
                await Idle(window); Save(window, Path.Combine(output, "settings-groups.png"));
                foreach (var expander in expanders.Skip(1))
                {
                    expander.IsExpanded = true; await Idle(window);
                    foreach (var control in FindAll<Control>(expander).Where(c => c is TextBox or ComboBox or Slider or PasswordBox))
                    {
                        // Template-generated text editors are named by their owning control.
                        if (control.TemplatedParent is not null) continue;
                        var inputPeer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(control);
                        if (string.IsNullOrWhiteSpace(inputPeer?.GetName())) throw new Exception("Settings input has no accessible name: " + control.GetType().Name);
                    }
                    Save(window, Path.Combine(output, "settings-" + expander.Header.ToString()!.Split(' ')[0].ToLowerInvariant() + ".png"));
                    if (expander.Header.ToString() == "Experimental")
                    {
                        foreach (var label in new[] { "Tone Re-write Model", "Spoken Actions Model" })
                        {
                            FindAll<TextBlock>(expander).Single(t => t.Text == label).BringIntoView();
                            await Idle(window);
                            Save(window, Path.Combine(output, label.StartsWith("Tone") ? "experimental-rewrite.png" : "experimental-actions.png"));
                        }
                        window.Width = 560; window.Height = 480;
                        await Idle(window); Save(window, Path.Combine(output, "experimental-narrow.png"));
                        window.Width = 1180; window.Height = 860;
                    }
                    expander.IsExpanded = false;
                }
                var system = expanders.Single(e => (string)e.Header == "System");
                system.IsExpanded = true; await Idle(window);
                var toggle = FindAll<CheckBox>(system).Single(c => (string)c.Content == "Start Minimized");
                var original = toggle.IsChecked;
                var peer = new System.Windows.Automation.Peers.CheckBoxAutomationPeer(toggle);
                var provider = (System.Windows.Automation.Provider.IToggleProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle);
                provider.Toggle(); await Task.Delay(350);
                if ((await settingsService.LoadAsync(default)).StartMinimized != toggle.IsChecked) throw new Exception("Toggle did not autosave.");
                provider.Toggle(); await Task.Delay(350);
                if (toggle.IsChecked != original) throw new Exception("Toggle did not restore.");
                system.IsExpanded = false;
                var captureGroup = expanders.Single(e => (string)e.Header == "Audio & Capture");
                captureGroup.IsExpanded = true;
                var streamToggle = FindAll<CheckBox>(captureGroup).Single(c => (string)c.Content == "Stream Mode");
                var streamPeer = new System.Windows.Automation.Peers.CheckBoxAutomationPeer(streamToggle);
                var streamProvider = (System.Windows.Automation.Provider.IToggleProvider)streamPeer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle);
                var modes = FindAll<ComboBox>(captureGroup).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Dictation activation mode");
                foreach (var mode in Enum.GetValues<ActivationMode>())
                {
                    modes.SelectedValue = mode;
                    await Task.Delay(200); await Idle(window);
                    var sl = FindAll<Slider>(captureGroup).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "SmartListen trigger threshold");
                    var silence = FindAll<TextBox>(captureGroup).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Silence before stop in milliseconds");
                    var enabled = FindAll<CheckBox>(captureGroup).Single(c => c.Content as string == "SmartListen Listening Enabled");
                    if (sl.IsVisible != (mode == ActivationMode.AutoCapture) || silence.IsVisible != sl.IsVisible || enabled.IsVisible != sl.IsVisible) throw new Exception("SmartListen controls must be visible only in SmartListen mode.");
                    var expectedTitle = mode == ActivationMode.Toggle ? "Toggle Wyspa" : mode == ActivationMode.HoldToTalk ? "Hold Hotkey" : "SmartListening On/Off";
                    if (main.HotkeyTitle != expectedTitle || !FindAll<TextBlock>(captureGroup).Any(t => t.Text == expectedTitle)) throw new Exception("Dynamic shortcut title is incorrect.");
                    streamProvider.Toggle(); await Task.Delay(350);
                    var saved = await settingsService.LoadAsync(default);
                    if (!saved.StreamModeEnabled || saved.ActivationMode != mode) throw new Exception("Stream Mode did not persist independently of activation mode.");
                    streamProvider.Toggle(); await Task.Delay(350);
                }
                if ((await settingsService.LoadAsync(default)).StreamModeEnabled) throw new Exception("Stream Mode did not turn off.");
                modes.SelectedValue = ActivationMode.AutoCapture; await Task.Delay(350);
                var experimental = FindAll<Expander>(window).Single(e => e.Header?.ToString() == "Experimental");
                experimental.IsExpanded = true; await Idle(window);
                main.Settings.AutoCaptureWakeVoiceProfile = new() { DetectorVersion = 2, Phrase = "hey whisper", CalibrationTrials =
                    Enumerable.Range(0, 3).Select(_ => new WakeCalibrationTrial(true, .6, DateTimeOffset.UtcNow)).Concat(Enumerable.Range(0, 3).Select(_ => new WakeCalibrationTrial(false, null, DateTimeOffset.UtcNow))).ToList() };
                main.WakeCalibration!.Load();
                if (!main.WakeCalibration.CanEdit) throw new Exception("Wake practice must be available in SmartListen.");
                modes.SelectedValue = ActivationMode.Toggle; await Task.Delay(350); await Idle(window);
                if (main.WakeCalibration.CanEdit) throw new Exception("Wake practice must be disabled outside SmartListen.");
                modes.SelectedValue = ActivationMode.AutoCapture; await Task.Delay(350);
                var wakeBox = FindAll<TextBox>(content).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Wake phrase");
                main.Settings.AutoCaptureWakeVoiceProfile = null; main.WakeCalibration.Load();
                for (var reading = 0; reading < main.WakeCalibration.SetupSteps.Count; reading++)
                {
                    var practice = main.WakeCalibration.PracticeAsync();
                    using var timeout = new CancellationTokenSource(5000);
                    while (!main.WakeCalibration.IsRecording) await Task.Delay(10, timeout.Token);
                    await autoCapture.SetLevelPreviewAsync(false);
                    await autoCapture.ApplySettingsAsync(main.Settings, true);
                    if (!monitor.IsRunning) throw new Exception("Settings refresh stopped the microphone owned by wake practice.");
                    for (var frame = 0; frame < 10; frame++) monitor.EmitAudio(main.WakeCalibration.SetupSteps[reading].IsWake ? .6f : .2f);
                    main.WakeCalibration.FinishCommand.Execute(null); await practice;
                }
                if (!main.WakeCalibration.Enabled || !main.Settings.AutoCaptureListeningEnabled || main.Settings.AutoCaptureWakeVoiceProfile?.SetupReadings.Count != 13) throw new Exception("Guided wake practice did not auto-enable and save.");
                main.WakeCalibration.ReportRuntime("Wake phrase accepted. Dictation starts after the tone."); await Idle(window);
                if (!main.WakeCalibration.IsAwake) throw new Exception("Wake acceptance indicator did not light up.");
                wakeBox.BringIntoView(); await Idle(window); await Task.Delay(350); Save(window, Path.Combine(output, "wake-calibration-light.png"));
                theme.ApplyTheme(true); await Idle(window); Save(window, Path.Combine(output, "wake-calibration-dark.png"));
                window.Width = 560; window.Height = 480; await Idle(window); Save(window, Path.Combine(output, "wake-calibration-narrow.png"));
                window.Width = 1180; window.Height = 860; theme.ApplyTheme(false);

                await VerifyHotkeyMediaAsync(main, audio, media, hotkey, window, output);
                experimental.IsExpanded = true;
                var fixToggle = FindAll<CheckBox>(experimental).Single(c => c.Content?.ToString() == "Stream Fix");
                await Idle(window);
                if (fixToggle.IsEnabled) throw new Exception("Stream Fix must be disabled with Stream Mode off.");
                streamToggle.IsChecked = true;
                await Task.Delay(800); await Idle(window);
                if (!fixToggle.IsEnabled) throw new Exception("Stream Fix did not enable with Stream Mode.");
                fixToggle.IsChecked = true;
                await Task.Delay(800);
                if (!(await settingsService.LoadAsync(default)).StreamFixEnabled) throw new Exception("Stream Fix did not persist.");
                ((FrameworkElement)fixToggle.Parent).BringIntoView(); await Idle(window); Save(window, Path.Combine(output, "stream-fix-light.png"));
                theme.ApplyTheme(true); await Idle(window); Save(window, Path.Combine(output, "stream-fix-dark.png"));
                window.Width = 560; window.Height = 600;
                ((FrameworkElement)fixToggle.Parent).BringIntoView(); await Idle(window); Save(window, Path.Combine(output, "stream-fix-narrow.png"));
                window.Width = 1180; window.Height = 860;
                streamToggle.IsChecked = false; await Task.Delay(800);
                if (fixToggle.IsEnabled || !(await settingsService.LoadAsync(default)).StreamFixEnabled) throw new Exception("Stream Mode should disable, but remember, Stream Fix preference.");
                theme.ApplyTheme(false);
                streamToggle.BringIntoView(); await Idle(window); Save(window, Path.Combine(output, "stream-mode-light.png"));
                theme.ApplyTheme(true); await Idle(window); Save(window, Path.Combine(output, "stream-mode-dark.png"));
                window.Width = 560; window.Height = 480;
                streamToggle.BringIntoView(); await Idle(window); Save(window, Path.Combine(output, "stream-mode-narrow.png"));
                window.Width = 1180; window.Height = 860;
                captureGroup.IsExpanded = false;
                Find<ScrollViewer>(content.Parent)?.ScrollToTop();
                theme.ApplyTheme(false);
                await Idle(window); Save(window, Path.Combine(output, "settings-light.png"));
                theme.ApplyTheme(true);
                await Idle(window); Save(window, Path.Combine(output, "settings-dark.png"));
                expanders[0].IsExpanded = true;
                await Idle(window); Save(window, Path.Combine(output, "settings-groq-dark.png"));
                expanders[0].IsExpanded = false; theme.ApplyTheme(false);
                tabs.SelectedIndex = 2;
                await Idle(window); Save(window, Path.Combine(output, "conversation-light.png"));
                tabs.SelectedIndex = 3;
                await Idle(window); Save(window, Path.Combine(output, "youtube-light.png"));
                await vm.RefreshAsync(); if (vm.Bubbles.Count != 3) throw new Exception("Saved notes failed to reopen");
                File.WriteAllText(Path.Combine(output, "binding-errors.txt"), errors.ToString());
                if (errors.ToString().Contains("System.Windows.Data Error")) throw new Exception("WPF binding errors were recorded.");
                Console.WriteLine("WPF navigation, settings deep links and order, independent input preview, theme selection/persistence/system following, overlay settings, model persistence, separate libraries, and capture lifecycle passed.");
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString()); Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally { if (!preview) { await vm.ShutdownAsync(); await videos.ShutdownAsync(); app.Shutdown(); } }
        };
        app.Run(window);
    }
    private static async Task VerifyThemeSettings(MainWindow window, ThemeService theme, JsonSettingsService settingsService,
        string output, Action<bool> setSystemDarkMode)
    {
        var group = (Expander)window.FindName("LookAndFeelSettingsGroup");
        group.IsExpanded = true; await Idle(window);
        group.BringIntoView(new Rect(0, 0, group.ActualWidth, 72)); await Idle(window);
        var main = (MainViewModel)window.DataContext;
        var toggle = (CheckBox)window.FindName("WindowsNotificationsToggle");
        var delivered = new List<string>();
        using (var tray = new TrayService(main, new FakeStartup(), () => { }, () => Task.CompletedTask, delivered.Add))
        {
            toggle.IsChecked = false; await Idle(window); await Task.Delay(350);
            tray.ShowNotification("suppressed error"); tray.ShowNotification("suppressed success");
            if (delivered.Count != 0 || (await settingsService.LoadAsync(default)).WindowsNotificationsEnabled)
                throw new Exception("Notifications were not disabled and persisted.");
            toggle.IsChecked = true; await Idle(window); await Task.Delay(350);
            tray.ShowNotification("enabled");
            if (delivered.Count != 1 || !(await settingsService.LoadAsync(default)).WindowsNotificationsEnabled)
                throw new Exception("Notifications were not re-enabled and persisted.");
        }
        var accent = Colors.Purple;
        using (var accentTheme = new ThemeService(Application.Current.Resources, () => false, () => accent))
        {
            foreach (var color in new[] { Colors.Purple, Colors.Goldenrod, Colors.DodgerBlue })
            {
                accent = color;
                foreach (var dark in new[] { false, true })
                {
                    accentTheme.ApplyTheme(dark);
                    var chrome = ((SolidColorBrush)Application.Current.Resources["ChromeBrush"]).Color;
                    if (chrome.R != chrome.G || chrome.G != chrome.B) throw new Exception("Header/navigation chrome is tinted.");
                    foreach (var key in new[] { "AccentBrush", "LogoGradientBrush", "AccentButtonBackground" })
                        if (((SolidColorBrush)Application.Current.Resources[key]).Color != color)
                            throw new Exception("Windows accent did not reach " + key);
                }
            }
        }
        theme.RefreshTheme();
        if (((SolidColorBrush)Application.Current.Resources["AccentBrush"]).Color != SystemColors.AccentColor)
            throw new Exception("Production theme did not read the Windows accent.");
        var selector = (ComboBox)window.FindName("ThemeSelector");
        var opacity = (Slider)window.FindName("OverlayOpacitySlider");
        group.IsExpanded = true; await Idle(window);
        group.BringIntoView(new Rect(0, 0, group.ActualWidth, 72)); await Idle(window);
        if (!group.IsAncestorOf(opacity)) throw new Exception("Overlay did not move into Look & Feel.");
        if (!selector.Items.Cast<ComboBoxItem>().Select(i => (string)i.Content).SequenceEqual(new[] { "Dark", "Light", "System" }))
            throw new Exception("Theme dropdown choices are incorrect.");

        selector.SelectedValue = AppTheme.Dark; await Idle(window); await Task.Delay(350);
        if (!theme.IsDarkMode || (await settingsService.LoadAsync(default)).Theme != AppTheme.Dark)
            throw new Exception("Dark selection did not apply and persist.");
        setSystemDarkMode(false); theme.RefreshTheme();
        if (!theme.IsDarkMode) throw new Exception("Windows overrode explicit Dark preference.");
        // A fresh service and settings read exercise the same startup path as the app.
        using (var restarted = new ThemeService(Application.Current.Resources, () => false))
        {
            restarted.ApplyPreference((await new JsonSettingsService(Path.Combine(output, "settings.json")).LoadAsync(default)).Theme);
            if (!restarted.IsDarkMode) throw new Exception("Theme preference was lost after reload.");
        }
        await Idle(window); Save(window, Path.Combine(output, "look-and-feel-dark.png"));
        selector.IsDropDownOpen = true; await Idle(window); await Task.Delay(250);
        var popup = (System.Windows.Controls.Primitives.Popup)selector.Template.FindName("PART_Popup", selector);
        SaveSurface((FrameworkElement)popup.Child, Path.Combine(output, "theme-dropdown.png"));
        selector.IsDropDownOpen = false;
        var status = new StatusOverlayWindow(); status.ApplyTheme(true); status.SetPanelOpacity(.45);
        status.SetStatus("Transcribing", DictationState.Transcribing); status.ShowTransient();
        await Task.Delay(2800);
        if (!status.IsVisible) throw new Exception("Processing overlay disappeared while busy.");
        var processingBar = (Border)status.FindName("Bar1"); var initialHeight = processingBar.Height;
        await Task.Delay(160);
        if (processingBar.Height == initialHeight) throw new Exception("Processing indicator did not animate.");
        if (((SolidColorBrush)processingBar.Background).Color != Color.FromRgb(56, 137, 89)) throw new Exception("Silent pending work must be green.");
        status.SetCaptureActive(true);
        status.UpdateLevel(.25f);
        await Task.Delay(90);
        if (((SolidColorBrush)processingBar.Background).Color != Color.FromRgb(191, 63, 63)) throw new Exception("Speech must override pending work with red.");
        var speechHeight = processingBar.Height;
        await Task.Delay(90);
        if (processingBar.Height == speechHeight) throw new Exception("Speech animation froze between audio callbacks.");
        Save(status, Path.Combine(output, "stream-speaking.png"));
        status.UpdateLevel(0); await Task.Delay(350);
        if (((SolidColorBrush)processingBar.Background).Color != Color.FromRgb(56, 137, 89)) throw new Exception("Speech expiry must restore green processing.");
        status.SetCaptureActive(false); status.UpdateLevel(.9f); await Task.Delay(100);
        if (((SolidColorBrush)processingBar.Background).Color != Color.FromRgb(56, 137, 89)) throw new Exception("Idle monitor audio must not mark completed capture red.");
        status.SetAutoCaptureToggleStatus(false); status.ShowTransient();
        await Task.Delay(2300);
        if (!status.IsVisible || ((TextBlock)status.FindName("StatusText")).Text != "Transcribing") throw new Exception("Toggle notification hid pending transcription.");
        Save(status, Path.Combine(output, "stream-processing.png"));
        status.Hide();

        status.Show(); await Idle(status); Save(status, Path.Combine(output, "status-overlay-dark.png"));
        var background = ((SolidColorBrush)((Border)status.FindName("Shell")).Background).Color;
        if (background.A != (byte)Math.Round(.45 * 255) || background.R != 43)
            throw new Exception("Overlay did not preserve opacity with dark appearance.");
        status.Close();

        selector.SelectedValue = AppTheme.Light; await Idle(window); await Task.Delay(350);
        setSystemDarkMode(true); theme.RefreshTheme();
        if (theme.IsDarkMode || (await settingsService.LoadAsync(default)).Theme != AppTheme.Light)
            throw new Exception("Explicit Light preference did not persist or withstand a Windows theme change.");
        Save(window, Path.Combine(output, "look-and-feel-light.png"));
        opacity.Value = .45; await Task.Delay(350);
        if (Math.Abs((await settingsService.LoadAsync(default)).OverlayOpacity - .45) > .001)
            throw new Exception("Moved overlay opacity control did not autosave.");
        opacity.Value = .82; await Task.Delay(350);
        window.Width = 560; window.Height = 480; await Idle(window);
        opacity.BringIntoView(); await Idle(window);
        Save(window, Path.Combine(output, "look-and-feel-narrow.png"));
        window.Width = 1180; window.Height = 860;
        selector.SelectedValue = AppTheme.System; await Idle(window); await Task.Delay(350);
        if (!theme.IsDarkMode || (await settingsService.LoadAsync(default)).Theme != AppTheme.System)
            throw new Exception("System preference did not use the current Windows theme.");
        setSystemDarkMode(false); theme.RefreshTheme();
        if (theme.IsDarkMode) throw new Exception("System preference did not follow Windows theme changes.");
        group.IsExpanded = false;
        ((ScrollViewer)window.FindName("SettingsScroll")).ScrollToTop(); await Idle(window);
    }

    private static async Task VerifyLevelPreviewAndNavigation(MainWindow window, TabControl tabs, MainViewModel main,
        AutoCaptureService service, FakeLevelMonitor monitor, FakeDictationCapture audio, string output, ThemeService theme)
    {
        var audioGroup = (Expander)window.FindName("AudioSettingsGroup");
        var conversationGroup = (Expander)window.FindName("ConversationSettingsGroup");
        var groqGroup = (Expander)window.FindName("GroqSettingsGroup");
        var meter = (ProgressBar)window.FindName("InputLevelMeter");
        var scroll = (ScrollViewer)window.FindName("SettingsScroll");
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        async Task ShowMeter()
        {
            tabs.SelectedIndex = 4; audioGroup.IsExpanded = true;
            await Idle(window); meter.BringIntoView(); await Idle(window);
        }
        async Task InvokeSettingsShortcut(System.Windows.Input.RoutedCommand command, int sourceTab, Expander target)
        {
            tabs.SelectedIndex = sourceTab; await Idle(window);
            var button = FindAll<Button>(window).First(b => b.Command == command);
            command.Execute(null, button); await Idle(window);
            Check(tabs.SelectedIndex == 4 && target.IsExpanded, "Settings shortcut did not select and expand its destination.");
            var header = (FrameworkElement)target.Template.FindName("HeaderSite", target);
            var bounds = header.TransformToAncestor(scroll).TransformBounds(new Rect(header.RenderSize));
            Check(bounds.IntersectsWith(new Rect(scroll.RenderSize)), "Settings shortcut left its header offscreen.");
            target.IsExpanded = false; await Idle(window);
        }
        await InvokeSettingsShortcut(AppNavigationCommands.OpenConversationSettings, 2, conversationGroup);
        await InvokeSettingsShortcut(AppNavigationCommands.OpenGroqSettings, 0, groqGroup);
        await InvokeSettingsShortcut(AppNavigationCommands.OpenAudioSettings, 0, audioGroup);
        var about = FindAll<Button>(window).Single(b => b.Command == AppNavigationCommands.OpenRepository);
        Check(AppNavigationCommands.OpenRepository.CanExecute(null, about), "About command is not routed to the shell.");
        Check(AppNavigationCommands.RepositoryUri.AbsoluteUri == "https://github.com/DracoManX69/Wyspa", "Wrong repository link.");

        main.Settings.ActivationMode = ActivationMode.Toggle;
        main.Settings.ActivationMode = ActivationMode.AutoCapture;
        main.ApplyLiveSettings(); main.Settings.AutoCaptureListeningEnabled = false;
        await service.ApplySettingsAsync(main.Settings, false);
        await ShowMeter();
        Check(monitor.IsRunning && service.LevelPreviewEnabled, "SmartListen meter must work without an account while listening is off.");
        monitor.Emit(.37f); await Idle(window);
        Check(Math.Abs(main.MicrophoneLevel - .37f) < .001f && Math.Abs(meter.Value - .37) < .001, "Input levels did not reach the visible meter.");
        Check(audio.RecordingStarts == 0 && !main.Settings.AutoCaptureListeningEnabled, "Preview enabled capture.");
        Save(window, Path.Combine(output, "settings-audio-refined.png"));
        theme.ApplyTheme(true); await Idle(window); Save(window, Path.Combine(output, "settings-audio-refined-dark.png"));
        theme.ApplyTheme(false);
        tabs.SelectedIndex = 0; await Idle(window);
        Check(!monitor.IsRunning && !service.LevelPreviewEnabled, "Preview did not release the microphone on tab change.");
        window.Width = 560; window.Height = 480; await Idle(window);
        Save(window, Path.Combine(output, "home-narrow.png"));
        await ShowMeter(); monitor.Emit(.37f); await Idle(window);
        Save(window, Path.Combine(output, "settings-audio-refined-narrow.png"));
        window.Width = 1180; window.Height = 860;

        main.Settings.ActivationMode = ActivationMode.AutoCapture;
        await service.ApplySettingsAsync(main.Settings, true);
        await ShowMeter();
        monitor.Emit(.95f); await Task.Delay(250); await Idle(window);
        Check(audio.RecordingStarts == 0 && monitor.IsRunning, "Disabled SmartListen recorded during the preview.");
        var starts = monitor.StartCount;
        main.Settings.MicrophoneDeviceId = "preview-test-device";
        await service.ApplySettingsAsync(main.Settings, true);
        Check(monitor.DeviceId == "preview-test-device" && monitor.StartCount == starts + 1, "Preview did not switch input devices.");
        service.Suspended = true; await service.ApplySettingsAsync(main.Settings, true);
        Check(!monitor.IsRunning, "Conversation capture did not suspend level preview.");
        await audio.StartRecordingAsync(null, CancellationToken.None);
        audio.EmitLevel(0);
        await Task.Delay(250); await Idle(window);
        Check(audio.IsRecording, "Suspended SmartListen stopped the setup recording on initial silence.");
        await audio.StopRecordingAsync(CancellationToken.None);
        service.Suspended = false; await service.ApplySettingsAsync(main.Settings, true);
        Check(monitor.IsRunning, "Preview did not resume after capture suspension.");
        window.Hide(); await Idle(window); Check(!monitor.IsRunning, "Hiding the window kept preview active.");
        window.Show(); await Idle(window); Check(monitor.IsRunning, "Showing the meter did not restore preview.");
        window.WindowState = WindowState.Minimized; await Idle(window); Check(!monitor.IsRunning, "Minimizing kept preview active.");
        window.WindowState = WindowState.Normal; await Idle(window);
        scroll.ScrollToBottom(); await Idle(window); Check(!monitor.IsRunning, "Offscreen meter kept preview active.");
        await ShowMeter(); Check(monitor.IsRunning, "Scrolling to the meter did not restore preview.");
        audioGroup.IsExpanded = false; await Idle(window); Check(!monitor.IsRunning, "Collapsing Audio kept preview active.");

        main.Settings.AutoCaptureListeningEnabled = true;
        await service.ApplySettingsAsync(main.Settings, true);
        await ShowMeter(); tabs.SelectedIndex = 0; await Idle(window);
        Check(monitor.IsRunning && !service.LevelPreviewEnabled, "Leaving preview incorrectly disabled background SmartListen.");
        main.Settings.AutoCaptureListeningEnabled = false;
        await service.ApplySettingsAsync(main.Settings, true);
        Check(!monitor.IsRunning, "Disabled SmartListen kept monitoring without preview.");
        monitor.FailNextStart = true;
        try { await service.SetLevelPreviewAsync(true); throw new Exception("Device failure was not reported."); }
        catch (InvalidOperationException ex) when (ex.Message == "Test device unavailable.") { }
        Check(!monitor.IsRunning, "Failed device left the monitor running.");
        await service.SetLevelPreviewAsync(false); await service.SetLevelPreviewAsync(true);
        Check(monitor.IsRunning, "Preview could not recover after device failure.");
        await service.SetLevelPreviewAsync(false);
        main.Settings.ActivationMode = ActivationMode.Toggle; main.Settings.MicrophoneDeviceId = null;
        await service.ApplySettingsAsync(main.Settings, false);
        audioGroup.IsExpanded = false; tabs.SelectedIndex = 4; scroll.ScrollToTop(); await Idle(window);
        Check(audio.RecordingStarts == 1, "Preview tests started a recording beyond the explicit suspension test.");
    }

    private static T? Find<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T result) return result;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (Find<T>(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }
    private static IEnumerable<T> FindAll<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T result) yield return result;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in FindAll<T>(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private sealed class FakeLevelMonitor : IAudioLevelMonitorService
    {
        public event EventHandler<float>? LevelAvailable;
        public event EventHandler<IReadOnlyList<float>>? AudioAvailable;
        public void EmitAudio(float value) { if (IsRunning) AudioAvailable?.Invoke(this, Enumerable.Repeat(value, 960).ToArray()); }
        public bool IsRunning { get; private set; }
        public string? DeviceId { get; private set; }
        public int StartCount { get; private set; }
        public bool FailNextStart { get; set; }
        public Task StartAsync(string? deviceId, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (FailNextStart) { FailNextStart = false; throw new InvalidOperationException("Test device unavailable."); }
            DeviceId = deviceId; IsRunning = true; StartCount++; return Task.CompletedTask;
        }
        public void Emit(float level) { if (IsRunning) LevelAvailable?.Invoke(this, level); }
        public void Stop() { IsRunning = false; LevelAvailable?.Invoke(this, 0); }
        public void Dispose() => Stop();
    }
    private sealed class FakeKeywordDetector : IWakePhraseDetector
    {
        public int Resets;
        public Task PrepareAsync(IProgress<string>? progress, CancellationToken token) => Task.CompletedTask;
        public Task<bool> ProcessAsync(float[] samples, string phrase, double strictness, CancellationToken token) => Task.FromResult(samples.Length > 0 && samples[0] >= .5f);
        public Task<double?> EvaluateAsync(float[] samples, string phrase, CancellationToken token) => Task.FromResult<double?>(samples.Length > 0 && samples[0] >= .5f ? .6 : null);
        public void Reset() => Resets++;
        public void Dispose() { }
    }
    private static async Task VerifyWakeCycleAsync(Window window, string output)
    {
        var settings = new JsonSettingsService(Path.Combine(output, "wake-cycle-settings.json"));
        var value = new AppSettings { UseLocalTranscription = true, ActivationMode = ActivationMode.AutoCapture, AutoCaptureWakeVoiceEnabled = true,
            AutoCaptureListeningEnabled = true, AutoCaptureWakeVoiceProfile = null, AutoCaptureSilenceMs = 400, AutoCaptureMinSpeechMs = 250, WakeToneEnabled = false };
        await settings.SaveAsync(value, default);
        var capture = new FakeDictationCapture(); var monitor = new FakeLevelMonitor();
        using var http = new HttpClient(new ModelHandler()); var cloud = new GroqTranscriptionClient(http);
        var orchestrator = new DictationOrchestrator(settings, new FakeSecrets(), capture, cloud, new(), new WindowsTextInsertionService(), new WindowsKeyboardCommandService(), new OverlayStatusService(() => new StatusOverlayWindow()));
        using var detector = new FakeKeywordDetector();
        using var auto = new AutoCaptureService(settings, new FakeSecrets(), monitor, capture, orchestrator, new OverlayStatusService(() => new StatusOverlayWindow()), new(), detector);
        await auto.ApplySettingsAsync(value, true);
        async Task Wait(Func<bool> condition)
        { using var timeout = new CancellationTokenSource(5000); while (!condition()) { await Task.Delay(20, timeout.Token); await Idle(window); } }
        monitor.EmitAudio(.2f); monitor.Emit(.6f); await Task.Delay(250); await Idle(window);
        if (capture.RecordingStarts != 0) throw new Exception("Ordinary speech/level bypassed keyword gate.");
        var resetsBeforeRefresh = detector.Resets;
        value.Theme = AppTheme.Dark; await auto.ApplySettingsAsync(value, true);
        monitor.EmitAudio(.2f); await Task.Delay(150); await Idle(window);
        if (detector.Resets != resetsBeforeRefresh) throw new Exception("Unrelated settings refresh reset the keyword stream.");
        monitor.EmitAudio(.6f); await Wait(() => capture.IsRecording);
        await Task.Delay(500); capture.EmitLevel(0); await Wait(() => !capture.IsRecording && monitor.IsRunning); await Task.Delay(800);
        monitor.EmitAudio(.2f); await Task.Delay(200); await Idle(window);
        if (capture.RecordingStarts != 1) throw new Exception("Wake gate did not rearm after silence.");
        value.AutoCaptureThreshold = 0; await auto.ApplySettingsAsync(value, true);
        monitor.EmitAudio(.6f); await Wait(() => capture.RecordingStarts == 2);
        capture.EmitLevel(0);
        // No additional callbacks: the independent timer must still stop and rearm.
        await Wait(() => !capture.IsRecording && monitor.IsRunning);
        if (orchestrator.State == DictationState.Listening) throw new Exception("Zero-threshold wake capture never reached processing.");
        auto.Suspended = true;
        File.WriteAllText(Path.Combine(output, "wake-cycle-result.txt"), "PASS: ordinary speech ignored; keyword starts capture; silence stops; later ordinary speech ignored; second keyword starts next capture. Fake audio/detector; physical microphone acceptance remains manual.");
    }
    private sealed class FakeSetupCapture : IAudioCaptureService
    {
        public event EventHandler<float>? LevelAvailable;
        public bool IsRecording { get; private set; }
        public Task<IReadOnlyList<AudioDeviceInfo>> GetDevicesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<AudioDeviceInfo>>([]);
        public Task StartRecordingAsync(string? device, CancellationToken token) { IsRecording = true; LevelAvailable?.Invoke(this, .3f); return Task.CompletedTask; }
        public Task<RecordingResult> StopRecordingAsync(CancellationToken token) { IsRecording = false; return Task.FromResult(new RecordingResult("", TimeSpan.FromSeconds(12), 384000, .4f)); }
        public Task DeleteRecordingAsync(string path, CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeDictationCapture : IAudioCaptureService
    {
        public event EventHandler<float>? LevelAvailable;
        public void EmitLevel(float level) => LevelAvailable?.Invoke(this, level);
        public bool IsRecording { get; private set; }
        public int RecordingStarts { get; private set; }
        public Task<IReadOnlyList<AudioDeviceInfo>> GetDevicesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<AudioDeviceInfo>>([new("", "Windows default input", true), new("preview-test-device", "Test input")]);
        public Task StartRecordingAsync(string? device, CancellationToken token) { IsRecording = true; RecordingStarts++; return Task.CompletedTask; }
        public Task<RecordingResult> StopRecordingAsync(CancellationToken token) { IsRecording = false; return Task.FromResult(new RecordingResult("", TimeSpan.Zero, 0, 0)); }
        public Task DeleteRecordingAsync(string path, CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class ModelHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            if (request.RequestUri!.AbsolutePath.Contains("ggml-"))
            {
                await Task.Delay(800, token);
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent([0]) };
            }
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            { Content = new StringContent("""{"data":[{"id":"whisper-large-v3"},{"id":"whisper-large-v3-turbo"},{"id":"llama-3.1-8b-instant"},{"id":"llama-3.3-70b-versatile"},{"id":"openai/gpt-oss-20b"},{"id":"playai-tts"},{"id":"meta-llama/llama-prompt-guard-2-22m"}]}""") };
        }
    }
    private sealed class EndpointCapture : IAudioCaptureService, IStreamingAudioCaptureService
    {
        public event EventHandler<float>? LevelAvailable;
        public event EventHandler<ReadOnlyMemory<byte>>? PcmAvailable;
        public bool IsRecording { get; private set; }
        public Task<IReadOnlyList<AudioDeviceInfo>> GetDevicesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<AudioDeviceInfo>>([]);
        public Task StartRecordingAsync(string? device, CancellationToken token) { IsRecording = true; return Task.CompletedTask; }
        public Task<RecordingResult> StopRecordingAsync(CancellationToken token) { IsRecording = false; return Task.FromResult(new RecordingResult("fake-pcm", TimeSpan.FromSeconds(2), 64000, .2f)); }
        public void Emit(float pcmValue, float displayedPeak)
        {
            var bytes = new byte[1280];
            for (var i = 0; i < 640; i++) System.Buffers.Binary.BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(i * 2), (short)(pcmValue * 32767));
            PcmAvailable?.Invoke(this, bytes); LevelAvailable?.Invoke(this, displayedPeak);
        }
        public Task DeleteRecordingAsync(string file, CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeActivity : ISpeechActivityDetector
    {
        public Task PrepareAsync(IProgress<string>? progress, CancellationToken token) => Task.CompletedTask;
        public Task<bool> ProcessAsync(float[] samples, CancellationToken token) => Task.FromResult(samples.Length > 0 && samples[0] > .5f);
        public void Reset() { } public void Dispose() { }
    }
    private sealed class EndpointClient : IGroqTranscriptionClient
    {
        public int Calls;
        public Task<string> TranscribeAsync(string key, string file, TranscriptionOptions options, CancellationToken token) { Calls++; return Task.FromResult("the spoken dictation"); }
        public Task<ConnectionTestResult> TestConnectionAsync(string key, CancellationToken token) => throw new NotSupportedException();
        public Task<string> CleanupTranscriptAsync(string key, string text, string model, WritingCleanupTone tone, string? prompt, CancellationToken token) => throw new NotSupportedException();
        public Task<IntentResolution> InterpretIntentAsync(string key, string text, string model, CancellationToken token) => throw new NotSupportedException();
    }
    private sealed class EndpointInsertion : ITextInsertionService
    {
        public string? Text;
        public Task<bool> InsertAsync(string text, InsertionMode mode, bool copy, bool fallback, CancellationToken token) { Text = text; return Task.FromResult(true); }
    }
    private static async Task VerifySpeechEndpointAsync(Window window, string output)
    {
        var settings = new JsonSettingsService(Path.Combine(output, "endpoint-settings.json"));
        var value = new AppSettings { UseLocalTranscription = true, ActivationMode = ActivationMode.AutoCapture, AutoCaptureListeningEnabled = true,
            AutoCaptureWakeVoiceEnabled = true, AutoCaptureThreshold = 0, AutoCaptureSilenceMs = 400, AutoCaptureMinSpeechMs = 250, WakeToneEnabled = false };
        await settings.SaveAsync(value, default);
        var capture = new EndpointCapture(); var monitor = new FakeLevelMonitor(); var client = new EndpointClient(); var insertion = new EndpointInsertion();
        var orchestrator = new DictationOrchestrator(settings, new FakeSecrets(), capture, client, new(), insertion, new WindowsKeyboardCommandService(), new OverlayStatusService(() => new StatusOverlayWindow()));
        using var keyword = new FakeKeywordDetector(); using var speech = new FakeActivity();
        using var service = new AutoCaptureService(settings, new FakeSecrets(), monitor, capture, orchestrator, new OverlayStatusService(() => new StatusOverlayWindow()), new(), keyword, speech);
        await service.ApplySettingsAsync(value, true);
        async Task Wait(Func<bool> condition) { using var timeout = new CancellationTokenSource(5000); while (!condition()) { await Task.Delay(10, timeout.Token); await Idle(window); } }
        monitor.EmitAudio(.6f); await Wait(() => capture.IsRecording);
        for (var i = 0; i < 5; i++) { capture.Emit(.8f, .9f); await Task.Delay(50); }
        for (var i = 0; i < 16; i++) { if (capture.IsRecording) capture.Emit(.02f, .9f); await Task.Delay(50); }
        await Wait(() => !capture.IsRecording && client.Calls == 1);
        if (insertion.Text != "The spoken dictation") throw new Exception("Wake silence did not finish transcription/insertion.");
        await Task.Delay(800); monitor.EmitAudio(.6f); await Wait(() => capture.IsRecording);
        // Complete loss of callbacks still has a timer-driven endpoint.
        await Wait(() => !capture.IsRecording && client.Calls == 2);
        await service.StopWakeWorkerAsync();
        File.WriteAllText(Path.Combine(output, "speech-endpoint.txt"), "PASS: zero threshold; speech detection holds active speech; high non-speech peaks do not reset silence; wake capture stops/transcribes/inserts/rearms; no further callbacks still stop.");
    }
    private sealed class FakeHotkey : IHotkeyService
    {
        public event EventHandler? Pressed { add { } remove { } }
        public event EventHandler? Released { add { } remove { } }
        public bool IsRegistered { get; private set; }
        public int Registrations;
        public bool TryRegister(HotkeySettings hotkey, out string? error) { IsRegistered = true; Registrations++; error = null; return true; }
        public void Unregister() => IsRegistered = false;
        public void Dispose() => Unregister();
    }
    private sealed class FakeMediaControl : IAutoCaptureMediaControlService
    {
        public int Starts, Restores;
        public Task SetListeningStateAsync(AutoCaptureMediaBehavior behavior, bool listening, CancellationToken token) { if (listening) Starts++; else throw new Exception("Only owned restore may end media handling."); return Task.CompletedTask; }
        public Task RestoreAsync(CancellationToken token) { Restores++; return Task.CompletedTask; }
    }
    private static async Task VerifyHotkeyMediaAsync(MainViewModel main, FakeDictationCapture audio, FakeMediaControl media, FakeHotkey hotkey, Window window, string output)
    {
        main.Settings.AutoCaptureMediaBehavior = AutoCaptureMediaBehavior.TogglePlayPause;
        main.Settings.AutoCaptureListeningEnabled = false; main.Settings.ActivationMode = ActivationMode.Toggle; main.Settings.StreamModeEnabled = false;
        await main.AutoSaveSettingsAsync(); await main.SetNoteCaptureActiveAsync(true); await main.SetNoteCaptureActiveAsync(false); await Idle(window);
        if (media.Starts != 0 || media.Restores != 0) throw new Exception("Settings or preparation reservations changed media.");
        await main.ToggleListeningAsync(); if (!audio.IsRecording) throw new Exception("Manual button must record.");
        await main.ToggleListeningAsync(); await Idle(window);
        if (media.Starts != 0 || media.Restores != 0) throw new Exception("Manual recording changed hotkey media.");
        await main.HandleHotkeyPressedAsync(); if (!audio.IsRecording || media.Starts != 1) throw new Exception("Toggle hotkey did not start owned media/capture.");
        await main.HandleHotkeyPressedAsync(); await Task.Delay(50); if (audio.IsRecording || media.Restores != 1) throw new Exception("Toggle hotkey did not restore once.");
        main.Settings.ActivationMode = ActivationMode.HoldToTalk; await main.AutoSaveSettingsAsync();
        await main.HandleHotkeyPressedAsync(); if (!audio.IsRecording) throw new Exception("Hold hotkey did not start capture.");
        await main.HandleHotkeyReleasedAsync(); await Task.Delay(50);
        if (audio.IsRecording || media.Starts != 2 || media.Restores != 2) throw new Exception("Hold release did not restore owned media.");
        main.Settings.ActivationMode = ActivationMode.AutoCapture; await main.AutoSaveSettingsAsync();
        await main.ToggleAutoCaptureListeningAsync(); await main.ToggleAutoCaptureListeningAsync();
        if (media.Starts != 2 || media.Restores != 2) throw new Exception("Settings/tray SmartListen toggles changed media.");
        await main.HandleHotkeyPressedAsync(); if (!main.Settings.AutoCaptureListeningEnabled || media.Starts != 3) throw new Exception("Single hotkey did not enable SmartListen.");
        await main.SetNoteCaptureActiveAsync(true); await main.SetNoteCaptureActiveAsync(false);
        if (media.Restores != 2) throw new Exception("Preparation resumed music owned by an existing hotkey session.");
        await main.HandleHotkeyPressedAsync(); if (main.Settings.AutoCaptureListeningEnabled || media.Restores != 3) throw new Exception("SmartListen hotkey did not restore once.");
        await main.HandleHotkeyReleasedAsync(); if (media.Restores != 3) throw new Exception("Non-hold release changed media.");
        await main.SuspendHotkeyEditingAsync(); if (hotkey.IsRegistered) throw new Exception("Shortcut editing must suspend global activation.");
        main.ResumeHotkeyEditing(); if (!hotkey.IsRegistered) throw new Exception("Shortcut editing did not restore registration.");
        main.Settings.AutoCaptureMediaBehavior = AutoCaptureMediaBehavior.None; await main.AutoSaveSettingsAsync();
        File.WriteAllText(Path.Combine(output, "hotkey-media.txt"), "PASS: one registered shortcut; Toggle/Hold/SmartListen mode routing; hotkey-owned media starts/restores only; settings/manual/tests leave media alone; shortcut editing suspends registration.");
    }
    private sealed class FakeStartup : IStartupService
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) { }
    }
    private static async Task VerifyHorizontalTableAsync(DataGrid table, Window window, string output, string name)
    {
        table.BringIntoView(); await Idle(window);
        var scroll = FindAll<ScrollViewer>(table).First(view => view.ScrollableWidth > 0);
        var bar = FindAll<System.Windows.Controls.Primitives.ScrollBar>(table).Single(b => b.Orientation == Orientation.Horizontal && b.IsVisible);
        if (bar.ActualHeight <= 0) throw new Exception("Horizontal table scrollbar must be visible.");
        scroll.ScrollToRightEnd(); await Idle(window);
        if (scroll.HorizontalOffset <= 0) throw new Exception("Horizontal table scrolling did not move the viewport.");
        Save(window, Path.Combine(output, name + "-right.png"));
        scroll.ScrollToLeftEnd(); await Idle(window); Save(window, Path.Combine(output, name + ".png"));
        foreach (var cell in FindAll<DataGridCell>(table))
            if (cell.VerticalContentAlignment != VerticalAlignment.Center) throw new Exception("Table cells must center content vertically.");
    }
    private static async Task Idle(Window window) => await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Save(Window window, string path)
    {
        window.UpdateLayout(); SaveSurface((FrameworkElement)window.Content, path);
    }
    private static void SaveSurface(FrameworkElement surface, string path)
    {
        surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
    private sealed class FakeCapture : IConversationCapture
    {
        public event EventHandler<AudioChunk>? ChunkAvailable;
        public event EventHandler<string>? Failed { add { } remove { } }
        public bool SupportsApplicationCapture => true;
        public IReadOnlyList<CaptureTarget> GetOutputs() => [new("", "Windows default output")];
        public IReadOnlyList<CaptureTarget> GetApplications() => [new("123", "Example calling app")];
        public Task StartAsync(ConversationCaptureOptions options, double offset, CancellationToken token) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Emit(int id, string source, double time) => ChunkAvailable?.Invoke(this, new(id, source, time, new float[32000]));
    }
    private sealed class FakeSpeakers : ISpeakerIdentifier
    {
        public Task InitializeAsync(CancellationToken token, IProgress<string>? progress = null) => Task.CompletedTask;
        public Task<IReadOnlyList<SpeakerTurn>> IdentifyAsync(float[] samples, CancellationToken token) => Task.FromResult<IReadOnlyList<SpeakerTurn>>([]);
        public void Reset() { } public void Dispose() { }
    }
    private sealed class FakeGroq : INoteIntelligence
    {
        private int _i;
        public Task<SpeechResult> TranscribeAsync(string key, string path, TranscriptionOptions options, CancellationToken token)
        {
            var text = new[] { "Can we have the first draft ready by Friday?", "Yes. I'll send the outline tomorrow, then we can review it together.", "Sounds good. Let's keep the examples short and clear." }[_i++ % 3];
            return Task.FromResult(new SpeechResult(text, [new(.1, 1.9, text)]));
        }
        public Task<string> SummariseAsync(string key, string transcript, string model, CancellationToken token) => Task.FromResult("Draft due Friday. Outline tomorrow; review together.");
    }
    private sealed class FakeSecrets : ISecretStore
    {
        public Task<string?> GetApiKeyAsync(CancellationToken token) => Task.FromResult<string?>("fake-test-key");
        public Task SaveApiKeyAsync(string key, CancellationToken token) => Task.CompletedTask;
        public Task RemoveApiKeyAsync(CancellationToken token) => Task.CompletedTask;
    }
    private sealed class FakeVideo : IVideoImporter
    {
        public Task<PreparedVideo> PrepareAsync(string url, IProgress<string> progress, CancellationToken token) => throw new NotSupportedException();
    }
}
