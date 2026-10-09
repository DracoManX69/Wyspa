using SherpaOnnx;
using Wyspa.Core.Abstractions;

namespace Wyspa.Core.Services;

public sealed class LocalSpeechActivityDetector(OptionalDependencyStore? store = null) : ISpeechActivityDetector
{
    public static OptionalDependency Model { get; } = new("silero-vad-v1", "Speech detection", "silero_vad.onnx", 643854,
        "9e2449e1087496d8d4caba907f23e0bd3f78d91fa552479bb9c23ac09cbb1fd6",
        "https://github.com/k2-fsa/sherpa-onnx/releases/download/asr-models/silero_vad.onnx", 643854,
        "9e2449e1087496d8d4caba907f23e0bd3f78d91fa552479bb9c23ac09cbb1fd6");
    private readonly LocalSpeechWorker _worker = new("Wyspa speech detection");
    private readonly object _sync = new();
    private VoiceActivityDetector? _vad;
    public async Task PrepareAsync(IProgress<string>? progress, CancellationToken token)
    {
        lock (_sync) if (_vad is not null) return;
        var path = await (store ?? OptionalDependencyStore.Default).EnsureAsync(Model, null, progress, token);
        await _worker.Run(() =>
        {
            lock (_sync)
            {
                if (_vad is not null) return true;
                var config = new VadModelConfig(); config.SampleRate = 16000; config.NumThreads = 1; config.Provider = "cpu";
                config.SileroVad.Model = path; config.SileroVad.Threshold = .5f;
                config.SileroVad.MinSpeechDuration = .064f; config.SileroVad.MinSilenceDuration = .096f;
                config.SileroVad.WindowSize = 512; config.SileroVad.MaxSpeechDuration = 120;
                _vad = new VoiceActivityDetector(config, 8);
                return true;
            }
        }, token);
    }
    public Task<bool> ProcessAsync(float[] samples, CancellationToken token) => _worker.Run(() =>
    {
        lock (_sync)
        {
            if (_vad is null) return false;
            _vad.AcceptWaveform(samples); var speech = _vad.IsSpeechDetected();
            while (!_vad.IsEmpty()) _vad.Pop(); // only activity is needed; never retain finished audio segments
            return speech;
        }
    }, token);
    public void Reset() { lock (_sync) _vad?.Reset(); }
    public void Dispose() { lock (_sync) { _vad?.Dispose(); _vad = null; } _worker.Dispose(); }
}
