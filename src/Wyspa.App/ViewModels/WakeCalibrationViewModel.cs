using System.Collections.ObjectModel;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.App.ViewModels;

public sealed class WakeCalibrationViewModel : ViewModelBase
{
    private readonly IAudioLevelMonitorService _monitor;
    private readonly IWakePhraseDetector _detector;
    private readonly Func<AppSettings> _settings;
    private readonly Func<bool, Task> _reserve;
    private readonly Func<Task> _save;
    private readonly Func<bool> _canTranscribe;
    private readonly SynchronizationContext? _context = SynchronizationContext.Current;
    private readonly List<float> _samples = [];
    private CancellationTokenSource? _cancel;
    private Task? _active;
    private TaskCompletionSource? _stop;
    private bool _busy, _recording, _awake;
    private readonly List<WakeSetupReading> _readings = [];
    private WakeVoiceProfile? _candidate;
    private bool _complete;
    private bool _wizardOpen;
    private string _wizardOpenError = "";
    public string WizardOpenError { get => _wizardOpenError; private set => SetProperty(ref _wizardOpenError, value); }
    private Task? _wizardEndTask;
    private int _suiteVariant = Random.Shared.Next(WakeSetupSuite.VariationCount);
    private WakeSampleEndpoint? _sampleEndpoint;
    public bool IsWizardOpen => _wizardOpen;
    private string? _setupMic;
    private string _feedback = "Use your usual microphone and position. Setup stays on this computer.";
    public IReadOnlyList<WakeSetupStep> SetupSteps { get { var variation = _suiteVariant + _readings.Count(r => r.StepId.StartsWith("tune-", StringComparison.Ordinal)); try { return WakeSetupSuite.Create(Phrase, variation); } catch (ArgumentException) { return WakeSetupSuite.Create(_settings().AutoCaptureWakePhrase, variation); } } }
    public int SetupIndex => _readings.Count(r => SetupSteps.Any(s => s.Id == r.StepId));
    public string WizardLabel => _complete ? "Review wake setup" : SetupIndex > 0 ? "Resume wake setup" : "Set up wake detection";
    public string LearnedPronunciations => string.Join(" · ", new[] { Phrase }.Concat(_settings().AutoCaptureWakeVoiceProfile?.PronunciationVariants ?? []));
    public string TuningSummary => _complete ? $"Wyspa applied your microphone profile and {(_settings().AutoCaptureWakeVoiceSensitivity):P0} matching strictness. Fresh wake and non-wake checks passed." : "Wyspa adjusts matching from your examples. Speak naturally; you do not need to match a particular accent or pronunciation.";
    public string SetupTitle => _complete ? "Your wake profile is ready" : SetupSteps[Math.Min(SetupIndex, SetupSteps.Count - 1)].Title;
    public string SetupHint => _complete ? "Say the phrase, wait for the tone, then dictate. Add a fresh setup if you change your microphone or room." : SetupSteps[Math.Min(SetupIndex, SetupSteps.Count - 1)].Hint;
    public string SampleFeedback { get => _feedback; private set => SetProperty(ref _feedback, value); }
    public double SetupProgress => _complete ? 100 : 100d * SetupIndex / SetupSteps.Count;
    public bool SetupComplete => _complete;
    private string _phrase = "hey whisper", _status = "Enter a wake phrase, then practice. Wyspa guides the readings and adjusts detection automatically.", _runtime = "Wake detection is off.";
    private float _level;
    public WakeCalibrationViewModel(IAudioLevelMonitorService monitor, IWakePhraseDetector detector, Func<AppSettings> settings,
        Func<bool, Task> reserve, Func<Task> save, Func<bool> canTranscribe)
    {
        _monitor = monitor; _detector = detector; _settings = settings; _reserve = reserve; _save = save; _canTranscribe = canTranscribe;
        _monitor.AudioAvailable += Audio; _monitor.LevelAvailable += LevelChanged;
        PracticeCommand = new AsyncRelayCommand(() => _active = PracticeAsync(), () => CanEdit);
        SetPhraseCommand = new AsyncRelayCommand(SetPhraseAsync, () => CanEdit);
        PositiveCommand = new AsyncRelayCommand(() => _active = RecordAsync(true), () => CanEdit);
        NegativeCommand = new AsyncRelayCommand(() => _active = RecordAsync(false), () => CanEdit);
        FinishCommand = new AsyncRelayCommand(() => { _stop?.TrySetResult(); return Task.CompletedTask; }, () => IsRecording);
        CancelCommand = new AsyncRelayCommand(() => { _cancel?.Cancel(); return Task.CompletedTask; }, () => IsWorking);
        ApplyCommand = new AsyncRelayCommand(ApplyAsync, () => CanEdit && Summary.Ready);
        ArmCommand = new AsyncRelayCommand(() => _active = ArmAsync(), () => CanEdit);
        DisarmCommand = new AsyncRelayCommand(DisarmAsync, () => CanEdit);
        ResetCommand = new AsyncRelayCommand(ResetAsync, () => CanEdit);
    }
    public ObservableCollection<WakeCalibrationTrial> Trials { get; } = [];
    public string Phrase { get => _phrase; set { if (SetProperty(ref _phrase, value)) { OnPropertyChanged(nameof(PhraseProblem)); OnPropertyChanged(nameof(CanStartWizard)); } } }
    public string PhraseProblem { get { try { WakePhraseCalibration.Normalize(Phrase); return ""; } catch (ArgumentException ex) { return ex.Message; } } }
    public bool CanStartWizard => CanEdit && PhraseProblem.Length == 0;
    public string UseInstructions => _canTranscribe() ? "Say your phrase naturally, wait for the ready tone, then dictate. Wyspa stops at your configured silence interval and waits for the next phrase." : "Your profile is saved. Install a local transcription model or connect Groq, then enable wake phrases in SmartListen.";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }
    public string Runtime { get => _runtime; private set => SetProperty(ref _runtime, value); }
    public float Level { get => _level; private set => SetProperty(ref _level, value); }
    public bool IsBusy { get => _busy; private set { SetProperty(ref _busy, value); Refresh(); } }
    public bool IsRecording { get => _recording; private set { SetProperty(ref _recording, value); Refresh(); } }
    public bool IsWorking => IsBusy || IsRecording;
    public bool IsSmartListen => _settings().ActivationMode == ActivationMode.AutoCapture;
    public bool CanEdit => !IsWorking && IsSmartListen;
    public async Task<bool> BeginWizardAsync()
    {
        if (_wizardOpen) return true;
        try
        {
            await _reserve(true); _wizardOpen = true; _wizardEndTask = null; WizardOpenError = "";
            if (_setupMic != _settings().MicrophoneDeviceId || Phrase.Trim().ToLowerInvariant() != _settings().AutoCaptureWakePhrase)
            { _readings.Clear(); _candidate = null; _complete = false; _setupMic = _settings().MicrophoneDeviceId; Refresh(); }
            OnPropertyChanged(nameof(IsWizardOpen)); return true;
        }
        catch (Exception ex) { WizardOpenError = Status = "Could not open wake setup: " + ex.Message; return false; }
    }
    private Task ReserveSampleAsync(bool reserved) => _wizardOpen ? Task.CompletedTask : _reserve(reserved);
    public Task EndWizardAsync() => _wizardEndTask ??= EndWizardCoreAsync();
    private async Task EndWizardCoreAsync()
    {
        await PauseSetupAsync();
        if (!_wizardOpen) return;
        try { await _reserve(false); }
        catch (Exception ex) { WizardOpenError = Status = "Could not resume listening: " + ex.Message; }
        finally { _wizardOpen = false; Load(); OnPropertyChanged(nameof(IsWizardOpen)); }
    }
    public bool Enabled { get => _settings().AutoCaptureWakeVoiceEnabled; set { if (CanEdit) _active = SetEnabledAsync(value); else OnPropertyChanged(); } }
    public bool IsAwake { get => _awake; private set => SetProperty(ref _awake, value); }
    public string PracticeLabel => _complete ? "Start a fresh setup" : "Record sample";
    public string PracticePrompt => _complete ? "“" + Phrase + "”" : SetupSteps[Math.Min(SetupIndex, SetupSteps.Count - 1)].Prompt;
    public string PracticeProgress => _complete ? "Validated with fresh wake and non-wake readings" : $"Step {SetupIndex + 1} of {SetupSteps.Count} · " + (SetupSteps[Math.Min(SetupIndex, SetupSteps.Count - 1)].IsValidation ? "Check your profile" : "Personalize detection");
    public AsyncRelayCommand PracticeCommand { get; }
    public WakeCalibrationSummary Summary => WakePhraseCalibration.Recommend(Trials);
    public string SummaryText => Summary.Description + " These are sample results, not a guarantee of 99.99% accuracy or acoustic model retraining.";
    public string LastSample { get; private set; } = "No samples tested yet.";
    public AsyncRelayCommand SetPhraseCommand { get; }
    public AsyncRelayCommand PositiveCommand { get; }
    public AsyncRelayCommand NegativeCommand { get; }
    public AsyncRelayCommand FinishCommand { get; }
    public AsyncRelayCommand CancelCommand { get; }
    public AsyncRelayCommand ApplyCommand { get; }
    public AsyncRelayCommand ArmCommand { get; }
    public AsyncRelayCommand DisarmCommand { get; }
    public AsyncRelayCommand ResetCommand { get; }
    public void Load()
    {
        Phrase = _settings().AutoCaptureWakePhrase; Trials.Clear();
        if (_settings().AutoCaptureWakeVoiceProfile is { DetectorVersion: 2 } p && p.Phrase == Phrase)
            foreach (var row in p.CalibrationTrials) Trials.Add(row);
        _readings.Clear(); _complete = false; _candidate = null; _setupMic = _settings().MicrophoneDeviceId;
        if (_settings().AutoCaptureWakeVoiceProfile is { EnrollmentVersion: 1 } enrolled && enrolled.Phrase == Phrase && enrolled.MicrophoneId == _settings().MicrophoneDeviceId)
        {
            _complete = enrolled.SetupValidated;
            if (!_complete && enrolled.SetupSuiteVersion == WakeSetupSuite.Version) _readings.AddRange(enrolled.SetupReadings.Take(40));
            else if (!_complete) SampleFeedback = "Setup now uses varied readings. Start the new suite; your phrase is kept.";
            _suiteVariant = enrolled.SetupPromptVariant;
            _candidate = WakeEnrollmentTrainer.Build(Phrase, _settings().MicrophoneDeviceId, _readings);
            if (_complete || enrolled.SetupSuiteVersion == WakeSetupSuite.Version) _candidate.TunedStrictness = enrolled.TunedStrictness;
        }
        Refresh();
    }
    public async Task InitializeAsync()
    {
        if (_settings().AutoCaptureWakeVoiceProfile is { DetectorVersion: < 2 } || (_settings().AutoCaptureWakeVoiceEnabled && _settings().AutoCaptureWakeVoiceProfile is null))
        {
            _settings().AutoCaptureWakeVoiceProfile = new() { DetectorVersion = 2, Phrase = _settings().AutoCaptureWakePhrase, TrainingSampleCount = 0 };
            _settings().AutoCaptureWakeVoiceSensitivity = .3;
            await _save();
            Status = "The older acoustic-match profile was replaced by phrase recognition. Add wake and ordinary-speech examples for this detector.";
        }
        Load();
    }
    public void ReportRuntime(string message) => Ui(() => { Runtime = message; IsAwake = message.StartsWith("Wake phrase accepted", StringComparison.Ordinal); });
    public void RefreshState() { if (!IsSmartListen && IsWorking) _cancel?.Cancel(); Refresh(); }
    private void Ui(Action action) { if (_context is null) action(); else _context.Post(_ => action(), null); }
    private void Refresh()
    {
        foreach (var p in new[] { nameof(CanEdit), nameof(CanStartWizard), nameof(UseInstructions), nameof(IsWorking), nameof(SummaryText), nameof(LastSample), nameof(Enabled), nameof(IsSmartListen), nameof(PracticePrompt), nameof(PracticeProgress), nameof(PracticeLabel), nameof(SetupTitle), nameof(SetupHint), nameof(SampleFeedback), nameof(SetupProgress), nameof(SetupComplete), nameof(WizardLabel), nameof(LearnedPronunciations), nameof(TuningSummary) }) OnPropertyChanged(p);
        foreach (var command in new[] { PracticeCommand, SetPhraseCommand, PositiveCommand, NegativeCommand, FinishCommand, CancelCommand, ApplyCommand, ArmCommand, DisarmCommand, ResetCommand }) command?.RaiseCanExecuteChanged();
    }
    private void Audio(object? sender, IReadOnlyList<float> samples)
    {
        if (!IsRecording) return;
        lock (_samples) { _samples.AddRange(samples); if (_samples.Count > 160000) _samples.RemoveRange(0, _samples.Count - 160000); }
        if (_sampleEndpoint?.Feed(samples) == true) _stop?.TrySetResult();
    }
    private void LevelChanged(object? sender, float level) { if (IsRecording) Ui(() => Level = level); }
    private async Task SetPhraseAsync()
    {
        try
        {
            var phrase = WakePhraseCalibration.Normalize(Phrase);
            if (_settings().AutoCaptureWakePhrase != phrase)
            {
                _settings().AutoCaptureWakePhrase = phrase;
                _settings().AutoCaptureWakeVoiceProfile = new() { DetectorVersion = 2, Phrase = phrase, TrainingSampleCount = 0 };
                _settings().AutoCaptureWakeVoiceSensitivity = .3;
                await _save(); _detector.Reset(); Load();
            }
            Status = "Phrase set to “" + phrase + "”. Record both kinds of examples to tune strictness.";
        }
        catch (Exception ex) { Status = "Could not set phrase: " + ex.Message; }
    }
    public async Task PracticeAsync()
    {
        if (!CanEdit) return;
        string normalized;
        try { normalized = WakePhraseCalibration.Normalize(Phrase); }
        catch (ArgumentException ex) { Status = ex.Message; return; }
        if (_setupMic != _settings().MicrophoneDeviceId) { _readings.Clear(); _candidate = null; _complete = false; _setupMic = _settings().MicrophoneDeviceId; SampleFeedback = "Microphone changed. Start this setup again with the new input."; }
        if (_complete) { _complete = false; _readings.Clear(); _candidate = null; _suiteVariant = (_suiteVariant + 1) % WakeSetupSuite.VariationCount; }
        await SetPhraseAsync(); if (_settings().AutoCaptureWakePhrase != normalized) return; Phrase = normalized;
        var step = SetupSteps[Math.Min(SetupIndex, SetupSteps.Count - 1)];
        using var cancellation = new CancellationTokenSource(); _cancel = cancellation; IsBusy = true; var reserved = false;
        try
        {
            await ReserveSampleAsync(true); reserved = true;
            await _detector.PrepareAsync(new Progress<string>(message => { if (IsWorking) Status = message; }), cancellation.Token);
            lock (_samples) _samples.Clear(); _stop = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _sampleEndpoint = step.IsRoom ? null : new WakeSampleEndpoint(_readings.FirstOrDefault(r => r.StepId == "room")?.Rms ?? .003, step.IsPhraseOnly);
            await _monitor.StartAsync(_settings().MicrophoneDeviceId, cancellation.Token);
            IsRecording = true; IsBusy = false; Status = "Recording · " + step.Prompt;
            await Task.WhenAny(Task.Delay(step.Seconds * 1000, cancellation.Token), _stop.Task);
            cancellation.Token.ThrowIfCancellationRequested(); IsBusy = true; IsRecording = false; _monitor.Stop(); Level = 0;
            Status = step.IsValidation ? "Checking your personal wake profile…" : "Learning this example…";
            float[] audio; lock (_samples) { audio = _samples.ToArray(); _samples.Clear(); }
            WakeSampleAnalysis analysis;
            if (step.IsRoom)
                analysis = new(null, "", Math.Sqrt(audio.Select(v => (double)v * v).DefaultIfEmpty().Average()), audio.Select(Math.Abs).DefaultIfEmpty().Max(), 0, []);
            else if (_detector is IWakeEnrollmentDetector learner) analysis = await learner.AnalyzeAsync(audio, Phrase, cancellation.Token);
            else
            {
                var score = await _detector.EvaluateAsync(audio, Phrase, cancellation.Token);
                analysis = new(score, score.HasValue ? Phrase : "everyday speech", Math.Sqrt(audio.Select(v => (double)v * v).DefaultIfEmpty().Average()), audio.Select(Math.Abs).DefaultIfEmpty().Max(), 0, WakeAcoustics.Extract(audio));
            }
            var problem = WakeSetupSuite.QualityProblem(step, analysis, audio.Length);
            if (problem is not null) { SampleFeedback = problem; Status = "Please retry this sample."; return; }
            if (step.IsWake && string.IsNullOrWhiteSpace(analysis.RecognizedText))
            { SampleFeedback = "Wyspa could not extract words from this recording. Check the input meter, then give it another natural example."; Status = "No pronunciation was available to learn."; return; }
            _candidate ??= WakeEnrollmentTrainer.Build(Phrase, _settings().MicrophoneDeviceId, _readings);
            var strictness = WakeEnrollmentTrainer.RecommendStrictness(_readings, _candidate);
            if (step.IsValidation)
            {
                _candidate ??= WakeEnrollmentTrainer.Build(Phrase, _settings().MicrophoneDeviceId, _readings);
                var found = _detector is IWakeEnrollmentDetector live ? await live.CheckAsync(audio, Phrase, _candidate, strictness, cancellation.Token) : WakeEnrollmentTrainer.Predict(analysis, Phrase, _candidate, strictness);
                if (found != step.IsWake)
                {
                    await AdaptFromMissAsync(step, analysis, strictness, cancellation.Token);
                    return;
                }
            }
            var reading = new WakeSetupReading(step.Id, step.IsWake, step.IsValidation, step.IsPhraseOnly, analysis.KeywordStrictness,
                analysis.RecognizedText, analysis.Rms, analysis.Peak, analysis.ClippedFraction, step.IsRoom ? null : analysis.Features);
            cancellation.Token.ThrowIfCancellationRequested();
            var oldProfile = _settings().AutoCaptureWakeVoiceProfile; var oldSensitivity = _settings().AutoCaptureWakeVoiceSensitivity;
            var oldEnabled = _settings().AutoCaptureWakeVoiceEnabled; var oldListening = _settings().AutoCaptureListeningEnabled;
            _readings.Add(reading);
            var profile = WakeEnrollmentTrainer.Build(Phrase, _settings().MicrophoneDeviceId, _readings);
            profile.SetupPromptVariant = _suiteVariant;
            profile.TunedStrictness = _candidate.TunedStrictness;
            profile.SetupReadings = _readings.ToList();
            profile.SetupValidated = SetupIndex == SetupSteps.Count;
            if (profile.SetupValidated)
            {
                profile.ValidatedAt = DateTimeOffset.UtcNow;
                profile.TunedStrictness = strictness;
                profile.SetupReadings = _readings.Select(r => r with { Features = null }).ToList();
                _settings().AutoCaptureWakeVoiceSensitivity = strictness;
                _complete = true;
            }
            _settings().AutoCaptureWakeVoiceProfile = profile;
            if (profile.SetupValidated && _canTranscribe()) { _settings().AutoCaptureWakeVoiceEnabled = true; _settings().AutoCaptureListeningEnabled = true; }
            try { await _save(); }
            catch
            {
                _readings.RemoveAt(_readings.Count - 1); _complete = false; _settings().AutoCaptureWakeVoiceProfile = oldProfile;
                _settings().AutoCaptureWakeVoiceSensitivity = oldSensitivity; _settings().AutoCaptureWakeVoiceEnabled = oldEnabled; _settings().AutoCaptureListeningEnabled = oldListening; throw;
            }
            SampleFeedback = step.IsRoom ? "Room sound learned." : "Heard: “" + analysis.RecognizedText + "”. " + (step.IsValidation ? "Check passed." : step.IsWake ? "Wyspa is learning how you say it." : "Non-wake example saved.");
            Status = _complete ? "Your personal wake profile passed the fresh checks and is ready. Say “" + Phrase + "”, wait for the tone, then dictate." : "Ready for the next reading.";
            if (!step.IsValidation) _candidate = profile;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Status = "Setup paused. Completed samples are kept; this recording was discarded."; }
        catch (Exception ex) { Status = "Could not finish this sample: " + ex.Message; }
        finally
        {
            IsRecording = false; _monitor.Stop(); Level = 0; lock (_samples) _samples.Clear(); _stop = null; _sampleEndpoint = null;
            try { if (reserved) await ReserveSampleAsync(false); } catch (Exception ex) { Status = "Could not resume listening: " + ex.Message; }
            _cancel = null; IsBusy = false;
        }
    }
    private async Task AdaptFromMissAsync(WakeSetupStep step, WakeSampleAnalysis analysis, double strictness, CancellationToken token)
    {
        var attempts = _readings.Count(r => r.StepId.StartsWith("tune-", StringComparison.Ordinal));
        if (attempts >= 6)
        {
            SampleFeedback = "Wyspa has not found a reliable separation for these samples. Your examples are kept. Check microphone placement or pause setup and try again later.";
            Status = "The adjusted profile is not active."; return;
        }
        // A failed check becomes a labelled teaching example. All checks must then be fresh again.
        var next = _readings.Where(r => !r.IsValidation).ToList();
        next.Add(new("tune-" + attempts, step.IsWake, false, step.IsPhraseOnly, analysis.KeywordStrictness,
            analysis.RecognizedText, analysis.Rms, analysis.Peak, analysis.ClippedFraction, analysis.Features));
        var adjusted = WakeEnrollmentTrainer.Build(Phrase, _settings().MicrophoneDeviceId, next);
        adjusted.SetupPromptVariant = _suiteVariant;
        adjusted.TunedStrictness = Math.Clamp(strictness + (step.IsWake ? -.15 : .15), 0, 1);
        adjusted.SetupReadings = next;
        token.ThrowIfCancellationRequested();
        var previous = _settings().AutoCaptureWakeVoiceProfile;
        _settings().AutoCaptureWakeVoiceProfile = adjusted;
        try { await _save(); }
        catch { _settings().AutoCaptureWakeVoiceProfile = previous; throw; }
        _readings.Clear(); _readings.AddRange(next); _candidate = adjusted;
        SampleFeedback = "Heard: “" + analysis.RecognizedText + "”. " + (step.IsWake ? "Wyspa learned from the missed pronunciation and relaxed its matching. Speak the next example in your usual voice." : "Wyspa learned from the unwanted trigger and tightened its matching.");
        Status = "Matching adjusted automatically. The four checks will now use new recordings.";
    }
    public async Task PauseSetupAsync()
    {
        _cancel?.Cancel();
        if (_active is { } work) await work;
    }
    public async Task SetEnabledAsync(bool enabled)
    {
        if (!CanEdit) { OnPropertyChanged(nameof(Enabled)); return; }
        if (!enabled)
        {
            var previous = _settings().AutoCaptureWakeVoiceEnabled;
            try { _settings().AutoCaptureWakeVoiceEnabled = false; await _save(); IsAwake = false; Status = "Wake phrases are off. SmartListen uses its speech threshold."; }
            catch (Exception ex) { _settings().AutoCaptureWakeVoiceEnabled = previous; Status = "Could not save wake setting: " + ex.Message; }
            Refresh(); return;
        }
        if (!_canTranscribe()) { Status = "Install a transcription model or connect Groq before enabling wake phrases."; Refresh(); return; }
        string normalized;
        try { normalized = WakePhraseCalibration.Normalize(Phrase); }
        catch (ArgumentException ex) { Status = ex.Message; Refresh(); return; }
        await SetPhraseAsync();
        if (_settings().AutoCaptureWakePhrase != normalized) return;
        Phrase = normalized;
        using var cancel = new CancellationTokenSource(); _cancel = cancel; IsBusy = true; var reserved = false;
        var oldEnabled = _settings().AutoCaptureWakeVoiceEnabled; var oldListening = _settings().AutoCaptureListeningEnabled;
        try
        {
            await ReserveSampleAsync(true); reserved = true;
            await _detector.PrepareAsync(new Progress<string>(message => { if (IsBusy) Status = message; }), cancel.Token);
            cancel.Token.ThrowIfCancellationRequested();
            _settings().AutoCaptureWakeVoiceEnabled = true; _settings().AutoCaptureListeningEnabled = true;
            await _save(); Status = "Ready for “" + _settings().AutoCaptureWakePhrase + "”. Say it, wait for the tone, then dictate.";
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { _settings().AutoCaptureWakeVoiceEnabled = oldEnabled; _settings().AutoCaptureListeningEnabled = oldListening; Status = "Wake setup cancelled."; }
        catch (Exception ex) { _settings().AutoCaptureWakeVoiceEnabled = oldEnabled; _settings().AutoCaptureListeningEnabled = oldListening; Status = "Could not enable wake phrases: " + ex.Message; }
        finally
        {
            try { if (reserved) await ReserveSampleAsync(false); } catch (Exception ex) { Status = "Could not resume listening: " + ex.Message; }
            _cancel = null; IsBusy = false;
        }
    }
    public async Task RecordAsync(bool isWake)
    {
        if (!CanEdit) return;
        using var cancellation = new CancellationTokenSource(); _cancel = cancellation; IsBusy = true; var reserved = false;
        try
        {
            var phrase = WakePhraseCalibration.Normalize(_settings().AutoCaptureWakePhrase);
            await ReserveSampleAsync(true); reserved = true;
            await _detector.PrepareAsync(new Progress<string>(message => { if (IsWorking) Status = message; }), cancellation.Token);
            lock (_samples) _samples.Clear();
            _stop = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await _monitor.StartAsync(_settings().MicrophoneDeviceId, cancellation.Token);
            IsRecording = true; IsBusy = false;
            Status = isWake ? "Recording for up to four seconds: say “" + phrase + "”, then pause." : "Recording for up to four seconds: say ordinary speech without the wake phrase. Try near phrases too.";
            await Task.WhenAny(Task.Delay(4000, cancellation.Token), _stop.Task);
            cancellation.Token.ThrowIfCancellationRequested();
            IsBusy = true; IsRecording = false; _monitor.Stop(); Level = 0;
            float[] audio; lock (_samples) { audio = _samples.ToArray(); _samples.Clear(); }
            if (audio.Length < 8000 || audio.Max(s => Math.Abs(s)) < .012f) throw new InvalidOperationException("Too little clear microphone audio. Check the input device and repeat.");
            Status = "Testing keyword matches at several strictness levels…";
            var matchedThrough = await _detector.EvaluateAsync(audio, phrase, cancellation.Token);
            var row = new WakeCalibrationTrial(isWake, matchedThrough, DateTimeOffset.UtcNow);
            Trials.Add(row); while (Trials.Count > 40) Trials.RemoveAt(0);
            _settings().AutoCaptureWakeVoiceProfile = new() { DetectorVersion = 2, Phrase = phrase, TrainingSampleCount = Trials.Count(t => t.IsWakePhrase), VoiceTrainingSampleCount = Trials.Count(t => !t.IsWakePhrase), CalibrationTrials = Trials.ToList() };
            await _save();
            LastSample = (isWake ? "Wake example: " : "Ordinary-speech example: ") + (matchedThrough is { } value ? $"matched through {value:P0} tested strictness." : "no keyword detected at the tested levels.");
            Status = "Sample added. " + Summary.Description;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { Status = "Sample cancelled. Previous examples are kept."; }
        catch (Exception ex) { Status = "Sample could not finish: " + ex.Message; }
        finally
        {
            IsRecording = false; _monitor.Stop(); Level = 0; lock (_samples) _samples.Clear(); _stop = null;
            try { if (reserved) await ReserveSampleAsync(false); }
            catch (Exception ex) { Status = "Could not resume listening: " + ex.Message; }
            _cancel = null; IsBusy = false;
        }
    }
    private async Task ApplyAsync()
    {
        if (!Summary.Ready) return;
        _settings().AutoCaptureWakeVoiceSensitivity = Summary.Strictness; await _save();
        Status = "Strictness applied. Add more wake and ordinary-speech samples if it misses or false-triggers.";
    }
    private Task ArmAsync() => SetEnabledAsync(true);
    private async Task DisarmAsync() { _settings().AutoCaptureListeningEnabled = false; _settings().AutoCaptureWakeVoiceEnabled = false; await _save(); Status = "Hands-free listening is off."; }
    public async Task RestartSetupAsync()
    {
        if (!CanEdit) return;
        try { await ResetAsync(); } catch (Exception ex) { Status = "Could not restart setup: " + ex.Message; }
    }
    private async Task ResetAsync()
    {
        var oldProfile = _settings().AutoCaptureWakeVoiceProfile;
        _settings().AutoCaptureWakeVoiceProfile = null;
        try { await _save(); } catch { _settings().AutoCaptureWakeVoiceProfile = oldProfile; throw; }
        _suiteVariant = (_suiteVariant + 1) % WakeSetupSuite.VariationCount;
        Load(); SampleFeedback = "Speak as you normally do. Wyspa will adapt to your pronunciation.";
        Status = "Wake setup cleared. Wyspa will learn a fresh profile from your next examples.";
    }
    public async Task ShutdownAsync()
    {
        _cancel?.Cancel(); if (_active is { } work) await work;
        await EndWizardAsync();
        _monitor.AudioAvailable -= Audio; _monitor.LevelAvailable -= LevelChanged;
    }
}
