using System;
using System.Runtime.InteropServices;
using SW = System.Windows;

namespace ClipManagerForWindows.Infrastructure;

// Marks clipboard content we set, so the listener can ignore self-originated updates.
public static class ClipboardMarker
{
    private const string MarkerFormat = "ClipManagerForWindows.Marker";

    public static void SetMarkedText(string text)
    {
        var data = new SW.DataObject();
        data.SetData(SW.DataFormats.UnicodeText, text);
        data.SetData(MarkerFormat, true);
        SW.Clipboard.SetDataObject(data, true);
    }

    public static bool IsMarked(SW.IDataObject? data)
    {
        try
        {
            if (data is null) return false;
            return data.GetDataPresent(MarkerFormat);
        }
        catch { return false; }
    }
}
