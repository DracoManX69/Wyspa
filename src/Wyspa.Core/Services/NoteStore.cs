using System.Text;
using System.Text.Json;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;

namespace Wyspa.Core.Services;

public sealed class NoteStore(string? directory = null) : INoteStore
{
    public string DirectoryPath { get; } = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Wyspa", "Notes");
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task SaveAsync(NoteSession note, CancellationToken token = default)
    {
        // Snapshot before any await: the live UI can continue adding entries while this save waits.
        var json = JsonSerializer.Serialize(note, JsonOptions);
        var text = Export(note);
        await _gate.WaitAsync(token);
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            await AtomicWriteAsync(Path.Combine(DirectoryPath, note.Id + ".json"), json, token);
            await AtomicWriteAsync(Path.Combine(DirectoryPath, note.Id + ".txt"), text, token);
        }
        finally { _gate.Release(); }
    }

    private static async Task AtomicWriteAsync(string path, string text, CancellationToken token)
    {
        var temporary = path + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, text, new UTF8Encoding(false), token);
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public async Task<IReadOnlyList<NoteSession>> LoadAsync(CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (!Directory.Exists(DirectoryPath)) return [];
            var notes = new List<NoteSession>();
            foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.json"))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var note = JsonSerializer.Deserialize<NoteSession>(await File.ReadAllTextAsync(path, token), JsonOptions);
                    if (note is not null && note.Entries is not null) notes.Add(note);
                }
                catch (JsonException) { /* Keep the damaged file available for recovery; load other notes. */ }
            }
            return notes.OrderByDescending(n => n.CreatedAt).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task DeleteAsync(Guid id, CancellationToken token = default)
    {
        await _gate.WaitAsync(token);
        try
        {
            foreach (var extension in new[] { ".json", ".txt", ".json.tmp", ".txt.tmp" })
                File.Delete(Path.Combine(DirectoryPath, id + extension));
        }
        finally { _gate.Release(); }
    }

    public static string Export(NoteSession note)
    {
        var result = new StringBuilder().AppendLine(note.Title).AppendLine(note.CreatedAt.ToString("f")).AppendLine(note.Kind);
        if (note.SourceUrl is not null) result.AppendLine(note.SourceUrl);
        if (!string.IsNullOrWhiteSpace(note.Summary)) result.AppendLine().AppendLine("SUMMARY").AppendLine(note.Summary);
        result.AppendLine().AppendLine("TRANSCRIPT");
        foreach (var entry in note.Entries.OrderBy(e => e.Start).ThenBy(e => e.Chunk))
            result.AppendLine($"[{TimeSpan.FromSeconds(entry.Start):hh\\:mm\\:ss}] {(entry.Speaker == note.MySpeaker ? "You (" + entry.Speaker + ")" : entry.Speaker)}: {entry.Text}");
        return result.ToString();
    }
}
