using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Wyspa.Infrastructure.Insertion;
using Wyspa.Core.Models;
using System.Text.Json;

internal static class StreamingInsertionSmoke
{
    public static int Run(string output)
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var exitCode = 0;
        app.Startup += async (_, _) =>
        {
            Process? target = null;
            var previous = Clipboard.GetDataObject();
            var insertion = new WindowsTextInsertionService();
            void EndStream()
            {
                var timer = Stopwatch.StartNew(); insertion.EndStream();
                File.AppendAllText(Path.Combine(output, "teardown-ms.txt"), timer.Elapsed.TotalMilliseconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) + "\n");
            }
            try
            {
                var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                info.ArgumentList.Add("stream-target"); info.ArgumentList.Add(output);
                target = Process.Start(info)!;
                await WaitFor(() => File.Exists(Path.Combine(output, "ready")));
                target.Refresh();
                SetForegroundWindow(target.MainWindowHandle);
                await WaitFor(() => System.Windows.Automation.AutomationElement.FocusedElement?.Current.ProcessId == target.Id);
                File.WriteAllText(Path.Combine(output, "target-verified.txt"), "Focused process matches the isolated target.");
                GetWindowThreadProcessId(GetForegroundWindow(), out var foregroundProcess);
                File.WriteAllText(Path.Combine(output, "input-context.txt"), $"target={target.Id}; foreground={foregroundProcess}; Ctrl={GetAsyncKeyState(0x11)}; Alt={GetAsyncKeyState(0x12)}; Shift={GetAsyncKeyState(0x10)}; Win={GetAsyncKeyState(0x5B)}");
                insertion.BeginStream();
                if (!await insertion.AppendStreamAsync("This", "This", default)) throw new Exception("Initial Unicode insertion failed: " + insertion.StreamFallbackReason);
                await CheckText(output, "Existing: This");
                if (Clipboard.GetText() != "This") throw new Exception("First clipboard value incorrect.");
                if (!await insertion.AppendStreamAsync(" is a test", "This is a test", default)) throw new Exception("Second insertion failed.");
                await CheckText(output, "Existing: This is a test");
                if (Clipboard.GetText() != "This is a test") throw new Exception("Clipboard only contains a delta.");
                // Simulate modifiers held by Hold to Talk. Release our keys in finally.
                KeybdEvent(0x11, 0, 0, UIntPtr.Zero); KeybdEvent(0x12, 0, 0, UIntPtr.Zero);
                try
                {
                    if (!await insertion.AppendStreamAsync(" of Wyspa — café 日本語 {+} 😀", "This is a test of Wyspa — café 日本語 {+} 😀", default)) throw new Exception("Held-modifier insertion failed.");
                    await CheckText(output, "Existing: This is a test of Wyspa — café 日本語 {+} 😀");
                }
                finally { KeybdEvent(0x12, 0, 2, UIntPtr.Zero); KeybdEvent(0x11, 0, 2, UIntPtr.Zero); }
                if (!await insertion.CompleteStreamAsync("Corrected final version.", true, default)) throw new Exception("Verified WPF correction failed: " + insertion.StreamFallbackReason);
                await CheckText(output, "Existing: Corrected final version.");
                if (Clipboard.GetText() != "Corrected final version.") throw new Exception("Clipboard correction was lost.");
                // Movement followed by return must still surrender ownership.
                KeybdEvent(0x25, 0, 0, UIntPtr.Zero); KeybdEvent(0x25, 0, 2, UIntPtr.Zero);
                KeybdEvent(0x27, 0, 0, UIntPtr.Zero); KeybdEvent(0x27, 0, 2, UIntPtr.Zero);
                await Task.Delay(100);
                if (await insertion.AppendStreamAsync(" forbidden", "Clipboard after caret movement", default)) throw new Exception("Caret movement was not respected.");
                await CheckText(output, "Existing: Corrected final version.");
                await Focus(output, "second");
                if (await insertion.AppendStreamAsync(" more", "Complete clipboard after focus change", default)) throw new Exception("Stream typed into a different field.");
                await WaitFor(() => Clipboard.GetText() == "Complete clipboard after focus change");
                if (File.ReadAllText(Path.Combine(output, "second.txt")) != "Other field") throw new Exception("Second field was modified.");
                EndStream(); insertion.BeginStream();
                if (!await insertion.AppendStreamAsync(" New session", "New session", default)) throw new Exception("New stream did not accept selected field: " + insertion.StreamFallbackReason);
                await WaitFor(() => File.ReadAllText(Path.Combine(output, "second.txt")) == "Other field New session");
                EndStream();
                foreach (var mode in new[] { InsertionMode.Paste, InsertionMode.Type })
                {
                    const string prefix = "Already: um words | Before: ";
                    const string suffix = " AFTER um words";
                    await ResetFirst(output, prefix + suffix, prefix.Length);
                    insertion.BeginStream(mode);
                    if (!await insertion.AppendStreamAsync("um words", "um words", default)) throw new Exception("Repeated-word setup failed: " + insertion.StreamFallbackReason);
                    if (!await insertion.CompleteStreamAsync("Words — café 日本語 😀.", true, default)) throw new Exception(mode + " correction failed: " + insertion.StreamFallbackReason);
                    await CheckText(output, prefix + "Words — café 日本語 😀." + suffix);
                    if (Clipboard.GetText() != "Words — café 日本語 😀.") throw new Exception("Final clipboard includes surrounding document text.");
                    EndStream();
                }
                await ResetFirst(output, "Before:  AFTER", 8);
                insertion.BeginStream();
                if (!await insertion.AppendStreamAsync("um", "um", default)) throw new Exception("Empty correction setup failed.");
                if (!await insertion.CompleteStreamAsync("", true, default)) throw new Exception("Filler-only correction failed: " + insertion.StreamFallbackReason);
                await CheckText(output, "Before:  AFTER");
                if (Clipboard.ContainsText()) throw new Exception("Filler-only clipboard was not cleared.");
                EndStream();

                foreach (var editedText in new[] { "Edited: um words AFTER", "Before: um words CHANGED" })
                {
                    await ResetFirst(output, "Before:  AFTER", 8);
                    insertion.BeginStream();
                    if (!await insertion.AppendStreamAsync("um words", "um words", default)) throw new Exception("Edit guard setup failed.");
                    await ResetFirst(output, editedText, 16);
                    if (await insertion.CompleteStreamAsync("Words.", true, default)) throw new Exception("External context edit was overwritten.");
                    await CheckText(output, editedText);
                    EndStream();
                }
                await ResetFirst(output, "Before: selected AFTER", 8, 8);
                insertion.BeginStream();
                if (await insertion.AppendStreamAsync("new words", "new words", default)) throw new Exception("Initial selection was overwritten.");
                await CheckText(output, "Before: selected AFTER");
                EndStream();

                await Focus(output, "opaque");
                insertion.BeginStream();
                if (!await insertion.AppendStreamAsync("Live words", "Live words", default))
                    throw new Exception("Editor without TextPattern rejected live typing: " + insertion.StreamFallbackReason);
                await WaitFor(() => File.ReadAllText(Path.Combine(output, "opaque.txt")) == "Live words");
                if (!await insertion.CompleteStreamAsync("Live words", false, default))
                    throw new Exception("Editor without TextPattern required a review step.");
                await WaitFor(() => Clipboard.GetText() == "Live words");
                EndStream();

                await Focus(output, "password");
                EndStream(); insertion.BeginStream();
                if (await insertion.AppendStreamAsync("secret-test", "secret-test", default)) throw new Exception("Stream typed into a password field.");
                if (File.ReadAllText(Path.Combine(output, "password.txt")) != "0") throw new Exception("Password field changed.");
                File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: native Unicode insertion, cumulative clipboard, held Ctrl+Alt, Unicode/surrogates/special characters, existing text preservation, focus-change and caret-move clipboard fallback, verified generic final replacement using Paste and Type, repeated words outside the dictation, preserved prefix/suffix, filler-only deletion, external prefix/suffix edit rejection, existing-selection protection, session reset, and password-field guard.");
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString()); exitCode = 1; }
            finally
            {
                EndStream();
                if (target is { HasExited: false }) { target.Kill(); await target.WaitForExitAsync(); }
                target?.Dispose();
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    try { if (previous is not null) Clipboard.SetDataObject(previous, true); else Clipboard.Clear(); break; }
                    catch (COMException) when (attempt < 19) { await Task.Delay(50); }
                }
                app.Shutdown();
            }
        };
        app.Run();
        return exitCode;
    }

    private sealed class OpaqueTextBox : TextBox
    {
        protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new OpaquePeer(this);
    }
    private sealed class OpaquePeer(OpaqueTextBox owner) : System.Windows.Automation.Peers.TextBoxAutomationPeer(owner)
    {
        public override object? GetPattern(System.Windows.Automation.Peers.PatternInterface patternInterface) =>
            patternInterface is System.Windows.Automation.Peers.PatternInterface.Text ? null : base.GetPattern(patternInterface);
    }

    public static void Target(string output)
    {
        var app = new Application();
        var first = new TextBox { Text = "Existing: ", AcceptsReturn = true, FontSize = 22, Margin = new Thickness(10) };
        var second = new TextBox { Text = "Other field", FontSize = 22, Margin = new Thickness(10) };
        var opaque = new OpaqueTextBox { FontSize = 22, Margin = new Thickness(10) };
        var password = new PasswordBox { Margin = new Thickness(10) };
        var stack = new StackPanel(); stack.Children.Add(first); stack.Children.Add(second); stack.Children.Add(password); stack.Children.Add(opaque);
        var window = new Window { Title = "Wyspa Stream Mode isolated insertion test", Width = 850, Height = 260, Content = stack };
        var lastFirst = first.Text; var lastSecond = second.Text; var lastPassword = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            try
            {
            // Do not put synchronous file I/O in the target's per-character input path.
            if (first.Text != lastFirst) { File.WriteAllText(Path.Combine(output, "first.txt"), first.Text); lastFirst = first.Text; }
            if (second.Text != lastSecond) { File.WriteAllText(Path.Combine(output, "second.txt"), second.Text); lastSecond = second.Text; }
            if (password.Password.Length != lastPassword) { File.WriteAllText(Path.Combine(output, "password.txt"), password.Password.Length.ToString()); lastPassword = password.Password.Length; }
            File.WriteAllText(Path.Combine(output, "opaque.txt"), opaque.Text);
            var reset = Path.Combine(output, "reset.json");
            if (File.Exists(reset))
            {
                var request = JsonSerializer.Deserialize<ResetRequest>(File.ReadAllText(reset))!;
                first.Text = request.Text; first.Focus(); first.Select(request.Caret, request.SelectionLength);
                File.Delete(reset); File.WriteAllText(Path.Combine(output, "reset-done"), "yes");
            }
            var command = Path.Combine(output, "focus");
            if (!File.Exists(command)) return;
            var which = File.ReadAllText(command);
            if (which == "second") { second.Focus(); second.CaretIndex = second.Text.Length; }
            else if (which == "opaque") opaque.Focus();
            else password.Focus();
            File.Delete(command);
            File.WriteAllText(Path.Combine(output, "focused-" + which), "yes");
            }
            catch (IOException) { /* Parent may briefly hold a result file while polling. Retry next tick. */ }
        };
        window.Loaded += (_, _) =>
        {
            window.Activate(); first.Focus(); first.CaretIndex = first.Text.Length;
            File.WriteAllText(Path.Combine(output, "first.txt"), first.Text);
            File.WriteAllText(Path.Combine(output, "second.txt"), second.Text);
            File.WriteAllText(Path.Combine(output, "password.txt"), "0");
            File.WriteAllText(Path.Combine(output, "ready"), "yes"); timer.Start();
        };
        app.Run(window);
    }

    private sealed record ResetRequest(string Text, int Caret, int SelectionLength);
    private static async Task ResetFirst(string output, string text, int caret, int selectionLength = 0)
    {
        File.Delete(Path.Combine(output, "reset-done"));
        var path = Path.Combine(output, "reset.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new ResetRequest(text, caret, selectionLength)));
        File.Move(path + ".tmp", path);
        await WaitFor(() => File.Exists(Path.Combine(output, "reset-done")));
        await CheckText(output, text);
        await Task.Delay(100); // allow the previous session's focus notification to drain
    }

    private static async Task CheckText(string output, string expected) => await WaitFor(() => File.ReadAllText(Path.Combine(output, "first.txt")) == expected);
    private static async Task Focus(string output, string field)
    {
        File.WriteAllText(Path.Combine(output, "focus"), field);
        await WaitFor(() => File.Exists(Path.Combine(output, "focused-" + field)));
    }
    private static async Task WaitFor(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        while (true)
        {
            try { if (condition()) return; } catch (IOException) { }
            await Task.Delay(30, timeout.Token);
        }
    }
    [DllImport("user32.dll", EntryPoint = "keybd_event")]
    private static extern void KeybdEvent(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
}
