namespace Wyspa.Core.Models;

public sealed class WakeVoiceProfile
{
    public int EnrollmentVersion { get; set; }
    public int SetupSuiteVersion { get; set; }
    public int SetupPromptVariant { get; set; }
    public string? MicrophoneId { get; set; }
    public bool SetupValidated { get; set; }
    public DateTimeOffset? ValidatedAt { get; set; }
    public double NoiseRms { get; set; }
    public double AcousticThreshold { get; set; }
    public double? TunedStrictness { get; set; }
    public List<float[][]> AcousticTemplates { get; set; } = [];
    public List<string> PronunciationVariants { get; set; } = [];
    public List<WakeSetupReading> SetupReadings { get; set; } = [];
    public int DetectorVersion { get; set; }
    public string Phrase { get; set; } = "hey whisper";
    public List<WakeCalibrationTrial> CalibrationTrials { get; set; } = [];
    public int SampleRate { get; set; } = 16000;
    public int DurationMs { get; set; }
    public int SegmentCount { get; set; }
    public double[] Features { get; set; } = [];
    public List<double[]> FeatureSets { get; set; } = [];
    public int TrainingSampleCount { get; set; } = 1;
    public double[] VoiceFeatures { get; set; } = [];
    public List<double[]> VoiceFeatureSets { get; set; } = [];
    public int VoiceTrainingSampleCount { get; set; }
}
