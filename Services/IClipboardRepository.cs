using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClipManagerForWindows.Models;

namespace ClipManagerForWindows.Services;

public interface IClipboardRepository
{
    Task InitializeAsync(CancellationToken ct);
    Task<long> InsertAsync(ClipboardEntry entry, CancellationToken ct);
    Task<IReadOnlyList<string>> GetRecentHashesAsync(int take, CancellationToken ct);
    Task<IReadOnlyList<ClipboardEntry>> GetRecentAsync(int take, CancellationToken ct);
    Task ClearAllAsync(CancellationToken ct);
    Task DeleteAsync(long id, CancellationToken ct);
}
