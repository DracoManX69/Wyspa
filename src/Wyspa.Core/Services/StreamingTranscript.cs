using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

// Local agreement: append a prefix agreed by consecutive hypotheses, holding back
// the unfinished tail. Never erase user text to apply a later recognition revision.
public sealed class StreamingTranscript
{
    private IReadOnlyList<TimedWord> _previous = [];
    private string _lastCommittedWord = "";
    public string Text { get; private set; } = "";
    public double CommittedThrough { get; private set; } = -1;

    public string Accept(SpeechResult speech, double start, double duration, bool final, bool atLimit)
    {
        var allWords = speech.Words
            .Select(w => w with { Start = start + w.Start, End = start + w.End, Text = w.Text.Trim() })
            .Where(w => w.End <= start + duration + .1 && w.Start >= start - .1)
            .ToArray();
        var words = allWords.Where(w => (w.Start + w.End) / 2 > CommittedThrough).ToArray();
        var previous = _previous.Where(w => (w.Start + w.End) / 2 > CommittedThrough).ToArray();
        var count = 0;
        if (final || atLimit)
        {
            count = atLimit ? words.TakeWhile(w => w.End <= start + duration - .6).Count() : words.Length;
        }
        else
        {
            while (count < Math.Min(words.Length - 1, previous.Length) &&
                   words[count].Text.Equals(previous[count].Text, StringComparison.OrdinalIgnoreCase) &&
                   Math.Abs(words[count].Start - previous[count].Start) < .5 &&
                   words[count].End <= start + duration - .25)
                count++;
        }

        var before = Text;
        // Whisper sometimes supplies punctuation only on a later hypothesis.
        // It can still be appended safely while this remains the last typed word.
        var revisedLast = allWords.LastOrDefault(w => (w.Start + w.End) / 2 <= CommittedThrough &&
            Math.Abs(w.End - CommittedThrough) < .4);
        if (_lastCommittedWord.Length > 0 && char.IsLetterOrDigit(_lastCommittedWord[^1]) &&
            revisedLast is not null && revisedLast.Text.StartsWith(_lastCommittedWord, StringComparison.OrdinalIgnoreCase))
        {
            var punctuation = revisedLast.Text[_lastCommittedWord.Length..];
            if (punctuation.Length > 0 && punctuation.All(c => c is '.' or ',' or '?' or '!' or ':' or ';'))
            {
                Text += punctuation;
                _lastCommittedWord += punctuation;
            }
        }
        if (count > 0)
        {
            var added = string.Join(" ", words.Take(count).Select(w => w.Text));
            var separator = Text.Length > 0 && added.Length > 0 && !char.IsPunctuation(added[0]) ? " " : "";
            Text += separator + added;
            CommittedThrough = words[count - 1].End;
            _lastCommittedWord = words[count - 1].Text;
        }
        _previous = words;
        return Text[before.Length..];
    }

    public void EndWindow() => _previous = [];
}
