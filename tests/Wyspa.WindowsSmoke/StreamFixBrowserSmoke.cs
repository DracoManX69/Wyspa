using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using Wyspa.Core.Models;
using Wyspa.Infrastructure.Insertion;

internal static class StreamFixBrowserSmoke
{
    public static int Run(string output)
    {
        var app = new Application(); var exit = 0;
        app.Startup += async (_, _) =>
        {
            var previous = Clipboard.GetDataObject();
            var insertion = new WindowsTextInsertionService();
            Process? process = null; IntPtr window = IntPtr.Zero;
            try
            {
                var title = "Wyspa isolated browser " + Guid.NewGuid().ToString("N");
                var html = Path.Combine(output, "target.html");
                File.WriteAllText(html, "<!doctype html><meta charset='utf-8'><title>" + title + "</title><textarea aria-label='Wyspa test dictation' style='width:90%;height:200px;font:24px sans-serif'>Before:  AFTER</textarea><div contenteditable='true' role='textbox' aria-label='Wyspa editable dictation' style='font:24px sans-serif;white-space:pre-wrap'>Before:  AFTER</div><script>const t=document.querySelector('textarea');onload=()=>{t.focus();t.setSelectionRange(8,8);};document.querySelector('div').onfocus=function(){if(this.dataset.ready)return;this.dataset.ready='1';const r=document.createRange();r.setStart(this.firstChild,8);r.collapse(true);const s=getSelection();s.removeAllRanges();s.addRange(r);};</script>");
                var edge = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe");
                var info = new ProcessStartInfo(edge) { UseShellExecute = false };
                info.ArgumentList.Add("--user-data-dir=" + Path.Combine(output, "browser-profile"));
                info.ArgumentList.Add("--no-first-run"); info.ArgumentList.Add("--no-default-browser-check");
                info.ArgumentList.Add("--app=" + new Uri(html).AbsoluteUri);
                process = Process.Start(info);
                await WaitFor(() =>
                {
                    var windows = AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));
                    foreach (AutomationElement candidate in windows)
                        if (candidate.Current.Name.Contains(title, StringComparison.Ordinal)) { window = (IntPtr)candidate.Current.NativeWindowHandle; return true; }
                    return false;
                });
                SetForegroundWindow(window);
                await WaitFor(() => GetForegroundWindow() == window && AutomationElement.FocusedElement.Current.Name == "Wyspa test dictation");
                foreach (var mode in new[] { InsertionMode.Paste, InsertionMode.Type })
                {
                    // Second session appends after the first, keeping its text intact.
                    insertion.BeginStream(mode);
                    var live = mode == InsertionMode.Paste ? "this is um a test" : " more um words";
                    var final = mode == InsertionMode.Paste ? "This is a test." : " More words.";
                    if (!await insertion.AppendStreamAsync(live, live, default)) throw new Exception("Browser live insertion failed: " + insertion.StreamFallbackReason);
                    if (!await insertion.CompleteStreamAsync(final, true, default)) throw new Exception("Browser " + mode + " correction failed: " + insertion.StreamFallbackReason);
                    var expected = mode == InsertionMode.Paste ? "Before: This is a test. AFTER" : "Before: This is a test. More words. AFTER";
                    var text = ((ValuePattern)AutomationElement.FocusedElement.GetCurrentPattern(ValuePattern.Pattern)).Current.Value;
                    if (text != expected) throw new Exception("Browser changed surrounding content: " + text);
                    if (Clipboard.GetText() != final) throw new Exception("Browser final clipboard incorrect.");
                    insertion.EndStream();
                }
                var editable = AutomationElement.FromHandle(window).FindFirst(TreeScope.Descendants,
                    new PropertyCondition(AutomationElement.NameProperty, "Wyspa editable dictation"));
                if (editable is null) throw new Exception("Isolated contenteditable control was not exposed.");
                editable.SetFocus();
                await WaitFor(() => AutomationElement.FocusedElement.Current.Name == "Wyspa editable dictation");
                await Task.Delay(150);
                insertion.BeginStream();
                if (!await insertion.AppendStreamAsync("this is um a test", "this is um a test", default)) throw new Exception("Contenteditable live insertion failed: " + insertion.StreamFallbackReason);
                if (!await insertion.CompleteStreamAsync("This is a test.", true, default)) throw new Exception("Contenteditable correction failed: " + insertion.StreamFallbackReason);
                var editableText = ((TextPattern)AutomationElement.FocusedElement.GetCurrentPattern(TextPattern.Pattern)).DocumentRange.GetText(-1).TrimEnd('\r', '\n');
                if (editableText != "Before: This is a test. AFTER") throw new Exception("Contenteditable surrounding text changed: " + editableText);
                insertion.EndStream();
                File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: real Microsoft Edge textarea and contenteditable live typing and final correction using both Paste and Type, preserved prefix/suffix and previous-session text, session-only clipboard; isolated fresh browser profile and local HTML, without forced accessibility flags.");
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString()); exit = 1; }
            finally
            {
                insertion.EndStream();
                if (window != IntPtr.Zero) PostMessage(window, 0x10, IntPtr.Zero, IntPtr.Zero);
                process?.Dispose();
                if (previous is not null) Clipboard.SetDataObject(previous, true); else Clipboard.Clear();
                app.Shutdown();
            }
        };
        app.Run(); return exit;
    }
    private static async Task WaitFor(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!condition()) await Task.Delay(100, timeout.Token);
    }
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
