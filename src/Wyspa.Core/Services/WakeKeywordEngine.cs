using System.Globalization;
using Microsoft.ML.Tokenizers;
using SherpaOnnx;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

public sealed class WakeKeywordEngine : IWakePhraseDetector, IWakeEnrollmentDetector
{
    private readonly LocalSpeechWorker _worker = new("Wyspa keyword detection");
    private readonly WakePhraseVerifier _verifier = new();
    private readonly Queue<float> _recent = new();
    private WakeVoiceProfile? _personal;
    private int _fallbackQuiet, _fallbackSpeech;
    private bool _fallbackAttempted;
    public void ConfigurePersonalization(WakeVoiceProfile? profile, string? microphoneId)
    {
        lock (_sync) _personal = profile is { SetupValidated: true, EnrollmentVersion: 1 } && profile.MicrophoneId == microphoneId && profile.AcousticTemplates.Count <= 6 && profile.AcousticThreshold is >= 0 and <= .75 && profile.PronunciationVariants.Count <= 3 ? profile : null;
    }
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly OptionalDependencyStore _store;
    private WakeKeywordSpotter? _spotter;
    private long _acceptedSamples;
    private long _retryAtSamples;
    public int LastVerificationSamples { get; private set; }
    private SentencePieceTokenizer? _tokenizer;
    private HashSet<string> _tokens = [];
    private string[] _paths = [];
    private OnlineStream? _live;
    private string? _configuration;
    private readonly object _sync = new();
    public WakeKeywordEngine(OptionalDependencyStore? store = null) => _store = store ?? OptionalDependencyStore.Default;
    public async Task PrepareAsync(IProgress<string>? progress, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_spotter is not null) { await _verifier.PrepareAsync(token, progress); return; }
            var paths = new List<string>();
            foreach (var file in WakeKeywordModel.Files) paths.Add(await _store.EnsureAsync(file, null, progress, token));
            _paths = paths.ToArray();
            await _worker.Run(() =>
            {
                using var bpe = KeywordTokenizerCompatibility.WithoutBosEos(File.ReadAllBytes(paths[3])); _tokenizer = SentencePieceTokenizer.Create(bpe, false, false);
                _tokens = File.ReadLines(paths[4]).Select(s => s.Split(' ')[0]).ToHashSet();
                var emptyKeywords = Path.Combine(Path.GetDirectoryName(paths[4])!, "empty-keywords.txt");
                File.WriteAllText(emptyKeywords, "");
                var config = new KeywordSpotterConfig();
                config.FeatConfig.SampleRate = 16000; config.FeatConfig.FeatureDim = 80;
                config.ModelConfig.Transducer.Encoder = paths[0]; config.ModelConfig.Transducer.Decoder = paths[1]; config.ModelConfig.Transducer.Joiner = paths[2];
                config.ModelConfig.Tokens = paths[4]; config.ModelConfig.NumThreads = 1; config.ModelConfig.Provider = "cpu";
                config.KeywordsFile = emptyKeywords; config.NumTrailingBlanks = 2; config.MaxActivePaths = 8;
                _spotter = new WakeKeywordSpotter(config);
                return true;
            }, token);
        }
        finally { _gate.Release(); }
        await _verifier.PrepareAsync(token, progress);
    }
    private string Keywords(string phrase, double strictness, IEnumerable<string>? variants = null)
    {
        var lines = new List<string>();
        foreach (var spelling in new[] { phrase }.Concat(variants ?? []).Distinct().Take(4))
        {
            var normalized = WakePhraseCalibration.Normalize(spelling);
            var pieces = _tokenizer!.EncodeToTokens(normalized.ToUpperInvariant(), out _, false, false);
            if (pieces.Count == 0 || pieces.Any(p => p.Value == "<unk>" || !_tokens.Contains(p.Value)))
            {
                if (spelling != phrase) continue;
                throw new InvalidOperationException("This wake phrase cannot be represented by the English detector. Try a different phrase.");
            }
            lines.Add(string.Join(" ", pieces.Select(p => p.Value)) + " :1.5 #" + WakePhraseCalibration.Threshold(strictness).ToString("0.000", CultureInfo.InvariantCulture) + " @" + normalized.Replace(' ', '_'));
        }
        return string.Join("\n", lines);
    }
    public async Task<bool> ProcessAsync(float[] samples, string phrase, double strictness, CancellationToken token)
    {
        float[] candidate = [];
        float[] fullCandidate = [];
        var retrying = false;
        var found = await _worker.Run(() =>
        {
            lock (_sync)
            {
                if (_spotter is null) return false;
                foreach (var sample in samples) _recent.Enqueue(sample);
                while (_recent.Count > 64000) _recent.Dequeue();
                var variants = _personal?.Phrase == phrase ? _personal.PronunciationVariants : [];
                var configuration = phrase + "|" + strictness.ToString("R", CultureInfo.InvariantCulture) + "|" + string.Join(",", variants);
                if (_live is null || _configuration != configuration)
                {
                    _live?.Dispose(); _live = _spotter.CreateStream(Keywords(phrase, strictness, variants)); _configuration = configuration; _acceptedSamples = 0; _retryAtSamples = 0;
                }
                _acceptedSamples += samples.Length;
                _live.AcceptWaveform(16000, samples);
                if (_retryAtSamples > 0)
                {
                    if (_acceptedSamples < _retryAtSamples) return false;
                    _retryAtSamples = 0; retrying = true; candidate = _recent.ToArray(); return true;
                }
                var keyword = DecodeResult(_live); var detected = keyword is not null;
                if (_personal is { } personal && personal.Phrase == phrase && personal.AcousticTemplates.Count >= 3)
                {
                    var rms = Math.Sqrt(samples.Select(v => (double)v * v).DefaultIfEmpty().Average());
                    if (rms > Math.Max(.0015, personal.NoiseRms * 2.5)) { _fallbackSpeech += samples.Length; _fallbackQuiet = 0; _fallbackAttempted = false; }
                    else _fallbackQuiet += samples.Length;
                    if (!detected && !_fallbackAttempted && _fallbackQuiet >= 3200 && _fallbackSpeech >= 4000)
                    {
                        _fallbackAttempted = true;
                        var count = Math.Min(48000, _fallbackSpeech + _fallbackQuiet + 1600); _fallbackSpeech = 0;
                        var audio = _recent.TakeLast(Math.Min(_recent.Count, count)).ToArray();
                        if (WakeAcoustics.Best(WakeAcoustics.Extract(audio), personal.AcousticTemplates) <= personal.AcousticThreshold) detected = true;
                    }
                }
                if (detected)
                {
                    var recent = _recent.ToArray();
                    fullCandidate = recent;
                    candidate = keyword is null ? recent : WakeVerificationWindow.Extract(recent, _acceptedSamples, keyword.StartTime, keyword.Timestamps, keyword.Tokens);
                    _spotter.Reset(_live); _fallbackAttempted = true;
                }
                return detected;
            }
        }, token);
        if (!found) return false;
        LastVerificationSamples = candidate.Length;
        var text = await _verifier.RecognizeAsync(candidate, token, Math.Clamp(VoicePersonalization.Words(phrase).Length * 3, 6, 24));
        WakeVoiceProfile? profile; lock (_sync) profile = _personal;
        bool Matches(string heard) => WakePhraseVerifier.Matches(heard, phrase) || profile?.Phrase == phrase && profile.PronunciationVariants.Any(variant => WakePhraseVerifier.Matches(heard, variant));
        if (Matches(text)) return true;
        // A shorter window must not sacrifice the original word check. A rare truncated
        // follow-on word can also need a little more incoming audio before deciding.
        if (!retrying && fullCandidate.Length > 0)
        {
            var fullText = await _verifier.RecognizeAsync(fullCandidate, token);
            if (Matches(fullText)) return true;
            lock (_sync) _retryAtSamples = _acceptedSamples + 3200;
        }
        return false;
    }
    private bool Decode(OnlineStream stream) => DecodeResult(stream) is not null;
    private WakeKeywordSpotter.Result? DecodeResult(OnlineStream stream)
    {
        WakeKeywordSpotter.Result? matched = null;
        while (_spotter!.IsReady(stream))
        {
            _spotter.Decode(stream);
            var result = _spotter.GetResult(stream);
            if (!string.IsNullOrWhiteSpace(result.Keyword)) matched = result;
        }
        return matched;
    }
    public async Task<bool> CheckAsync(float[] samples, string phrase, WakeVoiceProfile profile, double strictness, CancellationToken token)
    {
        WakeVoiceProfile? previous;
        lock (_sync) { previous = _personal; _personal = profile; }
        Reset();
        try
        {
            var audio = new float[samples.Length + 16000]; samples.CopyTo(audio, 0);
            for (var offset = 0; offset < audio.Length; offset += 960)
                if (await ProcessAsync(audio.AsSpan(offset, Math.Min(960, audio.Length - offset)).ToArray(), phrase, strictness, token)) return true;
            return false;
        }
        finally { Reset(); lock (_sync) _personal = previous; }
    }
    public async Task<WakeSampleAnalysis> AnalyzeAsync(float[] samples, string phrase, CancellationToken token)
    {
        await PrepareAsync(null, token);
        var text = await _verifier.RecognizeAsync(samples, token);
        var maximum = await EvaluateKeywordsAsync(samples, phrase, token);
        var rms = Math.Sqrt(samples.Select(v => (double)v * v).DefaultIfEmpty().Average());
        return new(maximum, text, rms, samples.Select(Math.Abs).DefaultIfEmpty().Max(), samples.Count(v => Math.Abs(v) >= .98f) / (double)Math.Max(1, samples.Length), WakeAcoustics.Extract(samples));
    }
    public async Task<double?> EvaluateAsync(float[] samples, string phrase, CancellationToken token)
    {
        var sample = await AnalyzeAsync(samples, phrase, token);
        return WakePhraseVerifier.Matches(sample.RecognizedText, phrase) ? sample.KeywordStrictness : null;
    }
    private Task<double?> EvaluateKeywordsAsync(float[] samples, string phrase, CancellationToken token) => _worker.Run<double?>(() =>
    {
        lock (_sync)
        {
            double? maximum = null;
            foreach (var level in WakePhraseCalibration.Levels)
            {
                token.ThrowIfCancellationRequested();
                using var stream = _spotter!.CreateStream(Keywords(phrase, level));
                stream.AcceptWaveform(16000, samples); stream.AcceptWaveform(16000, new float[16000]); stream.InputFinished();
                if (Decode(stream)) maximum = level;
            }
            return maximum;
        }
    }, token);
    public void Reset() { lock (_sync) { _live?.Dispose(); _live = null; _configuration = null; _recent.Clear(); _fallbackQuiet = _fallbackSpeech = 0; _fallbackAttempted = false; _acceptedSamples = 0; _retryAtSamples = 0; LastVerificationSamples = 0; } }
    public void Dispose() { lock (_sync) { _live?.Dispose(); _live = null; _spotter?.Dispose(); _spotter = null; _verifier.Dispose(); } _worker.Dispose(); _gate.Dispose(); }
}
