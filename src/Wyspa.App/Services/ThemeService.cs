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

    public event EventHandler<bool>? ThemeChanged;

    public ThemeService(ResourceDictionary resources, Func<bool>? readSystemDarkMode = null)
    {
        _resources = resources;
        _readSystemDarkMode = readSystemDarkMode ?? ShouldUseDarkMode;
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
        Set("ChromeBrush", darkMode ? "#1A2023" : "#E9F0F2");
        Set("PanelBrush", darkMode ? "#2B2B2B" : "#FCFCFC");
        Set("PanelAltBrush", darkMode ? "#343434" : "#F4F4F4");
        Set("InputBrush", darkMode ? "#242424" : "#FFFFFF");
        Set("InkBrush", darkMode ? "#F7F7F7" : "#171717");
        Set("MutedBrush", darkMode ? "#D1D1D1" : "#505050");
        Set("LineBrush", darkMode ? "#555555" : "#D1D1D1");
        Set("AccentBrush", darkMode ? "#5DD7CF" : "#2B7A78");
        Set("AccentDarkBrush", darkMode ? "#99E8E1" : "#19595A");
        Set("AccentSoftBrush", darkMode ? "#293F40" : "#E2EEEE");
        Set("AccentTextBrush", darkMode ? "#102120" : "#FFFFFF");
        Set("WarnBrush", darkMode ? "#FFBE90" : "#9D4C20");
        Set("SelectedTextBrush", darkMode ? "#102120" : "#FFFFFF");

        if (highContrast)
        {
            foreach (var name in new[] { "AppBackgroundBrush", "ChromeBrush", "PanelBrush", "PanelAltBrush", "InputBrush" })
                _resources[name] = WpfSystemColors.WindowBrush;
            foreach (var name in new[] { "InkBrush", "MutedBrush", "LineBrush", "WarnBrush" })
                _resources[name] = WpfSystemColors.WindowTextBrush;
            _resources["AccentBrush"] = WpfSystemColors.HighlightBrush;
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
        _resources["AccentButtonBackgroundPointerOver"] = highContrast ? WpfSystemColors.HighlightBrush : new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(darkMode ? "#81E1DB" : "#256E6D"));
        _resources["AccentButtonBackgroundPressed"] = highContrast ? WpfSystemColors.HighlightBrush : new SolidColorBrush((MediaColor)MediaColorConverter.ConvertFromString(darkMode ? "#48BAB3" : "#205E5D"));
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
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Accessibility)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(RefreshTheme);
        }
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
