using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TaskSplit.Models;
using TaskSplit.Services;

namespace TaskSplit.Views;

public partial class AddAppDialog : Window
{
    private readonly AppConfig _config;
    private readonly AppDiscoveryService _discovery;
    private readonly DispatcherTimer _searchTimer;
    private CancellationTokenSource? _searchCts;
    private string _lastSearchQuery = "";
    private bool _suppressSearch;

    public AppConfig? UpdatedConfig { get; private set; }

    public AddAppDialog(AppConfig config, AppDiscoveryService discovery)
    {
        InitializeComponent();
        _config = config;
        _discovery = discovery;

        GroupCombo.ItemsSource = config.Groups;
        if (config.Groups.Count > 0)
            GroupCombo.SelectedIndex = 0;

        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _searchTimer.Tick += async (_, _) =>
        {
            _searchTimer.Stop();
            await RunSearchAsync(SearchBox.Text);
        };

        Loaded += async (_, _) => await RunSearchAsync("");
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressSearch) return;

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private async Task RunSearchAsync(string query)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        _lastSearchQuery = query;

        StatusText.Text = "Searching…";
        AddButton.IsEnabled = false;

        try
        {
            var results = await _discovery.SearchAsync(query, token);
            if (token.IsCancellationRequested) return;

            ResultsList.ItemsSource = results;
            StatusText.Text = results.Count == 0
                ? "No matches — try Browse or another search term"
                : string.IsNullOrWhiteSpace(query)
                    ? $"{results.Count} app(s), newest first · right-click to delete junk"
                    : $"{results.Count} app(s) found · right-click to delete junk";
        }
        catch (OperationCanceledException)
        {
            // Superseded by newer search
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Search failed: {ex.Message}";
        }
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
        AddButton.IsEnabled = ResultsList.SelectedItem is DiscoveredApp;

    private void ResultsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindParent<ListBoxItem>(e.OriginalSource as DependencyObject);
        if (item == null) return;

        item.IsSelected = true;
        item.Focus();
        e.Handled = false;
    }

    private void ResultsList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (ResultsList.SelectedItem is not DiscoveredApp app)
        {
            DeleteFromSystemItem.IsEnabled = false;
            return;
        }

        DeleteFromSystemItem.IsEnabled = File.Exists(app.ExePath);
    }

    private async void DeleteFromSystemItem_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not DiscoveredApp app)
            return;

        var confirm = MessageBox.Show(
            $"Permanently delete this file from your PC?\n\n{app.DisplayName}\n{app.ExePath}\n\nThis cannot be undone.",
            "Delete from system",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (confirm != MessageBoxResult.Yes)
            return;

        var result = _discovery.TryDeleteExecutable(app.ExePath);
        if (!result.Success)
        {
            MessageBox.Show(result.Message, "Delete from system", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (RemoveProcessFromGroups(app.ProcessName))
            UpdatedConfig = _config;

        _config.KnownExePaths.Remove(app.ProcessName);
        _discovery.UnregisterKnownApp(app.ProcessName);

        _discovery.InvalidateIndex();
        await RunSearchAsync(_lastSearchQuery);

        StatusText.Text = $"Deleted {app.DisplayName}";
    }

    private bool RemoveProcessFromGroups(string processName)
    {
        var removed = false;
        foreach (var group in _config.Groups)
        {
            var matches = group.ProcessNames
                .Where(p => p.Equals(processName, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var match in matches)
            {
                group.ProcessNames.Remove(match);
                removed = true;
            }
        }

        return removed;
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T match)
                return match;
            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select an application",
            Filter = "Executables (*.exe)|*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
        };

        if (dialog.ShowDialog() != true) return;

        var app = _discovery.FromExePath(dialog.FileName);
        if (app == null)
        {
            MessageBox.Show("Could not read that executable.", "Add App", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ShowBrowsedApp(app);
    }

    private void ShowBrowsedApp(DiscoveredApp app)
    {
        _searchTimer.Stop();
        _searchCts?.Cancel();

        _lastSearchQuery = "";
        _suppressSearch = true;
        try
        {
            SearchBox.Text = app.DisplayName;
        }
        finally
        {
            _suppressSearch = false;
        }

        ResultsList.ItemsSource = new List<DiscoveredApp> { app };
        ResultsList.SelectedItem = app;
        ResultsList.ScrollIntoView(app);
        AddButton.IsEnabled = true;
        StatusText.Text = $"Browsed: {app.DisplayName} — click Add App";
    }

    private void RememberApp(DiscoveredApp app)
    {
        _config.KnownExePaths[app.ProcessName] = app.ExePath;
        _discovery.RegisterKnownApp(app);
    }

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        if (ResultsList.SelectedItem is not DiscoveredApp app)
            return;

        if (GroupCombo.SelectedItem is not TaskbarGroup group)
        {
            MessageBox.Show("Select a group first.", "Add App", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (group.ProcessNames.Contains(app.ProcessName, StringComparer.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                $"{app.DisplayName} is already in \"{group.Name}\".",
                "Add App",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        group.ProcessNames.Add(app.ProcessName);
        RememberApp(app);
        UpdatedConfig = _config;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
