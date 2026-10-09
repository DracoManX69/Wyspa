using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

public static class WakeSetupSuite
{
    public const int Version = 2;
    public const int VariationCount = 4;
    public static IReadOnlyList<WakeSetupStep> Create(string phrase, int variation = 0)
    {
        phrase = WakePhraseCalibration.Normalize(phrase);
        var last = phrase.Split(' ')[^1];
        var near = phrase == "hey whisper" ? "hey mister" : phrase.Split(' ')[0] + " listener";
        if (near == phrase) near = "hello speaker";
        var ordinary = new[] {
            "Tomorrow I will review my notes and send the finished message.",
            "The kitchen window is open and the afternoon feels warmer.",
            "I have moved the appointment to Friday and saved the address.",
            "Please put the blue notebook next to the keyboard." };
        var requests = new[] { "please make a note for tomorrow.", "write down the appointment time.", "remember to check the shopping list.", "start a message about the weekend." };
        var v = Math.Abs(variation % VariationCount);
        return [
            new("room", "Check your room", "Stay quiet for a moment.", "Keep your microphone where you normally use it. We measure background sound.", false, false, false, 3, true),
            new("wake-normal", "Your natural voice", phrase, "Say it as you usually would. Wyspa learns your pronunciation.", true, false, true, 5),
            new("ordinary", "An everyday sentence", ordinary[v], "Speak naturally. This example teaches Wyspa when to stay quiet.", false, false, false, 8),
            new("wake-sentence", "A short request", phrase + ", " + requests[v], "Use your normal rhythm for the full sentence. This tests waking while you continue speaking.", true, false, false, 8),
            new("near", "Similar words", near + ", " + requests[(v + 1) % 4], "These are different words. Keep your usual voice so Wyspa learns the distinction.", false, false, false, 8),
            new("wake-soft", "A softer voice", phrase, "Use the quieter voice you might use at your desk. Do not change your accent.", true, false, true, 5),
            new("word-alone", "A word in conversation", "I can say " + last + " without asking the app to listen.", "Read the sentence in your normal voice; a single word should not wake Wyspa.", false, false, false, 8),
            new("wake-context", "A different request", phrase + ", " + requests[(v + 2) % 4], "Read it comfortably. There is no need to slow down or exaggerate the phrase.", true, false, false, 8),
            new("ordinary-cadence", "Your conversational rhythm", ordinary[(v + 2) % 4], "Use your everyday emphasis and pauses. Wyspa should stay quiet.", false, false, false, 8),
            new("wake-distance", "Your usual position", phrase, "Sit as you normally would while working, then say the phrase.", true, false, true, 5),
            new("check-ordinary", "Check: everyday speech", ordinary[(v + 1) % 4], "A new recording checks the profile without teaching it.", false, true, false, 8),
            new("check-wake", "Check: wake phrase", phrase, "Say it naturally. Wyspa is checking its adjusted matching.", true, true, true, 5),
            new("check-near", "Check: similar words", near + ", " + requests[(v + 3) % 4], "A new near match checks that Wyspa stays quiet.", false, true, false, 8),
            new("check-request", "Check: a new request", phrase + ", " + requests[(v + 1) % 4], "Read the full request naturally. Wyspa should recognize the phrase as you continue.", true, true, false, 8)
        ];
    }
    public static string? QualityProblem(WakeSetupStep step, WakeSampleAnalysis analysis, int samples)
    {
        if (samples < 8000) return "The reading was too short. Wait for recording to start, then read the prompt.";
        if (step.IsRoom) return null;
        if (analysis.ClippedFraction > .015) return "Your microphone is clipping. Lower its input level or move slightly farther away, then retry.";
        if (analysis.Rms < .0015 || analysis.Peak < .012) return "Your voice is too quiet. Check the microphone or move closer, then retry.";
        return null;
    }
    public static bool PlausibleVariant(string text, string phrase)
    {
        var words = VoicePersonalization.Words(text); var expected = VoicePersonalization.Words(phrase);
        if (words.Length != expected.Length) return false;
        return words.Zip(expected).All(pair => pair.First == pair.Second || pair.Second == "hey" && pair.First == "hay" ||
            pair.Second.Length >= 5 && (VoicePersonalization.CharacterDistance(pair.First, pair.Second) <= Math.Max(1, pair.Second.Length / 3) || SoundKey(pair.Second).Length >= 2 && SoundKey(pair.First) == SoundKey(pair.Second)));
    }
    private static string SoundKey(string word)
    {
        var key = word.Replace("wh", "w").Replace("ph", "f").Replace("ck", "k");
        key = System.Text.RegularExpressions.Regex.Replace(key, "[aeiouy]", "");
        key = System.Text.RegularExpressions.Regex.Replace(key, @"(.)\1+", "$1");
        // Voiced/unvoiced pairs vary with accent and ASR spelling: whisba/whispa/whisper.
        return key.TrimEnd('r').Replace('b', 'p').Replace('d', 't').Replace('g', 'k').Replace('z', 's');
    }

}
