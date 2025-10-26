using System;

namespace ClipManagerForWindows.Models;

public sealed class ClipboardEntry
{
 public long Id { get; set; }
 public DateTime CreatedUtc { get; set; }
 public string TextContent { get; set; } = string.Empty;
 public string? SourceApp { get; set; }
 public string Hash { get; set; } = string.Empty;
 public string FormatType { get; set; } = "Text"; // "Html", "Rtf", "Text"
 public bool IsTruncated { get; set; }
 public long? OriginalLength { get; set; }
}
