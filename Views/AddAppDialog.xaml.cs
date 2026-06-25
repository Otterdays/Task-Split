using System.Windows;
using System.Windows.Controls;
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
        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private async Task RunSearchAsync(string query)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        StatusText.Text = "Searching…";
        AddButton.IsEnabled = false;

        try
        {
            var results = await _discovery.SearchAsync(query, token);
            if (token.IsCancellationRequested) return;

            ResultsList.ItemsSource = results;
            StatusText.Text = results.Count == 0
                ? "No matches — try Browse or another search term"
                : $"{results.Count} app(s) found";
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

        var list = (ResultsList.ItemsSource as IEnumerable<DiscoveredApp>)?.ToList() ?? [];
        list.Insert(0, app);
        ResultsList.ItemsSource = list;
        ResultsList.SelectedItem = app;
        SearchBox.Text = app.DisplayName;
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
