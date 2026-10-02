using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.Infrastructure.Audio;

public sealed class ConversationCapture(Func<AppSettings> settings) : IConversationCapture
{
    private readonly List<Source> _sources = [];
    private readonly List<IDisposable> _devices = [];
    private readonly Stopwatch _clock = new();
    private System.Threading.Timer? _timer;
    private double _offset;
    private long _sequence;
    public event EventHandler<AudioChunk>? ChunkAvailable;
    public event EventHandler<string>? Failed;
    public bool SupportsApplicationCapture => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348);
    public IReadOnlyList<CaptureTarget> GetOutputs()
    {
        using var enumerator = new MMDeviceEnumerator();
        var result = new List<CaptureTarget> { new("", "Windows default output") };
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            using (device) result.Add(new(device.ID, device.FriendlyName));
        return result;
    }
    public IReadOnlyList<CaptureTarget> GetApplications()
    {
        var results = new List<CaptureTarget>();
        foreach (var process in Process.GetProcesses())
            using (process)
                try
                {
                    if (process.Id != Environment.ProcessId && process.MainWindowHandle != IntPtr.Zero)
                        results.Add(new(process.Id.ToString(), $"{process.ProcessName} · {process.MainWindowTitle} ({process.Id})"));
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        return results.OrderBy(r => r.Name).ToArray();
    }
    public async Task StartAsync(ConversationCaptureOptions options, double offset, CancellationToken token)
    {
        if (_sources.Count > 0) throw new InvalidOperationException("Already capturing a conversation.");
        _offset = offset; _clock.Restart();
        try
        {
            token.ThrowIfCancellationRequested();
            var microphone = new WaveInEvent
            {
                WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 20,
                DeviceNumber = int.TryParse(options.MicrophoneId, out var number) ? number : -1
            };
            AddSource(microphone, options.Mode == ConversationMode.InPerson ? "Identifying speaker…" : "You");
            if (options.Mode == ConversationMode.ComputerCall)
            {
                if (options.AudioMode == CallAudioMode.Application)
                    AddSource(await ProcessLoopbackCapture.CreateAsync(options.ProcessId), "Other side");
                else
                {
                    using var enumerator = new MMDeviceEnumerator();
                    var device = string.IsNullOrEmpty(options.OutputId) ? enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia) : enumerator.GetDevice(options.OutputId);
                    _devices.Add(device);
                    AddSource(new WasapiLoopbackCapture(device), "Other side");
                }
            }
            token.ThrowIfCancellationRequested();
            foreach (var source in _sources) source.Start();
            _timer = new System.Threading.Timer(_ =>
            {
                foreach (var source in _sources) source.Tick(_offset + _clock.Elapsed.TotalSeconds);
            }, null, 100, 100);
        }
        catch { await StopAsync(); throw; }
    }
    private void AddSource(IWaveIn capture, string label)
    {
        var options = settings();
        _sources.Add(new Source(capture, () => _offset + _clock.Elapsed.TotalSeconds,
            (start, samples, overlap) => ChunkAvailable?.Invoke(this, new(Interlocked.Increment(ref _sequence), label, start, samples, overlap)),
            error => Failed?.Invoke(this, error), Math.Clamp(options.NoteChunkSeconds, 1.5, 10), Math.Clamp(options.NoteSpeechThreshold, .001f, .1f)));
    }
    public async Task StopAsync()
    {
        if (_timer is not null) { await _timer.DisposeAsync(); _timer = null; }
        var errors = new List<Exception>();
        foreach (var source in _sources)
            try { await source.StopAsync(); } catch (Exception ex) { errors.Add(ex); }
        _sources.Clear();
        foreach (var device in _devices) device.Dispose();
        _devices.Clear(); _clock.Stop();
        if (errors.Count > 0) throw new AggregateException("An audio source failed to stop cleanly.", errors);
    }
    public async ValueTask DisposeAsync() => await StopAsync();

    private sealed class Source
    {
        private readonly IWaveIn _capture;
        private readonly Func<double> _now;
        private readonly Action<string> _failed;
        private readonly SpeechChunker _chunker;
        private readonly object _gate = new();
        private readonly WdlResampler _resampler = new();
        private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _started;
        private bool _stopping;
        private double _next;
        public Source(IWaveIn capture, Func<double> now, Action<double, float[], double> emit, Action<string> failed, double maximum, float threshold)
        {
            _capture = capture; _now = now; _failed = failed;
            _chunker = new SpeechChunker(emit, maximum, threshold);
            _resampler.SetMode(true, 2, false); _resampler.SetFilterParms(); _resampler.SetFeedMode(true);
            _resampler.SetRates(capture.WaveFormat.SampleRate, 16000);
            _capture.DataAvailable += Data;
            _capture.RecordingStopped += (_, args) =>
            {
                _stopped.TrySetResult();
                if (args.Exception is not null || !_stopping)
                    _failed(args.Exception?.Message ?? "An audio device stopped. The session has been paused.");
            };
        }
        public void Start() { _capture.StartRecording(); _started = true; }
        public void Tick(double time) { lock (_gate) _chunker.Tick(time); }
        private void Data(object? sender, WaveInEventArgs args)
        {
            lock (_gate)
            {
                try
                {
                    var format = _capture.WaveFormat;
                    var frameCount = args.BytesRecorded / format.BlockAlign;
                    var available = _resampler.ResamplePrepare(frameCount, 1, out var input, out var inputOffset);
                    if (available < frameCount) throw new InvalidOperationException("Audio resampling buffer was too small.");
                    var floating = format.Encoding == WaveFormatEncoding.IeeeFloat ||
                        (format is WaveFormatExtensible extended && extended.SubFormat == new Guid("00000003-0000-0010-8000-00aa00389b71"));
                    for (var frame = 0; frame < frameCount; frame++)
                    {
                        float sum = 0;
                        for (var channel = 0; channel < format.Channels; channel++)
                        {
                            var i = frame * format.BlockAlign + channel * format.BitsPerSample / 8;
                            sum += floating ? BitConverter.ToSingle(args.Buffer, i) : format.BitsPerSample switch
                            {
                                16 => BitConverter.ToInt16(args.Buffer, i) / 32768f,
                                24 => ((args.Buffer[i] | args.Buffer[i + 1] << 8 | args.Buffer[i + 2] << 16) << 8) / 2147483648f,
                                32 => BitConverter.ToInt32(args.Buffer, i) / 2147483648f,
                                _ => throw new NotSupportedException("This output format is unsupported. Choose another device.")
                            };
                        }
                        input[inputOffset + frame] = sum / format.Channels;
                    }
                    var output = new float[(int)Math.Ceiling(frameCount * 16000d / format.SampleRate) + 64];
                    var length = _resampler.ResampleOut(output, 0, frameCount, output.Length, 1);
                    if (length == 0) return;
                    var start = _now() - length / 16000d;
                    if (_next == 0 || Math.Abs(start - _next) > .25) _next = Math.Max(0, start);
                    _chunker.Add(output[..length], _next); _next += length / 16000d;
                }
                catch (Exception ex) { _failed("Audio capture failed: " + ex.Message); }
            }
        }
        public async Task StopAsync()
        {
            _stopping = true;
            try
            {
                if (_started) { _capture.StopRecording(); await _stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            }
            finally
            {
                _capture.DataAvailable -= Data; _capture.Dispose();
                lock (_gate) _chunker.Flush();
            }
        }
    }
}
