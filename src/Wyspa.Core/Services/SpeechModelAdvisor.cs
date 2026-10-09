using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

public sealed record SpeechRecommendation(string Provider, string ModelId, string Explanation, bool Measured);

public static class SpeechModelAdvisor
{
    public static bool English(AppSettings settings)
    {
        var language = settings.LocalVoiceProfile?.Language ?? settings.Language;
        return string.IsNullOrWhiteSpace(language) ? settings.LocalModelId.Contains(".en", StringComparison.Ordinal) : language.Equals("en", StringComparison.OrdinalIgnoreCase);
    }
    public static SpeechRecommendation Hardware(LocalHardware pc, AppSettings settings)
    {
        var english = English(settings);
        var gpu = settings.LocalGpuEnabled && pc.Gpu is not null;
        var constrained = pc.LogicalProcessors < 4 || pc.MemoryBytes is > 0 and < 4UL * 1024 * 1024 * 1024;
        var id = english && (!gpu && settings.StreamModeEnabled || constrained) ? ZipformerModel.Id : english ? "tiny.en-q5_1" : "tiny-q5_1";
        var reason = id == ZipformerModel.Id ? "Start with the included Zipformer for lightweight English streaming on CPU. It uses incremental audio and up to two threads; Whisper, Parakeet and Moonshine offer other transcription/formatting options."
            : gpu ? "Start with Whisper Tiny Q5 on the detected GPU: a compact model for short dictation. GPU detection alone does not establish latency; test installed models below."
            : "Start with Whisper Tiny Q5 for compact CPU transcription. For English live streaming, the included Zipformer is another low-resource option.";
        var upgrade = gpu && pc.DiscreteGpu && pc.GpuMemoryBytes >= 6UL * 1024 * 1024 * 1024 && pc.MemoryBytes >= 8UL * 1024 * 1024 * 1024
            ? " For an accuracy-oriented upgrade, test Large v3 Turbo Q5 after downloading it."
            : pc.MemoryBytes >= 16UL * 1024 * 1024 * 1024 && pc.LogicalProcessors >= 8 ? " Parakeet INT8, Moonshine Base or Small Q5 can be tested for an accuracy upgrade; loading and processing can be slower." : " Keep larger models optional on this PC.";
        return new("local", id, "Hardware starting point: " + reason + upgrade + " Groq speed is unmeasured until a comparison is run.", false);
    }
    public static bool Current(SpeechPerformanceReport? report, LocalHardware hardware, bool gpu) => report is not null && report.SampleVersion == SpeechPerformanceBenchmark.SampleVersion && report.HardwareFingerprint == hardware.Fingerprint && report.GpuEnabled == gpu;
    public static SpeechRecommendation Recommend(LocalHardware pc, AppSettings settings, Func<string, bool> installed)
    {
        var fallback = Hardware(pc, settings);
        var report = settings.SpeechPerformance;
        if (!Current(report, pc, settings.LocalGpuEnabled)) return fallback;
        var english = English(settings);
        var locals = report!.Results.Where(r => r.Provider == "local" && r.Error is null && installed(r.ModelId) &&
            (english || LocalModelStore.Catalog.FirstOrDefault(m => m.Id == r.ModelId) is { EnglishOnly: false })).ToArray();
        if (locals.Length == 0) return fallback;
        var local = locals.OrderByDescending(r => r.Score).ThenBy(r => r.TypicalSeconds).First();
        if (local.WordErrors > local.ReferenceWords * .5) return fallback with { Explanation = "The installed models had too many errors on the test sample to recommend confidently. " + fallback.Explanation };
        var cloud = report.MeasuredAt >= DateTimeOffset.UtcNow.AddHours(-24) && report.MeasuredAt <= DateTimeOffset.UtcNow.AddMinutes(5)
            ? report.Results.LastOrDefault(r => r.Provider == "groq" && r.Error is null && r.ModelId == settings.ModelId) : null;
        var detail = $"{local.Name} ({local.DeviceDisplay}): {local.TypicalSeconds:0.00}s typical result, {local.WordErrors}/{local.ReferenceWords} sample word errors.";
        if (cloud is null) return new("local", local.ModelId, "Measured local choice: " + detail + " Groq has no current successful measurement; compare with Groq to choose between providers. Results cover one English sample.", true);
        if (cloud.Score > local.Score)
            return new("groq", cloud.ModelId, $"Groq wins this sample comparison with {cloud.ScoreDisplay}/100 versus local {local.ScoreDisplay}/100. {cloud.TypicalSeconds:0.00}s including upload/network/server response, {cloud.WordErrors}/{cloud.ReferenceWords} sample word errors. Local: {detail} Scores weight recognition quality 80% and response time 20%; your speech and connection can differ.", true);
        return new("local", local.ModelId, $"Local wins this sample comparison with {local.ScoreDisplay}/100 versus Groq {cloud.ScoreDisplay}/100. {detail} Groq: {cloud.TypicalSeconds:0.00}s full request, {cloud.WordErrors}/{cloud.ReferenceWords} sample errors. Scores weight recognition quality 80% and response time 20%; ties favour local offline processing. Your speech and connection can differ.", true);
    }
}
