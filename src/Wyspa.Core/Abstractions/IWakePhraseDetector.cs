namespace Wyspa.Core.Abstractions;

public interface IWakePhraseDetector : IDisposable
{
    Task PrepareAsync(IProgress<string>? progress, CancellationToken token);
    Task<bool> ProcessAsync(float[] samples, string phrase, double strictness, CancellationToken token);
    Task<double?> EvaluateAsync(float[] samples, string phrase, CancellationToken token);
    void Reset();
}
