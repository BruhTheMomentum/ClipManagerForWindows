using System.Threading;
using System.Threading.Tasks;

namespace ClipManagerForWindows.Services;

public interface ISettingsStore
{
    Task<string?> GetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, string value, CancellationToken ct);
}
