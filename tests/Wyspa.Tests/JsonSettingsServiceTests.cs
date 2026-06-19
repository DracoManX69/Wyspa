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
            CopyInsertedTextToClipboard = true,
            WakeToneEnabled = true,
            WakeTonePath = @"C:\Sounds\wake.wav",
            Hotkey = new HotkeySettings(HotkeyModifiers.Control | HotkeyModifiers.Shift, "F8"),
            AutoCaptureHotkey = new HotkeySettings(HotkeyModifiers.Control | HotkeyModifiers.Alt, "A"),
            AutoCaptureWakeVoiceEnabled = true,
            AutoCaptureWakeVoiceSensitivity = 0.81,
            AutoCaptureWakeVoiceProfile = new WakeVoiceProfile
            {
                DurationMs = 1200,
                SegmentCount = 12,
                Features = [0.1, 0.2, 0.3],
                FeatureSets = [[0.1, 0.2, 0.3]],
                TrainingSampleCount = 2,
                VoiceFeatures = [0.3, 0.2, 0.1],
                VoiceFeatureSets = [[0.3, 0.2, 0.1]],
                VoiceTrainingSampleCount = 4
            }
        };

        await service.SaveAsync(settings, CancellationToken.None);
        var loaded = await service.LoadAsync(CancellationToken.None);

        Assert.True(loaded.FirstRunComplete);
        Assert.Equal("en", loaded.Language);
        Assert.True(loaded.StartMinimized);
        Assert.True(loaded.CopyInsertedTextToClipboard);
        Assert.True(loaded.WakeToneEnabled);
        Assert.Equal(@"C:\Sounds\wake.wav", loaded.WakeTonePath);
        Assert.Equal("F8", loaded.Hotkey.Key);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Shift, loaded.Hotkey.Modifiers);
        Assert.Equal("A", loaded.AutoCaptureHotkey.Key);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, loaded.AutoCaptureHotkey.Modifiers);
        Assert.True(loaded.AutoCaptureWakeVoiceEnabled);
        Assert.Equal(0.81, loaded.AutoCaptureWakeVoiceSensitivity);
        Assert.NotNull(loaded.AutoCaptureWakeVoiceProfile);
        Assert.Equal(1200, loaded.AutoCaptureWakeVoiceProfile.DurationMs);
        Assert.Equal(2, loaded.AutoCaptureWakeVoiceProfile.TrainingSampleCount);
        Assert.Single(loaded.AutoCaptureWakeVoiceProfile.FeatureSets);
        Assert.Equal(4, loaded.AutoCaptureWakeVoiceProfile.VoiceTrainingSampleCount);
        Assert.Single(loaded.AutoCaptureWakeVoiceProfile.VoiceFeatureSets);
    }

    [Fact]
    public async Task ConcurrentAccess_ToSamePath_DoesNotThrow()
    {
        var path = Path.Combine(Path.GetTempPath(), "Wyspa.Tests", Guid.NewGuid().ToString("N"), "settings.json");
        var reader = new JsonSettingsService(path);
        var writer = new JsonSettingsService(path);
        var initialSettings = new AppSettings { FirstRunComplete = true, Language = "en" };

        await writer.SaveAsync(initialSettings, CancellationToken.None);

        var tasks = Enumerable.Range(0, 20).Select(async index =>
        {
            if (index % 2 == 0)
            {
                await writer.SaveAsync(new AppSettings
                {
                    FirstRunComplete = true,
                    Language = "en",
                    StartMinimized = index % 4 == 0
                }, CancellationToken.None);
            }
            else
            {
                _ = await reader.LoadAsync(CancellationToken.None);
            }
        });

        await Task.WhenAll(tasks);
        var loaded = await reader.LoadAsync(CancellationToken.None);

        Assert.True(loaded.FirstRunComplete);
        Assert.Equal("en", loaded.Language);
    }
}
