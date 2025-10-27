using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using ClipManagerForWindows.Models;

namespace ClipManagerForWindows.Services;

public interface IHistoryManager
{
    Task InitializeAsync(CancellationToken ct);
    Task AddAsync(ClipboardEntry entry, CancellationToken ct);

    // Retrieval
    Task<IReadOnlyList<ClipboardEntry>> GetRecentAsync(int take, CancellationToken ct);

    // Observable collections for live UI binding
    ObservableCollection<ClipboardEntry> RecentEntries { get; }

    // Commands
    Task DeleteAsync(long id, CancellationToken ct);
    Task ClearAllAsync(CancellationToken ct);
    Task UpdateMaxEntriesAsync(int maxEntries, CancellationToken ct);

    // Events
    event EventHandler<TruncationEventArgs>? TruncationDetected;
}

public sealed class TruncationEventArgs : EventArgs
{
    public long OriginalLength { get; init; }
    public string FormatType { get; init; } = string.Empty;
}
