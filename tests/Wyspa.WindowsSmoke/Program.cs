using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using NAudio.Wave;
using Wyspa.App;
using Wyspa.App.Services;
using Wyspa.App.ViewModels;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using Wyspa.Core.Services;
using Wyspa.Infrastructure.Audio;
using Wyspa.Infrastructure.Settings;
using Wyspa.Infrastructure.Hotkeys;
using Wyspa.Infrastructure.Insertion;
using Wyspa.Infrastructure.Media;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            var output = Path.GetFullPath(args[1]); Directory.CreateDirectory(output);
            if (args[0] == "stream-browser") return StreamFixBrowserSmoke.Run(output);
            if (args[0] == "stream-word") return StreamFixWordSmoke.Run(output);
            if (args[0] == "stream-insertion") return StreamingInsertionSmoke.Run(output);
            if (args[0] == "stream-target") { StreamingInsertionSmoke.Target(output); return 0; }
            if (args[0] == "stream-groq") { StreamingGroqSmoke.RunAsync(output, args[2]).GetAwaiter().GetResult(); return 0; }
            if (args[0] is "native" or "speakers") { NativeAsync(output, args[2], args[0] == "speakers").GetAwaiter().GetResult(); return 0; }
            if (args[0] == "video") { VideoAsync(output).GetAwaiter().GetResult(); return 0; }
            if (args[0] == "groq") { GroqAsync(output, args[2]).GetAwaiter().GetResult(); return 0; }
            if (args[0] == "models") { ModelsAsync(output).GetAwaiter().GetResult(); return 0; }
            Render(output, args[2], args[0] == "preview"); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static async Task ModelsAsync(string output)
    {
        var key = await new DpapiSecretStore().GetApiKeyAsync(default);
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("No saved Groq key is available.");
        using var http = new HttpClient();
        var models = new GroqModelsViewModel(new GroqTranscriptionClient(http), () => new AppSettings());
        var result = await models.RefreshAsync(key);
        if (!result.Success) throw new InvalidOperationException(result.UserMessage);
        File.WriteAllText(Path.Combine(output, "live-models.json"), JsonSerializer.Serialize(new { models.TranscriptionModels, models.TextModels, models.Status }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Groq model discovery: {models.TranscriptionModels.Count} speech models and {models.TextModels.Count} text models.");
    }
    private static async Task GroqAsync(string output, string wave)
    {
        var key = await new DpapiSecretStore().GetApiKeyAsync(default);
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("No saved Groq key is available for the smoke test.");
        using var http = new HttpClient(); var groq = new GroqNoteIntelligence(http);
        var speech = await groq.TranscribeAsync(key, wave, new("whisper-large-v3-turbo", "en", null), default);
        var summary = await groq.SummariseAsync(key, speech.Text, "openai/gpt-oss-20b", default);
        File.WriteAllText(Path.Combine(output, "groq-result.json"), JsonSerializer.Serialize(new { speech, summary }));
        Console.WriteLine($"Groq transcription: {speech.Words.Count} timestamped words. Summary: {summary.Length} characters.");
    }
    private static async Task VideoAsync(string output)
    {
        using var token = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        using var video = await new VideoImporter(Path.Combine(AppContext.BaseDirectory, "Tools", "Video"))
            .PrepareAsync("https://www.youtube.com/watch?v=jNQXAC9IVRw", new Progress<string>(Console.WriteLine), token.Token);
        Console.WriteLine($"Imported {video.Title}, {video.Parts.Count} audio parts");
        using var reader = new WaveFileReader(video.Parts[0]);
        File.WriteAllText(Path.Combine(output, "video-result.txt"), $"{video.Title}\n{video.Parts.Count} parts\n{reader.WaveFormat}\n{reader.TotalTime}");
        var key = await new DpapiSecretStore().GetApiKeyAsync(default);
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("No saved Groq key is available.");
        using var http = new HttpClient();
        var speech = await new GroqNoteIntelligence(http).TranscribeAsync(key, video.Parts[0], new("whisper-large-v3-turbo", "en", null), token.Token);
        File.WriteAllText(Path.Combine(output, "youtube-transcript.txt"), speech.Text);
        if (speech.Words.Count == 0) throw new Exception("Video speech returned no timestamped words.");
        Console.WriteLine($"YouTube audio transcribed: {speech.Words.Count} timestamped words.");
    }
    private static async Task NativeAsync(string output, string wave, bool speakersOnly)
    {
        var report = new List<string>();
        using var reader = new WaveFileReader(wave);
        var provider = reader.ToSampleProvider();
        var samples = new float[reader.SampleCount];
        var count = provider.Read(samples, 0, samples.Length); samples = samples[..count];
        using var identifier = new LocalSpeakerIdentifier(Path.Combine(AppContext.BaseDirectory, "Tools", "Speakers"), () => .55);
        await identifier.InitializeAsync(default);
        var watch = Stopwatch.StartNew();
        var turns = await identifier.IdentifyAsync(samples, default);
        report.Add($"Speaker model: {turns.Count} turns, {turns.Select(t => t.Speaker).Distinct().Count()} speakers, {watch.ElapsedMilliseconds} ms for {samples.Length/16000d:0.0}s.");
        report.AddRange(turns.Select(t => $"{t.Start:0.00}-{t.End:0.00}: {t.Speaker}"));
        if (turns.Count == 0) throw new InvalidOperationException("Speaker model returned no turns for speech fixture.");
        watch.Restart();
        var shortTurns = await identifier.IdentifyAsync(samples.Take(56000).ToArray(), default);
        report.Add($"Short-window model: {shortTurns.Count} turns in {watch.ElapsedMilliseconds} ms.");
        identifier.Reset();
        var streamed = new List<SpeakerTurn>();
        for (var start = 0; start < samples.Length; start += 56000)
        {
            var chunk = samples[start..Math.Min(start + 56000, samples.Length)];
            var detected = await identifier.IdentifyAsync(chunk, default);
            streamed.AddRange(detected.Select(t => t with { Start = t.Start + start / 16000d, End = t.End + start / 16000d }));
        }
        report.Add($"Live-sized chunks: {streamed.Count} turns, {streamed.Select(t => t.Speaker).Where(s => s.StartsWith("Speaker ")).Distinct().Count()} identified speaker labels (uncertain turns listed below).");
        report.AddRange(streamed.Select(t => $"  {t.Start:0.00}-{t.End:0.00}: {t.Speaker}"));
        report.Add("Windows: " + Environment.OSVersion.Version);
        File.WriteAllLines(Path.Combine(output, "speaker-result.txt"), report);
        if (speakersOnly) { Console.WriteLine(string.Join("\n", report)); return; }
        // Capture playback locally only; no microphone input or capture upload is used by this test.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
        {
            report.Add("Process loopback unavailable on this Windows build; checking device loopback instead.");
            using var deviceCapture = new WasapiLoopbackCapture();
            long captured = 0;
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            deviceCapture.DataAvailable += (_, e) => Interlocked.Add(ref captured, e.BytesRecorded);
            deviceCapture.RecordingStopped += (_, e) => { if (e.Exception is not null) done.TrySetException(e.Exception); else done.TrySetResult(); };
            deviceCapture.StartRecording();
            using var play = new WaveFileReader(wave); using var outputDevice = new WaveOutEvent(); outputDevice.Init(play); outputDevice.Play();
            await Task.Delay(4200); outputDevice.Stop(); deviceCapture.StopRecording(); await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (captured == 0) throw new InvalidOperationException("Device loopback produced no audio.");
            report.Add($"Device loopback: {captured} bytes captured.");
            File.WriteAllLines(Path.Combine(output, "native-result.txt"), report); Console.WriteLine(string.Join("\n", report));
            return;
        }
        // Record only our own synthetic playback via Windows process loopback, never microphone audio.
        using var process = await ProcessLoopbackCapture.CreateAsync(Environment.ProcessId);
        var bytes = 0L;
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        process.DataAvailable += (_, e) => Interlocked.Add(ref bytes, e.BytesRecorded);
        process.RecordingStopped += (_, e) => { if (e.Exception is not null) stopped.TrySetException(e.Exception); else stopped.TrySetResult(); };
        process.StartRecording();
        using var playbackReader = new WaveFileReader(wave);
        using var player = new WaveOutEvent(); player.Init(playbackReader); player.Play();
        await Task.Delay(4500); player.Stop(); process.StopRecording(); await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));
        if (bytes == 0) throw new InvalidOperationException("Process capture produced no audio.");
        report.Add($"Process loopback: {bytes} bytes captured from own process.");
        File.WriteAllLines(Path.Combine(output, "native-result.txt"), report); Console.WriteLine(string.Join("\n", report));
    }
    private static void Render(string output, string appXaml, bool preview = false)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var xamlStream = File.OpenRead(appXaml);
        var xml = XDocument.Load(xamlStream); XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var resources = xml.Root!.Element(ns + "Application.Resources")!;
        var dictionary = resources.Element(ns + "ResourceDictionary") ?? new XElement(ns + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"), resources.Elements());
        app.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
        var errors = new StringWriter(); PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(errors));
        var systemDarkMode = false;
        using var theme = new ThemeService(app.Resources, () => systemDarkMode);
        theme.ApplyTheme(false);
        var store = new NoteStore(Path.Combine(output, "notes"));
        var capture = new FakeCapture();
        var settings = new AppSettings();
        var vm = new NotesViewModel(capture, new FakeSpeakers(), new FakeGroq(), new FakeSecrets(), store, new FakeVideo(), () => settings,
            action => app.Dispatcher.InvokeAsync(action).Task.Unwrap(), _ => Task.CompletedTask, () => Task.CompletedTask);
        using var http = new HttpClient(new ModelHandler());
        var groq = new GroqTranscriptionClient(http);
        var settingsService = new JsonSettingsService(Path.Combine(output, "settings.json"));
        var audio = new FakeDictationCapture();
        var monitor = new FakeLevelMonitor();
        using var hotkey = new NativeHotkeyService(); using var autoHotkey = new NativeHotkeyService();
        var statusOverlay = new OverlayStatusService(() => new StatusOverlayWindow());
        var orchestrator = new DictationOrchestrator(settingsService, new FakeSecrets(), audio, groq, new TextCleanupService(), new WindowsTextInsertionService(), new WindowsKeyboardCommandService(), statusOverlay);
        var main = new MainViewModel(settingsService, new FakeSecrets(), groq, audio, monitor, hotkey, autoHotkey, new FakeStartup(), orchestrator, new GitHubUpdateService(http), new WindowsAutoCaptureMediaControlService());
        main.SettingsChanged += (_, _) => theme.ApplyPreference(main.Settings.Theme);
        using var autoCapture = new AutoCaptureService(settingsService, new FakeSecrets(), monitor, audio, orchestrator, statusOverlay, new WakeToneService());
        Exception? previewFailure = null;
        main.InputLevelPreviewChanged += async (_, visible) =>
        {
            try { await autoCapture.SetLevelPreviewAsync(visible); }
            catch (Exception ex) { previewFailure = ex; }
        };
        main.Notes = vm;
        settings = main.Settings;
        main.Devices.Add(new AudioDeviceInfo("", "Windows default input", true));
        main.Devices.Add(new AudioDeviceInfo("preview-test-device", "Test input"));
        settings.MicrophoneDeviceId = "";
        var videos = new NotesViewModel(new FakeCapture(), new FakeSpeakers(), new FakeGroq(), new FakeSecrets(), store, new FakeVideo(), () => settings,
            action => app.Dispatcher.InvokeAsync(action).Task.Unwrap(), _ => Task.CompletedTask, () => Task.CompletedTask, NoteLibrary.YouTube);
        main.Videos = videos;
        main.FileTranscription = new FileTranscriptionViewModel(new AudioFilePreparationService(Path.Combine(AppContext.BaseDirectory, "Tools", "Flac", "flac.exe")), groq, new FakeSecrets(), () => settings);
        var window = new MainWindow { Title = "WyspaFluent settings verification", DataContext = main };
        window.Loaded += async (_, _) =>
        {
            try
            {
                var tabs = Find<TabControl>(window)!;
                if (preview)
                {
                    tabs.SelectedIndex = 4;
                    await main.RefreshModelsAsync(); await Idle(window);
                    // Uses the real view and view models with local test dependencies only.
                    window.Closed += (_, _) => app.Shutdown();
                    return;
                }
                tabs.SelectedIndex = 0; await Idle(window); Save(window, Path.Combine(output, "home.png"));
                tabs.SelectedIndex = 1; await Idle(window); Save(window, Path.Combine(output, "audio-files.png"));
                tabs.SelectedIndex = 2;
                if (tabs.Items.Count != 5) throw new Exception("Expected Home, Audio Files, Conversation, YouTube, Settings.");
                await store.SaveAsync(new NoteSession { Title = "Video sample", Kind = "YouTube", Summary = "Independent video summary", Entries = [new(Guid.NewGuid(), 0, 0, 2, "Video", "This is the saved video transcript.")] });
                await vm.RefreshAsync();
                if (vm.Notes.Any(n => n.Kind == "YouTube")) throw new Exception("Video appeared in conversation history.");
                await videos.RefreshAsync();
                await Idle(window); Save(window, Path.Combine(output, "conversation-empty.png"));
                await vm.StartAsync();
                capture.Emit(1, "Other side", 1); capture.Emit(2, "You", 4); capture.Emit(3, "Other side", 7);
                await Task.Delay(350);
                await vm.PauseResumeAsync(); if (!vm.IsPaused) throw new Exception("Pause failed");
                await vm.PauseResumeAsync(); if (vm.IsPaused) throw new Exception("Resume failed");
                await vm.StopAsync();
                vm.Title = "Project catch-up"; await vm.SaveSelectedAsync();
                if (vm.Bubbles.Count != 3) throw new Exception("Expected three transcript bubbles");
                await Idle(window); Save(window, Path.Combine(output, "conversation-result.png"));
                var overlay = new NoteOverlayWindow { DataContext = vm }; overlay.Show();
                await Idle(overlay); Save(overlay, Path.Combine(output, "overlay.png")); overlay.Close();
                window.Width = 560; window.Height = 480;
                await Idle(window); Save(window, Path.Combine(output, "conversation-narrow.png"));
                window.Width = 1180; window.Height = 860; tabs.SelectedIndex = 3;
                await Idle(window); Save(window, Path.Combine(output, "youtube.png"));
                if (videos.SelectedNote?.Kind != "YouTube" || videos.Summary != "Independent video summary") throw new Exception("Video state was overwritten by a conversation.");
                tabs.SelectedIndex = 4;
                await main.RefreshModelsAsync(); await Idle(window);
                var content = (StackPanel)window.FindName("SettingsContent");
                var expanders = content.Children.OfType<Expander>().ToArray();
                if (!expanders.Select(e => (string)e.Header).SequenceEqual(new[] { "Groq", "Conversation", "Audio & Capture", "Look & Feel", "Privacy", "System", "Experimental" }))
                    throw new Exception("Unexpected settings group names or order.");
                await VerifyLevelPreviewAndNavigation(window, tabs, main, autoCapture, monitor, audio, output, theme);
                await VerifyThemeSettings(window, theme, settingsService, output, dark => systemDarkMode = dark);
                if (previewFailure is not null) throw new Exception("Level preview failed.", previewFailure);
                expanders[0].IsExpanded = true; await Idle(window);
                var modelBox = FindAll<ComboBox>(content).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Transcription model");
                if (modelBox.Items.Count != 2) throw new Exception("Speech dropdown contains incompatible models.");
                modelBox.SelectedItem = "whisper-large-v3";
                await Idle(window); await Task.Delay(350);
                if ((await settingsService.LoadAsync(default)).ModelId != "whisper-large-v3") throw new Exception("Model selection did not persist.");
                await main.RefreshModelsAsync(); await Idle(window);
                if ((string?)modelBox.SelectedItem != "whisper-large-v3") throw new Exception("Model refresh reset the selection.");
                Save(window, Path.Combine(output, "settings-groq.png"));
                modelBox.IsDropDownOpen = true; await Idle(window);
                await Task.Delay(250); // Settle the native popup animation before capturing its own surface.
                var popup = (System.Windows.Controls.Primitives.Popup)modelBox.Template.FindName("PART_Popup", modelBox);
                SaveSurface((FrameworkElement)popup.Child, Path.Combine(output, "model-dropdown.png"));
                modelBox.IsDropDownOpen = false;
                var languageBox = FindAll<TextBox>(content).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Transcription language");
                if (!languageBox.Focus()) throw new Exception("Language field could not receive keyboard focus.");
                languageBox.Text = "en";
                if (!languageBox.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next))) throw new Exception("Keyboard focus traversal failed.");
                await Idle(window); await Task.Delay(350);
                if ((await settingsService.LoadAsync(default)).Language != "en") throw new Exception("Text field did not autosave on focus loss.");
                if (System.Windows.Input.Keyboard.FocusedElement == languageBox) throw new Exception("Focus did not leave language field.");
                Find<ScrollViewer>(content.Parent)?.ScrollToTop();
                window.Width = 560; window.Height = 480;
                await Idle(window); Save(window, Path.Combine(output, "settings-narrow.png"));
                window.Width = 1180; window.Height = 860;
                foreach (var expander in expanders) expander.IsExpanded = false;
                await Idle(window); Save(window, Path.Combine(output, "settings-groups.png"));
                foreach (var expander in expanders.Skip(1))
                {
                    expander.IsExpanded = true; await Idle(window);
                    foreach (var control in FindAll<Control>(expander).Where(c => c is TextBox or ComboBox or Slider or PasswordBox))
                    {
                        // Template-generated text editors are named by their owning control.
                        if (control.TemplatedParent is not null) continue;
                        var inputPeer = System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(control);
                        if (string.IsNullOrWhiteSpace(inputPeer?.GetName())) throw new Exception("Settings input has no accessible name: " + control.GetType().Name);
                    }
                    Save(window, Path.Combine(output, "settings-" + expander.Header.ToString()!.Split(' ')[0].ToLowerInvariant() + ".png"));
                    if (expander.Header.ToString() == "Experimental")
                    {
                        foreach (var label in new[] { "Tone Re-write Model", "Spoken Actions Model" })
                        {
                            FindAll<TextBlock>(expander).Single(t => t.Text == label).BringIntoView();
                            await Idle(window);
                            Save(window, Path.Combine(output, label.StartsWith("Tone") ? "experimental-rewrite.png" : "experimental-actions.png"));
                        }
                        window.Width = 560; window.Height = 480;
                        await Idle(window); Save(window, Path.Combine(output, "experimental-narrow.png"));
                        window.Width = 1180; window.Height = 860;
                    }
                    expander.IsExpanded = false;
                }
                var system = expanders.Single(e => (string)e.Header == "System");
                system.IsExpanded = true; await Idle(window);
                var toggle = FindAll<CheckBox>(system).Single(c => (string)c.Content == "Start Minimized");
                var original = toggle.IsChecked;
                var peer = new System.Windows.Automation.Peers.CheckBoxAutomationPeer(toggle);
                var provider = (System.Windows.Automation.Provider.IToggleProvider)peer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle);
                provider.Toggle(); await Task.Delay(350);
                if ((await settingsService.LoadAsync(default)).StartMinimized != toggle.IsChecked) throw new Exception("Toggle did not autosave.");
                provider.Toggle(); await Task.Delay(350);
                if (toggle.IsChecked != original) throw new Exception("Toggle did not restore.");
                system.IsExpanded = false;
                var captureGroup = expanders.Single(e => (string)e.Header == "Audio & Capture");
                captureGroup.IsExpanded = true;
                var streamToggle = FindAll<CheckBox>(captureGroup).Single(c => (string)c.Content == "Stream Mode");
                var streamPeer = new System.Windows.Automation.Peers.CheckBoxAutomationPeer(streamToggle);
                var streamProvider = (System.Windows.Automation.Provider.IToggleProvider)streamPeer.GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle);
                var modes = FindAll<ComboBox>(captureGroup).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Dictation activation mode");
                foreach (var mode in Enum.GetValues<ActivationMode>())
                {
                    modes.SelectedValue = mode;
                    streamProvider.Toggle(); await Task.Delay(350);
                    var saved = await settingsService.LoadAsync(default);
                    if (!saved.StreamModeEnabled || saved.ActivationMode != mode) throw new Exception("Stream Mode did not persist independently of activation mode.");
                    streamProvider.Toggle(); await Task.Delay(350);
                }
                if ((await settingsService.LoadAsync(default)).StreamModeEnabled) throw new Exception("Stream Mode did not turn off.");
                modes.SelectedValue = ActivationMode.Toggle;
                var experimental = FindAll<Expander>(window).Single(e => e.Header?.ToString() == "Experimental");
                experimental.IsExpanded = true;
                var fixToggle = FindAll<CheckBox>(experimental).Single(c => c.Content?.ToString() == "Stream Fix");
                await Idle(window);
                if (fixToggle.IsEnabled) throw new Exception("Stream Fix must be disabled with Stream Mode off.");
                streamToggle.IsChecked = true;
                await Task.Delay(800); await Idle(window);
                if (!fixToggle.IsEnabled) throw new Exception("Stream Fix did not enable with Stream Mode.");
                fixToggle.IsChecked = true;
                await Task.Delay(800);
                if (!(await settingsService.LoadAsync(default)).StreamFixEnabled) throw new Exception("Stream Fix did not persist.");
                ((FrameworkElement)fixToggle.Parent).BringIntoView(); await Idle(window); Save(window, Path.Combine(output, "stream-fix-light.png"));
                theme.ApplyTheme(true); await Idle(window); Save(window, Path.Combine(output, "stream-fix-dark.png"));
                window.Width = 560; window.Height = 600;
                ((FrameworkElement)fixToggle.Parent).BringIntoView(); await Idle(window); Save(window, Path.Combine(output, "stream-fix-narrow.png"));
                window.Width = 1180; window.Height = 860;
                streamToggle.IsChecked = false; await Task.Delay(800);
                if (fixToggle.IsEnabled || !(await settingsService.LoadAsync(default)).StreamFixEnabled) throw new Exception("Stream Mode should disable, but remember, Stream Fix preference.");
                theme.ApplyTheme(false);
                streamToggle.BringIntoView(); await Idle(window); Save(window, Path.Combine(output, "stream-mode-light.png"));
                theme.ApplyTheme(true); await Idle(window); Save(window, Path.Combine(output, "stream-mode-dark.png"));
                window.Width = 560; window.Height = 480;
                streamToggle.BringIntoView(); await Idle(window); Save(window, Path.Combine(output, "stream-mode-narrow.png"));
                window.Width = 1180; window.Height = 860;
                captureGroup.IsExpanded = false;
                Find<ScrollViewer>(content.Parent)?.ScrollToTop();
                theme.ApplyTheme(false);
                await Idle(window); Save(window, Path.Combine(output, "settings-light.png"));
                theme.ApplyTheme(true);
                await Idle(window); Save(window, Path.Combine(output, "settings-dark.png"));
                expanders[0].IsExpanded = true;
                await Idle(window); Save(window, Path.Combine(output, "settings-groq-dark.png"));
                expanders[0].IsExpanded = false; theme.ApplyTheme(false);
                tabs.SelectedIndex = 2;
                await Idle(window); Save(window, Path.Combine(output, "conversation-light.png"));
                tabs.SelectedIndex = 3;
                await Idle(window); Save(window, Path.Combine(output, "youtube-light.png"));
                await vm.RefreshAsync(); if (vm.Bubbles.Count != 3) throw new Exception("Saved notes failed to reopen");
                File.WriteAllText(Path.Combine(output, "binding-errors.txt"), errors.ToString());
                if (errors.ToString().Contains("System.Windows.Data Error")) throw new Exception("WPF binding errors were recorded.");
                Console.WriteLine("WPF navigation, settings deep links and order, independent input preview, theme selection/persistence/system following, overlay settings, model persistence, separate libraries, and capture lifecycle passed.");
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString()); Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally { if (!preview) { await vm.ShutdownAsync(); await videos.ShutdownAsync(); app.Shutdown(); } }
        };
        app.Run(window);
    }
    private static async Task VerifyThemeSettings(MainWindow window, ThemeService theme, JsonSettingsService settingsService,
        string output, Action<bool> setSystemDarkMode)
    {
        var group = (Expander)window.FindName("LookAndFeelSettingsGroup");
        var selector = (ComboBox)window.FindName("ThemeSelector");
        var opacity = (Slider)window.FindName("OverlayOpacitySlider");
        group.IsExpanded = true; await Idle(window);
        group.BringIntoView(new Rect(0, 0, group.ActualWidth, 72)); await Idle(window);
        if (!group.IsAncestorOf(opacity)) throw new Exception("Overlay did not move into Look & Feel.");
        if (!selector.Items.Cast<ComboBoxItem>().Select(i => (string)i.Content).SequenceEqual(new[] { "Dark", "Light", "System" }))
            throw new Exception("Theme dropdown choices are incorrect.");

        selector.SelectedValue = AppTheme.Dark; await Idle(window); await Task.Delay(350);
        if (!theme.IsDarkMode || (await settingsService.LoadAsync(default)).Theme != AppTheme.Dark)
            throw new Exception("Dark selection did not apply and persist.");
        setSystemDarkMode(false); theme.RefreshTheme();
        if (!theme.IsDarkMode) throw new Exception("Windows overrode explicit Dark preference.");
        // A fresh service and settings read exercise the same startup path as the app.
        using (var restarted = new ThemeService(Application.Current.Resources, () => false))
        {
            restarted.ApplyPreference((await new JsonSettingsService(Path.Combine(output, "settings.json")).LoadAsync(default)).Theme);
            if (!restarted.IsDarkMode) throw new Exception("Theme preference was lost after reload.");
        }
        await Idle(window); Save(window, Path.Combine(output, "look-and-feel-dark.png"));
        selector.IsDropDownOpen = true; await Idle(window); await Task.Delay(250);
        var popup = (System.Windows.Controls.Primitives.Popup)selector.Template.FindName("PART_Popup", selector);
        SaveSurface((FrameworkElement)popup.Child, Path.Combine(output, "theme-dropdown.png"));
        selector.IsDropDownOpen = false;
        var status = new StatusOverlayWindow(); status.ApplyTheme(true); status.SetPanelOpacity(.45);
        status.SetStatus("Transcribing", DictationState.Transcribing); status.ShowTransient();
        await Task.Delay(2800);
        if (!status.IsVisible) throw new Exception("Processing overlay disappeared while busy.");
        var processingBar = (Border)status.FindName("Bar1"); var initialHeight = processingBar.Height;
        await Task.Delay(160);
        if (processingBar.Height == initialHeight) throw new Exception("Processing indicator did not animate.");
        status.SetAutoCaptureToggleStatus(false); status.ShowTransient();
        await Task.Delay(2300);
        if (!status.IsVisible || ((TextBlock)status.FindName("StatusText")).Text != "Transcribing") throw new Exception("Toggle notification hid pending transcription.");
        Save(status, Path.Combine(output, "stream-processing.png"));
        status.Hide();

        status.Show(); await Idle(status); Save(status, Path.Combine(output, "status-overlay-dark.png"));
        var background = ((SolidColorBrush)((Border)status.FindName("Shell")).Background).Color;
        if (background.A != (byte)Math.Round(.45 * 255) || background.R != 43)
            throw new Exception("Overlay did not preserve opacity with dark appearance.");
        status.Close();

        selector.SelectedValue = AppTheme.Light; await Idle(window); await Task.Delay(350);
        setSystemDarkMode(true); theme.RefreshTheme();
        if (theme.IsDarkMode || (await settingsService.LoadAsync(default)).Theme != AppTheme.Light)
            throw new Exception("Explicit Light preference did not persist or withstand a Windows theme change.");
        Save(window, Path.Combine(output, "look-and-feel-light.png"));
        opacity.Value = .45; await Task.Delay(350);
        if (Math.Abs((await settingsService.LoadAsync(default)).OverlayOpacity - .45) > .001)
            throw new Exception("Moved overlay opacity control did not autosave.");
        opacity.Value = .82; await Task.Delay(350);
        window.Width = 560; window.Height = 480; await Idle(window);
        opacity.BringIntoView(); await Idle(window);
        Save(window, Path.Combine(output, "look-and-feel-narrow.png"));
        window.Width = 1180; window.Height = 860;
        selector.SelectedValue = AppTheme.System; await Idle(window); await Task.Delay(350);
        if (!theme.IsDarkMode || (await settingsService.LoadAsync(default)).Theme != AppTheme.System)
            throw new Exception("System preference did not use the current Windows theme.");
        setSystemDarkMode(false); theme.RefreshTheme();
        if (theme.IsDarkMode) throw new Exception("System preference did not follow Windows theme changes.");
        group.IsExpanded = false;
        ((ScrollViewer)window.FindName("SettingsScroll")).ScrollToTop(); await Idle(window);
    }

    private static async Task VerifyLevelPreviewAndNavigation(MainWindow window, TabControl tabs, MainViewModel main,
        AutoCaptureService service, FakeLevelMonitor monitor, FakeDictationCapture audio, string output, ThemeService theme)
    {
        var audioGroup = (Expander)window.FindName("AudioSettingsGroup");
        var conversationGroup = (Expander)window.FindName("ConversationSettingsGroup");
        var groqGroup = (Expander)window.FindName("GroqSettingsGroup");
        var meter = (ProgressBar)window.FindName("InputLevelMeter");
        var scroll = (ScrollViewer)window.FindName("SettingsScroll");
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        async Task ShowMeter()
        {
            tabs.SelectedIndex = 4; audioGroup.IsExpanded = true;
            await Idle(window); meter.BringIntoView(); await Idle(window);
        }
        async Task InvokeSettingsShortcut(System.Windows.Input.RoutedCommand command, int sourceTab, Expander target)
        {
            tabs.SelectedIndex = sourceTab; await Idle(window);
            var button = FindAll<Button>(window).First(b => b.Command == command);
            command.Execute(null, button); await Idle(window);
            Check(tabs.SelectedIndex == 4 && target.IsExpanded, "Settings shortcut did not select and expand its destination.");
            var header = (FrameworkElement)target.Template.FindName("HeaderSite", target);
            var bounds = header.TransformToAncestor(scroll).TransformBounds(new Rect(header.RenderSize));
            Check(bounds.IntersectsWith(new Rect(scroll.RenderSize)), "Settings shortcut left its header offscreen.");
            target.IsExpanded = false; await Idle(window);
        }
        await InvokeSettingsShortcut(AppNavigationCommands.OpenConversationSettings, 2, conversationGroup);
        await InvokeSettingsShortcut(AppNavigationCommands.OpenGroqSettings, 0, groqGroup);
        await InvokeSettingsShortcut(AppNavigationCommands.OpenAudioSettings, 0, audioGroup);
        var about = FindAll<Button>(window).Single(b => b.Command == AppNavigationCommands.OpenRepository);
        Check(AppNavigationCommands.OpenRepository.CanExecute(null, about), "About command is not routed to the shell.");
        Check(AppNavigationCommands.RepositoryUri.AbsoluteUri == "https://github.com/DracoManX69/Wyspa", "Wrong repository link.");

        main.Settings.ActivationMode = ActivationMode.Toggle;
        main.Settings.AutoCaptureListeningEnabled = false;
        await service.ApplySettingsAsync(main.Settings, false);
        await ShowMeter();
        Check(monitor.IsRunning && service.LevelPreviewEnabled, "Meter must run in Toggle mode without a key.");
        monitor.Emit(.37f); await Idle(window);
        Check(Math.Abs(main.MicrophoneLevel - .37f) < .001f && Math.Abs(meter.Value - .37) < .001, "Input levels did not reach the visible meter.");
        Check(audio.RecordingStarts == 0 && !main.Settings.AutoCaptureListeningEnabled, "Preview enabled capture.");
        Save(window, Path.Combine(output, "settings-audio-refined.png"));
        theme.ApplyTheme(true); await Idle(window); Save(window, Path.Combine(output, "settings-audio-refined-dark.png"));
        theme.ApplyTheme(false);
        tabs.SelectedIndex = 0; await Idle(window);
        Check(!monitor.IsRunning && !service.LevelPreviewEnabled, "Preview did not release the microphone on tab change.");
        window.Width = 560; window.Height = 480; await Idle(window);
        Save(window, Path.Combine(output, "home-narrow.png"));
        await ShowMeter(); monitor.Emit(.37f); await Idle(window);
        Save(window, Path.Combine(output, "settings-audio-refined-narrow.png"));
        window.Width = 1180; window.Height = 860;

        main.Settings.ActivationMode = ActivationMode.AutoCapture;
        await service.ApplySettingsAsync(main.Settings, true);
        await ShowMeter();
        monitor.Emit(.95f); await Task.Delay(250); await Idle(window);
        Check(audio.RecordingStarts == 0 && monitor.IsRunning, "Disabled SmartListen recorded during the preview.");
        var starts = monitor.StartCount;
        main.Settings.MicrophoneDeviceId = "preview-test-device";
        await service.ApplySettingsAsync(main.Settings, true);
        Check(monitor.DeviceId == "preview-test-device" && monitor.StartCount == starts + 1, "Preview did not switch input devices.");
        service.Suspended = true; await service.ApplySettingsAsync(main.Settings, true);
        Check(!monitor.IsRunning, "Conversation capture did not suspend level preview.");
        service.Suspended = false; await service.ApplySettingsAsync(main.Settings, true);
        Check(monitor.IsRunning, "Preview did not resume after capture suspension.");
        window.Hide(); await Idle(window); Check(!monitor.IsRunning, "Hiding the window kept preview active.");
        window.Show(); await Idle(window); Check(monitor.IsRunning, "Showing the meter did not restore preview.");
        window.WindowState = WindowState.Minimized; await Idle(window); Check(!monitor.IsRunning, "Minimizing kept preview active.");
        window.WindowState = WindowState.Normal; await Idle(window);
        scroll.ScrollToBottom(); await Idle(window); Check(!monitor.IsRunning, "Offscreen meter kept preview active.");
        await ShowMeter(); Check(monitor.IsRunning, "Scrolling to the meter did not restore preview.");
        audioGroup.IsExpanded = false; await Idle(window); Check(!monitor.IsRunning, "Collapsing Audio kept preview active.");

        main.Settings.AutoCaptureListeningEnabled = true;
        await service.ApplySettingsAsync(main.Settings, true);
        await ShowMeter(); tabs.SelectedIndex = 0; await Idle(window);
        Check(monitor.IsRunning && !service.LevelPreviewEnabled, "Leaving preview incorrectly disabled background SmartListen.");
        main.Settings.AutoCaptureListeningEnabled = false;
        await service.ApplySettingsAsync(main.Settings, true);
        Check(!monitor.IsRunning, "Disabled SmartListen kept monitoring without preview.");
        monitor.FailNextStart = true;
        try { await service.SetLevelPreviewAsync(true); throw new Exception("Device failure was not reported."); }
        catch (InvalidOperationException ex) when (ex.Message == "Test device unavailable.") { }
        Check(!monitor.IsRunning, "Failed device left the monitor running.");
        await service.SetLevelPreviewAsync(false); await service.SetLevelPreviewAsync(true);
        Check(monitor.IsRunning, "Preview could not recover after device failure.");
        await service.SetLevelPreviewAsync(false);
        main.Settings.ActivationMode = ActivationMode.Toggle; main.Settings.MicrophoneDeviceId = null;
        await service.ApplySettingsAsync(main.Settings, false);
        audioGroup.IsExpanded = false; tabs.SelectedIndex = 4; scroll.ScrollToTop(); await Idle(window);
        Check(audio.RecordingStarts == 0, "Preview tests unexpectedly started a recording.");
    }

    private static T? Find<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T result) return result;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (Find<T>(VisualTreeHelper.GetChild(parent, i)) is { } child) return child;
        return null;
    }
    private static IEnumerable<T> FindAll<T>(DependencyObject parent) where T : DependencyObject
    {
        if (parent is T result) yield return result;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            foreach (var child in FindAll<T>(VisualTreeHelper.GetChild(parent, i))) yield return child;
    }
    private sealed class FakeLevelMonitor : IAudioLevelMonitorService
    {
        public event EventHandler<float>? LevelAvailable;
        public event EventHandler<IReadOnlyList<float>>? AudioAvailable { add { } remove { } }
        public bool IsRunning { get; private set; }
        public string? DeviceId { get; private set; }
        public int StartCount { get; private set; }
        public bool FailNextStart { get; set; }
        public Task StartAsync(string? deviceId, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (FailNextStart) { FailNextStart = false; throw new InvalidOperationException("Test device unavailable."); }
            DeviceId = deviceId; IsRunning = true; StartCount++; return Task.CompletedTask;
        }
        public void Emit(float level) { if (IsRunning) LevelAvailable?.Invoke(this, level); }
        public void Stop() { IsRunning = false; LevelAvailable?.Invoke(this, 0); }
        public void Dispose() => Stop();
    }
    private sealed class FakeDictationCapture : IAudioCaptureService
    {
        public event EventHandler<float>? LevelAvailable { add { } remove { } }
        public bool IsRecording { get; private set; }
        public int RecordingStarts { get; private set; }
        public Task<IReadOnlyList<AudioDeviceInfo>> GetDevicesAsync(CancellationToken token) => Task.FromResult<IReadOnlyList<AudioDeviceInfo>>([new("", "Windows default input", true), new("preview-test-device", "Test input")]);
        public Task StartRecordingAsync(string? device, CancellationToken token) { IsRecording = true; RecordingStarts++; return Task.CompletedTask; }
        public Task<RecordingResult> StopRecordingAsync(CancellationToken token) { IsRecording = false; return Task.FromResult(new RecordingResult("", TimeSpan.Zero, 0, 0)); }
        public Task DeleteRecordingAsync(string path, CancellationToken token) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class ModelHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
        { Content = new StringContent("""{"data":[{"id":"whisper-large-v3"},{"id":"whisper-large-v3-turbo"},{"id":"llama-3.1-8b-instant"},{"id":"llama-3.3-70b-versatile"},{"id":"openai/gpt-oss-20b"},{"id":"playai-tts"},{"id":"meta-llama/llama-prompt-guard-2-22m"}]}""") });
    }
    private sealed class FakeStartup : IStartupService
    {
        public bool IsEnabled() => false;
        public void SetEnabled(bool enabled) { }
    }
    private static async Task Idle(Window window) => await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    private static void Save(Window window, string path)
    {
        window.UpdateLayout(); SaveSurface((FrameworkElement)window.Content, path);
    }
    private static void SaveSurface(FrameworkElement surface, string path)
    {
        surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)surface.ActualWidth, (int)surface.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(surface);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var stream = File.Create(path); encoder.Save(stream);
    }
    private sealed class FakeCapture : IConversationCapture
    {
        public event EventHandler<AudioChunk>? ChunkAvailable;
        public event EventHandler<string>? Failed { add { } remove { } }
        public bool SupportsApplicationCapture => true;
        public IReadOnlyList<CaptureTarget> GetOutputs() => [new("", "Windows default output")];
        public IReadOnlyList<CaptureTarget> GetApplications() => [new("123", "Example calling app")];
        public Task StartAsync(ConversationCaptureOptions options, double offset, CancellationToken token) => Task.CompletedTask;
        public Task StopAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Emit(int id, string source, double time) => ChunkAvailable?.Invoke(this, new(id, source, time, new float[32000]));
    }
    private sealed class FakeSpeakers : ISpeakerIdentifier
    {
        public Task InitializeAsync(CancellationToken token) => Task.CompletedTask;
        public Task<IReadOnlyList<SpeakerTurn>> IdentifyAsync(float[] samples, CancellationToken token) => Task.FromResult<IReadOnlyList<SpeakerTurn>>([]);
        public void Reset() { } public void Dispose() { }
    }
    private sealed class FakeGroq : INoteIntelligence
    {
        private int _i;
        public Task<SpeechResult> TranscribeAsync(string key, string path, TranscriptionOptions options, CancellationToken token)
        {
            var text = new[] { "Can we have the first draft ready by Friday?", "Yes. I'll send the outline tomorrow, then we can review it together.", "Sounds good. Let's keep the examples short and clear." }[_i++ % 3];
            return Task.FromResult(new SpeechResult(text, [new(.1, 1.9, text)]));
        }
        public Task<string> SummariseAsync(string key, string transcript, string model, CancellationToken token) => Task.FromResult("Draft due Friday. Outline tomorrow; review together.");
    }
    private sealed class FakeSecrets : ISecretStore
    {
        public Task<string?> GetApiKeyAsync(CancellationToken token) => Task.FromResult<string?>("fake-test-key");
        public Task SaveApiKeyAsync(string key, CancellationToken token) => Task.CompletedTask;
        public Task RemoveApiKeyAsync(CancellationToken token) => Task.CompletedTask;
    }
    private sealed class FakeVideo : IVideoImporter
    {
        public Task<PreparedVideo> PrepareAsync(string url, IProgress<string> progress, CancellationToken token) => throw new NotSupportedException();
    }
}
