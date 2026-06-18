using Wyspa.Core.Models;
using System.Windows;
using System.Windows.Threading;

namespace Wyspa.App.Services;

public sealed class OverlayStatusService
{
    private readonly Func<StatusOverlayWindow> _windowFactory;
    private readonly Dispatcher _dispatcher;
    public event EventHandler<string>? NotificationRequested;

    public OverlayStatusService(Func<StatusOverlayWindow> windowFactory)
    {
        _windowFactory = windowFactory;
        _dispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public void Show(string message, DictationState state)
    {
        RunOnUi(() =>
        {
            var window = _windowFactory();
            window.SetStatus(message, state);
            window.ShowTransient();

            if (state is DictationState.Error)
            {
                NotificationRequested?.Invoke(this, message);
            }
        });
    }

    public void UpdateLevel(float level)
    {
        RunOnUi(() =>
        {
            var window = _windowFactory();
            window.UpdateLevel(level);
        });
    }

    public void SetOpacity(double opacity)
    {
        RunOnUi(() =>
        {
            var window = _windowFactory();
            window.SetPanelOpacity(opacity);
        });
    }

    public void Hide()
    {
        RunOnUi(() =>
        {
            var window = _windowFactory();
            if (window.IsVisible)
            {
                window.Hide();
            }
        });
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
            return;
        }

        _dispatcher.BeginInvoke(action);
    }
}
