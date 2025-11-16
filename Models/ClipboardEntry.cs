using System;

namespace ClipManagerForWindows.Models;

public sealed class ClipboardEntry
{
     public long Id { get; set; }
     public DateTime CreatedUtc { get; set; }
     public string TextContent { get; set; } = string.Empty;
}
