using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Wyspa.App.Services;
using Wyspa.Core.Models;
using MediaColor = System.Windows.Media.Color;

namespace Wyspa.App;

public partial class StatusOverlayWindow : Window
{
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _processingTimer;
    private double _processingPhase;
    private readonly Border[] _bars;
    private DictationState _currentState;
    private bool _captureActive;
    private float _speechThreshold = .012f;
    private float _level;
    private long _lastSpeechAt = long.MinValue / 2;
    private double _panelOpacity = .82;
    private bool _isDarkMode;

    public StatusOverlayWindow()
    {
        InitializeComponent();
        ShowActivated = false;
        _bars = [Bar1, Bar2, Bar3, Bar4, Bar5, Bar6, Bar7, Bar8, Bar9, Bar10, Bar11, Bar12];
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            if (IsVisible && _currentState is not (DictationState.Listening or DictationState.Transcribing))
            {
                Hide();
            }
        };
        _processingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _processingTimer.Tick += (_, _) =>
        {
            AnimateActivity();
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && _currentState is DictationState.Listening or DictationState.Transcribing) _processingTimer.Start();
            else _processingTimer.Stop();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeWindowStyler.Apply(this, _isDarkMode, transient: true);
    }

    public void ApplyTheme(bool darkMode)
    {
        _isDarkMode = darkMode;
        SetPanelOpacity(_panelOpacity);
        NativeWindowStyler.Apply(this, darkMode, transient: true);
    }

    public void SetStatus(string message, DictationState state)
    {
        _currentState = state;
        _timer.Stop();
        if (state is DictationState.Listening or DictationState.Transcribing && IsVisible) _processingTimer.Start();
        else _processingTimer.Stop();
        ToggleStatusText.Visibility = Visibility.Collapsed;
        StatusText.Text = message;
        var color = state switch
        {
            DictationState.Listening => MediaColor.FromRgb(191, 63, 63),
            DictationState.Transcribing => MediaColor.FromRgb(56, 137, 89),
            DictationState.Inserted => MediaColor.FromRgb(56, 137, 89),
            DictationState.Error => MediaColor.FromRgb(191, 96, 42),
            _ => MediaColor.FromRgb(100, 112, 132)
        };
        foreach (var bar in _bars)
        {
            bar.Background = new SolidColorBrush(color);
        }

        if (state is DictationState.Listening or DictationState.Transcribing) AnimateActivity();
        if (state is not (DictationState.Listening or DictationState.Transcribing))
        {
            SetBarHeights(Enumerable.Repeat(0d, _bars.Length).ToArray());
        }
    }

    public void SetAutoCaptureToggleStatus(bool isListening)
    {
        if (IsVisible && _currentState is DictationState.Listening or DictationState.Transcribing) return;
        _currentState = DictationState.Inserted;
        ToggleStatusText.Text = isListening ? "Listening on" : "Listening off";
        ToggleStatusText.Visibility = Visibility.Visible;
        StatusText.Text = "SmartListen";
        var color = isListening
            ? MediaColor.FromRgb(56, 137, 89)
            : MediaColor.FromRgb(100, 112, 132);
        ToggleStatusText.Foreground = new SolidColorBrush(color);
        foreach (var bar in _bars)
        {
            bar.Height = 0;
        }
    }

    public void ShowTransient()
    {
        Left = (SystemParameters.WorkArea.Width - Width) / 2 + SystemParameters.WorkArea.Left;
        Top = SystemParameters.WorkArea.Bottom - Height - 52;
        Show();
        _timer.Stop();
        if (_currentState is not (DictationState.Listening or DictationState.Transcribing))
        {
            _timer.Start();
        }
    }

    public void SetCaptureActive(bool active, float speechThreshold = .012f)
    {
        _captureActive = active;
        _speechThreshold = speechThreshold;
        _level = 0;
        _lastSpeechAt = long.MinValue / 2;
        if (IsVisible) AnimateActivity();
    }

    public void UpdateLevel(float level)
    {
        if (!_captureActive) return;
        _level = Math.Clamp(level, 0, 1);
        if (_level >= _speechThreshold) _lastSpeechAt = Environment.TickCount64;
    }

    private void AnimateActivity()
    {
        if (_currentState is not (DictationState.Listening or DictationState.Transcribing)) return;
        var speaking = _captureActive && Environment.TickCount64 - _lastSpeechAt < 250;
        var color = speaking ? MediaColor.FromRgb(191, 63, 63) : MediaColor.FromRgb(56, 137, 89);
        foreach (var bar in _bars)
            if (bar.Background is not SolidColorBrush brush || brush.Color != color) bar.Background = new SolidColorBrush(color);
        _processingPhase += .3;
        var amplitude = speaking ? 6 + Math.Clamp(_level * 5.5, 0, 1) * 16 : 14;
        SetBarHeights(Enumerable.Range(0, _bars.Length).Select(i => 5 + amplitude * (.5 + .5 * Math.Sin(_processingPhase - i * .55))).ToArray());
    }

    public void SetPanelOpacity(double opacity)
    {
        var normalized = Math.Clamp(opacity, 0.0, 1.0);
        _panelOpacity = normalized;
        var backgroundAlpha = (byte)Math.Round(normalized * 255);
        var borderAlpha = (byte)Math.Round(normalized * 92);
        var background = ((SolidColorBrush)FindResource("PanelBrush")).Color;
        var border = ((SolidColorBrush)FindResource("LineBrush")).Color;
        Shell.Background = new SolidColorBrush(MediaColor.FromArgb(backgroundAlpha, background.R, background.G, background.B));
        Shell.BorderBrush = new SolidColorBrush(MediaColor.FromArgb(borderAlpha, border.R, border.G, border.B));
    }

    private void SetBarHeights(IReadOnlyList<double> heights)
    {
        for (var index = 0; index < _bars.Length && index < heights.Count; index++)
        {
            _bars[index].Height = heights[index];
        }
    }
}
