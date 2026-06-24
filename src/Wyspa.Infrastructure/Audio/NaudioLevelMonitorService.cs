using NAudio.Wave;
using Wyspa.Core.Abstractions;

namespace Wyspa.Infrastructure.Audio;

public sealed class NaudioLevelMonitorService : IAudioLevelMonitorService
{
    private WaveInEvent? _waveIn;

    public event EventHandler<float>? LevelAvailable;
    public event EventHandler<IReadOnlyList<float>>? AudioAvailable;
    public bool IsRunning => _waveIn is not null;

    public Task StartAsync(string? deviceId, CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            return Task.CompletedTask;
        }

        var waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(16000, 16, 1),
            BufferMilliseconds = 60,
            DeviceNumber = ParseDeviceNumber(deviceId)
        };

        waveIn.DataAvailable += OnDataAvailable;
        try
        {
            waveIn.StartRecording();
            _waveIn = waveIn;
        }
        catch
        {
            waveIn.DataAvailable -= OnDataAvailable;
            waveIn.Dispose();
            throw;
        }

        return Task.CompletedTask;
    }

    public void Stop()
    {
        if (_waveIn is null)
        {
            return;
        }

        _waveIn.DataAvailable -= OnDataAvailable;
        _waveIn.StopRecording();
        _waveIn.Dispose();
        _waveIn = null;
        LevelAvailable?.Invoke(this, 0f);
    }

    public void Dispose()
    {
        Stop();
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        var peak = 0f;
        var samples = new float[args.BytesRecorded / 2];
        for (var index = 0; index + 1 < args.BytesRecorded; index += 2)
        {
            var sample = BitConverter.ToInt16(args.Buffer, index);
            var level = sample / 32768f;
            samples[index / 2] = level;
            peak = Math.Max(peak, Math.Abs(level));
        }

        LevelAvailable?.Invoke(this, peak);
        AudioAvailable?.Invoke(this, samples);
    }

    private static int ParseDeviceNumber(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId == "-1")
        {
            return -1;
        }

        return int.TryParse(deviceId, out var id) ? id : -1;
    }
}
