using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

public sealed class StreamingDictationSession : IDisposable
{
    private readonly StreamingAudioBuffer _audio = new();
    private readonly StreamingTranscript _transcript = new();
    private readonly IGroqTranscriptionClient _groq;
    private readonly string _key;
    private readonly TranscriptionOptions _options;
    private readonly Func<string, string, CancellationToken, Task> _publish;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _start;
    private long _lastSnapshotEnd;
    private Exception? _captureError;
    private string _pendingDelta = "";
    private string? _finalText;
    public string Text => _finalText ?? _transcript.Text;
    public event EventHandler<bool>? RequestActivityChanged;

    public StreamingDictationSession(IGroqTranscriptionClient groq, string key, TranscriptionOptions options,
        Func<string, string, CancellationToken, Task> publish)
    {
        _groq = groq;
        _key = key;
        _options = options with { ResponseFormat = "verbose_json" };
        _publish = publish;
    }

    public void AddAudio(object? sender, ReadOnlyMemory<byte> pcm)
    {
        try { _audio.Append(pcm.Span); }
        catch (Exception ex) { _captureError = ex; }
    }

    public async Task ProcessAsync(bool stopped, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            // A clipboard failure or cancellation before injection must not lose a
            // prefix already accepted by the recognizer. Native insertion never
            // throws after sending text; a partial send returns clipboard-only.
            await PublishPendingAsync(token);
            if (stopped)
            {
                _finalText = await TranscribeCompleteAudioAsync(token);
                var tail = StreamTextReconciliation.AppendOnlyTail(_transcript.Text, _finalText);
                if (_finalText.Length > 0) await _publish(tail, _finalText, token);
                return;
            }
            do
            {
                token.ThrowIfCancellationRequested();
                if (_captureError is not null) throw new IOException("Stream audio could not be buffered. Check free disk space.", _captureError);
                var window = _audio.Read(_start, stopped);
                if (window.Samples.Length == 0) break;
                if (!window.HasSpeech)
                {
                    // Preserve pre-roll and very short speech until more audio arrives.
                    if (stopped || window.Final || window.AtLimit)
                        _start = window.End;
                    else if (window.Samples.All(s => Math.Abs(s) < .0001f))
                        _start = Math.Max(_start, window.End - StreamingAudioBuffer.SampleRate / 5);
                    if (stopped && _start < _audio.SampleCount) continue;
                    break;
                }
                if (!stopped && !window.Final && !window.AtLimit &&
                    (window.End - _lastSnapshotEnd < StreamingAudioBuffer.SampleRate || window.Duration < 1)) break;

                var path = Path.Combine(Path.GetTempPath(), "Wyspa", $"stream-snapshot-{Guid.NewGuid():N}.wav");
                try
                {
                    PcmWave.Write(path, window.Samples);
                    var json = await RequestAsync(path, token);
                    token.ThrowIfCancellationRequested();
                    var result = GroqNoteIntelligence.Parse(json);
                    if (result.Words.Count == 0 && !string.IsNullOrWhiteSpace(result.Text))
                        throw new InvalidOperationException("Groq did not return word timestamps for Stream Mode. Try again or turn Stream Mode off.");
                    var delta = _transcript.Accept(result, window.StartSeconds, window.Duration, window.Final || stopped && !window.AtLimit, window.AtLimit);
                    _pendingDelta += delta;
                    await PublishPendingAsync(token);
                    _lastSnapshotEnd = window.End;
                    if (window.Final)
                    {
                        _start = window.End;
                        _transcript.EndWindow();
                    }
                    else if (window.AtLimit)
                    {
                        // Retain context behind the last complete word; its midpoint is
                        // filtered by the transcript cursor on the next window.
                        var through = (long)(_transcript.CommittedThrough * StreamingAudioBuffer.SampleRate);
                        _start = Math.Max(_start + StreamingAudioBuffer.SampleRate,
                            through > window.Start ? through - StreamingAudioBuffer.SampleRate / 4 : window.End - StreamingAudioBuffer.SampleRate);
                        _transcript.EndWindow();
                    }
                    else break;
                }
                finally { File.Delete(path); }
            } while (stopped && _start < _audio.SampleCount);
            if (stopped && Text.Length > 0) await _publish("", Text, token);
        }
        finally { _gate.Release(); }
    }

    private async Task<string> TranscribeCompleteAudioAsync(CancellationToken token)
    {
        if (_captureError is not null) throw new IOException("Stream audio could not be buffered. Check free disk space.", _captureError);
        // Up to two minutes is recognized in one request (3.84 MB PCM). Longer
        // recordings use bounded windows with four seconds of shared context.
        // Each side owns half the overlap; genuinely repeated speech is retained.
        const int windowSamples = 120 * StreamingAudioBuffer.SampleRate;
        const int overlap = 4 * StreamingAudioBuffer.SampleRate;
        var words = new List<string>();
        for (long start = 0; start < _audio.SampleCount; start += windowSamples - overlap)
        {
            token.ThrowIfCancellationRequested();
            var window = _audio.Read(start, true, windowSamples);
            if (!window.HasSpeech) continue;
            var path = Path.Combine(Path.GetTempPath(), "Wyspa", $"stream-final-{Guid.NewGuid():N}.wav");
            try
            {
                PcmWave.Write(path, window.Samples);
                var speech = GroqNoteIntelligence.Parse(await RequestAsync(path, token));
                if (start == 0 && window.Final) return speech.Text.Trim();
                if (speech.Words.Count == 0 && !string.IsNullOrWhiteSpace(speech.Text))
                    throw new InvalidOperationException("Groq did not return timestamps for the complete recording.");
                var from = start == 0 ? 0 : 2;
                var to = window.Final ? window.Duration + .1 : window.Duration - 2;
                words.AddRange(speech.Words.Where(w => (w.Start + w.End) / 2 >= from && (w.Start + w.End) / 2 < to).Select(w => w.Text.Trim()));
            }
            finally { File.Delete(path); }
            if (window.Final) break;
        }
        return string.Join(" ", words);
    }

    private async Task<string> RequestAsync(string path, CancellationToken token)
    {
        RequestActivityChanged?.Invoke(this, true);
        try { return await _groq.TranscribeAsync(_key, path, _options, token); }
        finally { RequestActivityChanged?.Invoke(this, false); }
    }

    private async Task PublishPendingAsync(CancellationToken token)
    {
        if (_pendingDelta.Length == 0) return;
        await _publish(_pendingDelta, Text, token);
        _pendingDelta = "";
    }

    public void Dispose()
    {
        _audio.Dispose();
        _gate.Dispose();
    }
}
