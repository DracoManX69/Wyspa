namespace Wyspa.Core.Models;

public sealed record RecordingResult(
    string FilePath,
    TimeSpan Duration,
    long BytesWritten,
    float PeakLevel)
{
    public bool LooksSilent => Duration < TimeSpan.FromMilliseconds(300) || BytesWritten < 4096 || PeakLevel < 0.012f;
}
