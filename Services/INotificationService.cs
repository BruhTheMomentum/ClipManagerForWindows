using System.Threading.Tasks;

namespace ClipManagerForWindows.Services;

public interface INotificationService
{
    Task ShowErrorAsync(string title, string message);
    Task ShowInfoAsync(string title, string message);
}
