namespace Wyspa.Core.Models;

public sealed record TranscriptionOptions(
    string ModelId,
    string? Language,
    string? Prompt,
    string ResponseFormat = "text",
    double Temperature = 0,
    bool UseLocal = false,
    string LocalModelId = "base.en",
    bool ApplyPersonalization = true,
    bool? LocalGpuOverride = null);
