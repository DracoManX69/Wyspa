using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.App.ViewModels;

public sealed class AudioFileItem(string path) : ViewModelBase
{
    private string _status = "Ready";
    private string _details = "";
    private string _transcript = "";
    public string Path { get; } = path;
    public string Name => System.IO.Path.GetFileName(Path);
    public bool IsComplete { get; set; }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public string Details { get => _details; set => SetProperty(ref _details, value); }
    public string Transcript { get => _transcript; set { SetProperty(ref _transcript, value); OnPropertyChanged(nameof(HasTranscript)); } }
    public bool HasTranscript => !string.IsNullOrWhiteSpace(Transcript);
}

public sealed class FileTranscriptionViewModel : ViewModelBase
{
    private readonly IAudioFilePreparationService _preparation;
    private readonly IGroqTranscriptionClient _client;
    private readonly ISecretStore _secrets;
    private readonly Func<AppSettings> _settings;
    private CancellationTokenSource? _cancellation;
    private Task? _activeTask;
    private AudioFileItem? _selectedFile;
    private bool _isBusy;
    private string _status = "Choose audio files from your PC. Upload starts only when you click Transcribe.";

    public FileTranscriptionViewModel(IAudioFilePreparationService preparation, IGroqTranscriptionClient client,
        ISecretStore secrets, Func<AppSettings> settings)
    {
        _preparation = preparation;
        _client = client;
        _secrets = secrets;
        _settings = settings;
        TranscribeCommand = new AsyncRelayCommand(StartAsync, () => !IsBusy && Files.Any(f => !f.IsComplete));
        CancelCommand = new AsyncRelayCommand(() => { Cancel(); return Task.CompletedTask; }, () => IsBusy);
        ClearCommand = new AsyncRelayCommand(() => { Files.Clear(); SelectedFile = null; Status = "Choose audio files to begin."; Refresh(); return Task.CompletedTask; }, () => !IsBusy && Files.Count > 0);
    }

    public ObservableCollection<AudioFileItem> Files { get; } = [];
    public AsyncRelayCommand TranscribeCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }
    public AsyncRelayCommand ClearCommand { get; }
    public AudioFileItem? SelectedFile { get => _selectedFile; set => SetProperty(ref _selectedFile, value); }
    public string Status { get => _status; set => SetProperty(ref _status, value); }
    public bool IsBusy { get => _isBusy; private set { SetProperty(ref _isBusy, value); OnPropertyChanged(nameof(CanChooseFiles)); Refresh(); } }
    public bool CanChooseFiles => !IsBusy;

    public void AddFiles(IEnumerable<string> paths)
    {
        if (IsBusy) return;
        foreach (var path in paths)
        {
            if (!Files.Any(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase)))
                Files.Add(new AudioFileItem(path));
        }
        SelectedFile ??= Files.FirstOrDefault();
        Status = $"{Files.Count} file(s) selected. Ready to transcribe with your saved Groq connection.";
        Refresh();
    }

    public Task StartAsync()
    {
        if (IsBusy) return _activeTask ?? Task.CompletedTask;
        _activeTask = TranscribeAsync();
        return _activeTask;
    }

    public void Cancel() => _cancellation?.Cancel();

    public async Task StopAsync()
    {
        Cancel();
        if (_activeTask is not null) await _activeTask;
    }

    private async Task TranscribeAsync()
    {
        IsBusy = true;
        using var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var token = cancellation.Token;
        AudioFileItem? current = null;
        try
        {
            var key = await _secrets.GetApiKeyAsync(token);
            if (string.IsNullOrWhiteSpace(key))
            {
                Status = "Add and test your API key in the Groq section first.";
                return;
            }
            var settings = _settings();
            var options = new TranscriptionOptions(settings.ModelId, settings.Language, settings.CustomPrompt);
            var pending = Files.Where(f => !f.IsComplete).ToArray();
            var failed = 0;
            for (var index = 0; index < pending.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                current = pending[index];
                var item = current;
                SelectedFile = item;
                item.Status = "Preparing…";
                Status = $"File {index + 1} of {pending.Length}: {item.Name}";
                var preparing = true;
                try
                {
                    // Preparation runs off the UI thread. Progress returns to the UI context.
                    var progress = new Progress<string>(message => { if (preparing && !token.IsCancellationRequested) item.Status = message; });
                    using var prepared = await Task.Run(() => _preparation.PrepareAsync(item.Path, progress, token), token);
                    preparing = false;
                    var saved = Math.Max(0, 100d * (1 - (double)prepared.UploadBytes / prepared.OriginalBytes));
                    item.Details = $"{prepared.OriginalBytes / 1_000_000d:0.##} MB → {prepared.UploadBytes / 1_000_000d:0.##} MB upload ({saved:0}% smaller). {prepared.Description}";
                    var transcripts = new List<string>();
                    for (var part = 0; part < prepared.Paths.Count; part++)
                    {
                        token.ThrowIfCancellationRequested();
                        item.Status = $"Transcribing part {part + 1} of {prepared.Paths.Count}…";
                        var text = await _client.TranscribeAsync(key, prepared.Paths[part], options, token);
                        transcripts.Add(text);
                        item.Transcript = string.Join(Environment.NewLine + Environment.NewLine, transcripts.Where(t => !string.IsNullOrWhiteSpace(t)));
                    }
                    token.ThrowIfCancellationRequested();
                    item.IsComplete = true;
                    item.Status = string.IsNullOrWhiteSpace(item.Transcript) ? "Complete — no speech detected" : "Complete";
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    preparing = false;
                    failed++;
                    item.Status = "Failed — " + FriendlyError(ex);
                    // A failed or partial result remains visible and can be copied/saved.
                }
                finally { preparing = false; }
            }
            Status = failed == 0 ? "Transcription complete. Select a file to copy or save its transcript." :
                $"Finished with {failed} failed file(s). Completed files will be skipped if you retry. Partial results remain available.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            if (current is not null && !current.IsComplete) current.Status = "Cancelled — any partial transcript is kept";
            Status = "Cancelled. Completed and partial transcripts are still available.";
        }
        catch (Exception ex) { Status = FriendlyError(ex); }
        finally
        {
            _cancellation = null;
            IsBusy = false;
        }
    }

    private static string FriendlyError(Exception ex) => ex switch
    {
        OperationCanceledException => "The upload timed out. Check your connection and try again.",
        HttpRequestException => "Could not reach Groq. Check your connection and try again.",
        FileNotFoundException => "The audio file could not be found. Select it again.",
        UnauthorizedAccessException => "Wyspa cannot access this file or its temporary folder.",
        _ => ex.Message
    };

    private void Refresh()
    {
        TranscribeCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        ClearCommand.RaiseCanExecuteChanged();
    }
}
