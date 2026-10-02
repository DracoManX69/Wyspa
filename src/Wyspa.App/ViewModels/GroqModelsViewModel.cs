using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.App.ViewModels;

public sealed class GroqModelsViewModel(IGroqTranscriptionClient client, Func<AppSettings> settings) : ViewModelBase
{
    private bool _isRefreshing;
    private bool _hasLoaded;
    private string _status = "Refresh models to load the current choices from Groq.";
    public IReadOnlyList<string> TranscriptionModels { get; private set; } = [];
    public IReadOnlyList<string> TextModels { get; private set; } = [];
    public bool IsRefreshing { get => _isRefreshing; private set { SetProperty(ref _isRefreshing, value); OnPropertyChanged(nameof(CanRefresh)); } }
    public bool CanRefresh => !IsRefreshing;
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    // Ignore null selection events when WPF rebuilds ItemsSource. Never replace
    // a saved model merely because a refresh failed or Groq retired that model.
    public string TranscriptionModel { get => settings().ModelId; set { if (TranscriptionModels.Contains(value)) { settings().ModelId = value; NotifySelections(); } } }
    public string SummaryModel { get => settings().SummaryModelId; set { if (TextModels.Contains(value)) { settings().SummaryModelId = value; NotifySelections(); } } }
    public string RewriteModel { get => settings().WritingCleanupModelId; set { if (TextModels.Contains(value)) { settings().WritingCleanupModelId = value; NotifySelections(); } } }
    public string IntentModel { get => settings().IntentModelId; set { if (TextModels.Contains(value)) { settings().IntentModelId = value; NotifySelections(); } } }
    public string TranscriptionSelection => Describe(TranscriptionModel, TranscriptionModels);
    public string SummarySelection => Describe(SummaryModel, TextModels);
    public string RewriteSelection => Describe(RewriteModel, TextModels);
    public string IntentSelection => Describe(IntentModel, TextModels);

    private string Describe(string id, IReadOnlyList<string> choices) => choices.Contains(id)
        ? "" : _hasLoaded ? $"Saved: {id} · not available in this list. Choose another model or refresh."
        : $"Saved: {id} · availability not yet checked.";

    public async Task<ConnectionTestResult> RefreshAsync(string key)
    {
        if (IsRefreshing) return ConnectionTestResult.Failed("A model refresh is already in progress.");
        if (string.IsNullOrWhiteSpace(key))
        {
            Status = "Save and test a Groq API key to load available models.";
            return ConnectionTestResult.Failed(Status);
        }
        IsRefreshing = true;
        Status = "Loading available models from Groq…";
        try
        {
            var result = await client.TestConnectionAsync(key, CancellationToken.None);
            if (!result.Success)
            {
                Status = result.UserMessage + " Saved model choices have been kept.";
                return result;
            }
            TranscriptionModels = GroqModelCatalog.ForTranscription(result.AvailableModels);
            TextModels = GroqModelCatalog.ForTextGeneration(result.AvailableModels);
            _hasLoaded = true;
            OnPropertyChanged(nameof(TranscriptionModels)); OnPropertyChanged(nameof(TextModels));
            NotifySelections();
            Status = $"Updated {DateTime.Now:t} · {TranscriptionModels.Count} speech-to-text models · {TextModels.Count} text models.";
            if (TranscriptionModels.Count == 0 || TextModels.Count == 0)
                Status += " No compatible models were returned for one or more tasks.";
            return result;
        }
        finally { IsRefreshing = false; }
    }

    public void Clear()
    {
        TranscriptionModels = []; TextModels = []; _hasLoaded = false;
        OnPropertyChanged(nameof(TranscriptionModels)); OnPropertyChanged(nameof(TextModels));
        NotifySelections(); Status = "Save and test a Groq API key to load available models.";
    }

    private void NotifySelections()
    {
        foreach (var name in new[] { nameof(TranscriptionModel), nameof(SummaryModel), nameof(RewriteModel), nameof(IntentModel),
                     nameof(TranscriptionSelection), nameof(SummarySelection), nameof(RewriteSelection), nameof(IntentSelection) }) OnPropertyChanged(name);
    }
}
