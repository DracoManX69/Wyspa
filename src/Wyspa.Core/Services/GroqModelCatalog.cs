namespace Wyspa.Core.Services;

/// <summary>
/// Groq's /models response has no documented capability field. Classify known
/// families conservatively, then intersect with the live, active model IDs.
/// Unknown families stay hidden until their endpoint compatibility is verified.
/// See https://console.groq.com/docs/models and /docs/speech-to-text.
/// </summary>
public static class GroqModelCatalog
{
    public static bool SupportsTranscription(string id) =>
        id.StartsWith("whisper-", StringComparison.OrdinalIgnoreCase);

    public static bool SupportsTextGeneration(string id)
    {
        var name = id.ToLowerInvariant();
        if (new[] { "guard", "safety", "moderation", "tts", "orpheus", "embedding" }.Any(name.Contains)) return false;
        return name.StartsWith("llama-") || name.StartsWith("meta-llama/llama-") ||
               name.StartsWith("openai/gpt-oss-") || name.StartsWith("qwen/") ||
               name.StartsWith("moonshotai/kimi-") || name.StartsWith("minimaxai/minimax-") ||
               name.StartsWith("deepseek-r1-distill-") || name.StartsWith("gemma2-");
    }

    public static IReadOnlyList<string> ForTranscription(IEnumerable<string> ids) => Filter(ids, SupportsTranscription);
    public static IReadOnlyList<string> ForTextGeneration(IEnumerable<string> ids) => Filter(ids, SupportsTextGeneration);

    private static IReadOnlyList<string> Filter(IEnumerable<string> ids, Func<string, bool> predicate) =>
        ids.Where(id => !string.IsNullOrWhiteSpace(id)).Where(predicate)
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
}
