using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WpfApplication = System.Windows.Application;
using WpfMessageBox = System.Windows.MessageBox;

namespace ClipManagerForWindows.Services;

public sealed class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    public Task ShowTruncationWarningAsync(long originalLength, string formatType)
    {
        var sizeMB = originalLength / (1024.0 * 1024.0);
        var message = $"Clipboard content was truncated from {sizeMB:F2} MB to 1 GB limit.\nFormat: {formatType}";
   
        _logger.LogWarning("Truncation: {size} MB, Format: {format}", sizeMB, formatType);
        
        WpfApplication.Current.Dispatcher.Invoke(() =>
  {
    WpfMessageBox.Show(message, "Content Truncated", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
      });

        return Task.CompletedTask;
    }

    public Task ShowErrorAsync(string title, string message)
  {
        _logger.LogError("Error notification: {title} - {message}", title, message);
        
WpfApplication.Current.Dispatcher.Invoke(() =>
        {
            WpfMessageBox.Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        });

     return Task.CompletedTask;
    }

    public Task ShowInfoAsync(string title, string message)
    {
        _logger.LogInformation("Info notification: {title} - {message}", title, message);
        
   WpfApplication.Current.Dispatcher.Invoke(() =>
   {
          WpfMessageBox.Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        });

        return Task.CompletedTask;
  }
}
