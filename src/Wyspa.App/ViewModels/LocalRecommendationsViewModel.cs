using System.Collections.ObjectModel;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.App.ViewModels;

public sealed class LocalRecommendationsViewModel : ViewModelBase
{
    private readonly Func<AppSettings> _settings;
    private readonly Func<LocalHardware> _hardware;
    private readonly Func<string, bool> _installed;
    private readonly Func<bool> _canConfigure, _hasGroq;
    private readonly Func<bool, Task> _reserve;
    private readonly Func<Task> _save;
    private readonly Func<SpeechRecommendation, CancellationToken, Task> _apply;
    private readonly SpeechPerformanceBenchmark _benchmark;
    private readonly string _sample;
    private CancellationTokenSource? _cancellation;
    private Task? _active;
    private bool _busy, _expanded;
    private string _status = "Hardware suggestions are provisional. Test installed models to measure response time and sample errors on this PC.";
    public LocalRecommendationsViewModel(Func<AppSettings> settings, Func<LocalHardware> hardware, Func<string, bool> installed,
        Func<bool> canConfigure, Func<bool> hasGroq, Func<bool, Task> reserve, Func<Task> save,
        Func<SpeechRecommendation, CancellationToken, Task> apply, SpeechPerformanceBenchmark benchmark, string sample)
    {
        _settings = settings; _hardware = hardware; _installed = installed; _canConfigure = canConfigure; _hasGroq = hasGroq;
        _reserve = reserve; _save = save; _apply = apply; _benchmark = benchmark; _sample = sample;
        TestLocalCommand = new AsyncRelayCommand(() => _active = RunAsync(false), () => !IsBusy && _canConfigure());
        CompareGroqCommand = new AsyncRelayCommand(() => _active = RunAsync(true), () => !IsBusy && _canConfigure() && _hasGroq() && HasLocalComparison);
        CancelCommand = new AsyncRelayCommand(() => { _cancellation?.Cancel(); return Task.CompletedTask; }, () => IsBusy);
        ApplyCommand = new AsyncRelayCommand(() => _active = ApplyAsync(), () => !IsBusy && _canConfigure() && (Choice.Provider != "groq" || _hasGroq()));
    }
    public bool IsExpanded { get => _expanded; set => SetProperty(ref _expanded, value); }
    public ObservableCollection<SpeechPerformanceResult> Results { get; } = [];
    public AsyncRelayCommand TestLocalCommand { get; }
    public AsyncRelayCommand CompareGroqCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }
    public AsyncRelayCommand ApplyCommand { get; }
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); Refresh(); } }
    public SpeechRecommendation Choice => SpeechModelAdvisor.Recommend(_hardware(), _settings(), _installed);
    public string Recommendation => Choice.Explanation;
    public string HardwareSuggestion => SpeechModelAdvisor.Hardware(_hardware(), _settings()).Explanation;
    public string SuggestedModel => LocalModelStore.Catalog.FirstOrDefault(m => m.Id == Choice.ModelId)?.DisplayName ?? "Groq " + Choice.ModelId;
    public string ApplyLabel => Choice.Provider == "groq" ? "Use recommended Groq provider" : _installed(Choice.ModelId) ? "Use recommended local model" : "Download and use recommended model";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public SpeechPerformanceResult? TopLocal => _settings().SpeechPerformance?.Results
        .Where(r => r.Provider == "local" && r.Score.HasValue && _installed(r.ModelId))
        .OrderByDescending(r => r.Score).ThenBy(r => r.TypicalSeconds).FirstOrDefault();
    public bool HasLocalComparison => _settings().SpeechPerformance is { LocalComparisonCompleted: true } p &&
        SpeechModelAdvisor.Current(p, _hardware(), _settings().LocalGpuEnabled) && TopLocal is not null;
    public string Winner => Results.FirstOrDefault(r => r.IsWinner) is { } winner ? $"Winner: {winner.Name} · {winner.DeviceDisplay} · {winner.ScoreDisplay}/100" : "Run Compare Models to find the best result on this sample.";
    public string ComparisonAvailability => !HasLocalComparison ? "Run Compare Models first. Groq will then be compared only with the highest-scoring installed local result." : _hasGroq() ? "Groq comparison is available. It sends only the included public sample, using four test requests and your saved key. API quota may apply." : "Save a Groq key to measure this connection. Hardware specs cannot determine Groq latency; local-only tests work without an account.";
    public string MeasurementDate => _settings().SpeechPerformance is { } p ?
        $"Measured {p.MeasuredAt.LocalDateTime:g}. " + (SpeechModelAdvisor.Current(p, _hardware(), _settings().LocalGpuEnabled) ? "Hardware and GPU preference match this test. Groq network timings expire for recommendations after 24 hours." : "Hardware, GPU preference or test format changed; these results are stale. Retest before using them.") : "No timings measured yet.";
    public void Load()
    {
        Display(_settings().SpeechPerformance);
        Refresh();
    }
    private void Display(SpeechPerformanceReport? report)
    {
        Results.Clear();
        if (report is null) { OnPropertyChanged(nameof(Winner)); return; }
        IEnumerable<SpeechPerformanceResult> rows = report.Results;
        if (report.ShowGroqComparison && TopLocal is { } local)
            rows = new[] { local }.Concat(report.Results.Where(r => r.Provider == "groq"));
        var sorted = rows.OrderByDescending(r => r.Score ?? -1).ThenBy(r => r.Provider == "groq" ? 1 : 0).ThenBy(r => r.TypicalSeconds).ToArray();
        for (var index = 0; index < sorted.Length; index++) Results.Add(sorted[index] with { IsWinner = index == 0 && sorted[index].Score.HasValue && report.LocalComparisonCompleted });
        OnPropertyChanged(nameof(Winner));
    }
    public void Refresh()
    {
        foreach (var property in new[] { nameof(Recommendation), nameof(HardwareSuggestion), nameof(SuggestedModel), nameof(ApplyLabel), nameof(ComparisonAvailability), nameof(MeasurementDate), nameof(HasLocalComparison), nameof(Winner) }) OnPropertyChanged(property);
        TestLocalCommand?.RaiseCanExecuteChanged(); CompareGroqCommand?.RaiseCanExecuteChanged(); CancelCommand?.RaiseCanExecuteChanged(); ApplyCommand?.RaiseCanExecuteChanged();
    }
    public async Task RunAsync(bool includeGroq)
    {
        if (IsBusy || !_canConfigure()) return;
        if (includeGroq && !HasLocalComparison) { Status = "Run Compare Models before comparing the winner with Groq."; return; }
        if (includeGroq && !_hasGroq()) { Status = "Save a Groq key before comparing providers."; return; }
        var settings = _settings(); var hardware = _hardware();
        var candidates = (includeGroq ? Array.Empty<string>() : LocalModelStore.Catalog.Select(model => model.Id).Where(_installed)
            .OrderByDescending(id => id == settings.LocalModelId).ToArray());
        if (candidates.Length == 0 && !includeGroq) { Status = "Install a local model before testing."; return; }
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation; IsExpanded = true; IsBusy = true;
        var measuring = false; var reserved = false; var finished = false; var report = new SpeechPerformanceReport { MeasuredAt = DateTimeOffset.UtcNow, HardwareFingerprint = hardware.Fingerprint, GpuEnabled = settings.LocalGpuEnabled };
        if (includeGroq && settings.SpeechPerformance is { } localProfile)
        {
            report.Results.AddRange(localProfile.Results.Where(r => r.Provider == "local"));
            report.MeasuredAt = localProfile.MeasuredAt; report.LocalComparisonCompleted = true; report.ShowGroqComparison = true;
        }
        try
        {
            await _reserve(true); reserved = true;
            Results.Clear(); if (includeGroq && TopLocal is { } best) Results.Add(best); measuring = true; Status = includeGroq ? "Comparing the highest-scoring local result with Groq using the included sample…" : $"Testing {candidates.Length} installed local model(s) with the included English sample. No microphone recording is needed. Dictation resumes after testing.";
            await _benchmark.RunAsync(_sample, candidates, includeGroq ? settings.ModelId : null,
                result => { report.Results.Add(result); Results.Add(result); },
                new Progress<string>(message => { if (measuring && IsBusy && !cancellation.IsCancellationRequested) Status = message; }), cancellation.Token, settings.LocalGpuEnabled && hardware.Gpu is not null);
            finished = true; report.LocalComparisonCompleted = includeGroq || report.Results.Any(r => r.Provider == "local" && r.Score.HasValue);
            if (includeGroq) report.CloudMeasuredAt = DateTimeOffset.UtcNow;
            measuring = false; Status = "Tests complete. Recommendations balance measured response time and sample word errors; your own speech can perform differently.";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Status = "Test cancelled. Completed measurements are retained."; }
        catch (Exception ex) { Status = "Could not test: " + ex.Message; }
        finally
        {
            measuring = false;
            try
            {
                if (reserved && report.Results.Count > 0 && !(includeGroq && !finished && !report.Results.Any(r => r.Provider == "groq")))
                {
                    if (includeGroq && !finished) report.ShowGroqComparison = report.Results.Any(r => r.Provider == "groq");
                    settings.SpeechPerformance = report; await _save(); Display(report);
                }
                else if (reserved)
                {
                    Display(settings.SpeechPerformance);
                }
            }
            catch (Exception ex) { Status = "Measurements are visible, but could not be saved: " + ex.Message; }
            finally
            {
                try { if (reserved) await _reserve(false); }
                catch (Exception ex) { Status = "Could not resume listening: " + ex.Message; }
                _cancellation = null; IsBusy = false;
            }
        }
    }
    private async Task ApplyAsync()
    {
        if (IsBusy || !_canConfigure()) return;
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation;
        var choice = Choice; var reserved = false; IsBusy = true;
        try
        {
            if (choice.Provider == "groq" && !_hasGroq()) throw new InvalidOperationException("Save a Groq key before switching to Groq.");
            await _reserve(true); reserved = true;
            await _apply(choice, cancellation.Token); Refresh();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Status = "Recommendation cancelled. Your previous provider/model remains selected."; }
        catch (Exception ex) { Status = "Could not apply recommendation: " + ex.Message; }
        finally
        {
            try { if (reserved) await _reserve(false); }
            catch (Exception ex) { Status = "Could not resume listening: " + ex.Message; }
            _cancellation = null; IsBusy = false;
        }
    }
    public async Task ShutdownAsync() { _cancellation?.Cancel(); if (_active is { } task) await task; }
}
