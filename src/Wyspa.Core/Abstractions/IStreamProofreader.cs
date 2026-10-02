using Wyspa.Core.Models;

namespace Wyspa.Core.Abstractions;

public interface IStreamProofreader
{
    Task<StreamFixResult> ProofreadStreamAsync(string apiKey, string text, string model, CancellationToken token);
}
