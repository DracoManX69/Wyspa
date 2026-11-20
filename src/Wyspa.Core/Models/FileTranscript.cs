namespace Wyspa.Core.Models;

public sealed record TranscriptSegment(double Start, double End, string Text);
public sealed record FileTranscript(string Text, IReadOnlyList<TranscriptSegment> Segments);
