using System;
using System.Diagnostics;
using ClipManagerForWindows.Interop;

namespace ClipManagerForWindows.Services;

public sealed class SourceAppResolver : ISourceAppResolver
{
    public string? TryGetForegroundProcessName()
    {
        try
        {
            var hwnd = WindowingNative.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return null;
            if (WindowingNative.GetWindowThreadProcessId(hwnd, out var pid) == 0) return null;
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName;
        }
        catch
        {
            return null;
        }
    }
}
