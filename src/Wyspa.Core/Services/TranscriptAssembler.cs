using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

public static class TranscriptAssembler
{
    public static IReadOnlyList<NoteEntry> Assemble(AudioChunk chunk, SpeechResult speech, IReadOnlyList<SpeakerTurn>? turns = null)
    {
        var entries = new List<NoteEntry>();
        foreach (var word in speech.Words)
        {
            var middle = (word.Start + word.End) / 2;
            if (middle < chunk.LeadingOverlap) continue;
            var speakers = turns?.Where(t => middle >= t.Start && middle <= t.End).Select(t => t.Speaker).Distinct().ToArray();
            var speaker = turns is null ? chunk.Source : speakers?.Length switch
            {
                1 => speakers[0],
                > 1 => "Overlapping voices",
                _ => "Unidentified voice"
            };
            var start = chunk.Start + Math.Clamp(word.Start, 0, chunk.Duration);
            var end = chunk.Start + Math.Clamp(word.End, 0, chunk.Duration);
            if (entries.LastOrDefault() is { } previous && previous.Speaker == speaker && start - previous.End < 1.2)
                entries[^1] = previous with { End = end, Text = previous.Text + " " + word.Text.Trim() };
            else entries.Add(new(Guid.NewGuid(), chunk.Sequence, start, end, speaker, word.Text.Trim()));
        }
        if (speech.Words.Count == 0 && !string.IsNullOrWhiteSpace(speech.Text))
            entries.Add(new(Guid.NewGuid(), chunk.Sequence, chunk.Start, chunk.Start + chunk.Duration,
                turns is null ? chunk.Source : "Unidentified voice", speech.Text.Trim()));
        return entries;
    }
}
