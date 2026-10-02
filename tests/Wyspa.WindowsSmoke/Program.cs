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
            if (args[0] is "native" or "speakers") { NativeAsync(output, args[2], args[0] == "speakers").GetAwaiter().GetResult(); return 0; }
            if (args[0] == "video") { VideoAsync(output).GetAwaiter().GetResult(); return 0; }
            if (args[0] == "groq") { GroqAsync(output, args[2]).GetAwaiter().GetResult(); return 0; }
            if (args[0] == "models") { ModelsAsync(output).GetAwaiter().GetResult(); return 0; }
            Render(output, args[2]); return 0;
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
    private static void Render(string output, string appXaml)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var xml = XDocument.Load(appXaml); XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var dictionary = new XElement(ns + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"), xml.Root!.Element(ns + "Application.Resources")!.Elements());
        app.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
        var errors = new StringWriter(); PresentationTraceSources.DataBindingSource.Listeners.Add(new TextWriterTraceListener(errors));
        using var theme = new ThemeService(app.Resources);
        var store = new NoteStore(Path.Combine(output, "notes"));
        var capture = new FakeCapture();
        var settings = new AppSettings();
        var vm = new NotesViewModel(capture, new FakeSpeakers(), new FakeGroq(), new FakeSecrets(), store, new FakeVideo(), () => settings,
            action => app.Dispatcher.InvokeAsync(action).Task.Unwrap(), _ => Task.CompletedTask, () => Task.CompletedTask);
        using var http = new HttpClient(new ModelHandler());
        var groq = new GroqTranscriptionClient(http);
        var settingsService = new JsonSettingsService(Path.Combine(output, "settings.json"));
        var audio = new NaudioCaptureService();
        var monitor = new NaudioLevelMonitorService();
        using var hotkey = new NativeHotkeyService(); using var autoHotkey = new NativeHotkeyService();
        var statusOverlay = new OverlayStatusService(() => new StatusOverlayWindow());
        var orchestrator = new DictationOrchestrator(settingsService, new FakeSecrets(), audio, groq, new TextCleanupService(), new WindowsTextInsertionService(), new WindowsKeyboardCommandService(), statusOverlay);
        var main = new MainViewModel(settingsService, new FakeSecrets(), groq, audio, monitor, hotkey, autoHotkey, new FakeStartup(), orchestrator, new GitHubUpdateService(http), new WindowsAutoCaptureMediaControlService());
        main.Notes = vm;
        settings = main.Settings;
        var videos = new NotesViewModel(new FakeCapture(), new FakeSpeakers(), new FakeGroq(), new FakeSecrets(), store, new FakeVideo(), () => settings,
            action => app.Dispatcher.InvokeAsync(action).Task.Unwrap(), _ => Task.CompletedTask, () => Task.CompletedTask, NoteLibrary.YouTube);
        main.Videos = videos;
        main.FileTranscription = new FileTranscriptionViewModel(new AudioFilePreparationService(Path.Combine(AppContext.BaseDirectory, "Tools", "Flac", "flac.exe")), groq, new FakeSecrets(), () => settings);
        var window = new MainWindow { Title = "Wyspa settings verification", DataContext = main };
        window.Loaded += async (_, _) =>
        {
            try
            {
                var tabs = Find<TabControl>(window)!; tabs.SelectedIndex = 2;
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
                window.Width = 880; window.Height = 740; tabs.SelectedIndex = 3;
                await Idle(window); Save(window, Path.Combine(output, "youtube.png"));
                if (videos.SelectedNote?.Kind != "YouTube" || videos.Summary != "Independent video summary") throw new Exception("Video state was overwritten by a conversation.");
                tabs.SelectedIndex = 4;
                await main.RefreshModelsAsync(); await Idle(window);
                var content = (StackPanel)window.FindName("SettingsContent");
                var expanders = content.Children.OfType<Expander>().ToArray();
                expanders[0].IsExpanded = true; await Idle(window);
                var modelBox = FindAll<ComboBox>(content).Single(c => System.Windows.Automation.AutomationProperties.GetName(c) == "Transcription model");
                if (modelBox.Items.Count != 2) throw new Exception("Speech dropdown contains incompatible models.");
                modelBox.SelectedItem = "whisper-large-v3";
                await Idle(window); await main.AutoSaveSettingsAsync();
                if ((await settingsService.LoadAsync(default)).ModelId != "whisper-large-v3") throw new Exception("Model selection did not persist.");
                await main.RefreshModelsAsync(); await Idle(window);
                if ((string?)modelBox.SelectedItem != "whisper-large-v3") throw new Exception("Model refresh reset the selection.");
                Save(window, Path.Combine(output, "settings-groq.png"));
                modelBox.IsDropDownOpen = true; await Idle(window);
                // Popup contents are checked above; RenderTargetBitmap captures the window surface only.
                modelBox.IsDropDownOpen = false;
                window.Width = 560; window.Height = 480;
                await Idle(window); Save(window, Path.Combine(output, "settings-narrow.png"));
                window.Width = 880; window.Height = 740;
                foreach (var expander in expanders) expander.IsExpanded = false;
                await Idle(window); Save(window, Path.Combine(output, "settings-groups.png"));
                foreach (var expander in expanders.Skip(1))
                {
                    expander.IsExpanded = true; await Idle(window);
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
                        window.Width = 880; window.Height = 740;
                    }
                    expander.IsExpanded = false;
                }
                Find<ScrollViewer>(content.Parent)?.ScrollToTop();
                app.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
                await Idle(window); Save(window, Path.Combine(output, "settings-light.png"));
                tabs.SelectedIndex = 2;
                await Idle(window); Save(window, Path.Combine(output, "conversation-light.png"));
                tabs.SelectedIndex = 3;
                await Idle(window); Save(window, Path.Combine(output, "youtube-light.png"));
                await vm.RefreshAsync(); if (vm.Bubbles.Count != 3) throw new Exception("Saved notes failed to reopen");
                File.WriteAllText(Path.Combine(output, "binding-errors.txt"), errors.ToString());
                if (errors.ToString().Contains("System.Windows.Data Error")) throw new Exception("WPF binding errors were recorded.");
                Console.WriteLine("WPF navigation, all settings groups, task-filtered dropdowns, model refresh/persistence, separate libraries, and capture lifecycle passed.");
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString()); Console.Error.WriteLine(ex); Environment.ExitCode = 1; }
            finally { await vm.ShutdownAsync(); await videos.ShutdownAsync(); app.Shutdown(); }
        };
        app.Run(window);
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
        window.UpdateLayout(); var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(window);
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
