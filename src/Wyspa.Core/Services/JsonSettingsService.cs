using System.Collections.Concurrent;
using System.Text.Json;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

public sealed class JsonSettingsService : ISettingsService
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _settingsPath;
    private readonly SemaphoreSlim _ioGate;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public JsonSettingsService(string? settingsPath = null)
    {
        _settingsPath = Path.GetFullPath(settingsPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Wyspa",
            "settings.json"));
        _ioGate = Gates.GetOrAdd(_settingsPath, _ => new SemaphoreSlim(1, 1));
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken)
    {
        await _ioGate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            await using var stream = File.OpenRead(_settingsPath);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, cancellationToken) ?? new AppSettings();
        }
        finally
        {
            _ioGate.Release();
        }
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        await _ioGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            await using var stream = File.Create(_settingsPath);
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        }
        finally
        {
            _ioGate.Release();
        }
    }
}
