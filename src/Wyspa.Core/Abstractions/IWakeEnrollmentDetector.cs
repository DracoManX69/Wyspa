using Wyspa.Core.Models;

namespace Wyspa.Core.Abstractions;

public interface IWakeEnrollmentDetector
{
    Task<WakeSampleAnalysis> AnalyzeAsync(float[] samples, string phrase, CancellationToken token);
    Task<bool> CheckAsync(float[] samples, string phrase, WakeVoiceProfile profile, double strictness, CancellationToken token);
    void ConfigurePersonalization(WakeVoiceProfile? profile, string? microphoneId);
}
