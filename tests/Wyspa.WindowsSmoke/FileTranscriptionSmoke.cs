using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using Wyspa.App;
using Wyspa.App.Services;
using Wyspa.App.ViewModels;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

internal static class FileTranscriptionSmoke
{
    internal static int Run(string output, string appXaml)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        using var xamlStream = File.OpenRead(appXaml);
        var xml = XDocument.Load(xamlStream);
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        app.Resources = (ResourceDictionary)XamlReader.Parse(xml.Root!.Element(ns + "Application.Resources")!.Element(ns + "ResourceDictionary")!.ToString());
        using var theme = new ThemeService(app.Resources);
        theme.ApplyTheme(false);
        using var errors = new StringWriter();
        var listener = new TextWriterTraceListener(errors);
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        using var http = new HttpClient(new Handler());
        var vm = new FileTranscriptionViewModel(new Preparation(output), new GroqTranscriptionClient(http), new Secrets(),
            () => new AppSettings(), new FileTranscriptCsvStore(Path.Combine(output, "csv")));
        var view = new FileTranscriptionView { DataContext = vm };
        var window = new Window { Title = "Audio file CSV verification", Content = view, Width = 940, Height = 860,
            ShowActivated = false, ShowInTaskbar = false, Background = (Brush)app.Resources["AppBackgroundBrush"] };
        var exitCode = 0;
        window.Loaded += async (_, _) =>
        {
            try
            {
                vm.AddFiles(["meeting.wav"]);
                await vm.StartAsync();
                await Idle(window);
                var item = vm.SelectedFile!;
                if (!item.IsComplete || !item.HasCsv || item.Segments.Count != 2 || item.Segments[1].Start != 31.25)
                    throw new Exception("Timestamped file transcription or automatic CSV failed: " + item.Status);
                var preview = Descendants<TextBox>(view).Single();
                if (preview.Text != item.TimestampedTranscript || !preview.Text.Contains("00:00:31.250"))
                    throw new Exception("Timestamped preview binding failed.");
                foreach (var label in new[] { "Open CSV", "Save CSV…", "Copy transcript", "Save text…" })
                    if (!Descendants<Button>(view).Single(b => b.Content?.ToString() == label).IsEnabled)
                        throw new Exception(label + " was not enabled for completed transcription.");
                if (!File.ReadAllText(item.CsvPath!).Contains("00:00:31.250,00:00:32.500")) throw new Exception("CSV source offset failed.");
                Capture(window, Path.Combine(output, "audio-files-light.png"));
                theme.ApplyTheme(true); window.Background = (Brush)app.Resources["AppBackgroundBrush"];
                await Idle(window); Capture(window, Path.Combine(output, "audio-files-dark.png"));
                window.Width = 560; window.Height = 480;
                Descendants<ScrollViewer>(view).First().ScrollToBottom();
                await Idle(window); Capture(window, Path.Combine(output, "audio-files-narrow-dark.png"));
                theme.ApplyTheme(false); window.Background = (Brush)app.Resources["AppBackgroundBrush"];
                await Idle(window); Capture(window, Path.Combine(output, "audio-files-narrow-light.png"));
                File.WriteAllText(Path.Combine(output, "binding-errors.txt"), errors.ToString());
                if (errors.GetStringBuilder().Length != 0) throw new Exception("WPF binding errors: " + errors);
                File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: actual Audio Files view, verbose JSON through production client, split offsets, CSV auto-save, preview and action bindings, light/dark/narrow renders. No live Groq request.");
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString()); exitCode = 1; }
            finally { window.Close(); app.Shutdown(); }
        };
        app.Run(window);
        PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
        return exitCode;
    }

    private static async Task Idle(Window window)
    {
        window.UpdateLayout();
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        await Task.Delay(100);
    }

    private static void Capture(Window window, string path)
    {
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private sealed class Handler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"text":"A timestamped speech segment, with punctuation.","segments":[{"start":1.25,"end":2.5,"text":"A timestamped speech segment, with punctuation."}]}""") });
    }

    private sealed class Preparation(string output) : IAudioFilePreparationService
    {
        public async Task<PreparedAudio> PrepareAsync(string path, IProgress<string>? progress, CancellationToken token)
        {
            var directory = Path.Combine(output, "temporary-audio"); Directory.CreateDirectory(directory);
            var paths = new[] { Path.Combine(directory, "one.flac"), Path.Combine(directory, "two.flac") };
            foreach (var part in paths) await File.WriteAllBytesAsync(part, [1], token);
            return new PreparedAudio(paths, 10, 2, "Test split audio", directory, [0, 30]);
        }
    }

    private sealed class Secrets : ISecretStore
    {
        public Task<string?> GetApiKeyAsync(CancellationToken token) => Task.FromResult<string?>("fixture-key");
        public Task SaveApiKeyAsync(string key, CancellationToken token) => Task.CompletedTask;
        public Task RemoveApiKeyAsync(CancellationToken token) => Task.CompletedTask;
    }
}
