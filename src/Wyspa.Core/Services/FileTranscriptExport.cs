using System.Globalization;
using System.Text;
using System.Text.Json;
using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

public static class FileTranscriptExport
{
    public static FileTranscript Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var text = root.GetProperty("text").GetString()?.Trim() ?? "";
            var segments = ReadSegments(root, "segments", "text");
            if (segments.Count == 0)
                segments = GroupWords(ReadSegments(root, "words", "word"));
            if (segments.Count == 0 && !string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("The transcription provider returned text without speech timestamps. Try transcribing again.");
            if (string.IsNullOrWhiteSpace(text)) text = string.Join(" ", segments.Select(s => s.Text));
            return new(text, segments.OrderBy(s => s.Start).ToArray());
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or OverflowException or KeyNotFoundException)
        {
            throw new InvalidOperationException("Could not read the speech timestamps. " +
                (ex is InvalidOperationException && ex.Message.StartsWith("The transcription provider", StringComparison.Ordinal)
                    ? ex.Message : "Try transcribing again."), ex);
        }
    }

    private static List<TranscriptSegment> ReadSegments(JsonElement root, string property, string textProperty)
    {
        var result = new List<TranscriptSegment>();
        if (!root.TryGetProperty(property, out var items)) return result;
        foreach (var item in items.EnumerateArray())
        {
            var text = item.GetProperty(textProperty).GetString()?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(text)) continue;
            var start = item.GetProperty("start").GetDouble();
            var end = item.GetProperty("end").GetDouble();
            if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end < start || end >= TimeSpan.MaxValue.TotalSeconds)
                throw new InvalidOperationException("Invalid speech timestamps.");
            result.Add(new(start, end, text));
        }
        return result;
    }

    // Local engines expose timed words or larger timed spans. Keep their supplied
    // timings and batch adjacent words into readable phrases without inventing times.
    private static List<TranscriptSegment> GroupWords(List<TranscriptSegment> words)
    {
        var result = new List<TranscriptSegment>();
        foreach (var word in words.OrderBy(w => w.Start))
        {
            if (result.Count == 0 || word.Start - result[^1].End >= .8 || word.End - result[^1].Start > 15 ||
                ".!?。！？".Contains(result[^1].Text[^1]))
                result.Add(word);
            else
            {
                var previous = result[^1];
                result[^1] = previous with { End = Math.Max(previous.End, word.End), Text = previous.Text + " " + word.Text };
            }
        }
        return result;
    }

    public static string Timestamp(double seconds)
    {
        var time = TimeSpan.FromMilliseconds(Math.Round(seconds * 1000, MidpointRounding.AwayFromZero));
        return string.Create(CultureInfo.InvariantCulture, $"{(long)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}.{time.Milliseconds:000}");
    }

    public static string ToText(IEnumerable<TranscriptSegment> segments) => string.Join(Environment.NewLine,
        segments.Select(s => $"[{Timestamp(s.Start)} – {Timestamp(s.End)}] {s.Text}"));

    public static string ToCsv(IEnumerable<TranscriptSegment> segments)
    {
        var csv = new StringBuilder("start,end,text\r\n");
        foreach (var segment in segments)
            csv.Append(Timestamp(segment.Start)).Append(',').Append(Timestamp(segment.End)).Append(',')
                .Append('"').Append(segment.Text.Replace("\"", "\"\"")).Append("\"\r\n");
        return csv.ToString();
    }
}

public sealed class FileTranscriptCsvStore(string? directory = null)
{
    public string DirectoryPath { get; } = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wyspa", "FileTranscripts");

    public async Task<string> SaveAsync(string sourcePath, IReadOnlyList<TranscriptSegment> segments, CancellationToken token)
    {
        Directory.CreateDirectory(DirectoryPath);
        var path = Path.Combine(DirectoryPath, Path.GetFileNameWithoutExtension(sourcePath) + "-" + Guid.NewGuid().ToString("N") + ".csv");
        var temporary = path + ".tmp";
        try
        {
            // BOM preserves non-ASCII speech when opened directly in Excel.
            await File.WriteAllTextAsync(temporary, FileTranscriptExport.ToCsv(segments), new UTF8Encoding(true), token);
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path);
            return path;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
