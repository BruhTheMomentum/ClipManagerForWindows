using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using ClipManagerForWindows.Infrastructure;
using ClipManagerForWindows.Models;
using ClipManagerForWindows.Services;
using Microsoft.Extensions.DependencyInjection;
using Application = System.Windows.Application;

namespace ClipManagerForWindows.ViewModels;

public partial class NotifyIconViewModel : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IHistoryManager _historyManager;
    private readonly NotifyIcon _notifyIcon;

    public NotifyIconViewModel(IServiceProvider serviceProvider, IHistoryManager historyManager)
    {
        _serviceProvider = serviceProvider;
        _historyManager = historyManager;

        _notifyIcon = new NotifyIcon();
        _notifyIcon.Icon = new Icon("Assets/clip.ico");
        _notifyIcon.Text = "Clip Manager";
        _notifyIcon.Visible = true;

        BuildMenu();

        _historyManager.RecentEntries.CollectionChanged += (s, e) => System.Windows.Application.Current.Dispatcher.Invoke(BuildMenu);
    }

    private void BuildMenu()
    {
        var contextMenu = new ContextMenuStrip();

        // Recent clips - now scrollable
        var recentItems = _historyManager.RecentEntries.ToList();
        if (recentItems.Any())
        {
            // Create a scrollable ListBox for clipboard entries
            var listBox = new ListBox
            {
                Height = 400,
                Width = 350,
                ScrollAlwaysVisible = true,
                SelectionMode = SelectionMode.One,
                BackColor = System.Drawing.Color.White,
                ForeColor = System.Drawing.Color.Black
            };

            // Add tooltip to guide users
            var toolTip = new ToolTip();
            toolTip.SetToolTip(listBox, "Double-click any item to copy it to clipboard");

            // Add clipboard entries to the ListBox
            foreach (var entry in recentItems)
            {
                var preview = entry.TextContent.Replace("\r", "").Replace("\n", " ").Trim();
                if (preview.Length > 50) preview = preview.Substring(0, 50) + "...";

                var formatBadge = entry.FormatType switch
                {
                    "Html" => "🌐",
                    "Rtf" => "📝",
                    _ => "📄"
                };

                var truncationIndicator = entry.IsTruncated ? " ⚠" : "";
                var displayText = $"{formatBadge} {preview}{truncationIndicator}";

                listBox.Items.Add(new ClipboardMenuItem(displayText, entry.TextContent));
            }

            // Handle double-click to copy to clipboard
            listBox.DoubleClick += (s, e) =>
            {
                if (listBox.SelectedItem is ClipboardMenuItem selectedItem)
                {
                    ClipboardMarker.SetMarkedText(selectedItem.FullText);
                }
            };

            // Embed the ListBox in the context menu using ToolStripControlHost
            var host = new ToolStripControlHost(listBox)
            {
                Padding = new Padding(0),
                Margin = new Padding(0)
            };

            contextMenu.Items.Add(host);
            contextMenu.Items.Add(new ToolStripSeparator());
        }

        // Settings
        var settingsItem = new ToolStripMenuItem("⚙ Settings");
        settingsItem.Click += (s, e) => ShowSettings();
        contextMenu.Items.Add(settingsItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        // Clear History
        var clearItem = new ToolStripMenuItem("🗑 Clear All History");
        clearItem.Click += async (s, e) => await _historyManager.ClearAllAsync(CancellationToken.None);
        contextMenu.Items.Add(clearItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        // Quit
        var quitItem = new ToolStripMenuItem("❌ Quit");
        quitItem.Click += (s, e) => System.Windows.Application.Current.Shutdown();
        contextMenu.Items.Add(quitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    private void ShowSettings()
    {
        using var scope = _serviceProvider.CreateScope();
        var settingsWindow = scope.ServiceProvider.GetRequiredService<SettingsWindow>();
        settingsWindow.ShowDialog();
    }

    public void Dispose()
    {
        _notifyIcon.Dispose();
    }
}

/// <summary>
/// Helper class to store display text and full clipboard text for ListBox items
/// </summary>
internal class ClipboardMenuItem
{
    public string DisplayText { get; }
    public string FullText { get; }

    public ClipboardMenuItem(string displayText, string fullText)
    {
        DisplayText = displayText;
        FullText = fullText;
    }

    public override string ToString()
    {
        return DisplayText;
    }
}
