using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using TaskSplit.Models;
using TaskSplit.Services;
using TaskSplit.Win32;

namespace TaskSplit.Views;

public partial class TaskbarOverlay : Window
{
    private const int ResizeBorder = 8;

    private const int WM_NCHITTEST = 0x0084;
    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
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

    public bool IsManualLayout => _manualLayout;

    public event Action<OverlayDiagnostics>? DiagnosticsUpdated;

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
        LocationChanged += OnLayoutChangedByUser;
        SizeChanged += OnLayoutChangedByUser;
    }

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
        Refresh();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        EnsureTopmost();
        HookResizeAndDrag();
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => SyncToTaskbar();

    private void OnLayoutChangedByUser(object? sender, EventArgs e)
    {
        if (_applyingAutoLayout || !IsLoaded) return;
        _manualLayout = true;
        _positionMode = "manual (drag/resize)";
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

        var screenPoint = new Point(
            (short)(lParam.ToInt32() & 0xFFFF),
            (short)((lParam.ToInt32() >> 16) & 0xFFFF));
        var point = PointFromScreen(screenPoint);

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return IntPtr.Zero;

        var onLeft = point.X <= ResizeBorder;
        var onRight = point.X >= width - ResizeBorder;
        var onTop = point.Y <= ResizeBorder;
        var onBottom = point.Y >= height - ResizeBorder;

        if (onTop && onLeft) { handled = true; return (IntPtr)HTTOPLEFT; }
        if (onTop && onRight) { handled = true; return (IntPtr)HTTOPRIGHT; }
        if (onBottom && onLeft) { handled = true; return (IntPtr)HTBOTTOMLEFT; }
        if (onBottom && onRight) { handled = true; return (IntPtr)HTBOTTOMRIGHT; }
        if (onLeft) { handled = true; return (IntPtr)HTLEFT; }
        if (onRight) { handled = true; return (IntPtr)HTRIGHT; }
        if (onTop) { handled = true; return (IntPtr)HTTOP; }
        if (onBottom) { handled = true; return (IntPtr)HTBOTTOM; }

        if (IsInteractiveElement(point))
        {
            handled = false;
            return (IntPtr)HTCLIENT;
        }

        handled = true;
        return (IntPtr)HTCAPTION;
    }

    private bool IsInteractiveElement(Point point)
    {
        if (InputHitTest(point) is not DependencyObject hit) return false;

        var current = hit;
        while (current != null)
        {
            if (current is Button or ButtonBase or TextBoxBase or ComboBox or ListBox or ListBoxItem)
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
                Left = topLeft.X;
                Top = topLeft.Y;
                Width = Math.Max(MinWidth, (bottomRight.X - topLeft.X) / 2);
                Height = Math.Max(MinHeight, bottomRight.Y - topLeft.Y);
                return;
            }

            var scale = NativeMethods.GetDpiScale(_taskbarService.TrayWindowHandle);
            Left = rect.Left / scale;
            Top = rect.Top / scale;
            Width = Math.Max(MinWidth, rect.Width / scale / 2);
            Height = Math.Max(MinHeight, rect.Height / scale);
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
            const double fallbackHeight = 72;
            Left = work.Left;
            Top = work.Bottom - fallbackHeight;
            Width = Math.Max(MinWidth, work.Width / 2);
            Height = fallbackHeight;
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
        if (_config == null) return;

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
            var btnRect = lastBtn.Rect;
            var scale = NativeMethods.GetDpiScale(_taskbarService.TrayWindowHandle);
            double x = (btnRect.Right / scale) - Left + (group.GapAfter / 2.0);

            if (_config.ShowDividers)
            {
                var line = new Line
                {
                    X1 = x, Y1 = 8,
                    X2 = x, Y2 = Height - 8,
                    Stroke = (SolidColorBrush)new BrushConverter().ConvertFrom(group.Color)!,
                    StrokeThickness = 2,
                    Opacity = 0.8
                };
                OverlayCanvas.Children.Add(line);
            }

            if (_config.ShowGroupLabels)
            {
                var label = new TextBlock
                {
                    Text = group.Name.ToUpper(),
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    Foreground = (SolidColorBrush)new BrushConverter().ConvertFrom(group.Color)!,
                    Opacity = 0.7
                };
                Canvas.SetLeft(label, x + 4);
                Canvas.SetTop(label, 2);
                OverlayCanvas.Children.Add(label);
            }
        }
    }

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
