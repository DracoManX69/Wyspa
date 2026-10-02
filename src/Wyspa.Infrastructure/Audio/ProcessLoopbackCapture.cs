using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wasapi.CoreAudioApi.Interfaces;
using NAudio.Wave;

namespace Wyspa.Infrastructure.Audio;

// Windows process-tree loopback: VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK, available on build 20348+.
public sealed class ProcessLoopbackCapture : IWaveIn
{
    private readonly AudioClient _client;
    private readonly CancellationTokenSource _stop = new();
    private Task? _pump;
    public WaveFormat WaveFormat { get; set; } = new(48000, 16, 2);
    public event EventHandler<WaveInEventArgs>? DataAvailable;
    public event EventHandler<StoppedEventArgs>? RecordingStopped;
    private ProcessLoopbackCapture(AudioClient client)
    {
        _client = client;
        _client.Initialize(AudioClientShareMode.Shared,
            AudioClientStreamFlags.Loopback | AudioClientStreamFlags.EventCallback | (AudioClientStreamFlags)0x80000000,
            0, 0, WaveFormat, Guid.Empty); // AUTOCONVERTPCM, as in Microsoft's process-loopback sample.
    }

    public static async Task<ProcessLoopbackCapture> CreateAsync(int processId)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 20348))
            throw new PlatformNotSupportedException("App audio capture requires Windows build 20348 or newer (Windows 11 recommended). Choose Output device on this PC.");
        if (processId <= 0) throw new InvalidOperationException("Choose the app whose audio you want to capture.");
        using var target = System.Diagnostics.Process.GetProcessById(processId);
        if (target.HasExited) throw new InvalidOperationException("That app has closed. Refresh the app list and choose it again.");
        var completion = new ActivationCompletion();
        var parameters = new ActivationParameters { Type = 1, ProcessId = (uint)processId, Mode = 0 };
        var memory = Marshal.AllocHGlobal(Marshal.SizeOf<ActivationParameters>());
        Marshal.StructureToPtr(parameters, memory, false);
        var variant = new BlobVariant { Type = 65, Length = (uint)Marshal.SizeOf<ActivationParameters>(), Data = memory };
        IActivateAudioInterfaceAsyncOperation? operation = null;
        try
        {
            var iid = typeof(IAudioClient).GUID;
            Marshal.ThrowExceptionForHR(ActivateAudioInterfaceAsync("VAD\\Process_Loopback", ref iid, ref variant, completion, out operation));
            var client = await completion.Result.Task;
            try { return new ProcessLoopbackCapture(client); }
            catch { client.Dispose(); throw; }
        }
        finally
        {
            Marshal.FreeHGlobal(memory);
            if (operation is not null) Marshal.ReleaseComObject(operation);
            GC.KeepAlive(completion);
        }
    }

    public void StartRecording()
    {
        if (_pump is not null) throw new InvalidOperationException("Capture already started.");
        _pump = Task.Run(() =>
        {
            Exception? error = null;
            try
            {
                using var ready = new AutoResetEvent(false);
                _client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle());
                var capture = _client.AudioCaptureClient;
                _client.Start();
                while (!_stop.IsCancellationRequested)
                {
                    WaitHandle.WaitAny([ready, _stop.Token.WaitHandle], 100);
                    while (!_stop.IsCancellationRequested && capture.GetNextPacketSize() > 0)
                    {
                        var pointer = capture.GetBuffer(out var frames, out var flags);
                        try
                        {
                            var data = new byte[frames * WaveFormat.BlockAlign];
                            if ((flags & AudioClientBufferFlags.Silent) == 0) Marshal.Copy(pointer, data, 0, data.Length);
                            DataAvailable?.Invoke(this, new WaveInEventArgs(data, data.Length));
                        }
                        finally { capture.ReleaseBuffer(frames); }
                    }
                }
            }
            catch (Exception ex) { error = ex; }
            finally
            {
                try { _client.Stop(); } catch (Exception ex) { error ??= ex; }
                RecordingStopped?.Invoke(this, new StoppedEventArgs(error));
            }
        });
    }
    public void StopRecording() => _stop.Cancel();
    public void Dispose() { _stop.Cancel(); _pump?.GetAwaiter().GetResult(); _client.Dispose(); _stop.Dispose(); }

    [StructLayout(LayoutKind.Sequential)]
    private struct ActivationParameters { public int Type; public uint ProcessId; public int Mode; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct BlobVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public uint Length;
        [FieldOffset(16)] public IntPtr Data;
    }
    [ComImport, Guid("94EA2B94-E9CC-49E0-C0FF-EE64CA8F5B90"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAgileObject { }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class ActivationCompletion : IActivateAudioInterfaceCompletionHandler, IAgileObject
    {
        public TaskCompletionSource<AudioClient> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation operation)
        {
            try
            {
                operation.GetActivateResult(out var hr, out var activated);
                Marshal.ThrowExceptionForHR(hr);
                Result.TrySetResult(new AudioClient((IAudioClient)activated));
            }
            catch (Exception ex) { Result.TrySetException(ex); }
        }
    }
    [DllImport("Mmdevapi.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int ActivateAudioInterfaceAsync(string path, ref Guid iid, ref BlobVariant parameters,
        IActivateAudioInterfaceCompletionHandler completion, out IActivateAudioInterfaceAsyncOperation operation);
}
