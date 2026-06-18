using System.Windows.Forms;
using System.Runtime.InteropServices;
using Wyspa.Core.Abstractions;
using Wyspa.Core.Models;

namespace Wyspa.Infrastructure.Insertion;

public sealed class WindowsKeyboardCommandService : IKeyboardCommandService
{
    public Task SendAsync(KeyPressCommand command, CancellationToken cancellationToken)
    {
        if (command.Modifiers.Contains("Win", StringComparer.OrdinalIgnoreCase))
        {
            SendInputCommand(command);
            return Task.CompletedTask;
        }

        SendKeys.SendWait(ToSendKeys(command));
        return Task.CompletedTask;
    }

    private static string ToSendKeys(KeyPressCommand command)
    {
        var prefix = string.Concat(command.Modifiers.Select(modifier => modifier switch
        {
            "Ctrl" => "^",
            "Shift" => "+",
            "Alt" => "%",
            "Win" => string.Empty,
            _ => string.Empty
        }));

        var key = command.Key switch
        {
            "Enter" => "{ENTER}",
            "Escape" => "{ESC}",
            "Tab" => "{TAB}",
            "Space" => " ",
            "Backspace" => "{BACKSPACE}",
            "Delete" => "{DELETE}",
            "Up" => "{UP}",
            "Down" => "{DOWN}",
            "Left" => "{LEFT}",
            "Right" => "{RIGHT}",
            "Home" => "{HOME}",
            "End" => "{END}",
            "PageUp" => "{PGUP}",
            "PageDown" => "{PGDN}",
            _ when command.Key.StartsWith('F') => "{" + command.Key + "}",
            _ => command.Key
        };

        return prefix + key;
    }

    private static void SendInputCommand(KeyPressCommand command)
    {
        var keyCodes = command.Modifiers
            .Select(ToVirtualKey)
            .Where(key => key != 0)
            .Append(ToVirtualKey(command.Key))
            .Where(key => key != 0)
            .ToArray();

        if (keyCodes.Length == 0)
        {
            return;
        }

        var inputs = new List<Input>();
        foreach (var key in keyCodes)
        {
            inputs.Add(KeyInput(key, keyUp: false));
        }

        for (var index = keyCodes.Length - 1; index >= 0; index--)
        {
            inputs.Add(KeyInput(keyCodes[index], keyUp: true));
        }

        SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>());
    }

    private static ushort ToVirtualKey(string key) => key switch
    {
        "Ctrl" => 0x11,
        "Shift" => 0x10,
        "Alt" => 0x12,
        "Win" => 0x5B,
        "Enter" => 0x0D,
        "Escape" => 0x1B,
        "Tab" => 0x09,
        "Space" => 0x20,
        "Backspace" => 0x08,
        "Delete" => 0x2E,
        "Up" => 0x26,
        "Down" => 0x28,
        "Left" => 0x25,
        "Right" => 0x27,
        "Home" => 0x24,
        "End" => 0x23,
        "PageUp" => 0x21,
        "PageDown" => 0x22,
        { Length: 1 } when char.IsLetterOrDigit(key[0]) => (ushort)char.ToUpperInvariant(key[0]),
        _ when key.StartsWith('F') && int.TryParse(key[1..], out var number) && number is >= 1 and <= 24 => (ushort)(0x70 + number - 1),
        _ => 0
    };

    private static Input KeyInput(ushort virtualKey, bool keyUp) => new()
    {
        Type = 1,
        U = new InputUnion
        {
            Ki = new KeyboardInput
            {
                Vk = virtualKey,
                Scan = 0,
                Flags = keyUp ? 0x0002u : 0,
                Time = 0,
                ExtraInfo = UIntPtr.Zero
            }
        }
    };

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KeyboardInput Ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }
}
