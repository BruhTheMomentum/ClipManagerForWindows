using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;

namespace ClipManagerForWindows.Interop;

public static class ClipboardNative
{
    public const int WM_CLIPBOARDUPDATE = 0x031D;
    public static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterClipboardFormat(string lpszFormat);
}
