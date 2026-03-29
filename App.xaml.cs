using System.Configuration;
using System.Data;
using System.Windows;
using System.Windows.Controls;
using System.Drawing;
using Hardcodet.Wpf.TaskbarNotification;
using TaskSplit.Models;
using TaskSplit.Services;
using TaskSplit.Views;

namespace TaskSplit;

/// <summary>Interaction logic for App.xaml</summary>
public partial class App : Application
{
    private TaskbarIcon? _notifyIcon;
    private TaskbarOverlay? _overlay;
    private ConfigService? _configService;
    private AppConfig? _config;
    private TaskbarService? _taskbarService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _configService = new ConfigService();
        _config = _configService.Load();
        _taskbarService = new TaskbarService();

        InitializeTray();
        InitializeOverlay();

        // Timer or periodic check for window updates? Let's use simple polling for MVP
        System.Windows.Threading.DispatcherTimer timer = new();
        timer.Interval = TimeSpan.FromSeconds(2); // Refresh every 2 seconds
        timer.Tick += (s, ev) => _overlay?.SyncToTaskbar();
        timer.Start();
    }

    private void InitializeTray()
    {
        _notifyIcon = new TaskbarIcon
        {
            ToolTipText = "TaskSplit - Organized Windows",
            Icon = SystemIcons.Application,
            Visibility = Visibility.Visible
        };

        var menu = new ContextMenu();
        var toggleItem = new MenuItem { Header = "Show Overlay", IsCheckable = true, IsChecked = true };
        toggleItem.Click += (s, ev) => {
            if (_overlay != null) {
                if (_overlay.IsVisible) _overlay.Hide();
                else _overlay.Show();
                toggleItem.IsChecked = _overlay.IsVisible;
            }
        };

        var settingsItem = new MenuItem { Header = "Settings" };
        settingsItem.Click += (s, ev) => ShowSettings();

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (s, ev) => Shutdown();

        menu.Items.Add(toggleItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        _notifyIcon.ContextMenu = menu;
    }

    private void InitializeOverlay()
    {
        _overlay = new TaskbarOverlay(_taskbarService!);
        _overlay.UpdateConfig(_config!);
        _overlay.Show();
    }

    private void ShowSettings()
    {
        // For MVP, just show a message box pointing to the config file
        MessageBox.Show($"TaskSplit Settings\n\nEdit config at:\n%AppData%\\TaskSplit\\config.json\n\n(Complete UI coming in Phase 2!)", "TaskSplit Settings");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        base.OnExit(e);
    }
}
