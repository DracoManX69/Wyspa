using Wyspa.Core.Models;

namespace Wyspa.Core.Abstractions;

public interface IAudioCaptureService : IAsyncDisposable
{
    event EventHandler<float>? LevelAvailable;
    bool IsRecording { get; }
    Task<IReadOnlyList<AudioDeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken);
    Task StartRecordingAsync(string? deviceId, CancellationToken cancellationToken);
    Task<RecordingResult> StopRecordingAsync(CancellationToken cancellationToken);
    Task DeleteRecordingAsync(string filePath, CancellationToken cancellationToken);
}
