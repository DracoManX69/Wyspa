namespace Wyspa.Core.Services;

// 16 kHz mono. Flush at a pause or after a short maximum window, preserving a small overlap.
public sealed class SpeechChunker(Action<double, float[], double> emit, double maximumSeconds = 3.5, float threshold = .008f)
{
    private readonly List<float> _samples = [];
    private readonly Queue<float> _preRoll = new();
    private double _start;
    private double _lastVoice;
    private double _lastEnd;
    private int _voicedSamples;
    private bool _continuation;

    public void Add(float[] samples, double start)
    {
        if (_samples.Count > 0 && start - _lastEnd > .3) Flush();
        // Work in 20 ms frames so a callback containing mixed silence/speech cannot defeat VAD.
        for (var i = 0; i < samples.Length; i += 320)
        {
            var count = Math.Min(320, samples.Length - i);
            var time = start + i / 16000d;
            var energy = 0d;
            for (var j = 0; j < count; j++) energy += samples[i + j] * samples[i + j];
            var voice = Math.Sqrt(energy / count) >= threshold;
            if (voice)
            {
                if (_samples.Count == 0)
                {
                    _start = Math.Max(0, time - _preRoll.Count / 16000d);
                    _samples.AddRange(_preRoll); _preRoll.Clear();
                }
                _lastVoice = time + count / 16000d;
                _voicedSamples += count;
            }
            if (_samples.Count > 0 || voice)
            {
                for (var j = 0; j < count; j++) _samples.Add(samples[i + j]);
                if (time - _lastVoice >= .45 || _samples.Count >= maximumSeconds * 16000)
                {
                    var continuous = voice;
                    Flush(continuous);
                }
            }
            else
            {
                for (var j = 0; j < count; j++) _preRoll.Enqueue(samples[i + j]);
                while (_preRoll.Count > 3200) _preRoll.Dequeue();
            }
        }
        _lastEnd = start + samples.Length / 16000d;
    }

    public void Tick(double now) { if (_samples.Count > 0 && now - _lastEnd > .5) Flush(); }
    public void Flush(bool retainOverlap = false)
    {
        if (_voicedSamples >= 1600 || (_continuation && _voicedSamples > 0)) emit(_start, _samples.ToArray(), _continuation ? .2 : 0);
        var tail = retainOverlap ? _samples.TakeLast(3200).ToArray() : [];
        _start += Math.Max(0, _samples.Count - tail.Length) / 16000d;
        _samples.Clear(); _samples.AddRange(tail);
        _voicedSamples = 0; _continuation = retainOverlap;
        _preRoll.Clear();
    }
}
