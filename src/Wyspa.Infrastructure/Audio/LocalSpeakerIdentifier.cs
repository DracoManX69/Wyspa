using SherpaOnnx;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.Infrastructure.Audio;

public sealed class LocalSpeakerIdentifier(string modelDirectory, Func<double> threshold, OptionalDependencyStore? dependencies = null) : ISpeakerIdentifier
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OfflineSpeakerDiarization? _diarizer;
    private SpeakerEmbeddingExtractor? _extractor;
    private readonly List<float[]> _voices = [];
    private float[] _history = [];
    private IReadOnlyList<SpeakerTurn> _historyTurns = [];
    public async Task InitializeAsync(CancellationToken token, IProgress<string>? progress = null)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_diarizer is not null) return;
            var store = dependencies ?? OptionalDependencyStore.Default;
            var segmentation = await store.EnsureAsync(OptionalDependencyStore.Segmentation, Path.Combine(modelDirectory, "segmentation.onnx"), progress, token);
            var embedding = await store.EnsureAsync(OptionalDependencyStore.Embedding, Path.Combine(modelDirectory, "embedding.onnx"), progress, token);
            progress?.Report("Loading local speaker models…");
            await Task.Run(() =>
            {
                var config = new OfflineSpeakerDiarizationConfig();
                config.Segmentation.Pyannote.Model = segmentation;
                config.Segmentation.NumThreads = 2;
                config.Embedding.Model = embedding; config.Embedding.NumThreads = 2;
                config.Clustering.NumClusters = -1; config.Clustering.Threshold = .5f;
                _diarizer = new OfflineSpeakerDiarization(config);
                if (_diarizer.SampleRate != 16000) throw new InvalidOperationException("The speaker model requires an unsupported sample rate.");
                _extractor = new SpeakerEmbeddingExtractor(config.Embedding);
            }, token);
        }
        catch { _diarizer?.Dispose(); _diarizer = null; _extractor?.Dispose(); _extractor = null; throw; }
        finally { _gate.Release(); }
    }
    public async Task<IReadOnlyList<SpeakerTurn>> IdentifyAsync(float[] samples, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            return await Task.Run<IReadOnlyList<SpeakerTurn>>(() =>
            {
                token.ThrowIfCancellationRequested();
                // A single short utterance has an unstable voice embedding. Re-segment a rolling
                // context and anchor cluster names to previously labelled overlapping speech.
                // Only the current tail is returned; old text never has to wait for this pass.
                var combined = new float[_history.Length + samples.Length];
                _history.CopyTo(combined, 0); samples.CopyTo(combined, _history.Length);
                var tailStart = _history.Length / 16000d;
                var segments = _diarizer!.Process(combined);
                var labels = new Dictionary<int, string>();
                var claimed = new HashSet<int>();
                foreach (var group in segments.GroupBy(s => s.Speaker))
                {
                    // Concatenate clean portions from this local cluster before computing its embedding.
                    var pieces = group.SelectMany(s => combined[Math.Clamp((int)(s.Start * 16000), 0, combined.Length)..Math.Clamp((int)(s.End * 16000), 0, combined.Length)]).ToArray();
                    // Do not invent a new speaker from a brief interjection or a segmentation fragment.
                    if (pieces.Length < 19200) { labels[group.Key] = "Unidentified voice"; continue; }
                    using var stream = _extractor!.CreateStream();
                    stream.AcceptWaveform(16000, pieces); stream.InputFinished();
                    if (!_extractor.IsReady(stream)) { labels[group.Key] = "Unidentified voice"; continue; }
                    var vector = _extractor.Compute(stream);
                    Normalize(vector);
                    var anchors = new Dictionary<int, double>();
                    foreach (var segment in group)
                        foreach (var previous in _historyTurns)
                        {
                            if (!previous.Speaker.StartsWith("Speaker ", StringComparison.Ordinal) || !int.TryParse(previous.Speaker.AsSpan(8), out var number)) continue;
                            var overlap = Math.Min(segment.End, previous.End) - Math.Max(segment.Start, previous.Start);
                            if (overlap > 0 && !claimed.Contains(number - 1)) anchors[number - 1] = anchors.GetValueOrDefault(number - 1) + overlap;
                        }
                    var anchor = anchors.OrderByDescending(a => a.Value).FirstOrDefault();
                    var best = anchor.Value >= .3 ? anchor.Key : -1;
                    var score = Math.Clamp(threshold(), .2, .9);
                    if (best < 0)
                        for (var i = 0; i < _voices.Count; i++)
                        {
                            var similarity = vector.Zip(_voices[i], (a, b) => a * b).Sum();
                            if (!claimed.Contains(i) && similarity >= score) { score = similarity; best = i; }
                        }
                    if (best < 0) { best = _voices.Count; _voices.Add(vector); }
                    else
                    {
                        for (var j = 0; j < vector.Length; j++) _voices[best][j] = .85f * _voices[best][j] + .15f * vector[j];
                        Normalize(_voices[best]);
                    }
                    labels[group.Key] = $"Speaker {best + 1}";
                    claimed.Add(best);
                }
                var labelled = segments.Select(s => new SpeakerTurn(s.Start, s.End, labels[s.Speaker])).ToArray();
                var trim = Math.Max(0, combined.Length - 24 * 16000);
                _history = combined[trim..];
                var shift = trim / 16000d;
                _historyTurns = labelled.Where(t => t.End > shift).Select(t => new SpeakerTurn(Math.Max(0, t.Start - shift), t.End - shift, t.Speaker)).ToArray();
                return labelled.Where(t => t.End > tailStart).Select(t => new SpeakerTurn(Math.Max(0, t.Start - tailStart), Math.Min(samples.Length / 16000d, t.End - tailStart), t.Speaker)).ToArray();
            }, token);
        }
        finally { _gate.Release(); }
    }
    private static void Normalize(float[] vector)
    {
        var length = Math.Sqrt(vector.Sum(x => (double)x * x));
        if (length > 0) for (var i = 0; i < vector.Length; i++) vector[i] /= (float)length;
    }
    public void Reset() { _voices.Clear(); _history = []; _historyTurns = []; }
    public void Dispose() { _diarizer?.Dispose(); _extractor?.Dispose(); _gate.Dispose(); }
}
