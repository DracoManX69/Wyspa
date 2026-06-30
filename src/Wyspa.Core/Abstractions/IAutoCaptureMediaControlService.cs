using Wyspa.Core.Models;

namespace Wyspa.Core.Abstractions;

public interface IAutoCaptureMediaControlService
{
    Task SetListeningStateAsync(AutoCaptureMediaBehavior behavior, bool isListening, CancellationToken cancellationToken);
    Task RestoreAsync(CancellationToken cancellationToken);
}
