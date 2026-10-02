using NAudio.Wave;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;

namespace Wyspa.Infrastructure.Audio;

public sealed class NaudioCaptureService : IAudioCaptureService, IStreamingAudioCaptureService, IPreRollAudioCaptureService
{
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private string? _currentPath;
    private DateTimeOffset _startedAt;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private long _bytesWritten;
    private float _peakLevel;
    private TaskCompletionSource? _recordingStopped;
    private byte[] _preRoll = [];
    public void SetPreRoll(ReadOnlyMemory<byte> pcm) => _preRoll = pcm.ToArray();

    public event EventHandler<float>? LevelAvailable;
    public event EventHandler<ReadOnlyMemory<byte>>? PcmAvailable;
    public bool IsRecording => _waveIn is not null;

    public Task<IReadOnlyList<AudioDeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken)
    {
        var devices = new List<AudioDeviceInfo>
        {
            new("-1", "Windows default microphone", IsDefault: true)
        };

        for (var index = 0; index < WaveInEvent.DeviceCount; index++)
        {
            var caps = WaveInEvent.GetCapabilities(index);
            devices.Add(new AudioDeviceInfo(index.ToString(System.Globalization.CultureInfo.InvariantCulture), caps.ProductName));
        }

        return Task.FromResult<IReadOnlyList<AudioDeviceInfo>>(devices);
    }

    public async Task StartRecordingAsync(string? deviceId, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsRecording)
            {
                return;
            }

            Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "Wyspa"));
            _currentPath = Path.Combine(Path.GetTempPath(), "Wyspa", $"dictation-{Guid.NewGuid():N}.wav");
            _startedAt = DateTimeOffset.UtcNow;
            _bytesWritten = 0;
            _peakLevel = 0;
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 40,
                DeviceNumber = ParseDeviceNumber(deviceId)
            };
            _writer = new WaveFileWriter(_currentPath, _waveIn.WaveFormat);
            if (_preRoll.Length > 0)
            {
                _writer.Write(_preRoll, 0, _preRoll.Length);
                _bytesWritten = _preRoll.Length;
                for (var i = 0; i + 1 < _preRoll.Length; i += 2)
                    _peakLevel = Math.Max(_peakLevel, Math.Abs(BitConverter.ToInt16(_preRoll, i) / 32768f));
                PcmAvailable?.Invoke(this, _preRoll);
                _preRoll = [];
            }
            _recordingStopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _waveIn.RecordingStopped += OnRecordingStopped;
            _waveIn.DataAvailable += OnDataAvailable;
            try { _waveIn.StartRecording(); }
            catch
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.RecordingStopped -= OnRecordingStopped;
                _waveIn.Dispose(); _writer.Dispose();
                _waveIn = null; _writer = null;
                File.Delete(_currentPath); _currentPath = null;
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RecordingResult> StopRecordingAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_waveIn is null || _writer is null || _currentPath is null)
            {
                throw new InvalidOperationException("Wyspa is not currently recording.");
            }

            var path = _currentPath;
            var duration = DateTimeOffset.UtcNow - _startedAt;
            _waveIn.StopRecording();
            // Drain the final callback before closing the WAV or ending a stream.
            if (_recordingStopped is not null) await _recordingStopped.Task;
            _waveIn.DataAvailable -= OnDataAvailable;
            _waveIn.RecordingStopped -= OnRecordingStopped;
            _waveIn.Dispose();
            _writer.Dispose();
            var bytesWritten = _bytesWritten;
            var peakLevel = _peakLevel;
            _waveIn = null;
            _writer = null;
            _currentPath = null;
            return new RecordingResult(path, duration, bytesWritten, peakLevel);
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task DeleteRecordingAsync(string filePath, CancellationToken cancellationToken)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _waveIn?.Dispose();
            _writer?.Dispose();
            _gate.Dispose();
        }
        finally
        {
        }
    }

    private static int ParseDeviceNumber(string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || deviceId == "-1")
        {
            return -1;
        }

        return int.TryParse(deviceId, out var id) ? id : -1;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs args)
    {
        _writer?.Write(args.Buffer, 0, args.BytesRecorded);
        _writer?.Flush();
        Interlocked.Add(ref _bytesWritten, args.BytesRecorded);
        PcmAvailable?.Invoke(this, args.Buffer.AsMemory(0, args.BytesRecorded));

        var localPeak = 0f;
        for (var index = 0; index + 1 < args.BytesRecorded; index += 2)
        {
            var sample = BitConverter.ToInt16(args.Buffer, index);
            var level = Math.Abs(sample / 32768f);
            if (level > localPeak)
            {
                localPeak = level;
            }
        }

        if (localPeak > _peakLevel)
        {
            _peakLevel = localPeak;
        }

        LevelAvailable?.Invoke(this, localPeak);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs args)
    {
        _recordingStopped?.TrySetResult();
    }
}
