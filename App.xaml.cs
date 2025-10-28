using ClipManagerForWindows.Infrastructure;
using ClipManagerForWindows.Services;
using ClipManagerForWindows.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Windows;

namespace ClipManagerForWindows;

public partial class App : System.Windows.Application
{
    private IHost? _host;
    private Mutex? _singleInstanceMutex;

    public App()
    {
        // Initialize SQLite native library
        SQLitePCL.Batteries.Init();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single instance guard
        var mutexName = "Global/ClipManagerForWindows_SingleInstance";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool isNewInstance);
        if (!isNewInstance)
        {
            Shutdown();
            return;
        }

        // Build Host
        _host = Host.CreateDefaultBuilder(e.Args)
            .ConfigureAppConfiguration(config =>
            {
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.AddConsole();
            })
            .ConfigureServices((ctx, services) =>
            {
                // Repositories & Stores
                services.AddSingleton<IClipboardRepository, SqliteClipboardRepository>();
                services.AddSingleton<ISettingsStore, SqliteSettingsStore>();

                // Core Services
                services.AddSingleton<IFormatRouter, FormatRouter>();
                services.AddSingleton<IHistoryManager, HistoryManager>();
                services.AddSingleton<ISourceAppResolver, SourceAppResolver>();
                services.AddSingleton<INotificationService, NotificationService>();
                services.AddSingleton<IStartupManager, StartupManager>();

                // ViewModels
                services.AddSingleton<NotifyIconViewModel>();

                // Background Services
                services.AddHostedService<ClipboardListenerService>();

                // Windows
                services.AddTransient<SettingsWindow>();
            })
            .Build();

        // Ensure AppData and DB directory
        var dbPath = AppPaths.GetDatabasePath(_host.Services.GetRequiredService<IConfiguration>()
            .GetSection("App").GetValue<string>("DatabasePath"));
        _host.Services.GetRequiredService<ILogger<App>>().LogInformation("DB path: {db}", dbPath);

        // Wire up truncation notifications
        var historyManager = _host.Services.GetRequiredService<IHistoryManager>();
        var notificationService = _host.Services.GetRequiredService<INotificationService>();
        historyManager.TruncationDetected += async (s, e) =>
        {
            await notificationService.ShowTruncationWarningAsync(e.OriginalLength, e.FormatType);
        };
        
        // Initialize the tray icon view model which will in turn create the icon
        _host.Services.GetRequiredService<NotifyIconViewModel>();

        // Start host
        _ = _host.StartAsync();
    }

    public async Task StopHostAsync()
    {
        if (_host is not null)
        {
            _host.Services.GetRequiredService<NotifyIconViewModel>().Dispose();
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await StopHostAsync();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
