using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ClipManagerForWindows.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using WpfApplication = System.Windows.Application;

namespace ClipManagerForWindows.Services;

public sealed class HistoryManager : IHistoryManager
{
    private readonly ILogger<HistoryManager> _logger;
    private readonly IClipboardRepository _repo;
    private readonly ISettingsStore _settings;
    private readonly Channel<ClipboardEntry> _queue = Channel.CreateUnbounded<ClipboardEntry>();
    private int _maxRecent;

    public ObservableCollection<ClipboardEntry> RecentEntries { get; } = new();


    public HistoryManager(ILogger<HistoryManager> logger, IClipboardRepository repo, ISettingsStore settings)
    {
        _logger = logger;
        _repo = repo;
        _settings = settings;
        _maxRecent = 500; // Default, will be updated during initialization
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        await _repo.InitializeAsync(ct);

        // Load max entries from settings
        var maxEntriesStr = await _settings.GetAsync("MaxEntries", ct) ?? "500";
        if (int.TryParse(maxEntriesStr, out var maxEntries) && maxEntries >= 10 && maxEntries <= 10000)
        {
            _maxRecent = maxEntries;
        }

        // Load recent entries
        var recent = await _repo.GetRecentAsync(_maxRecent, ct);
        await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
     {
         RecentEntries.Clear();
         foreach (var r in recent)
         {
             RecentEntries.Add(r);
         }
     });

        _ = Task.Run(ProcessQueueAsync);
    }

    public async Task AddAsync(ClipboardEntry entry, CancellationToken ct)
    {
        await _queue.Writer.WriteAsync(entry, ct);
    }

    private async Task ProcessQueueAsync()
    {
        while (await _queue.Reader.WaitToReadAsync())
        {
            while (_queue.Reader.TryRead(out var entry))
            {
                try
                {
                    var id = await _repo.InsertAsync(entry, CancellationToken.None);
                    entry.Id = id;

                    await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
                     {
                         RecentEntries.Insert(0, entry);
                         while (RecentEntries.Count > _maxRecent)
                             RecentEntries.RemoveAt(RecentEntries.Count - 1);
                     });

                    _logger.LogInformation("Saved entry {id} ({len} chars)",
                  id, entry.TextContent.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to save clipboard entry");
                }
            }
        }
    }


    public Task<IReadOnlyList<ClipboardEntry>> GetRecentAsync(int take, CancellationToken ct)
    {
        return Task.FromResult((IReadOnlyList<ClipboardEntry>)RecentEntries.Take(take).ToList());
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        await _repo.DeleteAsync(id, ct);

        await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
     {
         var recent = RecentEntries.FirstOrDefault(x => x.Id == id);
         if (recent is not null) RecentEntries.Remove(recent);
     });
    }

    public async Task ClearAllAsync(CancellationToken ct)
    {
        await _repo.ClearAllAsync(ct);

        await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
          {
              RecentEntries.Clear();
          });

    }

    public async Task UpdateMaxEntriesAsync(int maxEntries, CancellationToken ct)
    {
        if (maxEntries < 10 || maxEntries > 10000)
        {
            _logger.LogWarning("Invalid max entries value: {maxEntries}", maxEntries);
            return;
        }

        var oldMax = _maxRecent;
        _maxRecent = maxEntries;

        _logger.LogInformation("Updated max entries from {oldMax} to {maxEntries}", oldMax, maxEntries);

        // If the limit was reduced, prune excess entries
        if (maxEntries < oldMax)
        {
            await PruneExcessEntriesAsync(maxEntries, ct);
        }
    }

    private async Task PruneExcessEntriesAsync(int maxEntries, CancellationToken ct)
    {
        await WpfApplication.Current.Dispatcher.InvokeAsync(() =>
        {
            // Remove excess entries from memory (oldest first)
            while (RecentEntries.Count > maxEntries)
            {
                RecentEntries.RemoveAt(RecentEntries.Count - 1);
            }
        });

        // Clean up database entries beyond the limit
        await _repo.PruneOldEntriesAsync(maxEntries, ct);

        _logger.LogInformation("Pruned excess entries to maintain max limit of {maxEntries}", maxEntries);
    }
}
