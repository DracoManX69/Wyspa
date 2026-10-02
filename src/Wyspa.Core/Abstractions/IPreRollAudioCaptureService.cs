namespace Wyspa.Core.Abstractions;

public interface IPreRollAudioCaptureService
{
    // Local 16 kHz mono PCM, supplied immediately before starting capture.
    void SetPreRoll(ReadOnlyMemory<byte> pcm);
}
