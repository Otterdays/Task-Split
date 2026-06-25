using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using TaskSplit.Models;
using TaskSplit.Services;
using TaskSplit.Win32;

namespace TaskSplit.Views;

public partial class TaskbarOverlay : Window
{
    private const double DefaultPanelHeight = 220;
    private const double TaskbarStripMaxHeight = 96;

    private const double ResizeBorderHorizontal = 10;
    private const double ResizeBorderBottom = 12;

    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    private readonly TaskbarService _taskbarService;
    private readonly ConfigService _configService;
    private readonly AppDiscoveryService _appDiscovery;
    private AppConfig? _config;
    private string _positionMode = "uninitialized";
    private bool _manualLayout;
    private bool _applyingAutoLayout;
    private HwndSource? _hwndSource;
    private DispatcherTimer? _refreshDebounce;

    public bool IsManualLayout => _manualLayout;

    public event Action<OverlayDiagnostics>? DiagnosticsUpdated;
    public event Action<AppConfig>? ConfigChanged;

    public TaskbarOverlay(
        TaskbarService taskbarService,
        ConfigService configService,
        AppDiscoveryService appDiscovery,
        AppConfig config)
    {
        InitializeComponent();
        _taskbarService = taskbarService;
        _configService = configService;
        _appDiscovery = appDiscovery;
        _config = config;
        Opacity = config.OverlayOpacity;

        Loaded += OnLoaded;
        SourceInitialized += OnSourceInitialized;
        LocationChanged += OnLocationChangedByUser;
        SizeChanged += OnSizeChangedByUser;
        MouseMove += OnMouseMove;
        MouseLeave += (_, _) => SetResizeGripVisibility(ResizeZone.None);
    }

    private enum ResizeZone { None, Left, Right, Bottom, BottomLeft, BottomRight, TitleBar }

    public void UpdateConfig(AppConfig config)
    {
        _config = config;
        Opacity = config.OverlayOpacity;
        Refresh();
    }

    public void SnapToTaskbar()
    {
        _manualLayout = false;
        SyncToTaskbar();
    }

    private void AddAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;

        var dialog = new AddAppDialog(_config, _appDiscovery) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.UpdatedConfig == null)
            return;

        _config = dialog.UpdatedConfig;
        _configService.Save(_config);
        ConfigChanged?.Invoke(_config);
        Refresh();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        EnsureTopmost();
        HookResizeAndDrag();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateGripDashSizes();
        SyncToTaskbar();
    }

    private void OnLocationChangedByUser(object? sender, EventArgs e)
    {
        if (_applyingAutoLayout || !IsLoaded) return;
        _manualLayout = true;
        _positionMode = "manual (drag/resize)";
    }

    private void OnSizeChangedByUser(object? sender, SizeChangedEventArgs e)
    {
        if (_applyingAutoLayout || !IsLoaded) return;
        _manualLayout = true;
        _positionMode = "manual (drag/resize)";
        UpdateGripDashSizes();
        ScheduleRefresh();
    }

    private void ScheduleRefresh()
    {
        _refreshDebounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _refreshDebounce.Stop();
        _refreshDebounce.Tick -= OnRefreshDebounce;
        _refreshDebounce.Tick += OnRefreshDebounce;
        _refreshDebounce.Start();
    }

    private void OnRefreshDebounce(object? sender, EventArgs e)
    {
        _refreshDebounce?.Stop();
        Refresh();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_applyingAutoLayout) return;
        var point = e.GetPosition(this);
        var zone = GetResizeZone(point);
        SetResizeGripVisibility(zone);
        Cursor = ResolveCursor(point, zone);
    }

    public void SyncToTaskbar()
    {
        if (!_manualLayout)
        {
            var rect = _taskbarService.GetTaskbarRect();
            if (rect.HasValue)
            {
                ApplyPhysicalRect(rect.Value);
                _positionMode = "taskbar";
            }
            else
            {
                ApplyFallbackPosition();
                _positionMode = "fallback (taskbar not found or zero-size)";
            }
        }

        EnsureTopmost();
        Refresh();
        PublishDiagnostics();
    }

    public OverlayDiagnostics GetDiagnostics() => BuildDiagnostics();

    private void HookResizeAndDrag()
    {
        _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        _hwndSource?.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WM_NCHITTEST) return IntPtr.Zero;

        var point = PointFromScreen(GetScreenPointFromLParam(lParam));

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return IntPtr.Zero;

        var borderH = ScaledBorder(ResizeBorderHorizontal);
        var borderBottom = ScaledBorder(ResizeBorderBottom);
        var zone = GetResizeZone(point, width, height, borderH, borderBottom);

        switch (zone)
        {
            case ResizeZone.BottomLeft:
                handled = true;
                return (IntPtr)HTBOTTOMLEFT;
            case ResizeZone.BottomRight:
                handled = true;
                return (IntPtr)HTBOTTOMRIGHT;
            case ResizeZone.Left:
                handled = true;
                return (IntPtr)HTLEFT;
            case ResizeZone.Right:
                handled = true;
                return (IntPtr)HTRIGHT;
            case ResizeZone.Bottom:
                handled = true;
                return (IntPtr)HTBOTTOM;
            case ResizeZone.TitleBar:
                handled = true;
                return (IntPtr)HTCAPTION;
            default:
                handled = false;
                return (IntPtr)HTCLIENT;
        }
    }

    private ResizeZone GetResizeZone(Point point) =>
        GetResizeZone(point, ActualWidth, ActualHeight,
            ScaledBorder(ResizeBorderHorizontal),
            ScaledBorder(ResizeBorderBottom));

    private ResizeZone GetResizeZone(
        Point point, double width, double height, double borderH, double borderBottom)
    {
        if (width <= 0 || height <= 0) return ResizeZone.None;

        var onLeft = point.X <= borderH;
        var onRight = point.X >= width - borderH;
        var onBottom = point.Y >= height - borderBottom;

        if (onBottom && onLeft) return ResizeZone.BottomLeft;
        if (onBottom && onRight) return ResizeZone.BottomRight;
        if (onLeft) return ResizeZone.Left;
        if (onRight) return ResizeZone.Right;
        if (onBottom) return ResizeZone.Bottom;

        if (point.Y <= GetTitleBarHeight() && !IsInteractiveElement(point))
            return ResizeZone.TitleBar;

        return ResizeZone.None;
    }

    private double GetTitleBarHeight() =>
        TitleBarBorder.ActualHeight > 0 ? TitleBarBorder.ActualHeight : 28;

    private void SetResizeGripVisibility(ResizeZone zone)
    {
        const double visible = 1;
        const double hidden = 0;

        LeftGrip.Opacity = zone is ResizeZone.Left or ResizeZone.BottomLeft ? visible : hidden;
        RightGrip.Opacity = zone is ResizeZone.Right or ResizeZone.BottomRight ? visible : hidden;
        BottomGrip.Opacity = zone is ResizeZone.Bottom or ResizeZone.BottomLeft or ResizeZone.BottomRight
            ? visible
            : hidden;
    }

    private static Point GetScreenPointFromLParam(IntPtr lParam)
    {
        var lp = lParam.ToInt64();
        var x = (int)(short)(lp & 0xFFFF);
        var y = (int)(short)((lp >> 16) & 0xFFFF);
        return new Point(x, y);
    }

    private double ScaledBorder(double dip) => Math.Max(dip, dip * GetWindowDpiScale());

    private double GetWindowDpiScale()
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget != null)
            return source.CompositionTarget.TransformToDevice.M11;
        return NativeMethods.GetDpiScale(_taskbarService.TrayWindowHandle);
    }

    private Cursor ResolveCursor(Point point, ResizeZone zone) => zone switch
    {
        ResizeZone.BottomLeft => Cursors.SizeNESW,
        ResizeZone.BottomRight => Cursors.SizeNWSE,
        ResizeZone.Left or ResizeZone.Right => Cursors.SizeWE,
        ResizeZone.Bottom => Cursors.SizeNS,
        ResizeZone.TitleBar => Cursors.SizeAll,
        _ when IsInteractiveElement(point) => Cursors.Hand,
        _ => Cursors.Arrow,
    };

    private void UpdateGripDashSizes()
    {
        if (!IsLoaded) return;
        var dashV = Math.Clamp(ActualHeight * 0.35, 36, 120);
        var dashH = Math.Clamp(ActualWidth * 0.35, 36, 120);
        LeftGripDash.Y2 = dashV;
        RightGripDash.Y2 = dashV;
        BottomGripDash.X2 = dashH;
    }

    private bool IsInteractiveElement(Point point)
    {
        if (InputHitTest(point) is not DependencyObject hit) return false;

        var current = hit;
        while (current != null)
        {
            if (current is Button or ButtonBase or TextBoxBase or ComboBox or ListBox or ListBoxItem
                or ScrollViewer or ScrollBar or Thumb or RepeatButton)
                return true;
            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private void ApplyPhysicalRect(NativeMethods.RECT rect)
    {
        _applyingAutoLayout = true;
        try
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                var fromDevice = source.CompositionTarget.TransformFromDevice;
                var topLeft = fromDevice.Transform(new Point(rect.Left, rect.Top));
                var bottomRight = fromDevice.Transform(new Point(rect.Right, rect.Bottom));
                var taskbarHeight = bottomRight.Y - topLeft.Y;
                var width = Math.Max(MinWidth, (bottomRight.X - topLeft.X) / 2);
                var height = Math.Max(DefaultPanelHeight, taskbarHeight);
                Left = topLeft.X;
                Top = topLeft.Y + taskbarHeight - height;
                Width = width;
                Height = Math.Max(MinHeight, height);
                return;
            }

            var scale = NativeMethods.GetDpiScale(_taskbarService.TrayWindowHandle);
            var tbHeight = rect.Height / scale;
            var tbWidth = rect.Width / scale;
            var panelHeight = Math.Max(DefaultPanelHeight, tbHeight);
            Left = rect.Left / scale;
            Top = rect.Top / scale + tbHeight - panelHeight;
            Width = Math.Max(MinWidth, tbWidth / 2);
            Height = Math.Max(MinHeight, panelHeight);
        }
        finally
        {
            _applyingAutoLayout = false;
        }
    }

    private void ApplyFallbackPosition()
    {
        _applyingAutoLayout = true;
        try
        {
            var work = SystemParameters.WorkArea;
            const double fallbackTaskbarHeight = 72;
            var height = Math.Max(DefaultPanelHeight, fallbackTaskbarHeight);
            Left = work.Left;
            Top = work.Bottom - height;
            Width = Math.Max(MinWidth, work.Width / 2);
            Height = height;
        }
        finally
        {
            _applyingAutoLayout = false;
        }
    }

    private void EnsureTopmost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            try { hwnd = new WindowInteropHelper(this).EnsureHandle(); }
            catch { return; }
        }

        NativeMethods.EnsureTopmost(hwnd);

        const int GWL_EXSTYLE = -20;
        const int WS_EX_TOOLWINDOW = 0x00000080;
        var exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static int GetWindowLong(IntPtr hwnd, int index) =>
        IntPtr.Size == 8
            ? (int)GetWindowLongPtr64(hwnd, index)
            : GetWindowLong32(hwnd, index);

    private static void SetWindowLong(IntPtr hwnd, int index, int value)
    {
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(hwnd, index, new IntPtr(value));
        else
            SetWindowLong32(hwnd, index, value);
    }

    public void Refresh()
    {
        OverlayCanvas.Children.Clear();
        RenderGroupsPanel();
        RenderTaskbarDividers();
    }

    private void RenderGroupsPanel()
    {
        GroupsPanel.Children.Clear();
        if (_config == null) return;

        if (_config.Groups.Count == 0)
        {
            GroupsPanel.Children.Add(MakeHintText("No groups yet — use Add App to assign an application."));
            return;
        }

        var onTaskbar = new HashSet<string>(
            _taskbarService.GetTaskbarButtons().Select(b => b.ProcessName),
            StringComparer.OrdinalIgnoreCase);

        foreach (var group in _config.Groups)
        {
            var accent = ParseBrush(group.Color);
            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                BorderBrush = accent,
                BorderThickness = new Thickness(3, 0, 0, 0),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 8),
                Margin = new Thickness(0, 0, 0, 8),
            };

            var stack = new StackPanel();

            var header = new TextBlock
            {
                Text = group.Name,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = accent,
                Margin = new Thickness(0, 0, 0, 4),
            };
            stack.Children.Add(header);

            if (group.ProcessNames.Count == 0)
            {
                stack.Children.Add(MakeHintText("No apps — click Add App"));
            }
            else
            {
                var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
                foreach (var processName in group.ProcessNames)
                {
                    var running = onTaskbar.Contains(processName);
                    wrap.Children.Add(BuildAppChip(processName, running, accent));
                }
                stack.Children.Add(wrap);
            }

            card.Child = stack;
            GroupsPanel.Children.Add(card);
        }
    }

    private static Border BuildAppChip(string processName, bool onTaskbar, Brush accent)
    {
        var label = HumanizeProcessName(processName);
        var chip = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0x33, 0x5B, 0x8C, 0xFF)),
            BorderBrush = onTaskbar ? accent : new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 6, 4),
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (onTaskbar)
        {
            row.Children.Add(new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = new SolidColorBrush(Color.FromRgb(0x56, 0xD3, 0x64)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
            });
        }

        row.Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
        });

        chip.Child = row;
        return chip;
    }

    private static TextBlock MakeHintText(string text) => new()
    {
        Text = text,
        FontSize = 10,
        Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 2, 0, 2),
    };

    private void RenderTaskbarDividers()
    {
        // Panel mode — groups list is the UI; skip taskbar divider overlay.
        if (_config == null || _manualLayout || ActualHeight > TaskbarStripMaxHeight) return;

        var buttons = _taskbarService.GetTaskbarButtons();
        if (buttons.Count == 0) return;

        foreach (var group in _config.Groups)
        {
            var groupButtons = buttons
                .Where(b => group.ProcessNames.Any(p =>
                    p.Equals(b.ProcessName, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(b => b.Order)
                .ToList();

            if (groupButtons.Count == 0) continue;

            var lastBtn = groupButtons.Last();
            var x = TaskbarXToOverlay(lastBtn.Rect.Right, lastBtn.Rect.Top) + (group.GapAfter / 2.0);
            if (x < 0 || x > ActualWidth) continue;

            var accent = ParseBrush(group.Color);

            if (_config.ShowDividers)
            {
                OverlayCanvas.Children.Add(new Line
                {
                    X1 = x,
                    Y1 = 8,
                    X2 = x,
                    Y2 = Math.Max(Height - 8, 16),
                    Stroke = accent,
                    StrokeThickness = 2,
                    Opacity = 0.8,
                });
            }

            if (_config.ShowGroupLabels)
            {
                var label = new TextBlock
                {
                    Text = group.Name.ToUpper(),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = accent,
                    Opacity = 0.7,
                };
                Canvas.SetLeft(label, x + 4);
                Canvas.SetTop(label, 2);
                OverlayCanvas.Children.Add(label);
            }
        }
    }

    private double TaskbarXToOverlay(int screenXPhysical, int screenYPhysical)
    {
        try
        {
            return PointFromScreen(new Point(screenXPhysical, screenYPhysical)).X;
        }
        catch
        {
            var scale = NativeMethods.GetDpiScale(_taskbarService.TrayWindowHandle);
            return screenXPhysical / scale - Left;
        }
    }

    private static Brush ParseBrush(string hex) =>
        (SolidColorBrush)new BrushConverter().ConvertFrom(hex)!;

    private static string HumanizeProcessName(string processName) =>
        string.Join(' ',
            processName.Replace('_', ' ').Replace('-', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Length <= 1
                    ? w.ToUpperInvariant()
                    : char.ToUpperInvariant(w[0]) + w[1..]));

    private void PublishDiagnostics()
    {
        var diag = BuildDiagnostics();
        DiagnosticsUpdated?.Invoke(diag);

        try
        {
            var logDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskSplit");
            Directory.CreateDirectory(logDir);
            File.WriteAllText(System.IO.Path.Combine(logDir, "debug.log"), diag.ToReport());
        }
        catch
        {
            // Non-fatal
        }
    }

    private OverlayDiagnostics BuildDiagnostics() =>
        _taskbarService.GetDiagnostics(Left, Top, Width, Height, IsVisible, _positionMode);
}
