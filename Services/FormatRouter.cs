using System.Text;
using System.Windows;

namespace ClipManagerForWindows.Services;

using WpfClipboard = System.Windows.Clipboard;
using WpfDataFormats = System.Windows.DataFormats;

public interface IFormatRouter
{
    bool TryExtract(out ClipboardContent content);
}

public sealed class ClipboardContent
{
    public string Text { get; init; } = string.Empty;
    public string FormatType { get; init; } = "Text"; // "Html", "Rtf", "Text"
    public long OriginalLength { get; init; }
    public bool IsTruncated { get; init; }
}

public sealed class FormatRouter : IFormatRouter
{
    private const long MaxContentLength = 1_000_000_000; // SQLite limit

    public bool TryExtract(out ClipboardContent content)
    {
        content = new ClipboardContent();

        try
        {
            var dataObj = WpfClipboard.GetDataObject();
            if (dataObj == null) return false;

            // Priority: Try to get the richest text format available
            // 1. Try HTML (richest format with full markup)
            if (dataObj.GetDataPresent(WpfDataFormats.Html))
            {
                var html = dataObj.GetData(WpfDataFormats.Html) as string;
                if (!string.IsNullOrEmpty(html))
                {
                    content = CreateContent(html, "Html");
                    return true;
                }
            }

            // 2. Try RTF (rich text format)
            if (dataObj.GetDataPresent(WpfDataFormats.Rtf))
            {
                var rtf = dataObj.GetData(WpfDataFormats.Rtf) as string;
                if (!string.IsNullOrEmpty(rtf))
                {
                    content = CreateContent(rtf, "Rtf");
                    return true;
                }
            }

            // 3. Try Unicode Text (preserves exact string including special chars)
            if (dataObj.GetDataPresent(WpfDataFormats.UnicodeText))
            {
                var text = dataObj.GetData(WpfDataFormats.UnicodeText) as string;
                if (!string.IsNullOrEmpty(text))
                {
                    content = CreateContent(text, "Text");
                    return true;
                }
            }

            // 4. Fallback to plain text
            if (dataObj.GetDataPresent(WpfDataFormats.Text))
            {
                var text = dataObj.GetData(WpfDataFormats.Text) as string;
                if (!string.IsNullOrEmpty(text))
                {
                    content = CreateContent(text, "Text");
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

    private ClipboardContent CreateContent(string text, string formatType)
    {
        var originalLength = Encoding.UTF8.GetByteCount(text);
        var isTruncated = originalLength > MaxContentLength;

        var finalText = text;
        if (isTruncated)
        {
            // Truncate to max length
            var bytes = Encoding.UTF8.GetBytes(text);
            var truncated = new byte[MaxContentLength];
            Array.Copy(bytes, truncated, MaxContentLength);
            finalText = Encoding.UTF8.GetString(truncated);
        }

        return new ClipboardContent
        {
            Text = finalText,
            FormatType = formatType,
            OriginalLength = originalLength,
            IsTruncated = isTruncated
        };
    }
}
