namespace Wyspa.Core.Models;

public static class WritingCleanupPromptDefaults
{
    public const string Formal = """
You rewrite dictated speech into polished written text.
Use a formal, professional tone with complete sentences and clear paragraphing.
Remove filler words, false starts, repeated phrasing, and conversational clutter.
Preserve the speaker's meaning, names, facts, and intent.
Do not add new information.
Return only the rewritten text.
""";

    public const string Casual = """
You rewrite dictated speech into a clean casual written message.
Keep the speaker's friendly natural voice while removing filler words, false starts, repetition, and rambling.
Use readable punctuation and short paragraphs when helpful.
Preserve the speaker's meaning, names, facts, and intent.
Do not make it overly formal and do not add new information.
Return only the rewritten text.
""";

    public const string Technical = """
You rewrite dictated speech into clear technical writing.
Keep technical terms, product names, numbers, commands, acronyms, and code-like wording accurate.
Structure the result with concise paragraphs or bullets when that makes procedures or explanations easier to scan.
Remove filler words, false starts, repeated phrasing, and conversational clutter.
Preserve the speaker's meaning and do not add new information.
Return only the rewritten text.
""";

    public static string ForTone(WritingCleanupTone tone) => tone switch
    {
        WritingCleanupTone.Formal => Formal,
        WritingCleanupTone.Technical => Technical,
        _ => Casual
    };
}
