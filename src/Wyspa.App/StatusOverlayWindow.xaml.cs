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
    private readonly Border[] _bars;
    private DictationState _currentState;
    private readonly Queue<float> _levels = new();

    public StatusOverlayWindow()
    {
        InitializeComponent();
        ShowActivated = false;
        _bars = [Bar1, Bar2, Bar3, Bar4, Bar5, Bar6, Bar7, Bar8, Bar9, Bar10, Bar11, Bar12];
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        _timer.Tick += (_, _) =>
        {
            _timer.Stop();
            if (IsVisible)
            {
                Hide();
            }
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeWindowStyler.Apply(this, darkMode: false, transient: true);
    }

    public void SetStatus(string message, DictationState state)
    {
        _currentState = state;
        ToggleStatusText.Visibility = Visibility.Collapsed;
        StatusText.Text = message;
        var color = state switch
        {
            DictationState.Listening => MediaColor.FromRgb(191, 63, 63),
            DictationState.Transcribing => MediaColor.FromRgb(47, 111, 115),
            DictationState.Inserted => MediaColor.FromRgb(56, 137, 89),
            DictationState.Error => MediaColor.FromRgb(191, 96, 42),
            _ => MediaColor.FromRgb(100, 112, 132)
        };
        foreach (var bar in _bars)
        {
            bar.Background = new SolidColorBrush(color);
        }

        if (state is not (DictationState.Listening or DictationState.Transcribing))
        {
            SetBarHeights(Enumerable.Repeat(5d, _bars.Length).ToArray());
        }
    }

    public void SetAutoCaptureToggleStatus(bool isListening)
    {
        _currentState = DictationState.Inserted;
        ToggleStatusText.Text = isListening ? "Listening on" : "Listening off";
        ToggleStatusText.Visibility = Visibility.Visible;
        StatusText.Text = "AutoCapture";
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
        if (_currentState is not DictationState.Listening)
        {
            _timer.Start();
        }
    }

    public void UpdateLevel(float level)
    {
        if (!IsVisible || _currentState is not (DictationState.Listening or DictationState.Transcribing))
        {
            return;
        }

        var normalized = Math.Clamp(level * 5.5f, 0.02f, 1f);
        _levels.Enqueue(normalized);
        while (_levels.Count > _bars.Length)
        {
            _levels.Dequeue();
        }

        var heights = new double[_bars.Length];
        var values = _levels.ToArray();
        var offset = _bars.Length - values.Length;
        for (var index = 0; index < _bars.Length; index++)
        {
            var value = index < offset ? 0.02f : values[index - offset];
            heights[index] = 4 + value * 24;
        }

        SetBarHeights(heights);
    }

    public void SetPanelOpacity(double opacity)
    {
        var normalized = Math.Clamp(opacity, 0.0, 1.0);
        var backgroundAlpha = (byte)Math.Round(normalized * 255);
        var borderAlpha = (byte)Math.Round(normalized * 92);
        Shell.Background = new SolidColorBrush(MediaColor.FromArgb(backgroundAlpha, 255, 255, 255));
        Shell.BorderBrush = new SolidColorBrush(MediaColor.FromArgb(borderAlpha, 255, 255, 255));
    }

    private void SetBarHeights(IReadOnlyList<double> heights)
    {
        for (var index = 0; index < _bars.Length && index < heights.Count; index++)
        {
            _bars[index].Height = heights[index];
        }
    }
}
