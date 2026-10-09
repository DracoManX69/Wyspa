using Wyspa.App.ViewModels;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.Tests;

public sealed class WakePhraseCalibrationTests
{
    private static int TrainingCount => WakeSetupSuite.Create("hey whisper").TakeWhile(s => !s.IsValidation).Count();
    [Fact]
    public async Task NaturalSpeechAndPauseFinishAReadingWithoutTheFinishButton()
    {
        var settings = new AppSettings { ActivationMode = ActivationMode.AutoCapture }; var monitor = new Monitor();
        var vm = new WakeCalibrationViewModel(monitor, new Detector(), () => settings, _ => Task.CompletedTask, () => Task.CompletedTask, () => true);
        var room = vm.PracticeAsync(); await Until(() => vm.IsRecording); monitor.Emit(0); vm.FinishCommand.Execute(null); await room;
        var reading = vm.PracticeAsync(); await Until(() => vm.IsRecording); monitor.Emit(); monitor.Emit(0);
        await reading.WaitAsync(TimeSpan.FromSeconds(2)); Assert.Equal(2, vm.SetupIndex); Assert.False(monitor.IsRunning);
        await vm.ShutdownAsync();
    }
    [Fact]
    public void ChangedSuiteKeepsValidatedProfiles_ButRestartsIncompatiblePendingReadings()
    {
        var profile = new WakeVoiceProfile { EnrollmentVersion = 1, SetupSuiteVersion = 0, Phrase = "hey whisper", SetupReadings = [new("wake-normal",true,false,true,.6,"hey whisper",.1,.2,0)] };
        var settings = new AppSettings { ActivationMode = ActivationMode.AutoCapture, AutoCaptureWakeVoiceProfile = profile };
        var vm = new WakeCalibrationViewModel(new Monitor(), new Detector(), () => settings, _ => Task.CompletedTask, () => Task.CompletedTask, () => true);
        vm.Load(); Assert.Equal(0,vm.SetupIndex); Assert.Contains("varied",vm.SampleFeedback);
        profile.SetupValidated = true; vm.Load(); Assert.True(vm.SetupComplete);
    }
    [Fact]
    public async Task FailedWizardReservationShowsRecovery_AndDoesNotReleaseAnotherOwnersListening()
    {
        var settings = new AppSettings { ActivationMode = ActivationMode.AutoCapture }; var attempts = new List<bool>();
        var vm = new WakeCalibrationViewModel(new Monitor(), new Detector(), () => settings,
            reserved => { attempts.Add(reserved); return Task.FromException(new InvalidOperationException("Finish the current recording first.")); }, () => Task.CompletedTask, () => true);
        Assert.False(await vm.BeginWizardAsync()); Assert.Contains("Finish the current recording", vm.WizardOpenError);
        await vm.EndWizardAsync(); Assert.False(vm.IsWizardOpen); Assert.Equal(new[] { true }, attempts);
        await vm.ShutdownAsync();
    }
    [Fact]
    public async Task WizardReservesListeningBetweenSamples_AndClosingDiscardsActiveAudio()
    {
        var settings = new AppSettings { ActivationMode = ActivationMode.AutoCapture }; var monitor = new Monitor(); var reservations = new List<bool>();
        var vm = new WakeCalibrationViewModel(monitor, new Detector(), () => settings,
            value => { reservations.Add(value); return Task.CompletedTask; }, () => Task.CompletedTask, () => true);
        Assert.True(await vm.BeginWizardAsync());
        vm.PracticeCommand.Execute(null); await Until(() => vm.IsRecording); monitor.Emit(); vm.FinishCommand.Execute(null);
        await Until(() => !vm.IsWorking && vm.PracticeCommand.CanExecute(null));
        Assert.True(vm.IsWizardOpen); Assert.Equal(new[] { true }, reservations);
        vm.PracticeCommand.Execute(null); await Until(() => vm.IsRecording); monitor.Emit();
        await vm.EndWizardAsync();
        Assert.Equal(new[] { true, false }, reservations); Assert.False(monitor.IsRunning); Assert.False(vm.IsWorking); Assert.False(vm.IsWizardOpen);
        Assert.Equal(1, vm.SetupIndex); await vm.ShutdownAsync(); Assert.Equal(2, reservations.Count);
    }
    [Fact]
    public async Task MissesTeachTheProgram_AdjustStrictness_AndRestartIndependentChecks()
    {
        var settings = new AppSettings { ActivationMode = ActivationMode.AutoCapture }; var monitor = new Monitor(); var detector = new EnrollmentDetector();
        WakeCalibrationViewModel Create() => new(monitor, detector, () => settings, _ => Task.CompletedTask, () => Task.CompletedTask, () => true);
        var vm = Create();
        async Task Read()
        {
            var step = vm.SetupSteps[vm.SetupIndex]; detector.Match = step.IsWake ? .6 : null;
            detector.Text = step.Prompt.Replace("whisper", "whisba");
            var task = vm.PracticeAsync(); await Until(() => vm.IsRecording); monitor.Emit(); vm.FinishCommand.Execute(null); await task;
        }
        for (var i = 0; i < TrainingCount; i++) await Read();
        await Read(); detector.CheckResult = false; await Read();
        Assert.Equal(TrainingCount, vm.SetupIndex); Assert.False(vm.Enabled);
        Assert.InRange(settings.AutoCaptureWakeVoiceProfile!.TunedStrictness!.Value, .29, .31);
        Assert.Contains(settings.AutoCaptureWakeVoiceProfile.SetupReadings, r => r.StepId == "tune-0" && !r.IsValidation && r.IsWake);
        await vm.ShutdownAsync(); vm = Create(); vm.Load();
        detector.CheckResult = null; await Read(); await Read();
        detector.CheckResult = true; await Read(); // Ordinary speech falsely triggered after two successful checks.
        Assert.Equal(TrainingCount, vm.SetupIndex); Assert.DoesNotContain(settings.AutoCaptureWakeVoiceProfile!.SetupReadings, r => r.IsValidation);
        Assert.InRange(settings.AutoCaptureWakeVoiceProfile.TunedStrictness!.Value, .44, .46);
        detector.CheckResult = null;
        for (var i = 0; i < 4; i++) await Read();
        Assert.True(vm.SetupComplete); Assert.True(vm.Enabled);
        Assert.Contains("hey whisba", settings.AutoCaptureWakeVoiceProfile.PronunciationVariants);
        Assert.InRange(settings.AutoCaptureWakeVoiceSensitivity, .44, .46);
        await vm.ShutdownAsync();
    }
    [Fact]
    public async Task FailedAdjustmentSaveKeepsTheExistingSamplesAndProfile()
    {
        var settings = new AppSettings { ActivationMode = ActivationMode.AutoCapture }; var monitor = new Monitor(); var detector = new EnrollmentDetector(); var failSave = false;
        var vm = new WakeCalibrationViewModel(monitor, detector, () => settings, _ => Task.CompletedTask,
            () => failSave ? Task.FromException(new IOException("disk full")) : Task.CompletedTask, () => true);
        async Task Read()
        {
            var step = vm.SetupSteps[vm.SetupIndex]; detector.Match = step.IsWake ? .6 : null; detector.Text = step.Prompt;
            var task = vm.PracticeAsync(); await Until(() => vm.IsRecording); monitor.Emit(); vm.FinishCommand.Execute(null); await task;
        }
        for (var i = 0; i < TrainingCount; i++) await Read();
        await Read(); var previous = settings.AutoCaptureWakeVoiceProfile; failSave = true; detector.CheckResult = false; await Read();
        Assert.Same(previous, settings.AutoCaptureWakeVoiceProfile); Assert.Equal(TrainingCount + 1, vm.SetupIndex);
        Assert.DoesNotContain(previous!.SetupReadings, r => r.StepId.StartsWith("tune-")); Assert.Contains("disk full", vm.Status);
        await vm.ShutdownAsync();
    }
    [Fact]
    public async Task PendingSetupResumes_AndFreshChecksUseEveryTrainingReading()
    {
        var settings = new AppSettings { ActivationMode = ActivationMode.AutoCapture }; var monitor = new Monitor(); var detector = new EnrollmentDetector();
        WakeCalibrationViewModel Create() => new(monitor, detector, () => settings, _ => Task.CompletedTask, () => Task.CompletedTask, () => true);
        var vm = Create();
        async Task Read()
        {
            detector.Match = vm.SetupSteps[vm.SetupIndex].IsWake ? .6 : null;
            detector.Text = vm.SetupSteps[vm.SetupIndex].Prompt.Replace("whisper", "wyspa");
            var task = vm.PracticeAsync(); await Until(() => vm.IsRecording); monitor.Emit(); vm.FinishCommand.Execute(null); await task;
        }
        for (var index = 0; index < 5; index++) await Read();
        Assert.False(settings.AutoCaptureWakeVoiceProfile!.SetupValidated);
        await vm.ShutdownAsync(); vm = Create(); vm.Load(); Assert.Equal(5, vm.SetupIndex);
        for (var index = 5; index < vm.SetupSteps.Count; index++) await Read();
        Assert.True(vm.SetupComplete);
        Assert.Contains("hey wyspa", settings.AutoCaptureWakeVoiceProfile!.PronunciationVariants);
        Assert.Equal(Enumerable.Repeat(TrainingCount, 4), detector.CheckedTrainingCounts);
        Assert.All(settings.AutoCaptureWakeVoiceProfile!.SetupReadings, r => Assert.Null(r.Features));
        await vm.ShutdownAsync();
    }
    [Fact]
    public async Task FailedFreshCheckDoesNotAdvanceOrEnableProfile_AndMicrophoneChangeRestartsSuite()
    {
        var settings = new AppSettings { ActivationMode = ActivationMode.AutoCapture }; var monitor = new Monitor(); var detector = new Detector();
        var vm = new WakeCalibrationViewModel(monitor, detector, () => settings, _ => Task.CompletedTask, () => Task.CompletedTask, () => true);
        async Task Read()
        {
            var task = vm.PracticeAsync(); await Until(() => vm.IsRecording); monitor.Emit(); vm.FinishCommand.Execute(null); await task;
        }
        for (var index = 0; index < TrainingCount; index++) { detector.Match = vm.SetupSteps[index].IsWake ? .6 : null; await Read(); }
        detector.Match = null; await Read(); await Read();
        Assert.Equal(TrainingCount, vm.SetupIndex); Assert.False(vm.SetupComplete); Assert.False(vm.Enabled); Assert.False(settings.AutoCaptureWakeVoiceProfile!.SetupValidated);
        detector.Match = null; await Read(); detector.Match = .6; await Read(); Assert.Equal(TrainingCount + 2, vm.SetupIndex);
        settings.MicrophoneDeviceId = "new-mic"; await Read();
        Assert.Equal(1, vm.SetupIndex); Assert.Equal("new-mic", settings.AutoCaptureWakeVoiceProfile!.MicrophoneId);
        await vm.ShutdownAsync();
    }
    [Fact]
    public async Task OneGuidedPracticeSavesPhrase_TunesAutomatically_AndEnablesOnlySmartListen()
    {
        var settings = new AppSettings { ActivationMode = ActivationMode.AutoCapture }; var monitor = new Monitor(); var detector = new Detector();
        var reservations = new List<bool>();
        var vm = new WakeCalibrationViewModel(monitor, detector, () => settings, value => { reservations.Add(value); return Task.CompletedTask; }, () => Task.CompletedTask, () => true);
        vm.Phrase = "hey assistant";
        for (var reading = 0; reading < vm.SetupSteps.Count; reading++)
        {
            detector.Match = vm.SetupSteps[reading].IsWake ? .6 : null;
            var task = vm.PracticeAsync(); await Until(() => vm.IsRecording); monitor.Emit(); vm.FinishCommand.Execute(null); await task;
        }
        Assert.Equal("hey assistant", settings.AutoCaptureWakePhrase); Assert.True(vm.SetupComplete); Assert.Equal(vm.SetupSteps.Count, settings.AutoCaptureWakeVoiceProfile!.SetupReadings.Count);
        Assert.True(vm.Enabled); Assert.True(settings.AutoCaptureListeningEnabled);
        Assert.InRange(settings.AutoCaptureWakeVoiceSensitivity, .44, .46); Assert.False(vm.IsWorking); Assert.False(monitor.IsRunning);
        Assert.Equal(vm.SetupSteps.Count, reservations.Count(value => value)); Assert.Equal(vm.SetupSteps.Count, reservations.Count(value => !value));
        vm.ReportRuntime("Wake phrase accepted. Dictation starts after the tone."); await Until(() => vm.IsAwake);
        vm.ReportRuntime("Ready · waiting for hey assistant."); await Until(() => !vm.IsAwake);
        settings.ActivationMode = ActivationMode.HoldToTalk; vm.RefreshState();
        Assert.False(vm.CanEdit); Assert.False(vm.PracticeCommand.CanExecute(null));
        await vm.SetEnabledAsync(true); Assert.Equal(ActivationMode.HoldToTalk, settings.ActivationMode);
        await vm.ShutdownAsync();
    }
    [Fact]
    public async Task InvalidPracticePhraseDoesNotEnableOldPhrase_AndCancelledPracticeDoesNotRearm()
    {
        var settings = new AppSettings { ActivationMode = ActivationMode.AutoCapture }; var monitor = new Monitor(); var detector = new Detector();
        var vm = new WakeCalibrationViewModel(monitor, detector, () => settings, _ => Task.CompletedTask, () => Task.CompletedTask, () => true);
        vm.Phrase = "bad # phrase"; Assert.False(vm.CanStartWizard); await vm.SetEnabledAsync(true); Assert.False(vm.Enabled);
        vm.Phrase = "hey whisper";
        Assert.True(vm.CanStartWizard);
        var task = vm.PracticeAsync(); await Until(() => vm.IsRecording); vm.CancelCommand.Execute(null); await task;
        Assert.False(vm.Enabled); Assert.Empty(vm.Trials); Assert.Contains("paused", vm.Status);
        await vm.ShutdownAsync();
    }
    [Fact]
    public void OrdinarySpeechExamplesRaiseStrictness_AndBadSeparationIsNotReady()
    {
        var rows = Enumerable.Range(0, 3).Select(_ => new WakeCalibrationTrial(true, .75, DateTimeOffset.UtcNow))
            .Concat(Enumerable.Range(0, 3).Select(_ => new WakeCalibrationTrial(false, .3, DateTimeOffset.UtcNow))).ToArray();
        var suggestion = WakePhraseCalibration.Recommend(rows);
        Assert.True(suggestion.Ready); Assert.Equal(.75, suggestion.Strictness); Assert.Equal(3, suggestion.TruePositives); Assert.Equal(0, suggestion.FalsePositives);
        var bad = rows.Select(r => r with { MaximumMatchingStrictness = .45 });
        Assert.False(WakePhraseCalibration.Recommend(bad).Ready);
    }
    [Fact]
    public void MissingKeywordsNeverCountAsDetected_AndPhrasesAreValidated()
    {
        var rows = Enumerable.Range(0, 3).Select(_ => new WakeCalibrationTrial(true, null, DateTimeOffset.UtcNow))
            .Concat(Enumerable.Range(0, 3).Select(_ => new WakeCalibrationTrial(false, null, DateTimeOffset.UtcNow)));
        Assert.Equal(0, WakePhraseCalibration.Recommend(rows).TruePositives); Assert.False(WakePhraseCalibration.Recommend(rows).Ready);
        Assert.Equal("hey whisper", WakePhraseCalibration.Normalize(" HEY   Whisper "));
        Assert.Throws<ArgumentException>(() => WakePhraseCalibration.Normalize("whisper"));
        Assert.Throws<ArgumentException>(() => WakePhraseCalibration.Normalize("hey whisper #0.0"));
        Assert.True(WakePhraseCalibration.Threshold(.8) > WakePhraseCalibration.Threshold(.3));
    }
    [Fact]
    public async Task SamplesAreLocal_LabelledAndDiscarded_AndListenReservationIsReleased()
    {
        var settings = new AppSettings { ActivationMode = ActivationMode.AutoCapture }; var monitor = new Monitor(); var detector = new Detector(); var reserved = false; var saves = 0;
        var vm = new WakeCalibrationViewModel(monitor, detector, () => settings, value => { reserved = value; return Task.CompletedTask; }, () => { saves++; return Task.CompletedTask; }, () => true);
        var recording = vm.RecordAsync(true);
        await Until(() => vm.IsRecording); Assert.True(reserved); monitor.Emit(); vm.FinishCommand.Execute(null); await recording;
        Assert.False(reserved); Assert.False(monitor.IsRunning); Assert.Equal(1, saves); Assert.Single(vm.Trials);
        Assert.True(settings.AutoCaptureWakeVoiceProfile!.CalibrationTrials[0].IsWakePhrase); Assert.Equal(2, settings.AutoCaptureWakeVoiceProfile.DetectorVersion);
        Assert.False(settings.AutoCaptureWakeVoiceEnabled); Assert.Equal(1, detector.Evaluations);
        await vm.ShutdownAsync();
    }
    [Fact]
    public async Task CancellationPreventsSampleSaving_AndOlderAcousticProfilesAreMigrated()
    {
        var settings = new AppSettings { ActivationMode = ActivationMode.AutoCapture, AutoCaptureWakeVoiceProfile = new WakeVoiceProfile(), AutoCaptureWakeVoiceSensitivity = .9 };
        var monitor = new Monitor(); var vm = new WakeCalibrationViewModel(monitor, new Detector(), () => settings, _ => Task.CompletedTask, () => Task.CompletedTask, () => true);
        await vm.InitializeAsync(); Assert.Equal(.3, settings.AutoCaptureWakeVoiceSensitivity); Assert.Equal(2, settings.AutoCaptureWakeVoiceProfile!.DetectorVersion);
        var recording = vm.RecordAsync(false); await Until(() => vm.IsRecording); monitor.Emit(); vm.CancelCommand.Execute(null); await recording;
        Assert.Empty(vm.Trials); Assert.False(monitor.IsRunning); Assert.False(vm.IsWorking); await vm.ShutdownAsync();
    }
    private static async Task Until(Func<bool> condition) { using var c = new CancellationTokenSource(5000); while (!condition()) await Task.Delay(10, c.Token); }
    private sealed class Detector : IWakePhraseDetector
    {
        public double? Match { get; set; } = .6;
        public int Evaluations;
        public Task PrepareAsync(IProgress<string>? progress, CancellationToken token) => Task.CompletedTask;
        public Task<bool> ProcessAsync(float[] samples, string phrase, double strictness, CancellationToken token) => Task.FromResult(false);
        public Task<double?> EvaluateAsync(float[] samples, string phrase, CancellationToken token) { Evaluations++; return Task.FromResult(Match); }
        public void Reset() { }
        public void Dispose() { }
    }
    private sealed class EnrollmentDetector : IWakePhraseDetector, IWakeEnrollmentDetector
    {
        public double? Match;
        public string Text = "";
        public bool? CheckResult;
        public List<int> CheckedTrainingCounts = [];
        public Task PrepareAsync(IProgress<string>? progress, CancellationToken token) => Task.CompletedTask;
        public Task<bool> ProcessAsync(float[] samples, string phrase, double strictness, CancellationToken token) => Task.FromResult(false);
        public Task<double?> EvaluateAsync(float[] samples, string phrase, CancellationToken token) => Task.FromResult(Match);
        public Task<WakeSampleAnalysis> AnalyzeAsync(float[] samples, string phrase, CancellationToken token) => Task.FromResult(new WakeSampleAnalysis(Match, Text, .2, .2, 0, WakeAcoustics.Extract(samples)));
        public Task<bool> CheckAsync(float[] samples, string phrase, WakeVoiceProfile profile, double strictness, CancellationToken token)
        {
            CheckedTrainingCounts.Add(profile.SetupReadings.Count(r => !r.IsValidation)); return Task.FromResult(CheckResult ?? Match.HasValue);
        }
        public void ConfigurePersonalization(WakeVoiceProfile? profile, string? microphoneId) { }
        public void Reset() { }
        public void Dispose() { }
    }
    private sealed class Monitor : IAudioLevelMonitorService
    {
        public event EventHandler<float>? LevelAvailable;
        public event EventHandler<IReadOnlyList<float>>? AudioAvailable;
        public bool IsRunning { get; private set; }
        public Task StartAsync(string? id, CancellationToken token) { IsRunning = true; return Task.CompletedTask; }
        public void Emit(float level = .2f) { AudioAvailable?.Invoke(this, Enumerable.Repeat(level, 16000).ToArray()); LevelAvailable?.Invoke(this, level); }
        public void Stop() { IsRunning = false; }
        public void Dispose() => Stop();
    }
}
