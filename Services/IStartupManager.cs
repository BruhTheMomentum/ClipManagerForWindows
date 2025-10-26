using System.Threading.Tasks;

namespace ClipManagerForWindows.Services;

public interface IStartupManager
{
    Task<bool> IsEnabledAsync();
    Task SetEnabledAsync(bool enabled);
}
