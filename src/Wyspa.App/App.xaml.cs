using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using Wyspa.App.Services;
using Wyspa.App.ViewModels;
using Wyspa.Core.Services;
using Wyspa.Infrastructure.Audio;
using Wyspa.Infrastructure.Hotkeys;
using Wyspa.Infrastructure.Insertion;
using Wyspa.Infrastructure.Media;
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
    private NativeHotkeyService? _autoCaptureHotkeyService;
    private ThemeService? _themeService;
    private NaudioLevelMonitorService? _levelMonitor;
    private AutoCaptureService? _autoCaptureService;
    private WindowsAutoCaptureMediaControlService? _mediaControlService;
    private HttpClient? _httpClient;
    private HttpClient? _fileHttpClient;
    private HttpClient? _notesHttpClient;
    private NoteOverlayWindow? _notesOverlay;
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
            _autoCaptureHotkeyService = new NativeHotkeyService();
            _levelMonitor = new NaudioLevelMonitorService();
            _themeService = new ThemeService(Resources);

            var settingsService = new JsonSettingsService();
            var secretStore = new DpapiSecretStore();
            var groqClient = new GroqTranscriptionClient(_httpClient);
            var updateService = new GitHubUpdateService(_httpClient);
            var textCleanup = new TextCleanupService();
            var insertionService = new WindowsTextInsertionService();
            var keyboardCommandService = new WindowsKeyboardCommandService();
            var startupService = new WindowsStartupService(Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "Wyspa.exe");
            var overlayService = new OverlayStatusService(() => _overlay ??= new StatusOverlayWindow());
            var wakeToneService = new WakeToneService();
            var autoCaptureToggleFeedbackService = new AutoCaptureToggleFeedbackService(overlayService);
            _mediaControlService = new WindowsAutoCaptureMediaControlService();

            var orchestrator = new DictationOrchestrator(
                settingsService,
                secretStore,
                _audioCapture,
                groqClient,
                textCleanup,
                insertionService,
                keyboardCommandService,
                overlayService);

            _viewModel = new MainViewModel(settingsService, secretStore, groqClient, _audioCapture, _levelMonitor, _hotkeyService, _autoCaptureHotkeyService, startupService, orchestrator, updateService, _mediaControlService);
            _fileHttpClient = new HttpClient();
            _viewModel.FileTranscription = new FileTranscriptionViewModel(
                new AudioFilePreparationService(System.IO.Path.Combine(AppContext.BaseDirectory, "Tools", "Flac", "flac.exe")),
                new GroqTranscriptionClient(_fileHttpClient, TimeSpan.FromMinutes(10)),
                secretStore, () => _viewModel.Settings);
            _autoCaptureService = new AutoCaptureService(settingsService, secretStore, _levelMonitor, _audioCapture, orchestrator, overlayService, wakeToneService);
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
            _autoCaptureHotkeyService.Pressed += async (_, _) => await _viewModel.HandleAutoCaptureHotkeyPressedAsync();
            _viewModel.SettingsChanged += (_, _) => _ = ApplyLiveSettingsAsync(overlayService);
            _viewModel.AutoCaptureToggleFeedbackRequested += (_, isListening) =>
                autoCaptureToggleFeedbackService.Show(isListening, _viewModel.Settings.OverlayOpacity);

            await _viewModel.InitializeAsync();
            await _autoCaptureService.RefreshAsync();
            _notesHttpClient = new HttpClient();
            _viewModel.Notes = new NotesViewModel(
                new ConversationCapture(() => _viewModel.Settings),
                new LocalSpeakerIdentifier(System.IO.Path.Combine(AppContext.BaseDirectory, "Tools", "Speakers"), () => _viewModel.Settings.SpeakerMatchThreshold),
                new GroqNoteIntelligence(_notesHttpClient), secretStore, new NoteStore(),
                new VideoImporter(System.IO.Path.Combine(AppContext.BaseDirectory, "Tools", "Video")),
                () => _viewModel.Settings,
                action => Dispatcher.InvokeAsync(action).Task.Unwrap(),
                async reserved =>
                {
                    if (reserved && _viewModel.IsWakeVoiceRecording)
                        throw new InvalidOperationException("Finish wake-voice training before starting notes.");
                    _autoCaptureService.Suspended = reserved;
                    try
                    {
                        await orchestrator.ReserveForNotesAsync(reserved);
                        await _viewModel.SetNoteCaptureActiveAsync(reserved);
                        await _autoCaptureService.ApplySettingsAsync(_viewModel.Settings, _viewModel.HasApiKey);
                    }
                    catch
                    {
                        _autoCaptureService.Suspended = false;
                        _viewModel.NoteCaptureActive = false;
                        await orchestrator.ReserveForNotesAsync(false);
                        throw;
                    }
                }, () => _viewModel.AutoSaveSettingsAsync());
            await _viewModel.Notes.RefreshAsync();
            _viewModel.Videos = new NotesViewModel(
                new ConversationCapture(() => _viewModel.Settings),
                new LocalSpeakerIdentifier(System.IO.Path.Combine(AppContext.BaseDirectory, "Tools", "Speakers"), () => _viewModel.Settings.SpeakerMatchThreshold),
                new GroqNoteIntelligence(_notesHttpClient), secretStore, new NoteStore(),
                new VideoImporter(System.IO.Path.Combine(AppContext.BaseDirectory, "Tools", "Video")),
                () => _viewModel.Settings, action => Dispatcher.InvokeAsync(action).Task.Unwrap(),
                _ => Task.CompletedTask, () => _viewModel.AutoSaveSettingsAsync(), NoteLibrary.YouTube);
            await _viewModel.Videos.RefreshAsync();

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
        if (_isQuitting) return;
        _isQuitting = true;
        if (_viewModel is not null)
        {
            await _viewModel.Notes.ShutdownAsync();
            await _viewModel.Videos.ShutdownAsync();
            await _viewModel.StopIfNeededAsync();
        }

        Shutdown();
    }

    public void ShowNoteOverlay()
    {
        if (_viewModel is null) return;
        if (_notesOverlay is null)
        {
            _notesOverlay = new NoteOverlayWindow { DataContext = _viewModel.Notes };
            _notesOverlay.Closed += (_, _) => _notesOverlay = null;
        }
        _notesOverlay.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _trayService?.Dispose();
        _viewModel?.FileTranscription.Cancel();
        _fileHttpClient?.Dispose();
        _notesHttpClient?.Dispose();
        _hotkeyService?.Dispose();
        _autoCaptureHotkeyService?.Dispose();
        if (_mediaControlService is not null)
        {
            try
            {
                await _mediaControlService.RestoreAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                CrashLogService.Log(ex);
            }
        }

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
