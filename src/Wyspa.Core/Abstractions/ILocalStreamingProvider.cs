using Wyspa.Core.Models;
namespace Wyspa.Core.Abstractions;

public interface ILocalStreamingProvider
{
    bool SupportsLocalStream(TranscriptionOptions options);
    Task<ILocalRecognitionStream> CreateLocalStreamAsync(TranscriptionOptions options, CancellationToken token);
}
public interface ILocalRecognitionStream : IDisposable
{
    Task<string> ProcessAsync(float[] samples, bool finished, CancellationToken token);
}
