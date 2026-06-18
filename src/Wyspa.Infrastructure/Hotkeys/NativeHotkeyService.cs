using System.Runtime.InteropServices;
using System.Windows.Forms;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;
using Wyspa.Core.Services;

namespace Wyspa.Infrastructure.Hotkeys;

public sealed class NativeHotkeyService : NativeWindow, IHotkeyService
{
    private const int HotkeyId = 0x565458;
    private const int WmHotkey = 0x0312;
    private const int WhKeyboardLl = 13;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyUp = 0x0105;

    public event EventHandler? Pressed;
    public event EventHandler? Released;
    public bool IsRegistered { get; private set; }
    private HotkeySettings? _registeredHotkey;
    private uint _registeredVirtualKey;
    private bool _isPressed;
    private readonly LowLevelKeyboardProc _keyboardProc;
    private IntPtr _hookHandle;

    public NativeHotkeyService()
    {
        _keyboardProc = KeyboardHookCallback;
        CreateHandle(new CreateParams());
        _hookHandle = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, GetModuleHandle(null), 0);
    }

    public bool TryRegister(HotkeySettings hotkey, out string? errorMessage)
    {
        Unregister();

        if (!HotkeyValidator.TryValidate(hotkey, out errorMessage))
        {
            return false;
        }

        var virtualKey = KeyToVirtualKey(hotkey.Key);
        if (virtualKey == 0)
        {
            errorMessage = "That hotkey key is not supported yet.";
            return false;
        }

        if (!RegisterHotKey(Handle, HotkeyId, ToNativeModifiers(hotkey.Modifiers), virtualKey))
        {
            errorMessage = "That hotkey is already in use by another app.";
            return false;
        }

        _registeredHotkey = hotkey;
        _registeredVirtualKey = virtualKey;
        _isPressed = false;
        IsRegistered = true;
        errorMessage = null;
        return true;
    }

    public void Unregister()
    {
        if (IsRegistered)
        {
            UnregisterHotKey(Handle, HotkeyId);
            _registeredHotkey = null;
            _registeredVirtualKey = 0;
            _isPressed = false;
            IsRegistered = false;
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
        {
            _isPressed = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        base.WndProc(ref m);
    }

    public void Dispose()
    {
        Unregister();
        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
        DestroyHandle();
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 &&
            _isPressed &&
            _registeredHotkey is not null &&
            (wParam == WmKeyUp || wParam == WmSysKeyUp))
        {
            var info = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
            if (info.VkCode == _registeredVirtualKey)
            {
                _isPressed = false;
                Released?.Invoke(this, EventArgs.Empty);
            }
        }

        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private static uint ToNativeModifiers(HotkeyModifiers modifiers)
    {
        uint native = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) native |= 0x0001;
        if (modifiers.HasFlag(HotkeyModifiers.Control)) native |= 0x0002;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) native |= 0x0004;
        if (modifiers.HasFlag(HotkeyModifiers.Windows)) native |= 0x0008;
        return native;
    }

    private static uint KeyToVirtualKey(string key)
    {
        if (string.Equals(key, "Space", StringComparison.OrdinalIgnoreCase)) return 0x20;
        if (string.Equals(key, "Enter", StringComparison.OrdinalIgnoreCase)) return 0x0D;
        if (string.Equals(key, "Tab", StringComparison.OrdinalIgnoreCase)) return 0x09;
        if (string.Equals(key, "Pause", StringComparison.OrdinalIgnoreCase)) return 0x13;
        if (string.Equals(key, "Insert", StringComparison.OrdinalIgnoreCase)) return 0x2D;
        if (string.Equals(key, "Delete", StringComparison.OrdinalIgnoreCase)) return 0x2E;
        if (string.Equals(key, "Home", StringComparison.OrdinalIgnoreCase)) return 0x24;
        if (string.Equals(key, "End", StringComparison.OrdinalIgnoreCase)) return 0x23;
        if (string.Equals(key, "PageUp", StringComparison.OrdinalIgnoreCase)) return 0x21;
        if (string.Equals(key, "PageDown", StringComparison.OrdinalIgnoreCase)) return 0x22;
        if (key.Length == 1 && char.IsLetterOrDigit(key[0])) return char.ToUpperInvariant(key[0]);
        if (key.StartsWith('F') && int.TryParse(key[1..], out var functionKey) && functionKey is >= 1 and <= 24)
        {
            return (uint)(0x70 + functionKey - 1);
        }

        if (key.StartsWith('D') && key.Length == 2 && char.IsDigit(key[1]))
        {
            return key[1];
        }

        return 0;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr DwExtraInfo;
    }
}
