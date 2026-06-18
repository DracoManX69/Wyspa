using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Wyspa.App.Services;

public static class NativeWindowStyler
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwcpRound = 2;
    private const int DwmSystemBackdropMainWindow = 2;
    private const int DwmSystemBackdropTransientWindow = 3;

    public static void Apply(Window window, bool darkMode, bool transient = false)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var dark = darkMode ? 1 : 0;
            DwmSetWindowAttribute(handle, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int));

            var corners = DwmwcpRound;
            DwmSetWindowAttribute(handle, DwmwaWindowCornerPreference, ref corners, sizeof(int));

            var backdrop = transient ? DwmSystemBackdropTransientWindow : DwmSystemBackdropMainWindow;
            DwmSetWindowAttribute(handle, DwmwaSystemBackdropType, ref backdrop, sizeof(int));
        }
        catch
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int pvAttribute, int cbAttribute);
}
