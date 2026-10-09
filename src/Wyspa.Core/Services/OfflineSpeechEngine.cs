using SherpaOnnx;
using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

internal sealed class OfflineSpeechEngine : IDisposable
{
    private readonly OfflineRecognizer _recognizer;
    public OfflineSpeechEngine(LocalModel model, string directory, int threads)
    {
        var config = new OfflineRecognizerConfig();
        config.FeatConfig.SampleRate = 16000; config.FeatConfig.FeatureDim = model.Bundle!.Engine == "Parakeet" ? 128 : 80;
        config.ModelConfig.NumThreads = threads; config.ModelConfig.Provider = "cpu";
        config.ModelConfig.Tokens = Path.Combine(directory, "tokens.txt");
        string PathFor(string name) => Path.Combine(directory, name);
        if (model.Bundle.Engine == "Parakeet")
        {
            config.ModelConfig.ModelType = "nemo_transducer";
            config.ModelConfig.Transducer.Encoder = PathFor("encoder.int8.onnx");
            config.ModelConfig.Transducer.Decoder = PathFor("decoder.int8.onnx");
            config.ModelConfig.Transducer.Joiner = PathFor("joiner.int8.onnx");
        }
        else if (model.Bundle.Engine == "Moonshine")
        {
            config.ModelConfig.Moonshine.Preprocessor = PathFor("preprocess.onnx");
            config.ModelConfig.Moonshine.Encoder = PathFor("encode.int8.onnx");
            config.ModelConfig.Moonshine.UncachedDecoder = PathFor("uncached_decode.int8.onnx");
            config.ModelConfig.Moonshine.CachedDecoder = PathFor("cached_decode.int8.onnx");
        }
        else if (model.Bundle.Engine == "SenseVoice")
        {
            config.ModelConfig.SenseVoice.Model = PathFor("model.int8.onnx");
            config.ModelConfig.SenseVoice.UseInverseTextNormalization = 1;
            config.ModelConfig.SenseVoice.Language = "auto";
        }
        else throw new InvalidOperationException("Unsupported offline speech engine.");
        _recognizer = new OfflineRecognizer(config);
    }
    public Task<SpeechResult> TranscribeAsync(float[] samples, CancellationToken token) => LocalBackgroundWork.Run(() =>
    {
        using var stream = _recognizer.CreateStream();
        stream.AcceptWaveform(16000, samples); _recognizer.Decode(stream);
        token.ThrowIfCancellationRequested();
        var result = stream.Result;
        var text = result.Text.Trim();
        // Different exports use different subword timing conventions. Retain honest
        // segment-level timing rather than invent precise word timestamps.
        return new SpeechResult(text, string.IsNullOrWhiteSpace(text) ? [] : [new TimedWord(0, samples.Length / 16000d, text)]);
    }, token);
    public void Dispose() => _recognizer.Dispose();
}
