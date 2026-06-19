namespace Wyspa.Core.Models;

public sealed class WakeVoiceProfile
{
    public int SampleRate { get; set; } = 16000;
    public int DurationMs { get; set; }
    public int SegmentCount { get; set; }
    public double[] Features { get; set; } = [];
    public List<double[]> FeatureSets { get; set; } = [];
    public int TrainingSampleCount { get; set; } = 1;
}
