using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class JsonSettingsServiceTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTripsNonSecretSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), "Wyspa.Tests", Guid.NewGuid().ToString("N"), "settings.json");
        var service = new JsonSettingsService(path);
        var settings = new AppSettings
        {
            FirstRunComplete = true,
            Language = "en",
            StartMinimized = true,
            Hotkey = new HotkeySettings(HotkeyModifiers.Control | HotkeyModifiers.Shift, "F8")
        };

        await service.SaveAsync(settings, CancellationToken.None);
        var loaded = await service.LoadAsync(CancellationToken.None);

        Assert.True(loaded.FirstRunComplete);
        Assert.Equal("en", loaded.Language);
        Assert.True(loaded.StartMinimized);
        Assert.Equal("F8", loaded.Hotkey.Key);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Shift, loaded.Hotkey.Modifiers);
    }
}
