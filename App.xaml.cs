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
    private AppDiscoveryService? _appDiscoveryService;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _configService = new ConfigService();
        _config = _configService.Load();
        _taskbarService = new TaskbarService();
        _appDiscoveryService = new AppDiscoveryService();

        InitializeTray();
        InitializeOverlay();

        // Timer or periodic check for window updates? Let's use simple polling for MVP
        System.Windows.Threading.DispatcherTimer timer = new();
        timer.Interval = TimeSpan.FromSeconds(2); // Refresh every 2 seconds
        timer.Tick += (s, ev) =>
        {
            // Only poll taskbar position when auto-snapped; never rebuild UI during manual drag.
            if (_overlay?.IsManualLayout == true)
                _overlay.Refresh();
            else
                _overlay?.SyncToTaskbar();
        };
        timer.Start();
    }

    private void InitializeTray()
    {
        _notifyIcon = new TaskbarIcon
        {
            ToolTipText = "Task-Split — Organized Windows",
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

        var debugItem = new MenuItem { Header = "Debug Overlay Info" };
        debugItem.Click += (s, ev) => ShowDebugInfo();

        var snapItem = new MenuItem { Header = "Snap to Taskbar" };
        snapItem.Click += (s, ev) => _overlay?.SnapToTaskbar();

        var resetItem = new MenuItem { Header = "Reset Taskbar Cache" };
        resetItem.Click += (s, ev) =>
        {
            _taskbarService?.ResetCache();
            _overlay?.SnapToTaskbar();
        };

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (s, ev) => Shutdown();

        menu.Items.Add(toggleItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(snapItem);
        menu.Items.Add(debugItem);
        menu.Items.Add(resetItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        _notifyIcon.ContextMenu = menu;
    }

    private void InitializeOverlay()
    {
        _overlay = new TaskbarOverlay(_taskbarService!, _configService!, _appDiscoveryService!, _config!);
        _overlay.ConfigChanged += config => _config = config;
        _overlay.Show();
    }

    private void ShowSettings()
    {
        MessageBox.Show(
            $"Task-Split Settings\n\nEdit config at:\n%AppData%\\TaskSplit\\config.json\n\n(Complete UI coming in Phase 2!)",
            "Task-Split Settings");
    }

    private void ShowDebugInfo()
    {
        _overlay?.SyncToTaskbar();
        var diag = _overlay?.GetDiagnostics();
        if (diag == null) return;

        MessageBox.Show(
            diag.ToReport() + $"\n\nLog: %AppData%\\TaskSplit\\debug.log",
            "Task-Split Debug");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _notifyIcon?.Dispose();
        base.OnExit(e);
    }
}
