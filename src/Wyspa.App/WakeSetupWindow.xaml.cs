using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using Wyspa.App.Services;
using Wyspa.App.ViewModels;

namespace Wyspa.App;

public partial class WakeSetupWindow : Window
{
    public static readonly DependencyProperty IsStartedProperty = DependencyProperty.Register(nameof(IsStarted), typeof(bool), typeof(WakeSetupWindow), new PropertyMetadata(false));
    public bool IsStarted { get => (bool)GetValue(IsStartedProperty); private set => SetValue(IsStartedProperty, value); }
    private readonly WakeCalibrationViewModel _model;
    private bool _darkMode, _closing, _canClose;
    public WakeSetupWindow(WakeCalibrationViewModel model, bool darkMode)
    {
        InitializeComponent(); _model = model; DataContext = model; _darkMode = darkMode;
        IsStarted = model.SetupIndex > 0;
        Loaded += (_, _) => IsStarted = model.SetupIndex > 0;
        MaxWidth = SystemParameters.WorkArea.Width; MaxHeight = SystemParameters.WorkArea.Height;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }
    protected override void OnSourceInitialized(EventArgs e) { base.OnSourceInitialized(e); ApplyTheme(_darkMode); }
    public void ApplyTheme(bool darkMode)
    {
        _darkMode = darkMode;
#pragma warning disable WPF0001
        ThemeMode = System.Windows.Application.Current.ThemeMode;
#pragma warning restore WPF0001
        NativeWindowStyler.Apply(this, darkMode, transient: true);
    }
    private void Start_OnClick(object sender, RoutedEventArgs e) => IsStarted = true;
    private async void Restart_OnClick(object sender, RoutedEventArgs e)
    {
        await _model.RestartSetupAsync(); IsStarted = true;
    }
    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
    private async void Window_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_canClose) return;
        if (!_model.IsWorking) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        await _model.PauseSetupAsync();
        _canClose = true; Close();
    }
}
