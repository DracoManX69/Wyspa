using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Threading;
using Wyspa.App.Services;
using Wyspa.App.ViewModels;

namespace Wyspa.App;

public partial class MainWindow : Window
{
    private const string SavedApiKeyPlaceholder = "SavedApiKey";
    private const string SavedApiKeyDisplayText = "saved-api-key";
    private readonly DispatcherTimer _autoSaveTimer;
    private bool _isReadyForAutoSave;
    private bool _isUpdatingApiKeyBox;
    private MainViewModel? _observedViewModel;
    private HotkeyRecordingTarget _recordingHotkeyTarget = HotkeyRecordingTarget.None;
    private ScratchpadWindow? _scratchpadWindow;
    public bool IsDarkMode { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _autoSaveTimer.Tick += AutoSaveTimer_OnTick;
        Loaded += (_, _) =>
        {
            _isReadyForAutoSave = true;
            RefreshApiKeyBox();
        };
        DataContextChanged += MainWindow_OnDataContextChanged;
        AddHandler(System.Windows.Controls.CheckBox.CheckedEvent, new RoutedEventHandler(AutoSaveControl_OnChanged), handledEventsToo: true);
        AddHandler(System.Windows.Controls.CheckBox.UncheckedEvent, new RoutedEventHandler(AutoSaveControl_OnChanged), handledEventsToo: true);
        AddHandler(System.Windows.Controls.ComboBox.SelectionChangedEvent, new System.Windows.Controls.SelectionChangedEventHandler(AutoSaveComboBox_OnSelectionChanged), handledEventsToo: true);
        AddHandler(System.Windows.Controls.Slider.ValueChangedEvent, new RoutedPropertyChangedEventHandler<double>(AutoSaveSlider_OnValueChanged), handledEventsToo: true);
        AddHandler(System.Windows.Controls.TextBox.LostFocusEvent, new RoutedEventHandler(AutoSaveTextBox_OnLostFocus), handledEventsToo: true);
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

    private async void ChooseWakeToneButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose wake tone",
            Filter = "WAV audio (*.wav)|*.wav",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            await viewModel.SetWakeTonePathAsync(dialog.FileName);
        }
    }

    private async void ResetWakeToneButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.SetWakeTonePathAsync(null);
        }
    }

    private void TestWakeToneButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            new WakeToneService().Play(viewModel.Settings);
        }
    }

    private void MainWindow_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
        }

        _observedViewModel = e.NewValue as MainViewModel;
        if (_observedViewModel is not null)
        {
            _observedViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        }

        RefreshApiKeyBox();
    }

    private void ViewModel_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.HasApiKey) or nameof(MainViewModel.ApiKey))
        {
            Dispatcher.BeginInvoke(new Action(RefreshApiKeyBox));
        }
    }

    private void ApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingApiKeyBox || DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (IsShowingSavedApiKeyPlaceholder())
        {
            return;
        }

        viewModel.ApiKey = ApiKeyBox.Password;
    }

    private void ApiKeyBox_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (!IsShowingSavedApiKeyPlaceholder())
        {
            return;
        }

        _isUpdatingApiKeyBox = true;
        ApiKeyBox.Tag = null;
        ApiKeyBox.Clear();
        _isUpdatingApiKeyBox = false;
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ApiKey = string.Empty;
        }
    }

    private void ApiKeyBox_OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        RefreshApiKeyBox();
    }

    private void RefreshApiKeyBox()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        if (!viewModel.HasApiKey || !string.IsNullOrWhiteSpace(viewModel.ApiKey) || ApiKeyBox.IsKeyboardFocusWithin)
        {
            if (!viewModel.HasApiKey && IsShowingSavedApiKeyPlaceholder())
            {
                _isUpdatingApiKeyBox = true;
                ApiKeyBox.Tag = null;
                ApiKeyBox.Clear();
                _isUpdatingApiKeyBox = false;
            }

            return;
        }

        _isUpdatingApiKeyBox = true;
        ApiKeyBox.Tag = SavedApiKeyPlaceholder;
        ApiKeyBox.Password = SavedApiKeyDisplayText;
        _isUpdatingApiKeyBox = false;
    }

    private bool IsShowingSavedApiKeyPlaceholder() =>
        string.Equals(ApiKeyBox.Tag as string, SavedApiKeyPlaceholder, StringComparison.Ordinal);

    private void HotkeyRecorderBox_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _recordingHotkeyTarget = HotkeyRecordingTarget.Dictation;
        HotkeyRecorderBox.Text = "Press shortcut...";
    }

    private void AutoCaptureHotkeyRecorderBox_OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _recordingHotkeyTarget = HotkeyRecordingTarget.AutoCapture;
        AutoCaptureHotkeyRecorderBox.Text = "Press shortcut...";
    }

    private async void SaveHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        _recordingHotkeyTarget = HotkeyRecordingTarget.None;
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.SaveHotkeyAsync();
        }
    }

    private async void SaveAutoCaptureHotkeyButton_OnClick(object sender, RoutedEventArgs e)
    {
        _recordingHotkeyTarget = HotkeyRecordingTarget.None;
        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.SaveAutoCaptureHotkeyAsync();
        }
    }

    private void Window_OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_recordingHotkeyTarget is HotkeyRecordingTarget.None)
        {
            return;
        }

        e.Handled = true;
        var key = e.Key is Key.System ? e.SystemKey : e.Key;
        key = key is Key.ImeProcessed ? e.ImeProcessedKey : key;

        if (IsModifierKey(key))
        {
            SetRecordedHotkeyText(FormatModifiers(Keyboard.Modifiers));
            return;
        }

        var shortcut = FormatShortcut(Keyboard.Modifiers, key);
        SetRecordedHotkeyText(shortcut);
    }

    private void SetRecordedHotkeyText(string shortcut)
    {
        if (DataContext is MainViewModel viewModel)
        {
            if (_recordingHotkeyTarget is HotkeyRecordingTarget.AutoCapture)
            {
                viewModel.AutoCaptureHotkeyText = shortcut;
            }
            else
            {
                viewModel.HotkeyText = shortcut;
            }
        }

        if (_recordingHotkeyTarget is HotkeyRecordingTarget.AutoCapture)
        {
            AutoCaptureHotkeyRecorderBox.Text = shortcut;
        }
        else
        {
            HotkeyRecorderBox.Text = shortcut;
        }
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

    private void AutoSaveControl_OnChanged(object sender, RoutedEventArgs e)
    {
        QueueSettingsChange(saveImmediately: true);
    }

    private void AutoSaveComboBox_OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        QueueSettingsChange(saveImmediately: true);
    }

    private void AutoSaveSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        QueueSettingsChange(saveImmediately: false);
    }

    private void AutoSaveTextBox_OnLostFocus(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, HotkeyRecorderBox) ||
            ReferenceEquals(e.OriginalSource, AutoCaptureHotkeyRecorderBox) ||
            ReferenceEquals(e.OriginalSource, ApiKeyBox))
        {
            return;
        }

        QueueSettingsChange(saveImmediately: true);
    }

    private void QueueSettingsChange(bool saveImmediately)
    {
        if (!_isReadyForAutoSave || _recordingHotkeyTarget is not HotkeyRecordingTarget.None)
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(async () =>
        {
            if (DataContext is not MainViewModel viewModel || _recordingHotkeyTarget is not HotkeyRecordingTarget.None)
            {
                return;
            }

            viewModel.ApplyLiveSettings();
            if (saveImmediately)
            {
                _autoSaveTimer.Stop();
                await viewModel.AutoSaveSettingsAsync();
                return;
            }

            ScheduleAutoSave();
        }), DispatcherPriority.Background);
    }

    private void ScheduleAutoSave()
    {
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private async void AutoSaveTimer_OnTick(object? sender, EventArgs e)
    {
        _autoSaveTimer.Stop();
        if (_recordingHotkeyTarget is not HotkeyRecordingTarget.None)
        {
            return;
        }

        if (DataContext is MainViewModel viewModel)
        {
            await viewModel.AutoSaveSettingsAsync();
        }
    }

    private enum HotkeyRecordingTarget
    {
        None,
        Dictation,
        AutoCapture
    }
}
