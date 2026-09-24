using Wyspa.Core.Models;

namespace Wyspa.Core.Abstractions;

public interface IAudioFilePreparationService
{
    Task<PreparedAudio> PrepareAsync(string path, IProgress<string>? progress, CancellationToken cancellationToken);
}
