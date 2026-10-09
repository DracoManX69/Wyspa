using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.App.ViewModels;

public sealed class LocalModelsViewModel : ViewModelBase
{
    private readonly LocalModelStore _store;
    private readonly LocalTranscriptionClient _client;
    private readonly Func<AppSettings> _settings;
    private readonly Func<Task> _save;
    private CancellationTokenSource? _cancellation;
    private Task? _active;
    private bool _busy;
    private string _status = "Install models here, then choose Use to select one. Voice setup is optional.";
    private double _progress;
    public LocalModelsViewModel(LocalModelStore store, LocalTranscriptionClient client, Func<AppSettings> settings, Func<Task> save)
    {
        var context = SynchronizationContext.Current;
        client.RuntimeChanged += (_, _) => { if (context is null) OnPropertyChanged(nameof(RuntimeStatus)); else context.Post(_ => OnPropertyChanged(nameof(RuntimeStatus)), null); };
        _store = store; _client = client; _settings = settings; _save = save;
        Items = Models.Select(model => new LocalModelItemViewModel(this, model, () => store.IsInstalled(model.Id))).ToArray();
        DownloadCommand = new AsyncRelayCommand(() => StartBatch([SelectedItem], false), () => CanConfigure && !Installed);
        RemoveCommand = new AsyncRelayCommand(() => StartBatch([SelectedItem], true), () => CanConfigure && Installed);
        InstallSelectedCommand = new AsyncRelayCommand(() => StartBatch(Items.Where(row => row.IsSelected && !row.Installed).ToArray(), false), () => CanConfigure && Items.Any(row => row.IsSelected && !row.Installed));
        RemoveSelectedCommand = new AsyncRelayCommand(() => StartBatch(Items.Where(row => row.IsSelected && row.Installed).ToArray(), true), () => CanConfigure && Items.Any(row => row.IsSelected && row.Installed));
        CancelCommand = new AsyncRelayCommand(() => { _cancellation?.Cancel(); return Task.CompletedTask; }, () => IsBusy && _cancellation is not null);
    }
    public VoiceSetupViewModel? VoiceSetup { get; set; }
    public LocalRecommendationsViewModel? Recommendations { get; set; }
    // Removal reserves audio through the same app flow as model testing, so live contexts cannot be deleted during capture.
    public Func<bool, Task> ReserveRemovalAsync { get; set; } = _ => Task.CompletedTask;
    public IReadOnlyList<LocalModelItemViewModel> Items { get; }
    public IReadOnlyList<LocalModel> Models => _store.Models;
    private LocalModelItemViewModel SelectedItem => Items.First(row => row.Id == SelectedModelId);
    public string ModelDetails => Models.FirstOrDefault(m => m.Id == SelectedModelId)?.Details ?? "Choose an installed model.";
    public string ModelFile => Models.FirstOrDefault(m => m.Id == SelectedModelId)?.FileName ?? "";
    public string SourceUrl => Models.FirstOrDefault(m => m.Id == SelectedModelId)?.SourceUrl ?? "https://huggingface.co/ggerganov/whisper.cpp";
    public string SelectedModelId { get => _settings().LocalModelId; set { if (Models.Any(m => m.Id == value)) { _settings().LocalModelId = value; Refresh(); } } }
    public bool Enabled { get => _settings().UseLocalTranscription; set
        {
            if (!CanEnable) { OnPropertyChanged(); return; }
            _settings().UseLocalTranscription = value;
            if (value && !Installed) Status = "Install a model to start local dictation. Voice setup is optional.";
            Refresh();
        } }
    public bool GpuEnabled { get => _settings().LocalGpuEnabled; set { if (CanUseGpu) _settings().LocalGpuEnabled = value; OnPropertyChanged(); } }
    public bool GpuAvailable => !string.IsNullOrWhiteSpace(_client.Hardware.Gpu);
    public bool CanUseGpu => GpuAvailable && CanConfigure;
    public string HardwareDetails => _client.Hardware.Description;
    public string RuntimeStatus => _client.RuntimeStatus;
    public bool CanEnable => !IsBusy && VoiceSetup?.CanEdit != false && Recommendations?.IsBusy != true;
    public bool Installed => _store.IsInstalled(SelectedModelId);
    public string Availability => Installed ? "Active model: " + Models.First(m => m.Id == SelectedModelId).Name : "Install a model and choose Use before starting local dictation.";
    public string StoragePath => _store.DirectoryPath;
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); Refresh(); } }
    public bool CanConfigure => Enabled && CanEnable;
    public bool CanTrain => Enabled && !IsBusy && Recommendations?.IsBusy != true;
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string SelectionSummary => $"{Items.Count(row => row.IsSelected && !row.Installed)} to install · {Items.Count(row => row.IsSelected && row.Installed)} to remove";
    public AsyncRelayCommand DownloadCommand { get; }
    public AsyncRelayCommand RemoveCommand { get; }
    public AsyncRelayCommand InstallSelectedCommand { get; }
    public AsyncRelayCommand RemoveSelectedCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }
    public void Refresh()
    {
        foreach (var name in new[] { nameof(Enabled), nameof(SelectedModelId), nameof(Installed), nameof(Availability), nameof(CanConfigure), nameof(CanTrain), nameof(CanEnable), nameof(GpuEnabled), nameof(GpuAvailable), nameof(CanUseGpu), nameof(HardwareDetails), nameof(RuntimeStatus), nameof(ModelDetails), nameof(ModelFile), nameof(SourceUrl), nameof(SelectionSummary) }) OnPropertyChanged(name);
        foreach (var row in Items) row.Refresh();
        VoiceSetup?.Refresh(); Recommendations?.Refresh();
        DownloadCommand?.RaiseCanExecuteChanged(); RemoveCommand?.RaiseCanExecuteChanged(); InstallSelectedCommand?.RaiseCanExecuteChanged(); RemoveSelectedCommand?.RaiseCanExecuteChanged(); CancelCommand?.RaiseCanExecuteChanged();
    }
    public async Task InitializeAsync(string bundledDirectory)
    {
        if (!_settings().LocalDefaultsInitialized)
        {
            IsBusy = true; Status = "Preparing the included lightweight speech model…";
            try
            {
                await _store.InstallBundledAsync(bundledDirectory, CancellationToken.None);
                _settings().LocalDefaultsInitialized = true; await _save();
                Status = "Lightweight English dictation is installed. Voice setup is optional.";
            }
            catch (Exception ex) { Status = "Could not prepare the included model. Use Install to retry. " + ex.Message; }
            finally { IsBusy = false; }
        }
        if (Enabled && Installed)
        {
            try { await _client.PrepareAsync(SelectedModelId, CancellationToken.None); }
            catch (Exception ex) { Status = "Local engine could not start: " + ex.Message; }
        }
        Refresh();
    }
    public async Task ApplyRecommendationAsync(SpeechRecommendation recommendation, CancellationToken token = default)
    {
        if (IsBusy || VoiceSetup?.CanEdit == false) return;
        var settings = _settings(); var oldLocal = settings.LocalModelId; var oldCloud = settings.ModelId; var oldEnabled = settings.UseLocalTranscription;
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(token); _cancellation = cancel; IsBusy = true; Progress = 0;
        var row = Items.FirstOrDefault(item => item.Id == recommendation.ModelId);
        try
        {
            if (recommendation.Provider == "local")
            {
                if (!_store.IsInstalled(recommendation.ModelId))
                {
                    Status = "Downloading recommended model: " + _store.Find(recommendation.ModelId).Name;
                    if (row is not null) { row.IsWorking = true; row.Progress = 0; row.OperationStatus = "Downloading"; }
                    await _store.DownloadAsync(recommendation.ModelId, new Progress<double>(p => { if (row?.IsWorking == true && ReferenceEquals(_cancellation, cancel) && !cancel.IsCancellationRequested) row.Progress = p; Progress = p; }), cancel.Token);
                }
                if (row is not null) row.OperationStatus = "Loading engine…";
                await _client.PrepareAsync(recommendation.ModelId, cancel.Token);
                cancel.Token.ThrowIfCancellationRequested(); settings.LocalModelId = recommendation.ModelId; settings.UseLocalTranscription = true;
            }
            else { cancel.Token.ThrowIfCancellationRequested(); settings.ModelId = recommendation.ModelId; settings.UseLocalTranscription = false; }
            await _save(); Status = "Recommendation applied. You can change the model or provider whenever you prefer.";
        }
        catch { settings.LocalModelId = oldLocal; settings.ModelId = oldCloud; settings.UseLocalTranscription = oldEnabled; throw; }
        finally { if (row is not null) { row.IsWorking = false; row.OperationStatus = ""; } _cancellation = null; IsBusy = false; }
    }
    internal Task ManageAsync(LocalModelItemViewModel row) => StartBatch([row], row.Installed);
    internal async Task UseAsync(LocalModelItemViewModel row)
    {
        if (!CanConfigure || !row.Installed) return;
        SelectedModelId = row.Id;
        try { await _save(); Status = "Using " + row.Name + "."; }
        catch (Exception ex) { Status = "Could not save model choice: " + ex.Message; }
    }
    private Task StartBatch(IReadOnlyList<LocalModelItemViewModel> rows, bool remove)
    {
        if (!CanConfigure || rows.Count == 0) return Task.CompletedTask;
        return _active = RunBatchAsync(rows, remove);
    }
    private async Task RunBatchAsync(IReadOnlyList<LocalModelItemViewModel> rows, bool remove)
    {
        using var cancellation = new CancellationTokenSource(); _cancellation = cancellation; IsBusy = true;
        var reserved = false; var succeeded = 0; var failed = 0;
        foreach (var row in rows) { row.OperationStatus = "Queued"; row.Progress = 0; }
        Status = remove ? $"Removing {rows.Count} model(s)…" : $"Installing {rows.Count} model(s)… Two downloads can run together.";
        try
        {
            if (remove) { await ReserveRemovalAsync(true); reserved = true; }
            using var slots = new SemaphoreSlim(remove ? 1 : 2);
            await Task.WhenAll(rows.Select(async row =>
            {
                var acquired = false;
                try
                {
                    await slots.WaitAsync(cancellation.Token); acquired = true;
                    row.IsWorking = true; row.OperationStatus = remove ? "Removing…" : "Downloading";
                    if (remove) await _client.RemoveAsync(row.Id, cancellation.Token);
                    else await _store.DownloadAsync(row.Id, new Progress<double>(p => { if (row.IsWorking && ReferenceEquals(_cancellation, cancellation) && !cancellation.IsCancellationRequested) row.Progress = p; }), cancellation.Token);
                    row.Progress = remove ? 0 : 100; row.OperationStatus = ""; row.IsSelected = false; Interlocked.Increment(ref succeeded);
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { row.OperationStatus = "Cancelled · retry when ready"; }
                catch (Exception ex) { Interlocked.Increment(ref failed); row.OperationStatus = "Failed: " + ex.Message; }
                finally { row.IsWorking = false; row.Refresh(); if (acquired) slots.Release(); }
            }));
            // Keep local mode selected if no models remain; never switch to cloud implicitly.
            if (!Installed && Items.FirstOrDefault(row => row.Installed) is { } replacement) _settings().LocalModelId = replacement.Id;
            await _save();
            Status = $"{succeeded} model(s) {(remove ? "removed" : "installed")}" + (failed > 0 ? $" · {failed} failed; see each row and retry." : cancellation.IsCancellationRequested ? " · batch cancelled. Completed changes are kept." : ".");
        }
        catch (Exception ex) { Status = "Could not manage models: " + ex.Message; }
        finally
        {
            foreach (var row in rows) if (row.OperationStatus == "Queued") row.OperationStatus = "Not started";
            try { if (reserved) await ReserveRemovalAsync(false); }
            catch (Exception ex) { Status = "Could not resume listening: " + ex.Message; }
            _cancellation = null; IsBusy = false;
        }
    }
    public async Task ShutdownAsync() { _cancellation?.Cancel(); if (_active is { } task) await task; }
}

public sealed class LocalModelItemViewModel : ViewModelBase
{
    private readonly LocalModelsViewModel _owner;
    private readonly Func<bool> _installed;
    private bool _selected, _working;
    private double _progress;
    private string _operationStatus = "";
    internal LocalModelItemViewModel(LocalModelsViewModel owner, LocalModel model, Func<bool> installed)
    {
        _owner = owner; Model = model; _installed = installed;
        ManageCommand = new AsyncRelayCommand(() => owner.ManageAsync(this), () => owner.CanConfigure);
        UseCommand = new AsyncRelayCommand(() => owner.UseAsync(this), () => owner.CanConfigure && Installed && !IsActive);
    }
    public LocalModel Model { get; }
    public string Id => Model.Id;
    public string Name => Model.Name;
    public string Size => Model.Size;
    public string SourceUrl => Model.SourceUrl;
    public string InfoUrl => Model.InfoUrl;
    public string Details => Model.Details;
    public bool Installed => _installed();
    public bool IsActive => _owner.SelectedModelId == Id;
    public string UseLabel => IsActive ? "Active" : "Use";
    public string ActionLabel => Installed ? "Remove" : "Install";
    public bool CanSelect => _owner.CanConfigure;
    public bool IsSelected { get => _selected; set { if (SetProperty(ref _selected, value)) _owner.Refresh(); } }
    public bool IsWorking { get => _working; internal set { SetProperty(ref _working, value); OnPropertyChanged(nameof(State)); } }
    public double Progress { get => _progress; internal set { SetProperty(ref _progress, value); OnPropertyChanged(nameof(State)); } }
    public string OperationStatus { get => _operationStatus; internal set { SetProperty(ref _operationStatus, value); OnPropertyChanged(nameof(State)); } }
    public string State => IsWorking && OperationStatus == "Downloading" ? $"Downloading · {Progress:0}%" : OperationStatus.Length > 0 ? OperationStatus : Installed ? "Installed" : "Not installed";
    public AsyncRelayCommand ManageCommand { get; }
    public AsyncRelayCommand UseCommand { get; }
    internal void Refresh()
    {
        foreach (var property in new[] { nameof(Installed), nameof(IsActive), nameof(UseLabel), nameof(ActionLabel), nameof(State), nameof(CanSelect) }) OnPropertyChanged(property);
        ManageCommand.RaiseCanExecuteChanged(); UseCommand.RaiseCanExecuteChanged();
    }
}
