namespace Wyspa.Core.Services;

public sealed class WakePhraseVerifier : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Whisper.net.WhisperFactory? _factory;
    public static long DownloadBytes => LocalModelStore.Catalog.Single(m => m.Id == "tiny.en-q5_1").Bytes;
    public async Task PrepareAsync(CancellationToken token, IProgress<string>? progress = null)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_factory is not null) return;
            var model = LocalModelStore.Catalog.Single(m => m.Id == "tiny.en-q5_1");
            var cached = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Wyspa", "Models", model.FileName);
            var dependency = new OptionalDependency("wake-verifier-tiny-en-v1", "Wake phrase verification", model.FileName, model.Bytes, model.Sha256, model.SourceUrl, model.Bytes, model.Sha256);
            var path = await OptionalDependencyStore.Default.EnsureAsync(dependency, cached, progress, token);
            if (Whisper.net.LibraryLoader.RuntimeOptions.LoadedLibrary is null)
                Whisper.net.LibraryLoader.RuntimeOptions.RuntimeLibraryOrder = LocalHardware.Detect().Gpu is null
                    ? [Whisper.net.LibraryLoader.RuntimeLibrary.Cpu, Whisper.net.LibraryLoader.RuntimeLibrary.CpuNoAvx]
                    : [Whisper.net.LibraryLoader.RuntimeLibrary.Vulkan, Whisper.net.LibraryLoader.RuntimeLibrary.Cpu, Whisper.net.LibraryLoader.RuntimeLibrary.CpuNoAvx];
            _factory = await LocalBackgroundWork.Run(() => Whisper.net.WhisperFactory.FromPath(path, new() { UseGpu = false }), token);
        }
        finally { _gate.Release(); }
    }
    public async Task<string> RecognizeAsync(float[] samples, CancellationToken token, int maxTokens = 48)
    {
        await PrepareAsync(token);
        using var processor = _factory!.CreateBuilder().WithLanguage("en").WithThreads(2).WithAudioContextSize(Math.Clamp((int)Math.Ceiling(samples.Length / 320d) + 16, 64, 256)).WithNoContext()
            .WithSingleSegment().WithMaxTokensPerSegment(maxTokens).WithGreedySamplingStrategy().WithTemperature(0).WithTemperatureInc(0).Build();
        var text = new List<string>();
        await foreach (var part in processor.ProcessAsync(samples, token)) if (!string.IsNullOrWhiteSpace(part.Text)) text.Add(part.Text.Trim());
        return string.Join(" ", text);
    }
    public static bool Matches(string text, string phrase)
    {
        var words = VoicePersonalization.Words(text); var expected = VoicePersonalization.Words(phrase);
        for (var offset = 0; offset + expected.Length <= words.Length; offset++)
            if (expected.Select((word, index) => SameWord(words[offset + index], word)).All(match => match)) return true;
        return false;
    }
    private static bool SameWord(string heard, string expected) => heard == expected || expected == "hey" && heard == "hay";
    public void Dispose() { _factory?.Dispose(); _factory = null; }
}
