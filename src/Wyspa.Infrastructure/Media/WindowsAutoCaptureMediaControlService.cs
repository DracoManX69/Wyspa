using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;

namespace Wyspa.Infrastructure.Media;

public sealed class WindowsAutoCaptureMediaControlService : IAutoCaptureMediaControlService
{
    private readonly object _gate = new();
    private AutoCaptureMediaBehavior _activeBehavior = AutoCaptureMediaBehavior.None;
    private bool? _previousMuteState;

    public Task SetListeningStateAsync(AutoCaptureMediaBehavior behavior, bool isListening, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_activeBehavior is not AutoCaptureMediaBehavior.None && (!isListening || behavior != _activeBehavior))
            {
                RestoreActiveBehavior();
            }

            if (!isListening || behavior is AutoCaptureMediaBehavior.None || _activeBehavior == behavior)
            {
                return Task.CompletedTask;
            }

            ApplyBehavior(behavior);
            _activeBehavior = behavior;
        }

        return Task.CompletedTask;
    }

    public Task RestoreAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            RestoreActiveBehavior();
        }

        return Task.CompletedTask;
    }

    private void ApplyBehavior(AutoCaptureMediaBehavior behavior)
    {
        switch (behavior)
        {
            case AutoCaptureMediaBehavior.MuteSystemOutput:
                _previousMuteState = GetSystemMute();
                SetSystemMute(mute: true);
                break;
            case AutoCaptureMediaBehavior.TogglePlayPause:
                SendPlayPause();
                break;
            case AutoCaptureMediaBehavior.None:
            default:
                break;
        }
    }

    private void RestoreActiveBehavior()
    {
        switch (_activeBehavior)
        {
            case AutoCaptureMediaBehavior.MuteSystemOutput:
                if (_previousMuteState.HasValue)
                {
                    SetSystemMute(_previousMuteState.Value);
                }

                break;
            case AutoCaptureMediaBehavior.TogglePlayPause:
                SendPlayPause();
                break;
            case AutoCaptureMediaBehavior.None:
            default:
                break;
        }

        _activeBehavior = AutoCaptureMediaBehavior.None;
        _previousMuteState = null;
    }

    private static bool GetSystemMute()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return device.AudioEndpointVolume.Mute;
    }

    private static void SetSystemMute(bool mute)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        device.AudioEndpointVolume.Mute = mute;
    }

    private static void SendPlayPause()
    {
        var inputs = new[]
        {
            Input.Keyboard(VirtualKeyMediaPlayPause, 0),
            Input.Keyboard(VirtualKeyMediaPlayPause, KeyEventKeyUp)
        };

        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent != inputs.Length)
        {
            throw new InvalidOperationException("Could not send the Windows media play/pause key.");
        }
    }

    private const ushort VirtualKeyMediaPlayPause = 0xB3;
    private const uint KeyEventKeyUp = 0x0002;
    private const int InputKeyboard = 1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public int Type;
        public InputUnion Data;

        public static Input Keyboard(ushort virtualKey, uint flags) => new()
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyboardInput
                {
                    VirtualKey = virtualKey,
                    Flags = flags
                }
            }
        };
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MouseInput Mouse;

        [FieldOffset(0)]
        public KeyboardInput Keyboard;

        [FieldOffset(0)]
        public HardwareInput Hardware;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int X;
        public int Y;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HardwareInput
    {
        public uint Message;
        public ushort ParamLow;
        public ushort ParamHigh;
    }
}
