namespace Wyspa.Core.Services;

// Ends a bounded wizard sample after speech and a pause; noisy rooms retain Finish and the cap.
public sealed class WakeSampleEndpoint(double noiseRms, bool phraseOnly)
{
    private readonly double _floor = Math.Max(.002, noiseRms * 2.5);
    private readonly int _pauseSamples = phraseOnly ? 9600 : 16000;
    private int _speech, _quiet;
    public bool Feed(IReadOnlyList<float> samples)
    {
        var rms = Math.Sqrt(samples.Select(s => (double)s * s).DefaultIfEmpty().Average());
        if (rms > _floor) { _speech += samples.Count; _quiet = 0; }
        else _quiet += samples.Count;
        return _speech >= (phraseOnly ? 6400 : 16000) && _quiet >= _pauseSamples;
    }
}
