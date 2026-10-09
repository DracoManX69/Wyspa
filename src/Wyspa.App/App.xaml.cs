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
    private ThemeService? _themeService;
    private NaudioLevelMonitorService? _levelMonitor;
    private AutoCaptureService? _autoCaptureService;
    private WakeKeywordEngine? _wakeKeywordEngine;
    private LocalTranscriptionClient? _localClient;
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
            _levelMonitor = new NaudioLevelMonitorService();
            _themeService = new ThemeService(Resources);
            _themeService.ThemeChanged += (_, dark) =>
            {
                _mainWindow?.ApplyTheme(dark);
                _overlay?.ApplyTheme(dark);
                if (_notesOverlay is not null) NativeWindowStyler.Apply(_notesOverlay, dark);
            };

            var settingsService = new JsonSettingsService();
            var secretStore = new DpapiSecretStore();
            var localModels = new LocalModelStore(new HttpClient { Timeout = TimeSpan.FromMinutes(30) });
            var localClient = new LocalTranscriptionClient(localModels, System.IO.Path.Combine(AppContext.BaseDirectory, "Tools", "Video", "ffmpeg.exe"), () => _viewModel?.Settings.LocalVoiceProfile, () => _viewModel?.Settings.LocalGpuEnabled ?? true, () => _viewModel?.Settings.SpeechPerformance);
            _localClient = localClient;
            var groqClient = new TranscriptionRouter(new GroqTranscriptionClient(_httpClient), localClient, () => _viewModel?.Settings.UseLocalTranscription == true);
            var updateService = new GitHubUpdateService(_httpClient);
            var textCleanup = new TextCleanupService();
            var insertionService = new WindowsTextInsertionService();
            var keyboardCommandService = new WindowsKeyboardCommandService();
            var startupService = new WindowsStartupService(Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? "Wyspa.exe");
            var overlayService = new OverlayStatusService(() =>
            {
                if (_overlay is null)
                {
                    _overlay = new StatusOverlayWindow();
                    _overlay.ApplyTheme(_themeService.IsDarkMode);
                }
                return _overlay;
            });
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

            _viewModel = new MainViewModel(settingsService, secretStore, groqClient, _audioCapture, _levelMonitor, _hotkeyService, startupService, orchestrator, updateService, _mediaControlService);
            _viewModel.LocalModels = new LocalModelsViewModel(localModels, localClient, () => _viewModel.Settings, () => _viewModel.AutoSaveSettingsAsync());
            _fileHttpClient = new HttpClient();
            _viewModel.FileTranscription = new FileTranscriptionViewModel(
                new AudioFilePreparationService(System.IO.Path.Combine(AppContext.BaseDirectory, "Tools", "Flac", "flac.exe")),
                new TranscriptionRouter(new GroqTranscriptionClient(_fileHttpClient, TimeSpan.FromMinutes(10)), localClient, () => _viewModel.Settings.UseLocalTranscription),
                secretStore, () => _viewModel.Settings);
            _wakeKeywordEngine = new WakeKeywordEngine();
            _autoCaptureService = new AutoCaptureService(settingsService, secretStore, _levelMonitor, _audioCapture, orchestrator, overlayService, wakeToneService, _wakeKeywordEngine);
            _viewModel.InputLevelPreviewChanged += async (_, visible) =>
            {
                try
                {
                    await _autoCaptureService.SetLevelPreviewAsync(visible);
                    _viewModel.InputLevelPreviewError = string.Empty;
                }
                catch (Exception ex)
                {
                    _viewModel.InputLevelPreviewError = "Input level unavailable. Check the selected input device and Windows microphone access.";
                    CrashLogService.Log(ex);
                }
            };
            _trayService = new TrayService(_viewModel, startupService, ShowMainWindow, QuitAsync);
            overlayService.NotificationRequested += (_, message) => _trayService?.ShowNotification(message);
            _audioCapture.LevelAvailable += (_, level) => Dispatcher.BeginInvoke(() =>
            {
                _viewModel?.UpdateMicrophoneLevel(level);
                overlayService.UpdateLevel(level);
            });
            _levelMonitor.LevelAvailable += (_, level) => Dispatcher.BeginInvoke(() =>
            {
                overlayService.UpdateLevel(level);
            });
            _hotkeyService.Pressed += async (_, _) => { if (!_isQuitting) await _viewModel.HandleHotkeyPressedAsync(); };
            _hotkeyService.Released += async (_, _) => { if (!_isQuitting) await _viewModel.HandleHotkeyReleasedAsync(); };
            _viewModel.SettingsChanged += (_, _) => _ = ApplyLiveSettingsAsync(overlayService);
            _viewModel.AutoCaptureToggleFeedbackRequested += (_, isListening) =>
                autoCaptureToggleFeedbackService.Show(isListening, _viewModel.Settings.OverlayOpacity);

            Task ReserveLocalPreparationAsync(bool reserved) => ReservePreparationCoreAsync(reserved, false);
            async Task ReservePreparationCoreAsync(bool reserved, bool wakeOwner)
                {
                    if (reserved)
                    {
                        if (_viewModel.NoteCaptureActive || (!wakeOwner && _viewModel.IsWakeVoiceRecording) || _viewModel.IsScratchpadRecording)
                            throw new InvalidOperationException("Finish the current recording or conversation before voice setup or model preparation.");
                        await orchestrator.ReserveForNotesAsync(true);
                        try
                        {
                            _autoCaptureService.Suspended = true;
                            await _viewModel.SetNoteCaptureActiveAsync(true);
                            await _autoCaptureService.ApplySettingsAsync(_viewModel.Settings, false);
                        }
                        catch
                        {
                            _autoCaptureService.Suspended = false;
                            _viewModel.NoteCaptureActive = false;
                            await orchestrator.ReserveForNotesAsync(false);
                            throw;
                        }
                    }
                    else
                    {
                        await orchestrator.ReserveForNotesAsync(false);
                        await _viewModel.SetNoteCaptureActiveAsync(false);
                        _autoCaptureService.Suspended = _isQuitting;
                    }
                    if (!reserved) await _autoCaptureService.ApplySettingsAsync(_viewModel.Settings, _viewModel.CanTranscribe);
                }
            _viewModel.LocalModels.ReserveRemovalAsync = ReserveLocalPreparationAsync;
            _viewModel.WakeCalibration = new WakeCalibrationViewModel(_levelMonitor, _wakeKeywordEngine!, () => _viewModel.Settings,
                reserved => ReservePreparationCoreAsync(reserved, true), () => _viewModel.AutoSaveSettingsAsync(), () => _viewModel.CanTranscribe);
            _viewModel.WakeCalibration.PropertyChanged += (_, args) =>
            { if (args.PropertyName is nameof(WakeCalibrationViewModel.IsBusy) or nameof(WakeCalibrationViewModel.IsRecording) or nameof(WakeCalibrationViewModel.IsWizardOpen)) _viewModel.SetWakeCalibrationState(_viewModel.WakeCalibration.IsWorking || _viewModel.WakeCalibration.IsWizardOpen); };
            _autoCaptureService.WakeStatusChanged += (_, message) => _viewModel.WakeCalibration.ReportRuntime(message);
            _viewModel.LocalModels.VoiceSetup = new VoiceSetupViewModel(_audioCapture, () => _viewModel.Settings,
                localClient.TranscribeAsync, localModels.IsInstalled, ReserveLocalPreparationAsync, () => _viewModel.AutoSaveSettingsAsync());
            var comparison = new SpeechPerformanceBenchmark(localClient.PrepareAsync, localClient.TranscribeAsync,
                async (path, options, token) =>
                {
                    var key = await secretStore.GetApiKeyAsync(token);
                    if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("Save a Groq key before comparing providers.");
                    return await groqClient.TranscribeAsync(key, path, options, token);
                }, () => localClient.RuntimeStatus, localClient.PrepareBackendAsync, localClient.SupportsGpu, () => localClient.ActualDevice, () => localClient.WorkerCpuSeconds, () => localClient.WorkerRamMb);
            _viewModel.LocalModels.Recommendations = new LocalRecommendationsViewModel(() => _viewModel.Settings, () => localClient.Hardware,
                localModels.IsInstalled, () => _viewModel.LocalModels.CanConfigure && !_viewModel.NoteCaptureActive && !_viewModel.IsWakeVoiceRecording && !_viewModel.IsScratchpadRecording,
                () => _viewModel.HasApiKey, ReserveLocalPreparationAsync, () => _viewModel.AutoSaveSettingsAsync(),
                _viewModel.LocalModels.ApplyRecommendationAsync, comparison, System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "BenchmarkSample.wav"));
            _viewModel.LocalModels.Recommendations.PropertyChanged += (_, args) =>
            { if (args.PropertyName == nameof(LocalRecommendationsViewModel.IsBusy)) _viewModel.LocalModels.Refresh(); };
            _viewModel.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(MainViewModel.NoteCaptureActive) or nameof(MainViewModel.IsWakeVoiceRecording) or nameof(MainViewModel.IsScratchpadRecording) or nameof(MainViewModel.HasApiKey))
                    _viewModel.LocalModels.Recommendations.Refresh();
            };
            _viewModel.LocalModels.VoiceSetup.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName is nameof(VoiceSetupViewModel.IsBusy) or nameof(VoiceSetupViewModel.IsRecording)) _viewModel.LocalModels.Refresh();
            };
            await _viewModel.InitializeAsync();
            await _viewModel.WakeCalibration.InitializeAsync();
            _viewModel.LocalModels.VoiceSetup.Load();
            _viewModel.LocalModels.Recommendations.Load();
            _ = _viewModel.LocalModels.InitializeAsync(System.IO.Path.Combine(AppContext.BaseDirectory, "Tools", "LocalDefault"));
            _themeService.ApplyPreference(_viewModel.Settings.Theme);
            await _autoCaptureService.RefreshAsync();
            _notesHttpClient = new HttpClient();
            _viewModel.Notes = new NotesViewModel(
                new ConversationCapture(() => _viewModel.Settings),
                new LocalSpeakerIdentifier(System.IO.Path.Combine(AppContext.BaseDirectory, "Tools", "Speakers"), () => _viewModel.Settings.SpeakerMatchThreshold),
                new NoteTranscriptionRouter(new GroqNoteIntelligence(_notesHttpClient), localClient, () => _viewModel.Settings.UseLocalTranscription), secretStore, new NoteStore(),
                new VideoImporter(System.IO.Path.Combine(AppContext.BaseDirectory, "Tools", "Video")),
                () => _viewModel.Settings,
                action => Dispatcher.InvokeAsync(action).Task.Unwrap(),
                async reserved =>
                {
                    if (reserved && (_viewModel.NoteCaptureActive || _viewModel.IsWakeVoiceRecording || _viewModel.LocalModels?.VoiceSetup?.CanEdit == false || _viewModel.LocalModels?.Recommendations?.IsBusy == true))
                        throw new InvalidOperationException("Finish model testing, voice setup or wake-voice training before starting notes.");
                    _autoCaptureService.Suspended = reserved;
                    try
                    {
                        await orchestrator.ReserveForNotesAsync(reserved);
                        await _viewModel.SetNoteCaptureActiveAsync(reserved);
                        await _autoCaptureService.ApplySettingsAsync(_viewModel.Settings, _viewModel.CanTranscribe);
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
                new NoteTranscriptionRouter(new GroqNoteIntelligence(_notesHttpClient), localClient, () => _viewModel.Settings.UseLocalTranscription), secretStore, new NoteStore(),
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
            _themeService?.ApplyPreference(_viewModel.Settings.Theme);
            overlayService.SetOpacity(_viewModel.Settings.OverlayOpacity);
            await _autoCaptureService.ApplySettingsAsync(_viewModel.Settings, _viewModel.CanTranscribe);
            _viewModel.InputLevelPreviewError = string.Empty;
        }
        catch (Exception ex)
        {
            if (_autoCaptureService.LevelPreviewEnabled)
                _viewModel.InputLevelPreviewError = "Input level unavailable. Check the selected input device and Windows microphone access.";
            CrashLogService.Log(ex);
        }
    }

    private async Task QuitAsync()
    {
        if (_isQuitting) return;
        _isQuitting = true;
        if (_autoCaptureService is not null) _autoCaptureService.Suspended = true;
        if (_viewModel is not null)
        {
            if (_viewModel.WakeCalibration is { } calibration) await calibration.ShutdownAsync();
            if (_viewModel.LocalModels?.Recommendations is { } recommendation) await recommendation.ShutdownAsync();
            if (_viewModel.LocalModels?.VoiceSetup is { } setup) await setup.ShutdownAsync();
            if (_viewModel.LocalModels is { } models) await models.ShutdownAsync();
            await _viewModel.Notes.ShutdownAsync();
            await _viewModel.Videos.ShutdownAsync();
            await _viewModel.StopIfNeededAsync();
        }

        if (_autoCaptureService is not null) await _autoCaptureService.StopWakeWorkerAsync();
        _wakeKeywordEngine?.Dispose(); _wakeKeywordEngine = null;
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
        NativeWindowStyler.Apply(_notesOverlay, _themeService?.IsDarkMode ?? false);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        _trayService?.Dispose();
        _viewModel?.FileTranscription.Cancel();
        _localClient?.Dispose(); _localClient = null;
        _fileHttpClient?.Dispose();
        _notesHttpClient?.Dispose();
        _hotkeyService?.Dispose();
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
