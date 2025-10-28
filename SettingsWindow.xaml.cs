using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ClipManagerForWindows.Services;
using ClipManagerForWindows.ViewModels;
using Microsoft.Extensions.Logging;

namespace ClipManagerForWindows;

public partial class SettingsWindow : Window
{
    private readonly ISettingsStore _settings;
    private readonly IStartupManager _startup;
    private readonly IHistoryManager _history;
    private readonly NotifyIconViewModel _notifyIconViewModel;
    private readonly ILogger<SettingsWindow> _logger;

    public SettingsWindow(
        ISettingsStore settings,
        IStartupManager startup,
        IHistoryManager history,
        NotifyIconViewModel notifyIconViewModel,
        ILogger<SettingsWindow> logger)
    {
        _settings = settings;
        _startup = startup;
        _history = history;
        _notifyIconViewModel = notifyIconViewModel;
        _logger = logger;

        InitializeComponent();
        Loaded += OnLoaded;
        SaveButton.Click += OnSaveClick;
        CancelButton.Click += (s, e) => Close();
        ClearAllButton.Click += OnClearAllClick;
        RemoveEntirelyButton.Click += OnRemoveEntirelyClick;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Load settings
            var maxEntries = await _settings.GetAsync("MaxEntries", CancellationToken.None) ?? "500";
            var retentionDays = await _settings.GetAsync("RetentionDays", CancellationToken.None) ?? "0";
            var startupEnabled = await _startup.IsEnabledAsync();

            MaxEntriesTextBox.Text = maxEntries;
            RetentionDaysTextBox.Text = retentionDays;
            LaunchOnStartupCheckBox.IsChecked = startupEnabled;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load settings");
            ShowErrorDialog("Failed to load settings.");
        }
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            // Validate inputs
            if (!int.TryParse(MaxEntriesTextBox.Text, out var maxEntries) || maxEntries < 10 || maxEntries > 10000)
            {
                ShowWarningDialog("Max entries must be between 10 and 10000.");
                return;
            }

            if (!int.TryParse(RetentionDaysTextBox.Text, out var retentionDays) || retentionDays < 0 || retentionDays > 3650)
            {
                ShowWarningDialog("Retention days must be between 0 and 3650.");
                return;
            }

            // Save settings
            await _settings.SetAsync("MaxEntries", maxEntries.ToString(), CancellationToken.None);
            await _settings.SetAsync("RetentionDays", retentionDays.ToString(), CancellationToken.None);

            // Update startup
            await _startup.SetEnabledAsync(LaunchOnStartupCheckBox.IsChecked == true);

            // Apply the new max entries setting to HistoryManager
            await _history.UpdateMaxEntriesAsync(maxEntries, CancellationToken.None);

            _logger.LogInformation("Settings saved successfully");
            Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings");
            ShowErrorDialog("Failed to save settings.");
        }
    }

    private async void OnClearAllClick(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            "This will DELETE ALL clipboard history. This cannot be undone. Continue?",
            "Clear All",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                await _history.ClearAllAsync(CancellationToken.None);
                System.Windows.MessageBox.Show("All clipboard history cleared.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear all history");
                ShowErrorDialog("Failed to clear all history.");
            }
        }
    }

    private async void OnRemoveEntirelyClick(object sender, RoutedEventArgs e)
    {
        var result = System.Windows.MessageBox.Show(
            "This action cannot be undone",
            "Remove ClipManager Entirely",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                await _notifyIconViewModel.RemoveEntirelyAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove ClipManager entirely");
                ShowErrorDialog("Failed to remove ClipManager entirely.");
            }
        }
    }

    private void ShowErrorDialog(string message)
    {
        System.Windows.MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void ShowWarningDialog(string message)
    {
        System.Windows.MessageBox.Show(message, "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}