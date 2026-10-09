using System.Collections.ObjectModel;
using System.Diagnostics;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.App.ViewModels;

public sealed class VoiceSetupViewModel : ViewModelBase
{
    private readonly IAudioCaptureService _capture;
    private readonly Func<AppSettings> _settings;
    private readonly Func<string, TranscriptionOptions, CancellationToken, Task<SpeechResult>> _transcribe;
    private readonly Func<bool, Task> _reserve;
    private readonly Func<Task> _save;
    private readonly Func<string, bool> _installed;
    private readonly SynchronizationContext? _context;
    private CancellationTokenSource? _cancel, _limit;
    private Task? _activeTest;
    private bool _recording, _busy, _reserved, _expanded = false;
    private int _passage;
    private string _reference = VoicePersonalization.Passages[0], _vocabulary = "", _language = "en";
    private string _status = "Optional: test your recognition with three short readings. You can use local dictation without a profile.";
    private string _baseline = "", _personalized = "", _recordedReference = "", _recordedModel = "", _recordedVocabulary = "", _recordedLanguage = "en";
    private float _level;
    public VoiceSetupViewModel(IAudioCaptureService capture, Func<AppSettings> settings,
        Func<string, TranscriptionOptions, CancellationToken, Task<SpeechResult>> transcribe,
        Func<string, bool> installed, Func<bool, Task> reserve, Func<Task> save)
    {
        _capture = capture; _settings = settings; _transcribe = transcribe; _installed = installed; _reserve = reserve; _save = save;
        _context = SynchronizationContext.Current;
        capture.LevelAvailable += OnLevel;
        RecordCommand = new(StartAsync, () => CanEdit && _passage < 3 && _installed(_settings().LocalModelId));
        StopCommand = new(() => _activeTest = StopAndTestAsync(), () => IsRecording && !IsBusy);
        CancelCommand = new(CancelAsync, () => IsRecording || IsBusy);
        SaveCommand = new(SaveAsync, () => CanEdit && Results.Count == 3);
        ResetCommand = new(ResetAsync, () => CanEdit);
        DeleteCommand = new(DeleteAsync, () => CanEdit && _settings().LocalVoiceProfile is not null);
    }
    public AsyncRelayCommand RecordCommand { get; }
    public AsyncRelayCommand StopCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }
    public AsyncRelayCommand SaveCommand { get; }
    public AsyncRelayCommand ResetCommand { get; }
    public AsyncRelayCommand DeleteCommand { get; }
    public ObservableCollection<VoiceTestScore> Results { get; } = [];
    public bool IsRecording { get => _recording; private set { SetProperty(ref _recording, value); Refresh(); } }
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); Refresh(); } }
    public bool CanEdit => !IsRecording && !IsBusy;
    public bool IsExpanded { get => _expanded; set => SetProperty(ref _expanded, value); }
    public string Reference { get => _reference; set => SetProperty(ref _reference, value); }
    public string Vocabulary { get => _vocabulary; set => SetProperty(ref _vocabulary, value); }
    public string Language { get => _language; set => SetProperty(ref _language, value); }
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Baseline { get => _baseline; private set => SetProperty(ref _baseline, value); }
    public string Personalized { get => _personalized; private set => SetProperty(ref _personalized, value); }
    public float Level { get => _level; private set => SetProperty(ref _level, value); }
    public string Step => _passage < 3 ? $"Passage {_passage + 1} of 3 · speak naturally, then select Stop and test" : "Three passages tested · review the results and save";
    public string Summary => Results.Count == 0 ? "No test results yet." : string.Join(Environment.NewLine, Results.Select(r =>
        $"{r.ModelId}, passage {r.Passage}: baseline {100d * r.BaselineErrors / r.ReferenceWords:0.#}% word error → with hints {100d * r.PersonalizedErrors / r.ReferenceWords:0.#}%. {r.AudioSeconds:0.0}s audio; {r.BaselineSeconds:0.0}s / {r.PersonalizedSeconds:0.0}s processing."));
    public string SavedSummary => _settings().LocalVoiceProfile is { } p
        ? $"Profile saved {p.CompletedAt.LocalDateTime:g} · tested with {p.ModelId} · {p.Language}. Test word error without / with hints: {100d * p.Scores.Sum(s => s.BaselineErrors) / Math.Max(1, p.Scores.Sum(s => s.ReferenceWords)):0.#}% / {100d * p.Scores.Sum(s => s.PersonalizedErrors) / Math.Max(1, p.Scores.Sum(s => s.ReferenceWords)):0.#}%. Vocabulary hints apply to Whisper and Faster-Whisper; pace settings apply to SmartListen. Model weights are unchanged."
        : "No saved profile. Local dictation works without voice setup.";
    public void Load()
    {
        if (_settings().LocalVoiceProfile is { } p) { Vocabulary = p.Vocabulary; Language = p.Language; IsExpanded = false; }
        Refresh();
    }
    public void Refresh()
    {
        foreach (var name in new[] { nameof(CanEdit), nameof(Step), nameof(Summary), nameof(SavedSummary) }) OnPropertyChanged(name);
        RecordCommand?.RaiseCanExecuteChanged(); StopCommand?.RaiseCanExecuteChanged(); CancelCommand?.RaiseCanExecuteChanged();
        SaveCommand?.RaiseCanExecuteChanged(); ResetCommand?.RaiseCanExecuteChanged(); DeleteCommand?.RaiseCanExecuteChanged();
    }
    private void OnLevel(object? sender, float value)
    {
        if (!IsRecording) return;
        if (_context is null) Level = value; else _context.Post(_ => { if (IsRecording) Level = value; }, null);
    }
    public async Task StartAsync()
    {
        if (!CanEdit || _passage >= 3) return;
        if (!_installed(_settings().LocalModelId)) { Status = "Download the selected local model first."; return; }
        if (LocalModelStore.Catalog.FirstOrDefault(m => m.Id == _settings().LocalModelId)?.EnglishOnly == true && !string.IsNullOrWhiteSpace(Language) && Language.Trim().ToLowerInvariant() is not ("en" or "auto"))
        { Status = "This model recognises English only. Use en, or select a multilingual model for another language."; return; }
        if (VoicePersonalization.Words(Reference).Length < 8 || Reference.Length > 600) { Status = "Use a passage of at least eight words and no more than 600 characters."; return; }
        if (Results.Count > 0 && (_settings().LocalModelId != _recordedModel || Vocabulary.Trim() != _recordedVocabulary || (string.IsNullOrWhiteSpace(Language) ? "auto" : Language.Trim().ToLowerInvariant()) != _recordedLanguage))
        { Status = "Model, vocabulary or language changed. Select Restart tests to begin a consistent comparison."; return; }
        _cancel?.Dispose(); _cancel = new();
        IsBusy = true;
        try
        {
            await _reserve(true); _reserved = true;
            _recordedReference = Reference; _recordedModel = _settings().LocalModelId;
            _recordedVocabulary = Vocabulary.Trim(); _recordedLanguage = Language.Trim().ToLowerInvariant();
            if (_recordedLanguage.Length == 0) _recordedLanguage = "auto";
            _cancel.Token.ThrowIfCancellationRequested();
            await _capture.StartRecordingAsync(_settings().MicrophoneDeviceId, _cancel.Token);
            IsRecording = true; Status = "Recording locally… Read the passage above. Recording stops automatically after 45 seconds.";
            _limit = new(); _ = StopAtLimitAsync(_limit.Token);
        }
        catch (Exception ex) { Status = "Could not start setup recording: " + ex.Message; await ReleaseAsync(); }
        finally { IsBusy = false; }
    }
    private async Task StopAtLimitAsync(CancellationToken token)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(45), token); if (IsRecording && !IsBusy) { _activeTest = StopAndTestAsync(); await _activeTest; } }
        catch (OperationCanceledException) { }
    }
    public async Task StopAndTestAsync()
    {
        if (!IsRecording || IsBusy) return;
        IsBusy = true; _limit?.Cancel(); string? path = null;
        try
        {
            var audio = await _capture.StopRecordingAsync(CancellationToken.None); path = audio.FilePath;
            IsRecording = false; Level = 0;
            if (audio.LooksSilent) throw new InvalidOperationException("Very little speech was detected. Check your microphone and repeat this passage.");
            var token = _cancel!.Token;
            Status = "Testing locally without personal hints… Model loading can take time.";
            var options = new TranscriptionOptions("", _recordedLanguage, _settings().CustomPrompt, UseLocal: true, LocalModelId: _recordedModel, ApplyPersonalization: false);
            var clock = Stopwatch.StartNew(); var baseline = await _transcribe(path, options, token); var baselineSeconds = clock.Elapsed.TotalSeconds;
            token.ThrowIfCancellationRequested(); Baseline = baseline.Text;
            Status = LocalModelStore.Catalog.Any(m => m.Id == _recordedModel && !m.SupportsVocabulary) ? "Testing recognition and speaking pace. This engine does not use vocabulary hints." : "Testing the same recording with your vocabulary hints…";
            clock.Restart(); var personalized = LocalModelStore.Catalog.Any(m => m.Id == _recordedModel && !m.SupportsVocabulary) ? baseline : await _transcribe(path, options with { Prompt = VoicePersonalization.Prompt(options.Prompt, _recordedVocabulary) }, token);
            var personalizedSeconds = LocalModelStore.Catalog.Any(m => m.Id == _recordedModel && !m.SupportsVocabulary) ? baselineSeconds : clock.Elapsed.TotalSeconds;
            token.ThrowIfCancellationRequested(); Personalized = personalized.Text;
            if (string.IsNullOrWhiteSpace(baseline.Text) || string.IsNullOrWhiteSpace(personalized.Text)) throw new InvalidOperationException("No usable transcript. Repeat the passage or try a different model.");
            var words = VoicePersonalization.Words(_recordedReference).Length;
            var baselineErrors = VoicePersonalization.Errors(_recordedReference, baseline.Text);
            var personalizedErrors = VoicePersonalization.Errors(_recordedReference, personalized.Text);
            if (Math.Min(baselineErrors, personalizedErrors) > words / 2) throw new InvalidOperationException("The reading differed substantially from the passage. Check the language, read the displayed words, or restart tests with a larger model. No result was accepted.");
            Results.Add(new(_recordedModel, _passage + 1, words, baselineErrors, personalizedErrors, audio.Duration.TotalSeconds, baselineSeconds, personalizedSeconds));
            _passage++;
            if (_passage < 3) Reference = VoicePersonalization.Passages[_passage];
            Status = _passage == 3 ? "Tests complete. Lower word error is better. Save to apply your vocabulary, language and suggested SmartListen pause timing." : "Passage tested. Read the next passage in your usual voice.";
        }
        catch (OperationCanceledException) { Status = "Test cancelled. Repeat this passage when ready."; }
        catch (Exception ex) { Status = "Test could not finish: " + ex.Message; }
        finally
        {
            try { if (path is not null) await _capture.DeleteRecordingAsync(path, CancellationToken.None); }
            catch (Exception ex) { Status = "Could not remove temporary setup audio: " + ex.Message; }
            finally { IsRecording = _capture.IsRecording; if (!IsRecording) await ReleaseAsync(); IsBusy = false; Refresh(); }
        }
    }
    private async Task ReleaseAsync()
    {
        if (!_reserved) return;
        _reserved = false;
        try { await _reserve(false); }
        catch (Exception ex) { Status = "Setup ended, but microphone monitoring could not restart: " + ex.Message; }
    }
    public async Task CancelAsync()
    {
        _cancel?.Cancel(); _limit?.Cancel();
        if (_activeTest is { IsCompleted: false }) { await _activeTest; return; }
        if (IsRecording)
        {
            IsBusy = true;
            try { var audio = await _capture.StopRecordingAsync(CancellationToken.None); await _capture.DeleteRecordingAsync(audio.FilePath, CancellationToken.None); }
            catch (Exception ex) { Status = "Could not finish cancelling setup: " + ex.Message; return; }
            finally { IsRecording = _capture.IsRecording; IsBusy = false; Level = 0; if (!IsRecording) await ReleaseAsync(); }
        }
        Status = "Setup cancelled. No recording is kept.";
    }
    public async Task SaveAsync()
    {
        if (!CanEdit || Results.Count != 3) return;
        if (Results.Select(r => r.ModelId).Distinct().Count() != 1 || Results[0].ModelId != _settings().LocalModelId)
        { Status = "Use Restart tests after changing models, so all three readings measure the same model."; return; }
        if (Vocabulary.Trim() != _recordedVocabulary || (string.IsNullOrWhiteSpace(Language) ? "auto" : Language.Trim().ToLowerInvariant()) != _recordedLanguage)
        { Status = "Vocabulary or language changed after testing. Restart tests before saving these new settings."; return; }
        IsBusy = true;
        var settings = _settings(); var old = settings.LocalVoiceProfile; var oldSilence = settings.AutoCaptureSilenceMs; var oldEnabled = settings.UseLocalTranscription;
        try
        {
            // Keep hints only when they did not worsen this small sample.
            var keepHints = Results.Sum(r => r.PersonalizedErrors) <= Results.Sum(r => r.BaselineErrors);
            settings.LocalVoiceProfile = new() { CompletedAt = DateTimeOffset.UtcNow, Language = _recordedLanguage,
                Vocabulary = keepHints ? _recordedVocabulary : "", ModelId = _recordedModel,
                SuggestedSilenceMs = VoicePersonalization.SuggestedSilence(Results), Scores = Results.ToList() };
            settings.AutoCaptureSilenceMs = settings.LocalVoiceProfile.SuggestedSilenceMs;
            settings.UseLocalTranscription = true;
            await _save();
            Status = $"Profile saved; local transcription is enabled. SmartListen pause: {settings.AutoCaptureSilenceMs} ms (estimated from reading pace). " +
                (keepHints ? "Vocabulary hints saved." : "Vocabulary hints increased errors in these tests, so they were not applied.");
        }
        catch (Exception ex) { settings.LocalVoiceProfile = old; settings.AutoCaptureSilenceMs = oldSilence; settings.UseLocalTranscription = oldEnabled; Status = "Could not save profile: " + ex.Message; }
        finally { IsBusy = false; }
    }
    public Task ResetAsync()
    {
        if (!CanEdit) return Task.CompletedTask;
        Results.Clear(); _passage = 0; Reference = VoicePersonalization.Passages[0]; Baseline = Personalized = "";
        Status = "Read three passages using the selected model. Your previous saved profile stays active until you save a replacement."; Refresh();
        return Task.CompletedTask;
    }
    public async Task DeleteAsync()
    {
        if (!CanEdit) return;
        var old = _settings().LocalVoiceProfile;
        try { _settings().LocalVoiceProfile = null; await _save(); Status = "Personal vocabulary and test scores deleted. Current pause timing is unchanged; adjust it in Audio & Capture."; Refresh(); }
        catch (Exception ex) { _settings().LocalVoiceProfile = old; Status = "Could not delete profile: " + ex.Message; }
    }
    public async Task ShutdownAsync() { await CancelAsync(); _capture.LevelAvailable -= OnLevel; _cancel?.Dispose(); _limit?.Dispose(); }
}
