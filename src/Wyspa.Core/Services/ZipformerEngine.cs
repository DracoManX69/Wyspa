using SherpaOnnx;
using Wyspa.Core.Abstractions;

namespace Wyspa.Core.Services;

internal sealed class ZipformerEngine : IDisposable
{
    private readonly OnlineRecognizer _recognizer;
    private readonly object _sync = new();
    private int _streams;
    public bool InUse { get { lock (_sync) return _streams > 0; } }
    public ZipformerEngine(string directory, int threads, IReadOnlyList<LocalModelFile>? files = null)
    {
        var config = new OnlineRecognizerConfig();
        var names = files?.Select(f => f.Name).ToArray() ?? ZipformerModel.Files.Select(f => f.Name).ToArray();
        config.FeatConfig.SampleRate = 16000; config.FeatConfig.FeatureDim = 80;
        config.ModelConfig.Transducer.Encoder = Path.Combine(directory, names[0]);
        config.ModelConfig.Transducer.Decoder = Path.Combine(directory, names[1]);
        config.ModelConfig.Transducer.Joiner = Path.Combine(directory, names[2]);
        config.ModelConfig.Tokens = Path.Combine(directory, "tokens.txt");
        config.ModelConfig.NumThreads = threads;
        config.ModelConfig.Provider = "cpu";
        config.DecodingMethod = "greedy_search";
        config.EnableEndpoint = 1;
        config.Rule1MinTrailingSilence = 2.4f;
        config.Rule2MinTrailingSilence = .8f;
        config.Rule3MinUtteranceLength = 20;
        _recognizer = new OnlineRecognizer(config);
    }
    public ILocalRecognitionStream CreateStream()
    {
        lock (_sync) { var stream = _recognizer.CreateStream(); _streams++; return new Session(this, stream); }
    }
    private sealed class Session(ZipformerEngine engine, OnlineStream stream) : ILocalRecognitionStream
    {
        private string _completed = "";
        private bool _finished;
        private bool _disposed;
        public Task<string> ProcessAsync(float[] samples, bool finished, CancellationToken token) => LocalBackgroundWork.Run(() =>
        {
            lock (engine._sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_finished) return _completed;
                // Once accepted, finish this bounded chunk even if stop cancels the live worker.
                // The caller must advance its cursor exactly once for these samples.
                stream.AcceptWaveform(16000, samples);
                if (finished) { stream.AcceptWaveform(16000, new float[8000]); stream.InputFinished(); }
                while (engine._recognizer.IsReady(stream)) engine._recognizer.Decode(stream);
                var text = engine._recognizer.GetResult(stream).Text.Trim().ToLowerInvariant();
                var endpoint = finished || engine._recognizer.IsEndpoint(stream);
                if (endpoint)
                {
                    _completed = Join(_completed, text);
                    if (!finished) engine._recognizer.Reset(stream);
                    _finished = finished;
                    return _completed;
                }
                // A token can extend the final word. Publish complete words only.
                var space = text.LastIndexOf(' ');
                return Join(_completed, space < 0 ? "" : text[..space]);
            }
        }, token);
        private static string Join(string a, string b) => (a + " " + b).Trim();
        public void Dispose()
        {
            lock (engine._sync) { if (_disposed) return; _disposed = true; stream.Dispose(); engine._streams--; }
        }
    }
    public void Dispose() { lock (_sync) { if (_streams != 0) throw new InvalidOperationException("Finish local recording before removing this model."); _recognizer.Dispose(); } }
}
