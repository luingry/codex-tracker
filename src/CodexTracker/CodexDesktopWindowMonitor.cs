using System.Runtime.InteropServices;
using System.IO;

namespace CodexTracker;

public readonly record struct CodexDesktopWindowState(bool IsForeground, bool IsMinimized);

public static class CodexDesktopWindowMonitor
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint GaRoot = 2;
    private const int DwmwaCloaked = 14;

    public static CodexDesktopWindowState Read()
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero) return default;
        var root = GetAncestor(foreground, GaRoot);
        if (root == IntPtr.Zero) root = foreground;
        GetWindowThreadProcessId(root, out var processId);
        var path = TryGetProcessPath(processId);
        return Observe(path, IsWindowVisible(root), TryIsCloaked(root), IsIconic(root));
    }

    public static CodexDesktopWindowState Observe(string? path, bool isVisible, bool isCloaked, bool isMinimized) =>
        IsCodexDesktopExecutable(path) && isVisible && !isCloaked ? new(true, isMinimized) : default;

    private static bool TryIsCloaked(IntPtr window)
    {
        try
        {
            return DwmGetWindowAttribute(window, DwmwaCloaked, out var cloaked, Marshal.SizeOf<int>()) == 0 && cloaked != 0;
        }
        catch (DllNotFoundException) { return false; }
        catch (EntryPointNotFoundException) { return false; }
        catch (BadImageFormatException) { return false; }
    }

    public static bool IsCodexDesktopExecutable(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var executable = Path.GetFileName(path);
        if (!string.Equals(executable, "codex.exe", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(executable, "ChatGPT.exe", StringComparison.OrdinalIgnoreCase)) return false;
        var normalized = path!.Replace('/', '\\');
        return normalized.IndexOf("\\WindowsApps\\OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0 ||
               normalized.IndexOf("\\Programs\\Codex\\", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string? TryGetProcessPath(uint processId)
    {
        var process = OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process == IntPtr.Zero) return null;
        try
        {
            var capacity = 1024;
            var buffer = new System.Text.StringBuilder(capacity);
            return QueryFullProcessImageName(process, 0, buffer, ref capacity) ? buffer.ToString() : null;
        }
        finally { CloseHandle(process); }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, out int value, int valueSize);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, uint flags, System.Text.StringBuilder path, ref int size);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
