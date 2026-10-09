using NAudio.CoreAudioApi;
using Windows.Media.Control;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;

namespace Wyspa.Infrastructure.Media;

public sealed class WindowsAutoCaptureMediaControlService : IAutoCaptureMediaControlService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private AutoCaptureMediaBehavior _activeBehavior;
    private bool? _previousMuteState;
    private readonly List<GlobalSystemMediaTransportControlsSession> _paused = [];

    public async Task SetListeningStateAsync(AutoCaptureMediaBehavior behavior, bool isListening, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            if (_activeBehavior != AutoCaptureMediaBehavior.None && (!isListening || behavior != _activeBehavior)) await RestoreCoreAsync(token);
            if (!isListening || behavior == AutoCaptureMediaBehavior.None || _activeBehavior == behavior) return;
            _activeBehavior = behavior;
            try
            {
            if (behavior == AutoCaptureMediaBehavior.MuteSystemOutput)
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _previousMuteState = device.AudioEndpointVolume.Mute; device.AudioEndpointVolume.Mute = true;
            }
            else
            {
                // Pause only sessions that report playing, never a blind play/pause key.
                var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask(token);
                foreach (var session in manager.GetSessions())
                {
                    try
                    {
                        if (session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing && await session.TryPauseAsync().AsTask(token)) _paused.Add(session);
                    }
                    catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
                }
            }
            }
            catch { await RestoreCoreAsync(CancellationToken.None); throw; }
        }
        finally { _gate.Release(); }
    }
    public async Task RestoreAsync(CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try { await RestoreCoreAsync(token); }
        finally { _gate.Release(); }
    }
    private async Task RestoreCoreAsync(CancellationToken token)
    {
        if (_activeBehavior == AutoCaptureMediaBehavior.MuteSystemOutput && _previousMuteState is { } previous)
        {
            using var enumerator = new MMDeviceEnumerator();
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            device.AudioEndpointVolume.Mute = previous;
        }
        foreach (var session in _paused)
        {
            try
            {
                if (session.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused) await session.TryPlayAsync().AsTask(token);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException) { }
        }
        _paused.Clear(); _previousMuteState = null; _activeBehavior = AutoCaptureMediaBehavior.None;
    }
}
