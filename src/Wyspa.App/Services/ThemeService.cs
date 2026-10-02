using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using Wyspa.Core.Models;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using WpfSystemColors = System.Windows.SystemColors;

namespace Wyspa.App.Services;

public sealed class ThemeService : IDisposable
{
    private readonly ResourceDictionary _resources;
    private readonly Func<bool> _readSystemDarkMode;
    private readonly Func<MediaColor> _readAccentColor;

    public event EventHandler<bool>? ThemeChanged;

    public ThemeService(ResourceDictionary resources, Func<bool>? readSystemDarkMode = null, Func<MediaColor>? readAccentColor = null)
    {
        _resources = resources;
        _readSystemDarkMode = readSystemDarkMode ?? ShouldUseDarkMode;
        _readAccentColor = readAccentColor ?? (() => WpfSystemColors.AccentColor);
        try
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        catch
        {
        }

        RefreshTheme();
    }

    public bool IsDarkMode { get; private set; }
    public AppTheme Preference { get; private set; } = AppTheme.System;

    public void ApplyPreference(AppTheme preference)
    {
        if (!Enum.IsDefined(preference)) preference = AppTheme.System;
        if (Preference == preference) return;
        Preference = preference;
        RefreshTheme();
    }

    // Re-evaluate the saved preference when Windows appearance or accessibility changes.
    public void RefreshTheme() => ApplyTheme(Preference switch
    {
        AppTheme.Dark => true,
        AppTheme.Light => false,
        _ => _readSystemDarkMode()
    });

    // Also used by the native smoke harness without changing Windows preferences.
    public void ApplyTheme(bool darkMode)
    {
        IsDarkMode = darkMode;
        var highContrast = SystemParameters.HighContrast;
        // Let WPF own native color resources. Replacing a Fluent.Light dictionary leaves
        // instantiated styles holding that dictionary's colors after a live theme change.
        // The code API remains experimental in .NET 10; isolate that opt-in here.
#pragma warning disable WPF0001
        System.Windows.Application.Current.ThemeMode = highContrast ? ThemeMode.System : darkMode ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001

        Set("AppBackgroundBrush", darkMode ? "#1E1E1E" : "#EFEFEF");
        Set("ChromeBrush", darkMode ? "#202020" : "#E9E9E9");
        Set("PanelBrush", darkMode ? "#2B2B2B" : "#FCFCFC");
        Set("PanelAltBrush", darkMode ? "#343434" : "#F4F4F4");
        Set("InputBrush", darkMode ? "#242424" : "#FFFFFF");
        Set("InkBrush", darkMode ? "#F7F7F7" : "#171717");
        Set("MutedBrush", darkMode ? "#D1D1D1" : "#505050");
        Set("LineBrush", darkMode ? "#555555" : "#D1D1D1");
        var accent = _readAccentColor();
        accent.A = 255;
        var accentText = Contrast(accent, Colors.Black) >= Contrast(accent, Colors.White) ? Colors.Black : Colors.White;
        var surface = darkMode ? MediaColor.FromRgb(30, 30, 30) : MediaColor.FromRgb(239, 239, 239);
        var link = accent;
        var contrastTarget = darkMode ? Colors.White : Colors.Black;
        for (var i = 0; i < 20 && Contrast(link, surface) < 4.5; i++) link = Blend(link, contrastTarget, .12);
        _resources["AccentBrush"] = new SolidColorBrush(accent);
        _resources["LogoGradientBrush"] = new SolidColorBrush(accent);
        _resources["AccentDarkBrush"] = new SolidColorBrush(link);
        _resources["AccentSoftBrush"] = new SolidColorBrush(Blend(surface, accent, darkMode ? .22 : .12));
        _resources["AccentTextBrush"] = new SolidColorBrush(accentText);
        _resources["SelectedTextBrush"] = new SolidColorBrush(accentText);
        Set("WarnBrush", darkMode ? "#FFBE90" : "#9D4C20");

        if (highContrast)
        {
            foreach (var name in new[] { "AppBackgroundBrush", "ChromeBrush", "PanelBrush", "PanelAltBrush", "InputBrush" })
                _resources[name] = WpfSystemColors.WindowBrush;
            foreach (var name in new[] { "InkBrush", "MutedBrush", "LineBrush", "WarnBrush" })
                _resources[name] = WpfSystemColors.WindowTextBrush;
            _resources["AccentBrush"] = WpfSystemColors.HighlightBrush;
            _resources["LogoGradientBrush"] = WpfSystemColors.HighlightBrush;
            _resources["AccentDarkBrush"] = WpfSystemColors.HotTrackBrush;
            _resources["AccentSoftBrush"] = WpfSystemColors.ControlBrush;
            _resources["AccentTextBrush"] = WpfSystemColors.HighlightTextBrush;
            _resources["SelectedTextBrush"] = WpfSystemColors.HighlightTextBrush;
        }
        // Documented .NET Fluent resource roles; keep native hover/pressed/disabled templates.
        foreach (var name in new[] { "AccentButtonBackground", "AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed",
                     "AccentButtonBorderBrush", "AccentButtonBorderBrushPointerOver", "AccentButtonBorderBrushPressed",
                     "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush",
                     "SliderThumbBackground", "SliderThumbBackgroundPointerOver", "ProgressBarForeground", "ComboBoxItemPillFillBrush" })
            _resources[name] = _resources["AccentBrush"];
        foreach (var name in new[] { "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed" })
            _resources[name] = _resources["AccentTextBrush"];
        _resources["AccentButtonBackgroundPointerOver"] = highContrast ? WpfSystemColors.HighlightBrush : new SolidColorBrush(Blend(accent, accentText == Colors.Black ? Colors.White : Colors.Black, .08));
        _resources["AccentButtonBackgroundPressed"] = highContrast ? WpfSystemColors.HighlightBrush : new SolidColorBrush(Blend(accent, accentText == Colors.Black ? Colors.White : Colors.Black, .16));
        foreach (var name in new[] { "HyperlinkForeground", "HyperlinkForegroundPointerOver", "HyperlinkForegroundPressed", "TextControlBorderBrushFocused" })
            _resources[name] = _resources["AccentDarkBrush"];
        ThemeChanged?.Invoke(this, IsDarkMode);
    }

    public void Dispose()
    {
        try
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        }
        catch
        {
        }
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Accessibility or UserPreferenceCategory.Color or UserPreferenceCategory.Desktop)
        {
            System.Windows.Application.Current.Dispatcher.BeginInvoke(RefreshTheme);
        }
    }

    private static MediaColor Blend(MediaColor from, MediaColor to, double amount) => MediaColor.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));

    private static double Luminance(MediaColor color)
    {
        static double Linear(byte value) => value / 255d <= .04045 ? value / 255d / 12.92 : Math.Pow((value / 255d + .055) / 1.055, 2.4);
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }

    private static double Contrast(MediaColor a, MediaColor b)
    {
        var x = Luminance(a); var y = Luminance(b);
        return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05);
    }

    private static bool ShouldUseDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var value = key?.GetValue("AppsUseLightTheme");
            return value is int intValue && intValue == 0;
        }
        catch
        {
            return false;
        }
    }

    private void Set(string key, string color)
    {
        Set((object)key, color);
    }

    private void Set(object key, string color)
    {
        if (MediaColorConverter.ConvertFromString(color) is MediaColor parsed)
        {
            _resources[key] = new SolidColorBrush(parsed);
        }
    }
}
