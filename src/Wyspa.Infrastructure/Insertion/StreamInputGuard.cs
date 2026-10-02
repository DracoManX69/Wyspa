using System.Runtime.InteropServices;

namespace Wyspa.Infrastructure.Insertion;

// Observe user input only while a dictation owns an insertion point. No key text
// is retained or logged. Never suppress input. A user action permanently yields
// ownership, even if the caret later returns to the original position.
internal sealed class StreamInputGuard : IDisposable
{
    internal static readonly UIntPtr InputTag = (UIntPtr)0x57595350;
    private readonly Hook _keyboardCallback, _mouseCallback;
    private IntPtr _keyboard, _mouse;
    private volatile bool _interrupted;
    public bool Interrupted => _interrupted;
    public void Interrupt() => _interrupted = true;
    public bool IsAvailable => _keyboard != IntPtr.Zero && _mouse != IntPtr.Zero;

    public StreamInputGuard()
    {
        _keyboardCallback = Keyboard;
        _mouseCallback = Mouse;
        _keyboard = SetWindowsHookEx(13, _keyboardCallback, GetModuleHandle(null), 0);
        _mouse = SetWindowsHookEx(14, _mouseCallback, GetModuleHandle(null), 0);
    }

    private IntPtr Keyboard(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && ((int)message is 0x100 or 0x104))
        {
            var key = Marshal.PtrToStructure<KeyboardData>(data);
            var modifier = key.Key is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5;
            var stopShortcut = key.Key == 0x20 && (GetAsyncKeyState(0x11) < 0 || GetAsyncKeyState(0x12) < 0);
            if (key.Extra != InputTag && !modifier && !stopShortcut) Interrupt();
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private IntPtr Mouse(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && ((int)message is 0x201 or 0x204 or 0x207)) Interrupt();
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    public void Dispose()
    {
        if (_keyboard != IntPtr.Zero) UnhookWindowsHookEx(_keyboard);
        if (_mouse != IntPtr.Zero) UnhookWindowsHookEx(_mouse);
        _keyboard = _mouse = IntPtr.Zero;
    }
    private delegate IntPtr Hook(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] private struct KeyboardData { public uint Key, Scan, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll")] private static extern IntPtr SetWindowsHookEx(int id, Hook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
