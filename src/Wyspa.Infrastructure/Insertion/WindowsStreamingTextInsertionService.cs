using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using Wyspa.Core.Models;
using Clipboard = System.Windows.Forms.Clipboard;

namespace Wyspa.Infrastructure.Insertion;

public sealed partial class WindowsTextInsertionService
{
    private int[]? _streamTarget;
    private string? _selectionMismatch;
    private bool _streamCanCorrect;
    private volatile bool _streamPaused;
    private string _streamTyped = "", _streamPrefix = "", _streamSuffix = "";
    private StreamInputGuard? _streamGuard;
    private InsertionMode _streamInsertionMode;
    private AutomationFocusChangedEventHandler? _focusHandler;
    public string? StreamFallbackReason { get; private set; }

    public void BeginStream(InsertionMode mode = InsertionMode.Paste)
    {
        EndStream();
        _streamInsertionMode = mode;
        _streamPaused = false;
        _streamCanCorrect = false;
        StreamFallbackReason = null;
        _streamTyped = _streamPrefix = _streamSuffix = "";
    }

    public void EndStream()
    {
        if (_focusHandler is not null)
        {
            AutomationEventThread.Remove(_focusHandler);
            _focusHandler = null;
        }
        _streamGuard?.Dispose(); _streamGuard = null;
        _streamTarget = null;
        _streamTyped = _streamPrefix = _streamSuffix = "";
        _streamPaused = true;
    }

    public async Task<bool> AppendStreamAsync(string delta, string cumulativeText, CancellationToken cancellationToken)
    {
        await CopyStreamAsync(cumulativeText, cancellationToken);
        if (_streamPaused || _streamGuard?.Interrupted == true) return PauseStream("Input or focus changed");
        if (delta.Length == 0) return true;
        try
        {
            var focused = AutomationElement.FocusedElement;
            if (!IsEditable(focused)) return PauseStream("Field is not editable");
            var identity = focused.GetRuntimeId();
            if (_streamTarget is null)
            {
                // Accessibility text ranges are optional for live typing. Capture a
                // correction anchor when available; unsupported editors still receive
                // ordinary keyboard input without a review/paste step.
                if (HasExistingSelection(focused)) return PauseStream("Existing text selected");
                var snapshot = TryReadSelection(focused);
                _streamCanCorrect = snapshot is not null;
                if (snapshot is not null) (_streamPrefix, _streamSuffix) = snapshot.Value;
                _streamTarget = identity;
                var guard = _streamGuard = new StreamInputGuard();
                if (!guard.IsAvailable) return PauseStream("Input observation unavailable");
                // Capture this session's guard: a delayed callback must never pause
                // a later dictation that has already acquired its own target.
                _focusHandler = (sender, _) =>
                {
                    try
                    {
                        if (sender is not AutomationElement element || !identity.SequenceEqual(element.GetRuntimeId())) guard.Interrupt("Focus notification");
                    }
                    catch { guard.Interrupt("Focus notification"); }
                };
                await AutomationEventThread.AddAsync(_focusHandler);
            }
            // Do not make typing depend on text readback (some editors do not expose
            // it, or normalize text as it arrives). User input/focus guards still yield
            // the session permanently when the user moves away or edits manually.
            if (!HasStreamFocus()) return PauseStream("Input or focus changed");
            cancellationToken.ThrowIfCancellationRequested();
            var inputs = UnicodeInputs(delta);
            var sent = StreamSendInput((uint)inputs.Length, inputs, Marshal.SizeOf<StreamInput>());
            _streamTyped += delta;
            if (sent != inputs.Length) return PauseStream();
            return await SettleLiveDeliveryAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return PauseStream("Editor access failed (" + ex.GetType().Name + ")"); }
    }

    public async Task<bool> CompleteStreamAsync(string finalText, bool allowCorrection, CancellationToken cancellationToken)
    {
        await CopyStreamAsync(finalText, cancellationToken);
        if (_streamPaused || _streamGuard?.Interrupted == true) return false;
        TextPatternRange? owned = null;
        var sentInput = false;
        try
        {
            if (_streamTyped == finalText) return HasStreamFocus();
            if (!allowCorrection || !_streamCanCorrect || _streamTyped.Length == 0) return false;
            // A held stop shortcut must not turn Ctrl+V/Delete into another command.
            for (var attempt = 0; ModifiersPressed() && attempt < 100; attempt++) await Task.Delay(20, cancellationToken);
            if (ModifiersPressed()) return PauseStream("Release shortcut keys to finish insertion");
            if (!await VerifyStreamDeliveryAsync()) return false;
            var focused = AutomationElement.FocusedElement;
            if (!MatchesStream(focused)) return PauseStream("Text, caret or focus changed");
            var text = (TextPattern)focused.GetCurrentPattern(TextPattern.Pattern);
            var caret = text.GetSelection()[0];
            var beforeCaret = text.DocumentRange.Clone();
            beforeCaret.MoveEndpointByRange(TextPatternRangeEndpoint.End, caret, TextPatternRangeEndpoint.Start);
            // Search for the ENTIRE emitted passage, bounded by the verified caret.
            // Repeated first/last words elsewhere in a document cannot identify it.
            owned = beforeCaret.FindText(_streamTyped, true, false);
            if (owned is null || owned.GetText(-1) != _streamTyped ||
                owned.CompareEndpoints(TextPatternRangeEndpoint.End, caret, TextPatternRangeEndpoint.Start) != 0)
                return PauseStream("Dictation range unavailable");
            cancellationToken.ThrowIfCancellationRequested();
            owned.Select();
            if (!await WaitForSelectedStreamAsync(owned, cancellationToken)) return PauseStream("Dictation selection could not be verified: " + _selectionMismatch);
            cancellationToken.ThrowIfCancellationRequested();
            var inputs = finalText.Length == 0 ? new[] { KeyInput(0x2E, false), KeyInput(0x2E, true) } :
                _streamInsertionMode == InsertionMode.Type ? UnicodeInputs(finalText) :
                new[] { KeyInput(0x11, false), KeyInput(0x56, false), KeyInput(0x56, true), KeyInput(0x11, true) };
            // No await between the last ownership check and the single input batch.
            if (ModifiersPressed() || _streamGuard?.Interrupted != false ||
                finalText.Length > 0 && _streamInsertionMode == InsertionMode.Paste && Clipboard.GetText() != finalText)
                return PauseStream("Input or clipboard changed");
            if (!MatchesSelectedStream(owned)) return PauseStream("Text, caret or focus changed");
            sentInput = true;
            var sent = StreamSendInput((uint)inputs.Length, inputs, Marshal.SizeOf<StreamInput>());
            _streamTyped = finalText;
            if (sent != inputs.Length) return PauseStream();
            return await VerifyStreamDeliveryAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return PauseStream("Editor access failed (" + ex.GetType().Name + ")"); }
        finally
        {
            // If selecting succeeded but delivery was abandoned, restore the caret
            // only while that exact selection is still ours. Never undo user input.
            if (!sentInput && owned is not null)
            {
                try
                {
                    if (MatchesSelectedStream(owned, allowPaused: true))
                    {
                        owned.MoveEndpointByRange(TextPatternRangeEndpoint.Start, owned, TextPatternRangeEndpoint.End);
                        owned.Select();
                    }
                }
                catch { }
            }
        }
    }

    private async Task<bool> WaitForSelectedStreamAsync(TextPatternRange owned, CancellationToken token)
    {
        // Browser providers can acknowledge Select before GetSelection reflects it.
        // Wait only for that same verified range; never select again or weaken the
        // focus/input/context checks, and never inject into an unconfirmed range.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            token.ThrowIfCancellationRequested();
            if (MatchesSelectedStream(owned)) return true;
            if (_streamPaused || _streamGuard?.Interrupted != false) return false;
            await Task.Delay(20, token);
        }
        return false;
    }

    private bool MatchesSelectedStream(TextPatternRange owned, bool allowPaused = false)
    {
        var focused = AutomationElement.FocusedElement;
        if ((!allowPaused && _streamPaused) || _streamGuard?.Interrupted != false || !IsEditable(focused) ||
            _streamTarget is null || !_streamTarget.SequenceEqual(focused.GetRuntimeId())) { _selectionMismatch = _streamGuard?.InterruptionReason ?? "Focus or editability"; return false; }
        var text = (TextPattern)focused.GetCurrentPattern(TextPattern.Pattern);
        var selected = text.GetSelection();
        if (selected.Length != 1 || !selected[0].Compare(owned) || selected[0].GetText(-1) != _streamTyped) { _selectionMismatch = "Selection range has not matched"; return false; }
        var context = ReadContext(text, selected[0]);
        var matches = context is not null && context.Value.Prefix == _streamPrefix && context.Value.Suffix == _streamSuffix;
        if (!matches) _selectionMismatch = "Surrounding context changed";
        return matches;
    }

    private async Task<bool> SettleLiveDeliveryAsync()
    {
        // SendInput queues events. Let the target consume them before a final
        // correction or another caller moves its selection. Readback is optional:
        // missing/normalized text disables correction, not subsequent live typing.
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await Task.Delay(20);
            if (!HasStreamFocus()) return PauseStream("Input or focus changed");
            if (!_streamCanCorrect) return true;
            try { if (MatchesStream(AutomationElement.FocusedElement)) return true; }
            catch { _streamCanCorrect = false; return true; }
        }
        _streamCanCorrect = false;
        return true;
    }

    private async Task<bool> VerifyStreamDeliveryAsync()
    {
        // Read back native delivery. Once input has been sent, cancellation must
        // never cause a caller to retry the same text and duplicate it.
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await Task.Delay(20);
            if (_streamPaused || _streamGuard?.Interrupted == true) return PauseStream();
            if (MatchesStream(AutomationElement.FocusedElement)) return true;
        }
        return PauseStream();
    }

    private bool MatchesStream(AutomationElement focused)
    {
        if (_streamPaused || _streamGuard?.Interrupted == true || !IsEditable(focused) ||
            _streamTarget is null || !_streamTarget.SequenceEqual(focused.GetRuntimeId())) return false;
        var selection = ReadSelection(focused);
        return selection is not null && selection.Value.Prefix == _streamPrefix + _streamTyped && selection.Value.Suffix == _streamSuffix;
    }

    private static bool IsEditable(AutomationElement? focused)
    {
        if (focused is null || focused.Current.IsPassword || !focused.Current.HasKeyboardFocus ||
            focused.TryGetCurrentPattern(ValuePattern.Pattern, out var value) && ((ValuePattern)value).Current.IsReadOnly) return false;
        return focused.Current.ControlType == ControlType.Edit || focused.Current.ControlType == ControlType.Document ||
            focused.TryGetCurrentPattern(TextPattern.Pattern, out _);
    }

    private bool HasStreamFocus()
    {
        var focused = AutomationElement.FocusedElement;
        return !_streamPaused && _streamGuard?.Interrupted == false && IsEditable(focused) &&
            _streamTarget is not null && _streamTarget.SequenceEqual(focused.GetRuntimeId());
    }

    private static bool HasExistingSelection(AutomationElement focused)
    {
        try
        {
            if (!focused.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return false;
            return ((TextPattern)pattern).GetSelection().Any(range =>
                range.CompareEndpoints(TextPatternRangeEndpoint.Start, range, TextPatternRangeEndpoint.End) != 0);
        }
        catch { return false; }
    }

    private static (string Prefix, string Suffix)? TryReadSelection(AutomationElement focused)
    {
        try { return ReadSelection(focused); }
        catch { return null; } // Text access is optional for append-only live input.
    }

    private static (string Prefix, string Suffix)? ReadSelection(AutomationElement element)
    {
        if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern)) return null;
        var text = (TextPattern)pattern;
        var selected = text.GetSelection();
        if (selected.Length != 1 || selected[0].CompareEndpoints(TextPatternRangeEndpoint.Start, selected[0], TextPatternRangeEndpoint.End) != 0) return null;
        return ReadContext(text, selected[0]);
    }

    private static (string Prefix, string Suffix)? ReadContext(TextPattern text, TextPatternRange selection)
    {
        var before = text.DocumentRange.Clone();
        before.MoveEndpointByRange(TextPatternRangeEndpoint.End, selection, TextPatternRangeEndpoint.Start);
        var after = text.DocumentRange.Clone();
        after.MoveEndpointByRange(TextPatternRangeEndpoint.Start, selection, TextPatternRangeEndpoint.End);
        var prefix = before.GetText(500001); var suffix = after.GetText(500001);
        if (prefix.Length > 500000 || suffix.Length > 500000) return null;
        return (prefix, suffix);
    }

    private bool PauseStream(string reason = "Text delivery could not be verified") { _streamPaused = true; StreamFallbackReason = reason; return false; }

    private static async Task CopyStreamAsync(string text, CancellationToken token)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (text.Length == 0) Clipboard.Clear(); else Clipboard.SetText(text);
                return;
            }
            catch (ExternalException) when (attempt < 4) { await Task.Delay(40, token); }
        }
        throw new InvalidOperationException("Stream Mode could not update the clipboard. Another application is using it.");
    }

    private static bool ModifiersPressed() => new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C }.Any(key => StreamGetAsyncKeyState(key) < 0);

    private static StreamInput[] UnicodeInputs(string text)
    {
        var inputs = new StreamInput[text.Length * 2];
        for (var i = 0; i < text.Length; i++)
        {
            inputs[i * 2] = UnicodeInput(text[i], false);
            inputs[i * 2 + 1] = UnicodeInput(text[i], true);
        }
        return inputs;
    }

    private static StreamInput KeyInput(ushort key, bool up) => new()
    {
        Type = 1,
        Data = new StreamInputUnion { Keyboard = new StreamKeyboardInput { Key = key, Extra = StreamInputGuard.InputTag, Flags = up ? 0x0002u : 0 } }
    };

    [DllImport("user32.dll", EntryPoint = "GetAsyncKeyState")]
    private static extern short StreamGetAsyncKeyState(int key);

    private static StreamInput UnicodeInput(char value, bool up) => new()
    {
        Type = 1,
        Data = new StreamInputUnion { Keyboard = new StreamKeyboardInput { Scan = value, Extra = StreamInputGuard.InputTag, Flags = 0x0004u | (up ? 0x0002u : 0) } }
    };

    [DllImport("user32.dll", EntryPoint = "SendInput", SetLastError = true)]
    private static extern uint StreamSendInput(uint count, StreamInput[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct StreamInput { public uint Type; public StreamInputUnion Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct StreamInputUnion
    {
        [FieldOffset(0)] public StreamKeyboardInput Keyboard;
        // INPUT's union must include MOUSEINPUT's size, including x64 alignment.
        [FieldOffset(0)] public StreamMouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct StreamKeyboardInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct StreamMouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
}
