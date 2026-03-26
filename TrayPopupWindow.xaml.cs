using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ClipManagerForWindows.Infrastructure;
using ClipManagerForWindows.Models;
using ClipManagerForWindows.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ClipManagerForWindows;

public partial class TrayPopupWindow : Window
{
    private readonly IHistoryManager _historyManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TrayPopupWindow> _logger;
    public ObservableCollection<ClipboardEntry> RecentEntries => _historyManager.RecentEntries;

    private ICollectionView? EntriesView =>
        EntriesListBox?.ItemsSource != null
            ? CollectionViewSource.GetDefaultView(EntriesListBox.ItemsSource)
            : null;

    public TrayPopupWindow(
        IHistoryManager historyManager,
        IServiceProvider serviceProvider,
        ILogger<TrayPopupWindow> logger)
    {
        _historyManager = historyManager;
        _serviceProvider = serviceProvider;
        _logger = logger;

        InitializeComponent();
        DataContext = this;

        Deactivated += (_, _) => HidePopup();

        // Handle delete button clicks from DataTemplate via routed event
        EntriesListBox.AddHandler(System.Windows.Controls.Primitives.ButtonBase.ClickEvent,
            new RoutedEventHandler(OnDeleteButtonClick));

        // Track empty state
        _historyManager.RecentEntries.CollectionChanged += OnEntriesChanged;
        UpdateEmptyState();

#if DEBUG
        DevAddRecordsButton.Visibility = Visibility.Visible;
#endif
    }

    public void ShowPopup()
    {
        UpdateEmptyState();
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - Width - 12;
        Top = workArea.Bottom - Height - 24;

        Opacity = 0;
        Show();
        Activate();

        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150));
        var slideUp = new DoubleAnimation(Top + 10, Top, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuadraticEase()
        };
        BeginAnimation(OpacityProperty, fadeIn);
        BeginAnimation(TopProperty, slideUp);

        SearchTextBox.Focus();
    }

    public void HidePopup()
    {
        SearchTextBox.Text = string.Empty;
        Hide();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        HidePopup();
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HidePopup();
        }
        else if (e.Key == Key.Delete && !SearchTextBox.IsFocused)
        {
            DeleteSelectedEntry();
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            int? index = e.Key switch
            {
                Key.D1 => 0,
                Key.D2 => 1,
                Key.D3 => 2,
                _ => null
            };

            if (index.HasValue)
            {
                CopyEntryByVisibleIndex(index.Value);
                e.Handled = true;
            }
        }
    }

    private void CopyEntryByVisibleIndex(int visibleIndex)
    {
        if (visibleIndex < EntriesListBox.Items.Count)
        {
            var entry = EntriesListBox.Items[visibleIndex] as ClipboardEntry;
            if (entry != null)
            {
                ClipboardMarker.SetMarkedText(entry.TextContent);
                HidePopup();
            }
        }
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        // Guard: this can fire during InitializeComponent before fields are ready
        if (EntriesView == null || SearchPlaceholder == null)
            return;

        var searchText = SearchTextBox.Text;

        SearchPlaceholder.Visibility = string.IsNullOrEmpty(searchText)
            ? Visibility.Visible
            : Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(searchText))
        {
            EntriesView.Filter = null;
        }
        else
        {
            EntriesView.Filter = obj =>
                obj is ClipboardEntry entry &&
                FuzzyMatcher.FuzzyMatch(searchText, entry.TextContent);
        }

        UpdateEmptyState();
    }

    private void OnEntryClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (EntriesListBox.SelectedItem is ClipboardEntry entry)
        {
            ClipboardMarker.SetMarkedText(entry.TextContent);
            HidePopup();
        }
    }

    private void OnEntryRightClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Select the item under the cursor so the context menu knows which entry to act on
        var item = ItemsControl.ContainerFromElement(EntriesListBox,
            (DependencyObject)e.OriginalSource) as System.Windows.Controls.ListBoxItem;
        if (item != null)
        {
            EntriesListBox.SelectedItem = item.DataContext;
        }
    }

    private async void OnDeleteEntryClick(object sender, RoutedEventArgs e)
    {
        if (EntriesListBox.SelectedItem is ClipboardEntry entry)
        {
            await AnimateAndDeleteAsync(entry);
        }
    }

    private void OnDeleteButtonClick(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is System.Windows.Controls.Button btn &&
            btn.Name == "DeleteBtn" &&
            btn.Tag is ClipboardEntry entry)
        {
            e.Handled = true; // prevent click from bubbling to list item
            _ = AnimateAndDeleteAsync(entry);
        }
    }

    private void DeleteSelectedEntry()
    {
        if (EntriesListBox.SelectedItem is ClipboardEntry entry)
        {
            _ = AnimateAndDeleteAsync(entry);
        }
    }

    private async Task AnimateAndDeleteAsync(ClipboardEntry entry)
    {
        var container = EntriesListBox.ItemContainerGenerator.ContainerFromItem(entry) as ListBoxItem;
        if (container != null)
        {
            var itemHeight = container.ActualHeight;

            // Collect visible items below the deleted one for spring animation
            var entryIndex = EntriesListBox.Items.IndexOf(entry);
            var siblingContainers = new System.Collections.Generic.List<ListBoxItem>();
            for (int i = entryIndex + 1; i < EntriesListBox.Items.Count; i++)
            {
                var sibling = EntriesListBox.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
                if (sibling == null) break; // stop at non-visible items
                siblingContainers.Add(sibling);
            }

            // Phase 1: Fade out + slide left the deleted item (250ms)
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var slideLeft = new DoubleAnimation(0, -40, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            // Set up RenderTransform for slide
            var deleteTransform = new TranslateTransform();
            container.RenderTransform = deleteTransform;

            var phase1Done = new TaskCompletionSource<bool>();
            fadeOut.Completed += (_, _) => phase1Done.TrySetResult(true);

            container.BeginAnimation(OpacityProperty, fadeOut);
            deleteTransform.BeginAnimation(TranslateTransform.XProperty, slideLeft);

            await phase1Done.Task;

            // Phase 2: Collapse height of deleted item + spring siblings upward
            var collapseHeight = new DoubleAnimation(itemHeight, 0, TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            // Collapse padding/margin too
            var collapseMargin = new ThicknessAnimation(
                container.Margin, new Thickness(0), TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var collapsePadding = new ThicknessAnimation(
                container.Padding, new Thickness(0), TimeSpan.FromMilliseconds(350))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            container.BeginAnimation(HeightProperty, collapseHeight);
            container.BeginAnimation(MarginProperty, collapseMargin);
            container.BeginAnimation(PaddingProperty, collapsePadding);

            // Spring siblings upward — each starts with an offset from the gap
            foreach (var sibling in siblingContainers)
            {
                var springTransform = new TranslateTransform(0, itemHeight * 0.3);
                sibling.RenderTransform = springTransform;

                var springUp = new DoubleAnimation(itemHeight * 0.3, 0, TimeSpan.FromMilliseconds(500))
                {
                    EasingFunction = new ElasticEase
                    {
                        Oscillations = 1,
                        Springiness = 5,
                        EasingMode = EasingMode.EaseOut
                    }
                };
                springTransform.BeginAnimation(TranslateTransform.YProperty, springUp);
            }

            // Wait for collapse to finish
            var phase2Done = new TaskCompletionSource<bool>();
            collapseHeight.Completed += (_, _) => phase2Done.TrySetResult(true);
            await phase2Done.Task;
        }

        await _historyManager.DeleteAsync(entry.Id, CancellationToken.None);
    }

    private void OnSettingsClick(object sender, RoutedEventArgs e)
    {
        using var scope = _serviceProvider.CreateScope();
        var settingsWindow = scope.ServiceProvider.GetRequiredService<SettingsWindow>();
        settingsWindow.ShowDialog();
    }

    private async void OnClearAllClick(object sender, RoutedEventArgs e)
    {
        await _historyManager.ClearAllAsync(CancellationToken.None);
    }

    private void OnQuitClick(object sender, RoutedEventArgs e)
    {
        System.Windows.Application.Current.Shutdown();
    }

#if DEBUG
    private async void OnDevAddRecordsClick(object sender, RoutedEventArgs e)
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

            for (int i = 1; i <= 150; i++)
            {
                var contentIndex = (i - 1) % testContents.Length;
                var content = testContents[contentIndex] + i;
                var entry = new ClipboardEntry
                {
                    TextContent = content,
                    CreatedUtc = DateTime.UtcNow.AddMinutes(-i)
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
#else
    private void OnDevAddRecordsClick(object sender, RoutedEventArgs e) { }
#endif

    private void OnEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(UpdateEmptyState,
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void UpdateEmptyState()
    {
        var totalCount = _historyManager.RecentEntries.Count;
        var visibleCount = EntriesListBox.Items.Count;

        if (totalCount == 0)
        {
            EmptyStateText.Text = "No clipboard entries yet";
            EmptyStateText.Visibility = Visibility.Visible;
            EntriesListBox.Visibility = Visibility.Collapsed;
        }
        else if (visibleCount == 0)
        {
            EmptyStateText.Text = "No matching entries";
            EmptyStateText.Visibility = Visibility.Visible;
            EntriesListBox.Visibility = Visibility.Collapsed;
        }
        else
        {
            EmptyStateText.Visibility = Visibility.Collapsed;
            EntriesListBox.Visibility = Visibility.Visible;
        }
    }
}
