namespace Wyspa.Core.Services;

// Monotonic, lock-protected endpoint state; the start threshold never controls the silence clock.
public sealed class SpeechEndpoint
{
    private readonly object _sync = new();
    private long _generation;
    private TimeSpan _started, _speech;
    private bool _active;
    public long Begin(TimeSpan now) { lock (_sync) { _active = true; _started = _speech = now; return ++_generation; } }
    public long Generation { get { lock (_sync) return _generation; } }
    public void Speech(long generation, TimeSpan now) { lock (_sync) if (_active && generation == _generation && now > _speech) _speech = now; }
    public bool ShouldStop(TimeSpan now, int silenceMs, int minimumMs)
    {
        lock (_sync) return _active && now - _speech >= TimeSpan.FromMilliseconds(Math.Clamp(silenceMs, 400, 5000)) && now - _started >= TimeSpan.FromMilliseconds(minimumMs);
    }
    public void End() { lock (_sync) { _active = false; ++_generation; } }
    public static bool FallbackSpeech(float level, float startThreshold) => level >= Math.Max(.015f, startThreshold * .65f);
}
