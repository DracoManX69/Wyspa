using System.Text.RegularExpressions;

namespace Wyspa.Core.Services;

public static class StreamTextReconciliation
{
    // Append only when the complete final hypothesis confirms every live word.
    // Punctuation/case changes wait for a verified range replacement.
    public static string AppendOnlyTail(string live, string final)
    {
        if (final.StartsWith(live, StringComparison.Ordinal)) return final[live.Length..];
        var oldWords = Regex.Matches(live, @"\S+");
        var newWords = Regex.Matches(final, @"\S+");
        if (oldWords.Count >= newWords.Count) return "";
        string Normalize(string s) => s.Trim('.', ',', '!', '?', ';', ':').ToUpperInvariant();
        for (var i = 0; i < oldWords.Count; i++)
            if (Normalize(oldWords[i].Value) != Normalize(newWords[i].Value)) return "";
        return (live.Length == 0 ? "" : " ") + final[newWords[oldWords.Count].Index..];
    }
}
