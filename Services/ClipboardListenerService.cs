using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ClipManagerForWindows.Interop;
using ClipManagerForWindows.Models;
using ClipManagerForWindows.Infrastructure;

namespace ClipManagerForWindows.Services;

public interface IClipboardListenerService
{
    Task StartAsync(CancellationToken token);
}

public sealed class ClipboardListenerService : BackgroundService, IClipboardListenerService
{
    private readonly ILogger<ClipboardListenerService> _logger;
    private readonly IFormatRouter _router;
    private readonly IHistoryManager _history;
    private readonly IConfiguration _config;
    private readonly ISourceAppResolver _sourceAppResolver;

    private HwndSource? _hwndSource;
    private Dispatcher? _dispatcher;
    private TimeSpan _debounce;
    private DateTime _lastHandledUtc = DateTime.MinValue;
    private string[] _ignoreList = Array.Empty<string>();

    public ClipboardListenerService(
        ILogger<ClipboardListenerService> logger,
 IFormatRouter router,
    IHistoryManager history,
        IConfiguration configuration,
    ISourceAppResolver sourceAppResolver)
    {
        _logger = logger;
        _router = router;
        _history = history;
        _config = configuration;
        _sourceAppResolver = sourceAppResolver;
        _debounce = TimeSpan.FromMilliseconds(_config.GetSection("App").GetValue<int>("DebounceMs", 250));
        _ignoreList = _config.GetSection("App:IgnoreProcesses").Get<string[]>() ?? Array.Empty<string>();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ClipboardListenerService starting");
        await _history.InitializeAsync(stoppingToken);

        var started = new TaskCompletionSource<bool>();
        var thread = new Thread(() => ClipboardStaThread(started)) { IsBackground = true, Name = "ClipboardListenerSTA" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await started.Task.ConfigureAwait(false);
        _logger.LogInformation("ClipboardListenerService started");

        using var reg = stoppingToken.Register(() =>
    {
        if (_dispatcher is not null)
        {
            try { _dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); } catch { }
        }
    });

        await Task.Run(() => thread.Join(), stoppingToken);
        _logger.LogInformation("ClipboardListenerService stopped");
    }

    private void ClipboardStaThread(TaskCompletionSource<bool> started)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        var parms = new HwndSourceParameters("ClipboardListenerHiddenWindow")
        {
            ParentWindow = ClipboardNative.HWND_MESSAGE,
            WindowStyle = 0
        };
        _hwndSource = new HwndSource(parms);
        _hwndSource.AddHook(WndProc);
        ClipboardNative.AddClipboardFormatListener(_hwndSource.Handle);
        started.TrySetResult(true);
        Dispatcher.Run();
        try
        {
            ClipboardNative.RemoveClipboardFormatListener(_hwndSource.Handle);
            _hwndSource.RemoveHook(WndProc);
            _hwndSource.Dispose();
        }
        catch { }
        finally
        {
            _hwndSource = null;
            _dispatcher = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == ClipboardNative.WM_CLIPBOARDUPDATE)
        {
            var now = DateTime.UtcNow;
            if (now - _lastHandledUtc < _debounce)
            {
                return IntPtr.Zero;
            }
            _lastHandledUtc = now;

            try
            {
                // Prevent self-trigger loop
                var dataObj = System.Windows.Clipboard.GetDataObject();
                if (Infrastructure.ClipboardMarker.IsMarked(dataObj))
                {
                    return IntPtr.Zero;
                }

                if (_router.TryExtract(out var clipContent))
                {
                    var source = _sourceAppResolver.TryGetForegroundProcessName();
                    if (!string.IsNullOrWhiteSpace(source) &&
                     _ignoreList.Any(x => source!.Contains(x, StringComparison.OrdinalIgnoreCase)))
                    {
                        return IntPtr.Zero;
                    }

                    var entry = new ClipboardEntry
                    {
                        CreatedUtc = DateTime.UtcNow,
                        TextContent = clipContent.Text,
                        SourceApp = source,
                        Hash = HistoryManager.ComputeHash(clipContent.Text),
                        FormatType = clipContent.FormatType,
                        IsTruncated = clipContent.IsTruncated,
                        OriginalLength = clipContent.IsTruncated ? clipContent.OriginalLength : null
                    };

                    _ = _history.AddAsync(entry, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling clipboard update");
            }

            handled = false;
        }
        return IntPtr.Zero;
    }

    Task IClipboardListenerService.StartAsync(CancellationToken token) => ExecuteAsync(token);
}
