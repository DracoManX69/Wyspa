using Wyspa.Core.Models;

namespace Wyspa.Core.Abstractions;

public interface IGroqTranscriptionClient
{
    Task<ConnectionTestResult> TestConnectionAsync(string apiKey, CancellationToken cancellationToken);
    Task<string> TranscribeAsync(string apiKey, string audioFilePath, TranscriptionOptions options, CancellationToken cancellationToken);
    Task<IntentResolution> InterpretIntentAsync(string apiKey, string transcript, string modelId, CancellationToken cancellationToken);
}
