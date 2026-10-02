using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Wyspa.App;

internal static class HelpHintsSmoke
{
    internal static async Task VerifyAsync(MainWindow window, string output)
    {
        var content = (StackPanel)window.FindName("SettingsContent");
        var original = new Dictionary<Expander, bool>();
        GetCursorPos(out var previous);
        try
        {
            // Load all authored controls, including nested collapsed prompt groups.
            for (var pass = 0; pass < 3; pass++)
            {
                foreach (var expander in Descendants<Expander>(content).ToArray())
                {
                    original.TryAdd(expander, expander.IsExpanded);
                    expander.IsExpanded = true;
                }
                await Idle(window);
            }
            var help = Descendants<HelpButton>(content).ToArray();
            if (help.Length != 27) throw new Exception($"Expected 27 functioning help buttons, found {help.Length}.");
            foreach (var button in help)
            {
                if (string.IsNullOrWhiteSpace(button.HelpText) || button.ToolTip is not ToolTip tip || string.IsNullOrWhiteSpace(tip.Content as string))
                    throw new Exception("An empty help icon is visible.");
                var peer = UIElementAutomationPeer.CreatePeerForElement(button)!;
                if (peer.GetAutomationControlType() != AutomationControlType.Button || !peer.GetName().StartsWith("Help: "))
                    throw new Exception("Help does not expose a named accessible button.");
                if (ToolTipService.GetInitialShowDelay(button) != 400 || !ToolTipService.GetShowOnDisabled(button))
                    throw new Exception("Help timing/disabled guidance is inconsistent.");
            }
            var controls = Descendants<Control>(content).Where(c => c.TemplatedParent is null &&
                c is Button or ComboBox or Slider or CheckBox or TextBox or PasswordBox).ToArray();
            foreach (var control in controls)
            {
                if (control.ToolTip is null) throw new Exception("Settings control has no guidance: " + control.GetType().Name + " " + control.Name);
                if (!ToolTipService.GetShowOnDisabled(control)) throw new Exception("Disabled setting has no hover guidance: " + control.Name);
            }
            if (new HelpButton { HelpText = "  " }.Visibility != Visibility.Collapsed)
                throw new Exception("Empty help must be hidden.");

            var target = help[0]; target.BringIntoView(); await Idle(window);
            window.Activate(); target.Focus(); await Task.Delay(150);
            var point = target.PointToScreen(new Point(2, target.ActualHeight / 2));
            SetCursorPos((int)point.X, (int)point.Y); // Transparent padding, outside the question glyph.
            var tooltip = (ToolTip)target.ToolTip;
            await WaitFor(() => tooltip.IsOpen);
            Save(tooltip, Path.Combine(output, "help-hover.png"));
            var away = window.PointToScreen(new Point(window.ActualWidth - 30, 30));
            SetCursorPos((int)away.X, (int)away.Y);
            await WaitFor(() => !tooltip.IsOpen);

            var invoke = (IInvokeProvider)UIElementAutomationPeer.CreatePeerForElement(target)!.GetPattern(PatternInterface.Invoke)!;
            invoke.Invoke(); await WaitFor(() => tooltip.IsOpen);
            Escape(); await WaitFor(() => !tooltip.IsOpen);
            target.Focus(); await Idle(window);
            if (!target.IsKeyboardFocused) throw new Exception("Help button cannot receive keyboard focus.");
            Key(0x20); await WaitFor(() => tooltip.IsOpen); // Space activates the real Button.
            Escape(); await WaitFor(() => !tooltip.IsOpen);
            Key(0x0D); await WaitFor(() => tooltip.IsOpen); // Enter also activates it.
            Escape(); await WaitFor(() => !tooltip.IsOpen);
            invoke.Invoke(); await WaitFor(() => tooltip.IsOpen);
            var themeBox = (ComboBox)window.FindName("ThemeSelector"); themeBox.Focus();
            await WaitFor(() => !tooltip.IsOpen);
            var originalHelp = target.HelpText;
            target.HelpText = "";
            if (target.Visibility != Visibility.Collapsed || target.ToolTip is not null) throw new Exception("Clearing help left an inert question mark.");
            target.HelpText = originalHelp;
            File.WriteAllText(Path.Combine(output, "help-validation.txt"), $"PASS: {help.Length} named help buttons, {controls.Length} settings controls with hints, hover over padding, Invoke, Space, Enter, Escape, focus dismissal, empty help hidden and disabled hints enabled.");
        }
        finally
        {
            foreach (var (expander, expanded) in original) expander.IsExpanded = expanded;
            SetCursorPos(previous.X, previous.Y);
            await Idle(window);
        }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T item) yield return item;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static async Task Idle(Window window) => await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
    private static async Task WaitFor(Func<bool> predicate)
    {
        for (var i = 0; i < 100; i++) { if (predicate()) return; await Task.Delay(30); }
        throw new Exception("Help interaction did not reach its expected open/closed state.");
    }
    private static void Escape() => Key(0x1B);
    private static void Key(byte key) { keybd_event(key, 0, 0, UIntPtr.Zero); keybd_event(key, 0, 2, UIntPtr.Zero); }
    private static void Save(FrameworkElement element, string path)
    {
        element.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); png.Save(file);
    }
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
}
