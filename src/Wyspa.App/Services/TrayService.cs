using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Wyspa.App.ViewModels;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;

namespace Wyspa.App.Services;

public sealed class TrayService : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly Func<Task> _quitAsync;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _toggleItem;
    private readonly ToolStripMenuItem _autoCaptureItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly Icon _baseIcon;
    private Icon? _statusIcon;
    private bool _updatingStartupItem;

    public TrayService(MainViewModel viewModel, IStartupService startupService, Action showMainWindow, Func<Task> quitAsync)
    {
        _viewModel = viewModel;
        _quitAsync = quitAsync;
        _toggleItem = new ToolStripMenuItem("Start Listening");
        _autoCaptureItem = new ToolStripMenuItem("AutoCapture listening") { CheckOnClick = false };
        _startupItem = new ToolStripMenuItem("Start with Windows") { CheckOnClick = true, Checked = startupService.IsEnabled() };
        _baseIcon = LoadAppIcon();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => showMainWindow());
        menu.Items.Add(_toggleItem);
        menu.Items.Add(_autoCaptureItem);
        menu.Items.Add("Settings", null, (_, _) => showMainWindow());
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, async (_, _) => await _quitAsync());

        _toggleItem.Click += async (_, _) => await _viewModel.ToggleListeningAsync();
        _autoCaptureItem.Click += async (_, _) => await _viewModel.ToggleAutoCaptureListeningAsync();
        _startupItem.CheckedChanged += async (_, _) =>
        {
            if (_updatingStartupItem)
            {
                return;
            }

            await _viewModel.SetStartWithWindowsAsync(_startupItem.Checked);
        };

        _notifyIcon = new NotifyIcon
        {
            Icon = _baseIcon,
            Text = "Wyspa",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => showMainWindow();
        _viewModel.PropertyChanged += ViewModelOnPropertyChanged;
        _viewModel.AutoCaptureListeningChanged += ViewModelOnAutoCaptureListeningChanged;
        _viewModel.StartupSettingChanged += ViewModelOnStartupSettingChanged;
        UpdateTrayState();
    }

    public void ShowNotification(string message)
    {
        _notifyIcon.BalloonTipTitle = "Wyspa";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.ShowBalloonTip(3000);
    }

    public void Dispose()
    {
        _viewModel.PropertyChanged -= ViewModelOnPropertyChanged;
        _viewModel.AutoCaptureListeningChanged -= ViewModelOnAutoCaptureListeningChanged;
        _viewModel.StartupSettingChanged -= ViewModelOnStartupSettingChanged;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _statusIcon?.Dispose();
        _baseIcon.Dispose();
    }

    private void ViewModelOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Status) or nameof(MainViewModel.Settings) or nameof(MainViewModel.StartWithWindows))
        {
            UpdateTrayState();
        }
    }

    private void ViewModelOnAutoCaptureListeningChanged(object? sender, EventArgs e)
    {
        UpdateTrayState();
    }

    private void ViewModelOnStartupSettingChanged(object? sender, EventArgs e)
    {
        UpdateTrayState();
    }

    private void UpdateTrayState()
    {
        var isRecording = _viewModel.Status is DictationState.Listening;
        var isAutoMode = _viewModel.IsAutoCaptureMode;
        var isAutoListening = _viewModel.IsAutoCaptureListening;

        _toggleItem.Text = isRecording ? "Stop Listening" : "Start Listening";
        _toggleItem.Enabled = !isAutoMode || isRecording;

        _autoCaptureItem.Visible = isAutoMode;
        _autoCaptureItem.Checked = isAutoListening;
        _autoCaptureItem.Text = isAutoListening ? "AutoCapture listening: On" : "AutoCapture listening: Off";

        _updatingStartupItem = true;
        _startupItem.Checked = _viewModel.StartWithWindows;
        _updatingStartupItem = false;

        var text = isRecording
            ? "Wyspa - Recording"
            : isAutoListening
                ? "Wyspa - AutoCapture listening"
                : "Wyspa";
        _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
        SetStatusIcon(isRecording, isAutoListening);
    }

    private void SetStatusIcon(bool recording, bool autoListening)
    {
        var shouldBadge = recording || autoListening;
        _statusIcon?.Dispose();
        _statusIcon = shouldBadge ? CreateBadgedIcon(_baseIcon, recording ? Color.FromArgb(216, 74, 74) : Color.FromArgb(42, 173, 118)) : null;
        _notifyIcon.Icon = _statusIcon ?? _baseIcon;
    }

    private static Icon LoadAppIcon()
    {
        try
        {
            var processPath = Environment.ProcessPath;
            return !string.IsNullOrWhiteSpace(processPath)
                ? Icon.ExtractAssociatedIcon(processPath) ?? SystemIcons.Application
                : SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    private static Icon CreateBadgedIcon(Icon source, Color color)
    {
        using var bitmap = source.ToBitmap();
        using var canvas = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(canvas))
        {
            graphics.Clear(Color.Transparent);
            graphics.DrawImage(bitmap, 0, 0, 32, 32);
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var outline = new SolidBrush(Color.White);
            using var fill = new SolidBrush(color);
            graphics.FillEllipse(outline, 19, 19, 12, 12);
            graphics.FillEllipse(fill, 21, 21, 8, 8);
        }

        var handle = canvas.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr handle);
}
