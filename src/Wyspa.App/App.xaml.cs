using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using Wyspa.App.Services;
using Wyspa.App.ViewModels;
using Wyspa.Core.Services;
using Wyspa.Infrastructure.Audio;
using Wyspa.Infrastructure.Hotkeys;
using Wyspa.Infrastructure.Insertion;
using Wyspa.Infrastructure.Settings;
using Wyspa.Infrastructure.Startup;

namespace Wyspa.App;

public partial class App : System.Windows.Application
{
    private AppLifecycleService? _lifecycle;
    private MainWindow? _mainWindow;
    private StatusOverlayWindow? _overlay;
    private TrayService? _trayService;
    private MainViewModel? _viewModel;
    private NaudioCaptureService? _audioCapture;
    private NativeHotkeyService? _hotkeyService;
    private ThemeService? _themeService;
    private NaudioLevelMonitorService? _levelMonitor;
    private AutoCaptureService? _autoCaptureService;
    private HttpClient? _httpClient;
    private bool _isQuitting;

    public App()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLogService.Log(args.Exception);
            args.Handled = true;
            System.Windows.MessageBox.Show(
                "Wyspa hit a startup error. Details were written to %AppData%\\Wyspa\\crash.log.",
                "Wyspa",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                CrashLogService.Log(exception);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            CrashLogService.Log(args.Exception);
            args.SetObserved();
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            base.OnStartup(e);

            var quitExisting = e.Args.Any(arg => string.Equals(arg, "--quit-existing", StringComparison.OrdinalIgnoreCase));

            _lifecycle = new AppLifecycleService("Wyspa.SingleInstance", "Wyspa.ShowSettings", "Wyspa.Quit");
            if (!_lifecycle.TryStart(
                () => Dispatcher.Invoke(ShowMainWindow),
                () => Dispatcher.BeginInvoke(new Action(async () => await QuitAsync()))))
            {
                if (quitExisting)
                {
                    _lifecycle.SignalExistingQuit();
                }
                else
                {
                    _lifecycle.SignalExistingShow();
                }

                Shutdown();
                return;
            }

            if (quitExisting)
            {
                Shutdown();
                return;
            }

            _httpClient = new HttpClient();
            _audioCapture = new NaudioCaptureService();
            _hotkeyService = new NativeHotkeyService();
            _levelMonitor = new NaudioLevelMonitorService();
            _themeService = new ThemeService(Resources);

            var settingsService = new JsonSettingsService();
            var secretStore = new DpapiSecretStore();
            var groqClient = new GroqTranscriptionClient(_httpClient);
            var textCleanup = new TextCleanupService();
            var insertionService = new WindowsTextInsertionService();
            var keyboardCommandService = new WindowsKeyboardCommandService();
            var startupService = new WindowsStartupService(Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "Wyspa.exe");
            var overlayService = new OverlayStatusService(() => _overlay ??= new StatusOverlayWindow());

            var orchestrator = new DictationOrchestrator(
                settingsService,
                secretStore,
                _audioCapture,
                groqClient,
                textCleanup,
                insertionService,
                keyboardCommandService,
                overlayService);

            _viewModel = new MainViewModel(settingsService, secretStore, groqClient, _audioCapture, _hotkeyService, startupService, orchestrator);
            _autoCaptureService = new AutoCaptureService(settingsService, secretStore, _levelMonitor, _audioCapture, orchestrator, overlayService);
            _trayService = new TrayService(_viewModel, startupService, ShowMainWindow, QuitAsync);
            overlayService.NotificationRequested += (_, message) => _trayService?.ShowNotification(message);
            _audioCapture.LevelAvailable += (_, level) => Dispatcher.BeginInvoke(() =>
            {
                _viewModel?.UpdateMicrophoneLevel(level);
                overlayService.UpdateLevel(level);
            });
            _levelMonitor.LevelAvailable += (_, level) => Dispatcher.BeginInvoke(() =>
            {
                _viewModel?.UpdateMicrophoneLevel(level);
                overlayService.UpdateLevel(level);
            });
            _hotkeyService.Pressed += async (_, _) => await _viewModel.HandleHotkeyPressedAsync();
            _hotkeyService.Released += async (_, _) => await _viewModel.HandleHotkeyReleasedAsync();
            _viewModel.SettingsChanged += (_, _) => _ = ApplyLiveSettingsAsync(overlayService);

            await _viewModel.InitializeAsync();
            await _autoCaptureService.RefreshAsync();

            var launchMinimized = e.Args.Any(arg => string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase)) ||
                _viewModel.Settings.StartMinimized;
            if (!launchMinimized)
            {
                ShowMainWindow();
            }
        }
        catch (Exception ex)
        {
            CrashLogService.Log(ex);
            System.Windows.MessageBox.Show(
                "Wyspa could not open. Details were written to %AppData%\\Wyspa\\crash.log.",
                "Wyspa",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
        }
    }

    private void ShowMainWindow()
    {
        if (_viewModel is null)
        {
            return;
        }

        if (_mainWindow is null)
        {
            _mainWindow = new MainWindow
            {
                DataContext = _viewModel,
                IsDarkMode = _themeService?.IsDarkMode ?? false
            };
            if (_themeService is not null)
            {
                _themeService.ThemeChanged += (_, dark) => _mainWindow.ApplyTheme(dark);
            }
            _mainWindow.Closing += (_, args) =>
            {
                if (!_isQuitting)
                {
                    args.Cancel = true;
                    _mainWindow.Hide();
                }
            };
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private async Task ApplyLiveSettingsAsync(OverlayStatusService overlayService)
    {
        if (_viewModel is null || _autoCaptureService is null)
        {
            return;
        }

        try
        {
            overlayService.SetOpacity(_viewModel.Settings.OverlayOpacity);
            await _autoCaptureService.ApplySettingsAsync(_viewModel.Settings, _viewModel.HasApiKey);
        }
        catch (Exception ex)
        {
            CrashLogService.Log(ex);
        }
    }

    private async Task QuitAsync()
    {
        _isQuitting = true;
        if (_viewModel is not null)
        {
            await _viewModel.StopIfNeededAsync();
        }

        Shutdown();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _trayService?.Dispose();
        _hotkeyService?.Dispose();
        if (_audioCapture is not null)
        {
            await _audioCapture.DisposeAsync();
        }
        _httpClient?.Dispose();
        _autoCaptureService?.Dispose();
        _themeService?.Dispose();
        _lifecycle?.Dispose();
        base.OnExit(e);
    }
}
