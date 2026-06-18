using Microsoft.Win32;
using System.Windows;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using MediaColorConverter = System.Windows.Media.ColorConverter;
using WpfSystemColors = System.Windows.SystemColors;

namespace Wyspa.App.Services;

public sealed class ThemeService : IDisposable
{
    private readonly ResourceDictionary _resources;

    public event EventHandler<bool>? ThemeChanged;

    public ThemeService(ResourceDictionary resources)
    {
        _resources = resources;
        try
        {
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        }
        catch
        {
        }

        ApplySystemTheme();
    }

    public bool IsDarkMode { get; private set; }

    public void ApplySystemTheme()
    {
        IsDarkMode = ShouldUseDarkMode();
        Set("AppBackgroundBrush", IsDarkMode ? "#111416" : "#F3F6F8");
        Set("ChromeBrush", IsDarkMode ? "#F0181D20" : "#E8FFFFFF");
        Set("PanelBrush", IsDarkMode ? "#F022282C" : "#F4FBFCFD");
        Set("PanelAltBrush", IsDarkMode ? "#F02B3338" : "#ECF4F7F9");
        Set("InputBrush", IsDarkMode ? "#FF15191C" : "#FFFFFFFF");
        Set("InkBrush", IsDarkMode ? "#FFF7FAFC" : "#172025");
        Set("MutedBrush", IsDarkMode ? "#FFB9C4CA" : "#5F6C72");
        Set("LineBrush", IsDarkMode ? "#FF49545C" : "#D5DEE5");
        Set("AccentBrush", IsDarkMode ? "#FF5DD7CF" : "#2B7A78");
        Set("AccentDarkBrush", IsDarkMode ? "#FFB8FFF8" : "#19595A");
        Set("AccentSoftBrush", IsDarkMode ? "#FF1F4C4D" : "#DDF3EE");
        Set("WarnBrush", IsDarkMode ? "#FFFFB074" : "#B85B35");
        Set("SelectedTextBrush", "#FFFFFFFF");
        Set(WpfSystemColors.WindowBrushKey, IsDarkMode ? "#FF15191C" : "#FFFFFFFF");
        Set(WpfSystemColors.WindowTextBrushKey, IsDarkMode ? "#FFF7FAFC" : "#172025");
        Set(WpfSystemColors.ControlBrushKey, IsDarkMode ? "#FF15191C" : "#FFFFFFFF");
        Set(WpfSystemColors.ControlTextBrushKey, IsDarkMode ? "#FFF7FAFC" : "#172025");
        Set(WpfSystemColors.HighlightBrushKey, IsDarkMode ? "#FF1F4C4D" : "#DDF3EE");
        Set(WpfSystemColors.HighlightTextBrushKey, IsDarkMode ? "#FFB8FFF8" : "#19595A");
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
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.VisualStyle)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(ApplySystemTheme);
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
