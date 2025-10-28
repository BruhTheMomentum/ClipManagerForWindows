using System;
using System.IO;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using ClipManagerForWindows.Infrastructure;
using ClipManagerForWindows.Models;
using ClipManagerForWindows.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Application = System.Windows.Application;

namespace ClipManagerForWindows.ViewModels;

public partial class NotifyIconViewModel : IDisposable
{
    #region P/Invoke Declarations for File Handle Management

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEM_HANDLE_TABLE_ENTRY_INFO
    {
        public ushort UniqueProcessId;
        public ushort CreatorBackTraceIndex;
        public byte ObjectTypeIndex;
        public byte HandleAttributes;
        public ushort HandleValue;
        public IntPtr Object;
        public IntPtr GrantedAccess;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct OBJECT_NAME_INFORMATION
    {
        public UNICODE_STRING Name;
    }

    [DllImport("ntdll.dll", SetLastError = true)]
    public static extern int NtQuerySystemInformation(
        int SystemInformationClass,
        IntPtr SystemInformation,
        int SystemInformationLength,
        out int ReturnLength);

    [DllImport("ntdll.dll", SetLastError = true)]
    public static extern int NtQueryObject(
        IntPtr Handle,
        int ObjectInformationClass,
        IntPtr ObjectInformation,
        int ObjectInformationLength,
        out int ReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(
        uint dwDesiredAccess,
        bool bInheritHandle,
        uint dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool DuplicateHandle(
        IntPtr hSourceProcessHandle,
        IntPtr hSourceHandle,
        IntPtr hTargetProcessHandle,
        out IntPtr lpTargetHandle,
        uint dwDesiredAccess,
        bool bInheritHandle,
        uint dwOptions);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint GetLastError();

    private const int SystemHandleInformation = 16;
    private const int ObjectNameInformation = 1;
    private const uint PROCESS_DUP_HANDLE = 0x0040;
    private const uint DUPLICATE_CLOSE_SOURCE = 0x00000001;

    #endregion

    private readonly IServiceProvider _serviceProvider;
    private readonly IHistoryManager _historyManager;
    private readonly IStartupManager _startupManager;
    private readonly ILogger<NotifyIconViewModel> _logger;
    private readonly NotifyIcon _notifyIcon;

    public NotifyIconViewModel(IServiceProvider serviceProvider, IHistoryManager historyManager, IStartupManager startupManager, ILogger<NotifyIconViewModel> logger)
    {
        _serviceProvider = serviceProvider;
        _historyManager = historyManager;
        _startupManager = startupManager;
        _logger = logger;

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

        // Recent clips - now scrollable with max 10 items
        var recentItems = _historyManager.RecentEntries.ToList();
        if (recentItems.Any())
        {
            // Create a scrollable ListBox for clipboard entries
            var listBox = new ListBox
            {
                Height = 280,
                Width = 550, 
                MaximumSize = new System.Drawing.Size(550, 280), // Enforce maximum size
                MinimumSize = new System.Drawing.Size(550, 280), // Enforce minimum size
                ScrollAlwaysVisible = true, // Show scrollbar when content overflows
                SelectionMode = SelectionMode.One,
                BackColor = System.Drawing.Color.White,
                ForeColor = System.Drawing.Color.Black,
                IntegralHeight = false // Allow partial items at bottom
            };

            // Load all items to enable scrolling
            var displayItems = recentItems.ToList();

            
            // Add clipboard entries to the ListBox
            foreach (var entry in displayItems)
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

            // Handle single-click to copy to clipboard
            listBox.Click += (s, e) =>
            {
                if (listBox.SelectedItem is ClipboardMenuItem selectedItem)
                {
                    ClipboardMarker.SetMarkedText(selectedItem.FullText);
                    contextMenu.Close(); // Close the tray menu after copying
                }
            };

            // Embed the ListBox in the context menu using ToolStripControlHost
            var host = new ToolStripControlHost(listBox)
            {
                Padding = new Padding(0),
                Margin = new Padding(0),
                Size = new System.Drawing.Size(555, 280), // Enforce host size (increased width)
                AutoSize = false // Prevent automatic resizing
            };

            contextMenu.Items.Add(host);
            contextMenu.Items.Add(new ToolStripSeparator());
        }

        // Settings
        var settingsItem = new ToolStripMenuItem("⚙ Settings");
        settingsItem.Click += (s, e) =>
        {
            ShowSettings();
            contextMenu.Close(); // Close the tray menu after opening settings
        };
        contextMenu.Items.Add(settingsItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        // Clear History
        var clearItem = new ToolStripMenuItem("🗑 Clear All History");
        clearItem.Click += async (s, e) =>
        {
            await _historyManager.ClearAllAsync(CancellationToken.None);
            contextMenu.Close(); // Close the tray menu after clearing history
        };
        contextMenu.Items.Add(clearItem);

        contextMenu.Items.Add(new ToolStripSeparator());

#if DEBUG
        // [DEV] Add Records - only in Debug builds
        var addRecordsItem = new ToolStripMenuItem("[DEV] Add Records");
        addRecordsItem.Click += async (s, e) => await AddTestRecordsAsync();
        contextMenu.Items.Add(addRecordsItem);

        contextMenu.Items.Add(new ToolStripSeparator());
#endif

        // Quit
        var quitItem = new ToolStripMenuItem("❌ Quit");
        quitItem.Click += (s, e) => System.Windows.Application.Current.Shutdown();
        contextMenu.Items.Add(quitItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
    }

#if DEBUG
    /// <summary>
    /// Creates 150 test records for debugging the scroll functionality
    /// </summary>
    private async Task AddTestRecordsAsync()
    {
        try
        {
            _logger.LogInformation("Adding 150 test records for debugging");

            var testContents = new[]
            {
                "Sample text content #",
                "Lorem ipsum dolor sit amet #",
                "Test clipboard entry #",
                "Development record #",
                "Debug data item #",
                "Sample code snippet #",
                "Example text #",
                "Test data #",
                "Development entry #",
                "Debug content #"
            };

            var formats = new[] { "Text", "Html", "Rtf" };

            for (int i = 1; i <= 150; i++)
            {
                var contentIndex = (i - 1) % testContents.Length;
                var formatIndex = (i - 1) % formats.Length;

                var content = testContents[contentIndex] + i;
                var format = formats[formatIndex];

                // Create a clipboard entry based on format
                switch (format)
                {
                    case "Html":
                        content = $"<p>{content}</p>";
                        break;
                    case "Rtf":
                        content = $@"{{\rtf1\ansi\deff0 {{\fonttbl {{\f0 Times New Roman;}}}}\f0\fs24 {content}}}";
                        break;
                }

                // Create the clipboard entry
                var entry = new ClipboardEntry
                {
                    TextContent = content,
                    FormatType = format,
                    CreatedUtc = DateTime.UtcNow.AddMinutes(-i), // Stagger timestamps
                    SourceApp = "DebugTestApp",
                    IsTruncated = false
                };

                await _historyManager.AddAsync(entry, CancellationToken.None);
            }

            _logger.LogInformation("Successfully added 150 test records");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add test records");
        }
    }
#endif

    /// <summary>
    /// Forces closure of all file handles for a specific file path
    /// </summary>
    private void ForceCloseFileHandles(string filePath)
    {
        try
        {
            _logger.LogInformation("Attempting to force close file handles for: {path}", filePath);

            // Get full path for comparison
            var fullPath = Path.GetFullPath(filePath);
            var handlesClosed = 0;

            // Query system handle information
            int sizeNeeded;
            int result = NtQuerySystemInformation(SystemHandleInformation, IntPtr.Zero, 0, out sizeNeeded);

            if (result != 0 && sizeNeeded == 0)
            {
                _logger.LogWarning("Failed to query system handle information");
                return;
            }

            IntPtr buffer = Marshal.AllocHGlobal(sizeNeeded);
            try
            {
                result = NtQuerySystemInformation(SystemHandleInformation, buffer, sizeNeeded, out sizeNeeded);
                if (result != 0)
                {
                    _logger.LogWarning("Failed to get system handle information: {result}", result);
                    return;
                }

                int handleCount = Marshal.ReadInt32(buffer);
                IntPtr handleInfoPtr = buffer + 4;

                for (int i = 0; i < handleCount; i++)
                {
                    var handleInfo = (SYSTEM_HANDLE_TABLE_ENTRY_INFO)Marshal.PtrToStructure(
                        handleInfoPtr, typeof(SYSTEM_HANDLE_TABLE_ENTRY_INFO));

                    // Skip handles from our own process (we'll handle them separately)
                    if (handleInfo.UniqueProcessId == Environment.ProcessId || handleInfo.HandleValue == 0)
                    {
                        handleInfoPtr += Marshal.SizeOf(typeof(SYSTEM_HANDLE_TABLE_ENTRY_INFO));
                        continue;
                    }

                    // Open the target process to duplicate its handle
                    IntPtr hProcess = OpenProcess(PROCESS_DUP_HANDLE, false, handleInfo.UniqueProcessId);
                    if (hProcess == IntPtr.Zero)
                    {
                        handleInfoPtr += Marshal.SizeOf(typeof(SYSTEM_HANDLE_TABLE_ENTRY_INFO));
                        continue;
                    }

                    try
                    {
                        IntPtr duplicatedHandle;
                        bool success = DuplicateHandle(hProcess,
                            (IntPtr)handleInfo.HandleValue,
                            IntPtr.Zero,
                            out duplicatedHandle,
                            0,
                            false,
                            DUPLICATE_CLOSE_SOURCE);

                        // Try to get the file name for this handle
                        int nameInfoSize = 512;
                        IntPtr nameInfoPtr = Marshal.AllocHGlobal(nameInfoSize);
                        try
                        {
                            result = NtQueryObject(duplicatedHandle, ObjectNameInformation, nameInfoPtr, nameInfoSize, out nameInfoSize);
                            if (result == 0)
                            {
                                var nameInfo = (OBJECT_NAME_INFORMATION)Marshal.PtrToStructure(nameInfoPtr, typeof(OBJECT_NAME_INFORMATION));
                                if (nameInfo.Name.Buffer != null)
                                {
                                    var handlePath = nameInfo.Name.Buffer;
                                    if (string.Equals(handlePath, fullPath, StringComparison.OrdinalIgnoreCase))
                                    {
                                        _logger.LogInformation("Closing handle from process {pid} for file: {path}", handleInfo.UniqueProcessId, fullPath);
                                        CloseHandle(duplicatedHandle);
                                        handlesClosed++;
                                    }
                                }
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(nameInfoPtr);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Error processing handle from process {pid}", handleInfo.UniqueProcessId);
                    }
                    finally
                    {
                        CloseHandle(hProcess);
                    }

                    handleInfoPtr += Marshal.SizeOf(typeof(SYSTEM_HANDLE_TABLE_ENTRY_INFO));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }

            _logger.LogInformation("Force closed {count} file handles for: {path}", handlesClosed, fullPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while trying to force close file handles for: {path}", filePath);
        }
    }

    public async Task RemoveEntirelyAsync()
    {
        try
        {
            _logger.LogInformation("Starting complete removal of ClipManagerForWindows");

            // Remove from Windows startup
            await _startupManager.SetEnabledAsync(false);
            _logger.LogInformation("Removed from Windows startup");

            // Get paths before shutting down services
            var databasePath = AppPaths.GetDatabasePath(null);
            var appDataDir = AppPaths.GetAppDataDirectory();


            // Close context menu to prevent UI interactions
            _notifyIcon.ContextMenuStrip?.Close();

            // Stop the host to close all database connections
            var app = (App)Application.Current;
            await app.StopHostAsync();

            // Clear SQLite connection pools to ensure all connections are properly disposed
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            _logger.LogInformation("Cleared all SQLite connection pools");

            // Force close any remaining file handles on the database
            ForceCloseFileHandles(databasePath);

            // Small delay to ensure handle cleanup
            await Task.Delay(500);

            // Now delete the entire app data directory (should work after force handle closure)
            if (Directory.Exists(appDataDir))
            {
                try
                {
                    Directory.Delete(appDataDir, recursive: true);
                    _logger.LogInformation("Deleted app data directory: {path}", appDataDir);
                }
                catch (IOException ex)
                {
                    _logger.LogWarning(ex, "Could not delete app data directory: {path}", appDataDir);

                    // Fallback: try individual file deletion
                    if (File.Exists(databasePath))
                    {
                        try
                        {
                            File.Delete(databasePath);
                            _logger.LogInformation("Deleted database file: {path}", databasePath);
                        }
                        catch (Exception fileEx)
                        {
                            _logger.LogError(fileEx, "Failed to delete database file even after force handle closure: {path}", databasePath);
                        }
                    }
                }
            }

            _logger.LogInformation("Complete removal finished successfully");

            // Force shutdown
            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to completely remove ClipManagerForWindows");

            System.Windows.MessageBox.Show(
                $"Failed to completely remove ClipManagerForWindows:\n{ex.Message}",
                "Removal Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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
