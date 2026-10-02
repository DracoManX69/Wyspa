using System.ComponentModel;
using System.Text.Json.Serialization;
namespace Wyspa.Core.Models;

public enum ConversationMode { ComputerCall, InPerson }
public enum CallAudioMode { OutputDevice, Application }
public sealed record CaptureTarget(string Id, string Name);
public sealed record ConversationCaptureOptions(ConversationMode Mode, string? MicrophoneId, CallAudioMode AudioMode, string? OutputId, int ProcessId);
public sealed record AudioChunk(long Sequence, string Source, double Start, float[] Samples, double LeadingOverlap = 0)
{
    public double Duration => Samples.Length / 16000d;
}
public sealed record TimedWord(double Start, double End, string Text);
public sealed record SpeechResult(string Text, IReadOnlyList<TimedWord> Words);
public sealed record SpeakerTurn(double Start, double End, string Speaker);
public sealed record NoteEntry(Guid Id, long Chunk, double Start, double End, string Speaker, string Text, bool IsError = false);

public sealed class NoteSession : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public Guid Id { get; set; } = Guid.NewGuid();
    private string _title = "Conversation";
    public string Title
    {
        get => _title;
        set { _title = value; PropertyChanged?.Invoke(this, new(nameof(Title))); PropertyChanged?.Invoke(this, new(nameof(DisplayLabel))); }
    }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public string Kind { get; set; } = "Computer call";
    public string? SourceUrl { get; set; }
    public string? MySpeaker { get; set; }
    public string Summary { get; set; } = "";
    public string SummaryModel { get; set; } = "";
    public int SummaryEntryCount { get; set; }
    public List<NoteEntry> Entries { get; set; } = [];
    [JsonIgnore] public string DisplayLabel => $"{CreatedAt:dd MMM HH:mm} · {Title}";
    public override string ToString() => DisplayLabel;
}
