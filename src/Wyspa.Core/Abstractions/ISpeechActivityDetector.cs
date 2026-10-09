namespace Wyspa.Core.Abstractions;

public interface ISpeechActivityDetector : IDisposable
{
    Task PrepareAsync(IProgress<string>? progress, CancellationToken token);
    Task<bool> ProcessAsync(float[] samples, CancellationToken token);
    void Reset();
}
