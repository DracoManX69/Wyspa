namespace Wyspa.Core.Abstractions;

public interface ISecretStore
{
    Task SaveApiKeyAsync(string apiKey, CancellationToken cancellationToken);
    Task<string?> GetApiKeyAsync(CancellationToken cancellationToken);
    Task RemoveApiKeyAsync(CancellationToken cancellationToken);
}
