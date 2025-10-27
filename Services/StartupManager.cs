using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace ClipManagerForWindows.Services;

public sealed class StartupManager : IStartupManager
{
    private const string AppName = "ClipManagerForWindows";
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    
    private readonly ILogger<StartupManager> _logger;

    public StartupManager(ILogger<StartupManager> logger)
    {
     _logger = logger;
    }

    public Task<bool> IsEnabledAsync()
  {
        try
 {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
      var value = key?.GetValue(AppName) as string;
  return Task.FromResult(!string.IsNullOrEmpty(value));
        }
        catch (Exception ex)
  {
            _logger.LogError(ex, "Failed to check startup status");
         return Task.FromResult(false);
      }
    }

    public Task SetEnabledAsync(bool enabled)
    {
    try
        {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
       if (key == null)
            {
    _logger.LogError("Unable to open Run registry key");
 return Task.CompletedTask;
            }

            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    _logger.LogError("Unable to determine current executable path");
                    return Task.CompletedTask;
                }

                if (!File.Exists(exePath))
                {
                    _logger.LogError("Executable path does not exist: {path}", exePath);
                    return Task.CompletedTask;
                }

                key.SetValue(AppName, $"\"{exePath}\"");
                _logger.LogInformation("Enabled startup: {path}", exePath);
            }
        else
            {
     key.DeleteValue(AppName, false);
          _logger.LogInformation("Disabled startup");
    }
        }
    catch (Exception ex)
        {
_logger.LogError(ex, "Failed to set startup status");
      }

        return Task.CompletedTask;
    }
}
