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
    private MenuItem? _showOverlayItem;
    private MenuItem? _compactBarItem;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _configService = new ConfigService();
        _config = _configService.Load();
        _taskbarService = new TaskbarService();
        _appDiscoveryService = new AppDiscoveryService();
        _appDiscoveryService.SetKnownExePaths(_config.KnownExePaths);

        InitializeTray();
        InitializeOverlay();

        System.Windows.Threading.DispatcherTimer timer = new();
        timer.Interval = TimeSpan.FromSeconds(2);
        timer.Tick += (s, ev) =>
        {
            if (_overlay?.VisibilityMode == OverlayVisibilityMode.Hidden
                || _overlay?.IsVisibilityAnimating == true)
                return;

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
        menu.Opened += (_, _) => SyncTrayMenuFromOverlay();

        _showOverlayItem = new MenuItem { Header = "Show Overlay", IsCheckable = true, IsChecked = true };
        _showOverlayItem.Click += (_, _) =>
        {
            _overlay?.ToggleHidden();
            SyncTrayMenuFromOverlay();
        };

        _compactBarItem = new MenuItem { Header = "Compact bar", IsCheckable = true, IsChecked = false };
        _compactBarItem.Click += (_, _) =>
        {
            _overlay?.ToggleCompact();
            SyncTrayMenuFromOverlay();
        };

        var settingsItem = new MenuItem { Header = "Settings" };
        settingsItem.Click += (_, _) => ShowSettings();

        var debugItem = new MenuItem { Header = "Debug Overlay Info" };
        debugItem.Click += (_, _) => ShowDebugInfo();

        var snapItem = new MenuItem { Header = "Snap to Taskbar" };
        snapItem.Click += (_, _) => _overlay?.SnapToTaskbar();

        var resetItem = new MenuItem { Header = "Reset Taskbar Cache" };
        resetItem.Click += (_, _) =>
        {
            _taskbarService?.ResetCache();
            _overlay?.SnapToTaskbar();
        };

        var exitItem = new MenuItem { Header = "Exit" };
        exitItem.Click += (_, _) => Shutdown();

        menu.Items.Add(_showOverlayItem);
        menu.Items.Add(_compactBarItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(settingsItem);
        menu.Items.Add(snapItem);
        menu.Items.Add(debugItem);
        menu.Items.Add(resetItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(exitItem);

        _notifyIcon.ContextMenu = menu;

        _notifyIcon.TrayMouseDoubleClick += (_, _) =>
        {
            _overlay?.RestoreFromTray();
            SyncTrayMenuFromOverlay();
        };
    }

    private void InitializeOverlay()
    {
        _overlay = new TaskbarOverlay(_taskbarService!, _configService!, _appDiscoveryService!, _config!);
        _overlay.ConfigChanged += config =>
        {
            _config = config;
            _appDiscoveryService?.SetKnownExePaths(config.KnownExePaths);
        };
        _overlay.VisibilityModeChanged += SyncTrayMenuFromOverlay;
        _overlay.Show();
    }

    private void SyncTrayMenuFromOverlay()
    {
        if (_overlay == null || _showOverlayItem == null || _compactBarItem == null) return;

        var mode = _overlay.VisibilityMode;
        var visible = mode != OverlayVisibilityMode.Hidden;

        _showOverlayItem.IsChecked = visible;

        _compactBarItem.IsEnabled = visible;
        _compactBarItem.IsChecked = mode == OverlayVisibilityMode.Compact;
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
