using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using ToolTip = System.Windows.Controls.ToolTip;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;

namespace Wyspa.App;

// The same help is available by pointer hover, click and keyboard invocation.
public sealed class HelpButton : Button
{
    public static readonly DependencyProperty HelpTextProperty = DependencyProperty.Register(
        nameof(HelpText), typeof(string), typeof(HelpButton), new PropertyMetadata(null, HelpChanged));
    private readonly DispatcherTimer _dismiss = new() { Interval = TimeSpan.FromSeconds(20) };
    private Window? _owner;
    private ToolTip? _tip;

    public string? HelpText { get => (string?)GetValue(HelpTextProperty); set => SetValue(HelpTextProperty, value); }

    public HelpButton()
    {
        _dismiss.Tick += (_, _) => CloseHelp();
        Unloaded += (_, _) => CloseHelp();
    }

    private static void HelpChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var button = (HelpButton)sender;
        button.CloseHelp();
        var text = (e.NewValue as string)?.Trim();
        button.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        if (string.IsNullOrEmpty(text)) { button.ToolTip = button._tip = null; return; }
        AutomationProperties.SetName(button, "Help: " + text.Split('\n')[0]);
        AutomationProperties.SetHelpText(button, text);
        button._tip = new ToolTip { Content = text, PlacementTarget = button, Placement = PlacementMode.Bottom };
        button._tip.Opened += (_, _) => button.ObserveDismissal();
        button._tip.Closed += (_, _) => button.StopObserving();
        button.ToolTip = button._tip;
    }

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        if (string.IsNullOrWhiteSpace(HelpText)) Visibility = Visibility.Collapsed;
    }

    protected override void OnClick()
    {
        base.OnClick();
        if (_tip is not null) _tip.IsOpen = !_tip.IsOpen;
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        CloseHelp();
    }

    private void ObserveDismissal()
    {
        StopObserving();
        _owner = Window.GetWindow(this);
        if (_owner is not null)
        {
            _owner.PreviewKeyDown += OwnerKeyDown;
            _owner.PreviewMouseDown += OwnerMouseDown;
            _owner.Deactivated += OwnerDeactivated;
        }
        _dismiss.Start();
    }

    private void OwnerKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        CloseHelp(); e.Handled = true;
    }
    private void OwnerMouseDown(object sender, MouseButtonEventArgs e) { if (!IsMouseOver) CloseHelp(); }
    private void OwnerDeactivated(object? sender, EventArgs e) => CloseHelp();
    private void CloseHelp() { if (_tip is not null) _tip.IsOpen = false; StopObserving(); }
    private void StopObserving()
    {
        _dismiss.Stop();
        if (_owner is null) return;
        _owner.PreviewKeyDown -= OwnerKeyDown;
        _owner.PreviewMouseDown -= OwnerMouseDown;
        _owner.Deactivated -= OwnerDeactivated;
        _owner = null;
    }
}
