using System;
using System.IO;
using System.Drawing;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using ClipManagerForWindows.Infrastructure;
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
    private readonly TrayPopupWindow _popupWindow;

    public NotifyIconViewModel(
        IServiceProvider serviceProvider,
        IHistoryManager historyManager,
        IStartupManager startupManager,
        TrayPopupWindow popupWindow,
        ILogger<NotifyIconViewModel> logger)
    {
        _serviceProvider = serviceProvider;
        _historyManager = historyManager;
        _startupManager = startupManager;
        _popupWindow = popupWindow;
        _logger = logger;

        _notifyIcon = new NotifyIcon();
        using var iconStream = typeof(NotifyIconViewModel).Assembly
            .GetManifestResourceStream("ClipManagerForWindows.Assets.clip.ico");
        _notifyIcon.Icon = new Icon(iconStream!);
        _notifyIcon.Text = "Clip Manager";
        _notifyIcon.Visible = true;

        _notifyIcon.MouseClick += (s, e) =>
        {
            Application.Current.Dispatcher.Invoke(TogglePopup);
        };
    }

    private void TogglePopup()
    {
        if (_popupWindow.IsVisible)
            _popupWindow.HidePopup();
        else
            _popupWindow.ShowPopup();
    }

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

            // Hide popup to prevent UI interactions
            _popupWindow.HidePopup();

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

    public void Dispose()
    {
        _notifyIcon.Dispose();
    }
}
