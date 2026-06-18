using System.Security.Cryptography;
using System.Text;
using Wyspa.Core.Abstractions;

namespace Wyspa.Infrastructure.Settings;

public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _secretPath;

    public DpapiSecretStore(string? secretPath = null)
    {
        _secretPath = secretPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Wyspa",
            "groq-key.bin");
    }

    public async Task SaveApiKeyAsync(string apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key is required.", nameof(apiKey));
        }

        Directory.CreateDirectory(Path.GetDirectoryName(_secretPath)!);
        var bytes = Encoding.UTF8.GetBytes(apiKey.Trim());
        var protectedBytes = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        await File.WriteAllBytesAsync(_secretPath, protectedBytes, cancellationToken);
    }

    public async Task<string?> GetApiKeyAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_secretPath))
        {
            return null;
        }

        var protectedBytes = await File.ReadAllBytesAsync(_secretPath, cancellationToken);
        var bytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    public Task RemoveApiKeyAsync(CancellationToken cancellationToken)
    {
        if (File.Exists(_secretPath))
        {
            File.Delete(_secretPath);
        }

        return Task.CompletedTask;
    }
}
