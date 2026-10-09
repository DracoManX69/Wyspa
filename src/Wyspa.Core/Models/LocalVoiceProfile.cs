namespace Wyspa.Core.Models;

public sealed class LocalVoiceProfile
{
    public DateTimeOffset CompletedAt { get; set; }
    public string Language { get; set; } = "en";
    public string Vocabulary { get; set; } = "";
    public string ModelId { get; set; } = "";
    public int SuggestedSilenceMs { get; set; } = 1200;
    public List<VoiceTestScore> Scores { get; set; } = [];
}

public sealed record VoiceTestScore(string ModelId, int Passage, int ReferenceWords, int BaselineErrors, int PersonalizedErrors,
    double AudioSeconds, double BaselineSeconds, double PersonalizedSeconds);
