using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CodexTracker;

internal static class Backdrop
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwcpRound = 2;
    private const int DwmColorNone = unchecked((int)0xFFFFFFFE);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Window window, bool dark)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var darkMode = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
            var rounded = DwmwcpRound;
            _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref rounded, sizeof(int));
            var borderColor = DwmColorNone;
            _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref borderColor, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}
