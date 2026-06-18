using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using Wyspa.App.Services;
using Wyspa.App.ViewModels;

namespace Wyspa.App;

public partial class MainWindow : Window
{
    private bool _isRecordingHotkey;
    private ScratchpadWindow? _scratchpadWindow;
    public bool IsDarkMode { get; set; }

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyTheme(IsDarkMode);
    }

    public void ApplyTheme(bool darkMode)
    {
        IsDarkMode = darkMode;
        NativeWindowStyler.Apply(this, darkMode);
        _scratchpadWindow?.ApplyTheme(darkMode);
    }

    private void OpenScratchpadButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_scratchpadWindow is null || !_scratchpadWindow.IsLoaded)
        {
            _scratchpadWindow = new ScratchpadWindow(IsDarkMode)
            {
                Owner = this,
                DataContext = DataContext
            };
            _scratchpadWindow.Closed += (_, _) => _scratchpadWindow = null;
        }

        _scratchpadWindow.Show();
        _scratchpadWindow.Activate();
    }

    private void RecordHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        _isRecordingHotkey = true;
        HotkeyRecorderBox.Text = "Press shortcut...";
        HotkeyRecorderBox.Focus();
        Keyboard.Focus(HotkeyRecorderBox);
    }

    private void Window_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (!_isRecordingHotkey)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key is Key.System ? e.SystemKey : e.Key;
        key = key is Key.ImeProcessed ? e.ImeProcessedKey : key;

        if (IsModifierKey(key))
        {
            HotkeyRecorderBox.Text = FormatModifiers(Keyboard.Modifiers);
            return;
        }

        var shortcut = FormatShortcut(Keyboard.Modifiers, key);
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.HotkeyText = shortcut;
        }
        else
        {
            HotkeyRecorderBox.Text = shortcut;
        }

        _isRecordingHotkey = false;
    }

    private static bool IsModifierKey(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
            Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
    }

    private static string FormatShortcut(ModifierKeys modifiers, Key key)
    {
        var parts = new List<string>();
        if (modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(FormatKey(key));
        return string.Join("+", parts);
    }

    private static string FormatModifiers(ModifierKeys modifiers)
    {
        var text = FormatShortcut(modifiers, Key.None);
        return text.EndsWith("+None", StringComparison.Ordinal) ? text[..^5] + "+" : text;
    }

    private static string FormatKey(Key key)
    {
        return key switch
        {
            Key.None => "None",
            Key.Space => "Space",
            Key.Return => "Enter",
            Key.Prior => "PageUp",
            Key.Next => "PageDown",
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            >= Key.NumPad0 and <= Key.NumPad9 => ((int)(key - Key.NumPad0)).ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => key.ToString()
        };
    }

    private void Hyperlink_OnRequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
        {
            UseShellExecute = true
        });
        e.Handled = true;
    }
}
