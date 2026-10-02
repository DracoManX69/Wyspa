using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using Wyspa.Infrastructure.Insertion;
using Clipboard = System.Windows.Forms.Clipboard;

internal static class StreamFixWordSmoke
{
    public static int Run(string output)
    {
        var app = new Application(); var exit = 0;
        app.Startup += async (_, _) =>
        {
            object? wordObject = null, documentObject = null;
            var previousClipboard = Clipboard.GetDataObject();
            var insertion = new WindowsTextInsertionService();
            try
            {
                var type = Type.GetTypeFromProgID("Word.Application") ?? throw new Exception("Desktop Word is unavailable.");
                wordObject = Activator.CreateInstance(type)!;
                dynamic word = wordObject;
                word.Visible = true;
                documentObject = word.Documents.Add();
                dynamic document = documentObject;
                await Reset("Before:  AFTER", 8);
                insertion.BeginStream();
                if (!await insertion.AppendStreamAsync("this is um a test", "this is um a test", default)) throw new Exception("Word live insertion failed: " + insertion.StreamFallbackReason);
                if (!await insertion.CompleteStreamAsync("This is a test.", true, default)) throw new Exception("Verified Word correction was not applied: " + insertion.StreamFallbackReason);
                Check("Before: This is a test. AFTER");
                if (Clipboard.GetText() != "This is a test.") throw new Exception("Final clipboard was not corrected.");
                insertion.EndStream();
                document.Undo(1);
                Check("Before: this is um a test AFTER");

                await Reset("Before:  AFTER", 8);
                insertion.BeginStream();
                if (!await insertion.AppendStreamAsync("um words", "um words", default)) throw new Exception("Word second insertion failed.");
                document.Range(0, 0).Select();
                if (await insertion.CompleteStreamAsync("Words.", true, default)) throw new Exception("Caret move should reject correction.");
                Check("Before: um words AFTER");
                insertion.EndStream();

                await Reset("Before:  AFTER", 8);
                insertion.BeginStream();
                if (!await insertion.AppendStreamAsync("um words", "um words", default)) throw new Exception("Word third insertion failed.");
                document.Range(0, 6).Text = "Edited";
                document.Range(16, 16).Select();
                if (await insertion.CompleteStreamAsync("Words.", true, default)) throw new Exception("Edited prefix should reject correction.");
                Check("Edited: um words AFTER");
                insertion.EndStream();

                await Reset("Before: existing AFTER", 8);
                document.Range(8, 16).Select();
                insertion.BeginStream();
                if (await insertion.AppendStreamAsync("new", "new", default)) throw new Exception("Pre-existing selection must be preserved.");
                Check("Before: existing AFTER");
                insertion.EndStream();
                File.WriteAllText(Path.Combine(output, "result.txt"), "PASS: real desktop Word live insertion, verified final range replacement, prefix/suffix preservation, one-step native undo, caret-move rejection, document-edit rejection, existing-selection protection and corrected clipboard.");

                async Task Reset(string text, int caret)
                {
                    document.Content.Text = text;
                    document.Activate();
                    document.Range(caret, caret).Select();
                    var window = (IntPtr)(int)word.ActiveWindow.Hwnd;
                    SetForegroundWindow(window);
                    await Task.Delay(500);
                    GetWindowThreadProcessId(window, out var expectedProcess);
                    var focused = AutomationElement.FocusedElement;
                    if (focused.Current.ProcessId != expectedProcess || GetForegroundWindow() != window)
                        throw new Exception("Word test did not acquire its isolated document focus; no input was sent.");
                    File.AppendAllText(Path.Combine(output, "focus.txt"), focused.Current.ControlType.ProgrammaticName + " " + focused.Current.ClassName + "\n");
                }
                void Check(string expected)
                {
                    var actual = ((string)document.Content.Text).TrimEnd('\r');
                    if (actual != expected) throw new Exception($"Word text mismatch. Expected '{expected}', actual '{actual}'.");
                }
            }
            catch (Exception ex) { File.WriteAllText(Path.Combine(output, "error.txt"), ex.ToString()); exit = 1; }
            finally
            {
                insertion.EndStream();
                if (documentObject is not null) { try { ((dynamic)documentObject).Close(0); } catch { } Marshal.ReleaseComObject(documentObject); }
                if (wordObject is not null)
                {
                    // Close only the application instance with no remaining documents.
                    try { if ((int)((dynamic)wordObject).Documents.Count == 0) ((dynamic)wordObject).Quit(0); } catch { }
                    Marshal.ReleaseComObject(wordObject);
                }
                if (previousClipboard is not null) Clipboard.SetDataObject(previousClipboard, true); else Clipboard.Clear();
                app.Shutdown();
            }
        };
        app.Run(); return exit;
    }
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
}
