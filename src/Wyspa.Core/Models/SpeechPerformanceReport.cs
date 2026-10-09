namespace Wyspa.Core.Models;

public sealed record SpeechPerformanceResult(string Provider, string ModelId, string Name, string Engine,
    double PreparationSeconds, double FirstSeconds, double TypicalSeconds, double CpuSeconds,
    double AppRamMb, int WordErrors, int ReferenceWords, double AudioSeconds, string? Error = null, string Device = "", string? RequestedDevice = null)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsWinner { get; init; }
    public double? Score => Error is null && ReferenceWords > 0 && TypicalSeconds >= 0 ? Math.Round(
        80 * Math.Pow(Math.Clamp(1 - WordErrors / (double)ReferenceWords, 0, 1), 4) + 20 / (1 + TypicalSeconds), 1) : null;
    public string ScoreDisplay => Score is { } score ? $"{score:0.0}" : "—";
    public string DeviceDisplay => Provider == "groq" ? "Cloud" : RequestedDevice == "GPU" && Device == "CPU" ? "CPU fallback" : Device.Length > 0 ? Device : "Not recorded";
    public string TypicalDisplay => Error is null ? $"{TypicalSeconds:0.00} s" : "Failed";
    public string FirstDisplay => Error is null ? $"{FirstSeconds:0.00} s" : "—";
    public string ProcessingDisplay => Error is null ? $"{AudioSeconds / Math.Max(.001, TypicalSeconds):0.#}× real time" : "—";
    public string CpuDisplay => Error is null ? $"{CpuSeconds:0.00} CPU s" : "—";
    public string MemoryDisplay => Error is null ? $"{AppRamMb:0} MB" : "—";
    public string AccuracyDisplay => Error is null ? $"{WordErrors}/{ReferenceWords} ({WordErrors * 100d / Math.Max(1, ReferenceWords):0.#}%)" : Error;
}
public sealed class SpeechPerformanceReport
{
    public DateTimeOffset MeasuredAt { get; set; }
    public string HardwareFingerprint { get; set; } = "";
    public bool GpuEnabled { get; set; }
    public string SampleVersion { get; set; } = "jfk-v3-ranking";
    public bool LocalComparisonCompleted { get; set; }
    public bool ShowGroqComparison { get; set; }
    public DateTimeOffset? CloudMeasuredAt { get; set; }
    public List<SpeechPerformanceResult> Results { get; set; } = [];
}
