namespace Wyspa.Core.Services;

// Disk-backed backlog: network delays never grow a queue of PCM arrays in memory.
// A snapshot contains at most 20 seconds and never interrupts microphone capture.
public sealed class StreamingAudioBuffer : IDisposable
{
    public const int SampleRate = 16000;
    public const int MaximumSamples = 20 * SampleRate;
    private readonly object _sync = new();
    private readonly string _path = Path.Combine(Path.GetTempPath(), "Wyspa", $"stream-{Guid.NewGuid():N}.pcm");
    private readonly FileStream _file;
    private bool _disposed;
    public StreamingAudioBuffer()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        _file = new FileStream(_path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
    }

    public long SampleCount { get { lock (_sync) return _disposed ? 0 : _file.Length / 2; } }

    public void Append(ReadOnlySpan<byte> pcm)
    {
        lock (_sync)
        {
            if (_disposed) return;
            _file.Position = _file.Length;
            _file.Write(pcm[..(pcm.Length - pcm.Length % 2)]);
        }
    }

    public StreamAudioWindow Read(long start, bool stopped, int maximumSamples = MaximumSamples)
    {
        float[] samples;
        long total;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            total = _file.Length / 2;
            var count = (int)Math.Clamp(total - start, 0, maximumSamples);
            var bytes = new byte[count * 2];
            _file.Position = start * 2;
            _file.ReadExactly(bytes);
            samples = new float[count];
            for (var i = 0; i < count; i++)
                samples[i] = System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(i * 2, 2)) / 32768f;
        }

        var voiced = 0;
        var end = samples.Length;
        for (var frame = 0; frame < samples.Length; frame += 320)
        {
            var count = Math.Min(320, samples.Length - frame);
            double energy = 0;
            for (var i = frame; i < frame + count; i++) energy += samples[i] * samples[i];
            if (Math.Sqrt(energy / count) >= (stopped ? 1d / 65536 : .001))
            {
                voiced += count;
            }
        }
        // Pauses never finalize a segment. Quiet endings and all intervening
        // silence remain in the audio supplied to the recognizer.
        return new(start, start + end, samples, voiced >= 320,
            stopped && start + end == total, end == maximumSamples && start + end < total);
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            _file.Dispose();
            File.Delete(_path);
        }
    }
}

public sealed record StreamAudioWindow(long Start, long End, float[] Samples, bool HasSpeech, bool Final, bool AtLimit)
{
    public double StartSeconds => Start / (double)StreamingAudioBuffer.SampleRate;
    public double Duration => Samples.Length / (double)StreamingAudioBuffer.SampleRate;
}
