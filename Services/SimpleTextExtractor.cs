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

            // Only try to get plain text - no format priority system
            if (dataObj.GetDataPresent(WpfDataFormats.Text))
            {
                var clipboardText = dataObj.GetData(WpfDataFormats.Text) as string;
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