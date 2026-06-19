using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class HotkeyValidatorTests
{
    [Fact]
    public void TryValidate_AllowsDefaultHotkey()
    {
        var valid = HotkeyValidator.TryValidate(HotkeySettings.Default, out var message);

        Assert.True(valid);
        Assert.Null(message);
    }

    [Fact]
    public void TryValidate_AllowsDefaultAutoCaptureHotkey()
    {
        var valid = HotkeyValidator.TryValidate(HotkeySettings.DefaultAutoCapture, out var message);

        Assert.True(valid);
        Assert.Null(message);
    }

    [Fact]
    public void TryValidate_RejectsMissingModifier()
    {
        var valid = HotkeyValidator.TryValidate(new HotkeySettings(HotkeyModifiers.None, "Space"), out var message);

        Assert.False(valid);
        Assert.Contains("modifier", message);
    }

    [Fact]
    public void TryParse_ReadsUserFriendlyHotkeyText()
    {
        var valid = HotkeyValidator.TryParse("Ctrl+Alt+Space", out var hotkey, out var message);

        Assert.True(valid);
        Assert.Null(message);
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Alt, hotkey.Modifiers);
        Assert.Equal("Space", hotkey.Key);
    }

    [Fact]
    public void TryParse_AllowsDedicatedMacroFunctionKey()
    {
        var valid = HotkeyValidator.TryParse("F13", out var hotkey, out var message);

        Assert.True(valid);
        Assert.Null(message);
        Assert.Equal(HotkeyModifiers.None, hotkey.Modifiers);
        Assert.Equal("F13", hotkey.Key);
    }

    [Fact]
    public void TryParse_AllowsModifiedFunctionKey()
    {
        var valid = HotkeyValidator.TryParse("Ctrl+F4", out var hotkey, out var message);

        Assert.True(valid);
        Assert.Null(message);
        Assert.Equal(HotkeyModifiers.Control, hotkey.Modifiers);
        Assert.Equal("F4", hotkey.Key);
    }
}
