using System.Windows;

namespace ClipManagerForWindows.Services;

using WpfClipboard = System.Windows.Clipboard;
using WpfDataFormats = System.Windows.DataFormats;

public interface ISimpleTextExtractor
{
    bool TryExtractText(out string text);
}

public sealed class SimpleTextExtractor : ISimpleTextExtractor
{
    public bool TryExtractText(out string text)
    {
        text = string.Empty;

        try
        {
            var dataObj = WpfClipboard.GetDataObject();
            if (dataObj == null) return false;

            // Use UnicodeText to preserve all characters (Text is ANSI-only)
            if (dataObj.GetDataPresent(WpfDataFormats.UnicodeText))
            {
                var clipboardText = dataObj.GetData(WpfDataFormats.UnicodeText) as string;
                if (!string.IsNullOrEmpty(clipboardText))
                {
                    text = clipboardText;
                    return true;
                }
            }
        }
        catch
        {
            // Intentionally swallow; caller logs
        }

        return false;
    }
}