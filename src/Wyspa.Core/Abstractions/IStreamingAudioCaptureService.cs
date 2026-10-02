namespace Wyspa.Core.Abstractions;

// Buffers are 16 kHz mono signed 16-bit PCM and are valid only during the callback.
public interface IStreamingAudioCaptureService
{
    event EventHandler<ReadOnlyMemory<byte>>? PcmAvailable;
}
