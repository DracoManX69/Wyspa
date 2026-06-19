namespace Wyspa.Core.Abstractions;

public interface IAudioLevelMonitorService : IDisposable
{
    event EventHandler<float>? LevelAvailable;
    event EventHandler<IReadOnlyList<float>>? AudioAvailable;
    bool IsRunning { get; }
    Task StartAsync(string? deviceId, CancellationToken cancellationToken);
    void Stop();
}
