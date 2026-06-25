using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TaskSplit.Models;
using TaskSplit.Services;
using TaskSplit.Win32;

namespace TaskSplit.Views;

public partial class TaskbarOverlay : Window
{
    private const double DefaultPanelHeight = 220;
    private const double CompactBarHeight = 38;
    private const double TaskbarStripMaxHeight = 96;
    private const int VisibilityTransitionMs = 200;
    private const int HideFadeMs = 140;

    private const double ResizeBorderHorizontal = 10;
    private const double ResizeBorderBottom = 12;

    private const int WM_NCHITTEST = 0x0084;
    private const int WM_NCMOUSELEAVE = 0x02A2;
    private const int HTCLIENT = 1;
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
    private bool _suppressManualLayoutCapture;
    private bool _applyingAutoLayout;
    private HwndSource? _hwndSource;
    private DispatcherTimer? _refreshDebounce;
    private ResizeZone _activeGripZone = ResizeZone.None;
    private bool _trackingMouseLeave;
    private bool _trackingNcMouseLeave;
    private bool _sizingWindow;
    private bool _draggingWindow;
    private bool _titleDragArmed;
    private Point? _titleDragOrigin;
    private bool _lastInTitleBarBand;
    private bool _lastOverChromeButton;
    private ResizeZone _lastCursorZone = (ResizeZone)(-1);
    private bool _lastChromeButtonCursor;

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
        MouseMove += OnWindowMouseMove;
    }

    private void OnWindowMouseMove(object sender, MouseEventArgs e)
    {
        if (_sizingWindow || _draggingWindow || _applyingAutoLayout) return;
        SyncResizeCursor(GetResizeZone(e.GetPosition(this)), e.GetPosition(this));
    }

    private void OnTitleBarDragAreaMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        if (e.ClickCount == 2 && _visibilityMode == OverlayVisibilityMode.Compact)
        {
            SetVisibilityMode(OverlayVisibilityMode.Expanded);
            CancelTitleDragArm();
            e.Handled = true;
            return;
        }

        if (e.ClickCount != 1) return;

        if (_visibilityMode == OverlayVisibilityMode.Compact)
        {
            _titleDragOrigin = e.GetPosition(this);
            _titleDragArmed = true;
            TitleBarDragArea.CaptureMouse();
            e.Handled = true;
            return;
        }

        StartTitleDrag();
        e.Handled = true;
    }

    private void OnTitleBarDragAreaMouseMove(object sender, MouseEventArgs e)
    {
        if (!_titleDragArmed || e.LeftButton != MouseButtonState.Pressed || !_titleDragOrigin.HasValue)
            return;

        var point = e.GetPosition(this);
        var delta = point - _titleDragOrigin.Value;
        if (Math.Abs(delta.X) < 3 && Math.Abs(delta.Y) < 3)
            return;

        StartTitleDrag();
    }

    private void OnTitleBarDragAreaMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || !_titleDragArmed) return;
        CancelTitleDragArm();
    }

    private void StartTitleDrag()
    {
        CancelTitleDragArm();
        _draggingWindow = true;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // primary button already released before DragMove
        }
        finally
        {
            _draggingWindow = false;
        }
    }

    private void CancelTitleDragArm()
    {
        _titleDragArmed = false;
        _titleDragOrigin = null;
        if (TitleBarDragArea.IsMouseCaptured)
            TitleBarDragArea.ReleaseMouseCapture();
    }

    private void CollapseButton_Click(object sender, RoutedEventArgs e)
    {
        SetVisibilityMode(_visibilityMode == OverlayVisibilityMode.Compact
            ? OverlayVisibilityMode.Expanded
            : OverlayVisibilityMode.Compact);
    }

    private void HideButton_Click(object sender, RoutedEventArgs e) =>
        SetVisibilityMode(OverlayVisibilityMode.Hidden);

    private enum ResizeZone { None, Left, Right, Bottom, BottomLeft, BottomRight, TitleBar }

    private enum AppPresence { Running, Installed, NotFound }

    private int _refreshCount;
    private OverlayVisibilityMode _visibilityMode = OverlayVisibilityMode.Expanded;
    private OverlayVisibilityMode _lastVisibleMode = OverlayVisibilityMode.Expanded;
    private double? _savedExpandedHeight;
    private bool _visibilityAnimating;
    private Storyboard? _visibilityStoryboard;

    public OverlayVisibilityMode VisibilityMode => _visibilityMode;
    public bool IsVisibilityAnimating => _visibilityAnimating;

    public event Action? VisibilityModeChanged;

    public void SetVisibilityMode(OverlayVisibilityMode mode, bool animate = true)
    {
        if (mode == _visibilityMode && (mode != OverlayVisibilityMode.Hidden || IsVisible))
            return;

        if (mode == OverlayVisibilityMode.Compact
            && _visibilityMode == OverlayVisibilityMode.Expanded
            && Height > CompactBarHeight + 4)
        {
            _savedExpandedHeight = Height;
        }

        if (mode != OverlayVisibilityMode.Hidden)
            _lastVisibleMode = mode;

        var previousMode = _visibilityMode;
        _visibilityMode = mode;

        if (!animate || !IsLoaded)
        {
            ApplyVisibilityModeInstant(mode, previousMode);
            return;
        }

        StopVisibilityAnimation();

        switch (mode)
        {
            case OverlayVisibilityMode.Hidden:
                AnimateHide();
                return;
            case OverlayVisibilityMode.Compact when previousMode == OverlayVisibilityMode.Hidden:
            case OverlayVisibilityMode.Expanded when previousMode == OverlayVisibilityMode.Hidden:
                AnimateShow(mode);
                return;
            case OverlayVisibilityMode.Compact when previousMode == OverlayVisibilityMode.Expanded && IsVisible:
                AnimateCollapse();
                return;
            case OverlayVisibilityMode.Expanded when previousMode == OverlayVisibilityMode.Compact && IsVisible:
                AnimateExpand();
                return;
            default:
                ApplyVisibilityModeInstant(mode, previousMode);
                break;
        }
    }

    private void ApplyVisibilityModeInstant(OverlayVisibilityMode mode, OverlayVisibilityMode previousMode)
    {
        StopVisibilityAnimation();

        switch (mode)
        {
            case OverlayVisibilityMode.Hidden:
                Opacity = _config?.OverlayOpacity ?? 1.0;
                Hide();
                VisibilityModeChanged?.Invoke();
                return;
            case OverlayVisibilityMode.Compact:
                ApplyCompactLayout();
                break;
            case OverlayVisibilityMode.Expanded:
                ApplyExpandedLayout();
                break;
        }

        if (!IsVisible)
            Show();

        Opacity = _config?.OverlayOpacity ?? 1.0;
        MainContent.Opacity = 1.0;
        EnsureTopmost();
        UpdateGripDashSizes();
        Refresh();
        VisibilityModeChanged?.Invoke();
    }

    private void AnimateHide()
    {
        BeginVisibilityTransition();
        var fromOpacity = _config?.OverlayOpacity ?? Opacity;
        var fade = CreateEaseAnimation(fromOpacity, 0, HideFadeMs);
        fade.Completed += (_, _) =>
        {
            StopVisibilityAnimation();
            Opacity = _config?.OverlayOpacity ?? 1.0;
            Hide();
            EndVisibilityTransition();
            VisibilityModeChanged?.Invoke();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void AnimateCollapse()
    {
        BeginVisibilityTransition();
        ApplyCompactChrome();

        var startHeight = Height;
        var targetHeight = CompactBarHeight;
        var startTop = Top;
        var targetTop = startTop + (startHeight - targetHeight);

        MainContent.Visibility = Visibility.Visible;
        var contentFade = CreateEaseAnimation(1, 0, (int)(VisibilityTransitionMs * 0.55));
        Storyboard.SetTarget(contentFade, MainContent);
        Storyboard.SetTargetProperty(contentFade, new PropertyPath(UIElement.OpacityProperty));

        var heightAnim = CreateEaseAnimation(startHeight, targetHeight, VisibilityTransitionMs);
        Storyboard.SetTarget(heightAnim, this);
        Storyboard.SetTargetProperty(heightAnim, new PropertyPath(HeightProperty));

        var topAnim = CreateEaseAnimation(startTop, targetTop, VisibilityTransitionMs);
        Storyboard.SetTarget(topAnim, this);
        Storyboard.SetTargetProperty(topAnim, new PropertyPath(TopProperty));

        RunVisibilityStoryboard(() => CompleteVisibilityAnimation(), contentFade, heightAnim, topAnim);
    }

    private void AnimateExpand()
    {
        BeginVisibilityTransition();
        ApplyExpandedChrome();
        MaxHeight = double.PositiveInfinity;
        MinHeight = 180;

        var targetHeight = Math.Max(MinHeight, _savedExpandedHeight ?? DefaultPanelHeight);
        var startHeight = Height;
        var startTop = Top;
        var targetTop = startTop + (startHeight - targetHeight);

        MainContent.Visibility = Visibility.Visible;
        MainContent.Opacity = 0;
        ResizeGripLayer.Visibility = Visibility.Visible;

        var contentFade = CreateEaseAnimation(0, 1, (int)(VisibilityTransitionMs * 0.75));
        Storyboard.SetTarget(contentFade, MainContent);
        Storyboard.SetTargetProperty(contentFade, new PropertyPath(UIElement.OpacityProperty));

        var heightAnim = CreateEaseAnimation(startHeight, targetHeight, VisibilityTransitionMs);
        Storyboard.SetTarget(heightAnim, this);
        Storyboard.SetTargetProperty(heightAnim, new PropertyPath(HeightProperty));

        var topAnim = CreateEaseAnimation(startTop, targetTop, VisibilityTransitionMs);
        Storyboard.SetTarget(topAnim, this);
        Storyboard.SetTargetProperty(topAnim, new PropertyPath(TopProperty));

        RunVisibilityStoryboard(() => CompleteVisibilityAnimation(), contentFade, heightAnim, topAnim);
    }

    private void AnimateShow(OverlayVisibilityMode mode)
    {
        BeginVisibilityTransition();

        if (mode == OverlayVisibilityMode.Compact)
            ApplyCompactLayout();
        else
            ApplyExpandedLayout();

        Opacity = 0;
        Show();
        EnsureTopmost();

        var fade = CreateEaseAnimation(0, _config?.OverlayOpacity ?? 1.0, HideFadeMs + 40);
        fade.Completed += (_, _) =>
        {
            StopVisibilityAnimation();
            Opacity = _config?.OverlayOpacity ?? 1.0;
            CompleteVisibilityAnimation();
        };
        BeginAnimation(OpacityProperty, fade);
    }

    private void CompleteVisibilityAnimation()
    {
        var finalTop = Top;
        var finalHeight = Height;
        StopVisibilityAnimation();
        Top = finalTop;
        Height = finalHeight;

        if (_visibilityMode == OverlayVisibilityMode.Compact)
        {
            MainContent.Opacity = 0;
            MainContent.Visibility = Visibility.Collapsed;
            ResizeGripLayer.Visibility = Visibility.Collapsed;
            MinHeight = CompactBarHeight;
            MaxHeight = CompactBarHeight;
            Height = CompactBarHeight;
            Top = finalTop + (finalHeight - CompactBarHeight);
        }
        else
        {
            MainContent.Opacity = 1;
            MainContent.Visibility = Visibility.Visible;
        }

        UpdateGripDashSizes();
        Refresh();
        EndVisibilityTransition();
        VisibilityModeChanged?.Invoke();
        if (!_manualLayout)
            SyncToTaskbar();
    }

    private void BeginVisibilityTransition()
    {
        _visibilityAnimating = true;
        _suppressManualLayoutCapture = true;
        _applyingAutoLayout = true;
        CancelTitleDragArm();
        ClearResizeHoverFx(immediate: true);
    }

    private void EndVisibilityTransition()
    {
        _visibilityAnimating = false;
        _applyingAutoLayout = false;
        Dispatcher.BeginInvoke(
            () => _suppressManualLayoutCapture = false,
            DispatcherPriority.ApplicationIdle);
    }

    private void StopVisibilityAnimation()
    {
        _visibilityStoryboard?.Stop();
        _visibilityStoryboard = null;
        BeginAnimation(HeightProperty, null);
        BeginAnimation(TopProperty, null);
        BeginAnimation(OpacityProperty, null);
        MainContent.BeginAnimation(UIElement.OpacityProperty, null);
    }

    private void RunVisibilityStoryboard(Action onComplete, params DoubleAnimation[] animations)
    {
        var sb = new Storyboard { Duration = TimeSpan.FromMilliseconds(VisibilityTransitionMs) };
        foreach (var anim in animations)
            sb.Children.Add(anim);

        sb.Completed += (_, _) => onComplete();
        _visibilityStoryboard = sb;
        sb.Begin();
    }

    private static DoubleAnimation CreateEaseAnimation(double from, double to, int ms) =>
        new(from, to, TimeSpan.FromMilliseconds(ms))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.HoldEnd,
        };

    public void RestoreFromHidden() =>
        SetVisibilityMode(_lastVisibleMode);

    public void ToggleHidden()
    {
        if (_visibilityMode == OverlayVisibilityMode.Hidden)
            RestoreFromHidden();
        else
            SetVisibilityMode(OverlayVisibilityMode.Hidden);
    }

    public void ToggleCompact()
    {
        if (_visibilityMode == OverlayVisibilityMode.Hidden)
            SetVisibilityMode(OverlayVisibilityMode.Compact);
        else
            SetVisibilityMode(_visibilityMode == OverlayVisibilityMode.Compact
                ? OverlayVisibilityMode.Expanded
                : OverlayVisibilityMode.Compact);
    }

    private void ApplyCompactLayout()
    {
        ClearResizeHoverFx(immediate: true);
        ApplyCompactChrome();
        MainContent.Visibility = Visibility.Collapsed;
        MainContent.Opacity = 0;
        ResizeGripLayer.Visibility = Visibility.Collapsed;

        // Measure actual required height for compact content to avoid clipping
        TitleBarBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var requiredHeight = TitleBarBorder.DesiredSize.Height;
        var compactHeight = Math.Max(CompactBarHeight, Math.Min(50, requiredHeight));

        MinHeight = compactHeight;
        MaxHeight = compactHeight;
        Height = compactHeight;
    }

    private void ApplyCompactChrome()
    {
        HintText.Visibility = Visibility.Collapsed;
        CompactHint.Visibility = Visibility.Visible;
        BrandDot.Visibility = Visibility.Visible;

        Background = CompactBackground;
        TitleBarBorder.Background = CompactBackground;
        TitleBarBorder.BorderBrush = CompactAccentBrush;
        TitleBarBorder.BorderThickness = new Thickness(3, 0, 0, 0);
        TitleBarBorder.Padding = new Thickness(10, 0, 6, 0);

        ChromeBorder.BorderBrush = CompactChromeBrush;
        ChromeBorder.BorderThickness = new Thickness(1);
        ChromeBorder.CornerRadius = new CornerRadius(8);
        ChromeBorder.Margin = new Thickness(0);

        Grid.SetRowSpan(TitleBarBorder, 2);
        TitleBarGrid.VerticalAlignment = VerticalAlignment.Center;

        CollapseButtonGlyph.Text = "⤢";
        CollapseButtonGlyph.FontSize = 11;
        ToolTipService.SetToolTip(CollapseButton, MakeTooltip("Expand panel"));
        ToolTipService.SetToolTip(TitleBarBorder, MakeTooltip("Drag to move · Double-click to expand"));
    }

    private void ApplyExpandedLayout()
    {
        ApplyExpandedChrome();
        MainContent.Visibility = Visibility.Visible;
        MainContent.Opacity = 1;
        ResizeGripLayer.Visibility = Visibility.Visible;
        MaxHeight = double.PositiveInfinity;
        MinHeight = 180;
        Height = Math.Max(MinHeight, _savedExpandedHeight ?? DefaultPanelHeight);
    }

    private void ApplyExpandedChrome()
    {
        HintText.Visibility = Visibility.Visible;
        CompactHint.Visibility = Visibility.Collapsed;
        BrandDot.Visibility = Visibility.Collapsed;

        Background = ExpandedBackground;
        TitleBarBorder.Background = ExpandedTitleBackground;
        TitleBarBorder.BorderBrush = TitleDividerBrush;
        TitleBarBorder.BorderThickness = new Thickness(0, 0, 0, 1);
        TitleBarBorder.Padding = new Thickness(8, 5, 8, 5);

        ChromeBorder.BorderBrush = Brushes.White;
        ChromeBorder.BorderThickness = new Thickness(2);
        ChromeBorder.CornerRadius = new CornerRadius(0);
        ChromeBorder.Margin = new Thickness(0);

        Grid.SetRowSpan(TitleBarBorder, 1);
        TitleBarGrid.VerticalAlignment = VerticalAlignment.Center;

        HintText.Text = "drag title · resize sides";
        CollapseButtonGlyph.Text = "─";
        CollapseButtonGlyph.FontSize = 10;
        ToolTipService.SetToolTip(CollapseButton, MakeTooltip("Collapse to title bar (stay on screen)"));
        ToolTipService.SetToolTip(TitleBarBorder, MakeTooltip("Drag to move · Pull sides or bottom edge to resize"));
    }

    private static readonly Brush TitleBarHoverExpanded =
        new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x26));
    private static readonly Brush TitleBarHoverCompact =
        new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2C));
    private static readonly Brush CompactBackground = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1F));
    private static readonly Brush ExpandedBackground = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x26));
    private static readonly Brush ExpandedTitleBackground = new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1C));
    private static readonly Brush TitleDividerBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF));
    private static readonly Brush CompactAccentBrush = new SolidColorBrush(Color.FromRgb(0x5B, 0x8C, 0xFF));
    private static readonly Brush CompactChromeBrush = new SolidColorBrush(Color.FromArgb(0x66, 0x5B, 0x8C, 0xFF));

    public void UpdateConfig(AppConfig config)
    {
        _config = config;
        Opacity = config.OverlayOpacity;
        Refresh();
    }

    public void SnapToTaskbar()
    {
        // NOTE: WPF can raise LocationChanged/SizeChanged after ApplyPhysicalRect returns,
        // which used to immediately re-lock manual layout and break snap on the next timer tick.
        _suppressManualLayoutCapture = true;
        _manualLayout = false;
        EnsureVisibleExpandedForSnap(animate: false);
        SyncToTaskbar();
        Dispatcher.BeginInvoke(
            () => _suppressManualLayoutCapture = false,
            DispatcherPriority.ApplicationIdle);
    }

    /// <summary>Shows overlay expanded above taskbar — used by tray double-click and snap.</summary>
    public void RestoreFromTray()
    {
        _suppressManualLayoutCapture = true;
        _manualLayout = false;
        var wasHidden = _visibilityMode == OverlayVisibilityMode.Hidden;
        EnsureVisibleExpandedForSnap(animate: !wasHidden);
        SyncToTaskbar();
        Dispatcher.BeginInvoke(
            () => _suppressManualLayoutCapture = false,
            DispatcherPriority.ApplicationIdle);

        // Don't activate/focus — just show the window.
        // Let the user click it if they want to interact.
        // This prevents stealing focus from their current work.

        VisibilityModeChanged?.Invoke();
    }

    private void EnsureVisibleExpandedForSnap(bool animate = false)
    {
        if (_visibilityMode == OverlayVisibilityMode.Hidden)
            SetVisibilityMode(OverlayVisibilityMode.Expanded, animate);
        else if (_visibilityMode == OverlayVisibilityMode.Compact)
            SetVisibilityMode(OverlayVisibilityMode.Expanded, animate);
        else if (!IsVisible)
            Show();
    }

    private void AddAppButton_Click(object sender, RoutedEventArgs e)
    {
        if (_config == null) return;

        var dialog = new AddAppDialog(_config, _appDiscovery) { Owner = this };
        dialog.ShowDialog();

        if (dialog.UpdatedConfig == null)
            return;

        _config = dialog.UpdatedConfig;
        if (!_configService.Save(_config))
        {
            MessageBox.Show(
                "Failed to save settings. Please check that the config directory is accessible.",
                "Save Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
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
        _ = WarmDiscoveryAndRefreshAsync();
    }

    private async Task WarmDiscoveryAndRefreshAsync()
    {
        try
        {
            await _appDiscovery.WarmIndexAsync().ConfigureAwait(false);
            await Dispatcher.InvokeAsync(Refresh);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TaskSplit] App discovery warm failed: {ex.Message}");
        }
    }

    private async Task RefreshDiscoveryIndexAsync()
    {
        try
        {
            await _appDiscovery.RefreshIndexAsync().ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() =>
            {
                OverlayCanvas.Children.Clear();
                RenderGroupsPanel();
                RenderTaskbarDividers();
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TaskSplit] App discovery refresh failed: {ex.Message}");
        }
    }

    private void OnLocationChangedByUser(object? sender, EventArgs e)
    {
        if (_applyingAutoLayout || _suppressManualLayoutCapture || !IsLoaded) return;
        _manualLayout = true;
        _positionMode = "manual (drag/resize)";
    }

    private void OnSizeChangedByUser(object? sender, SizeChangedEventArgs e)
    {
        if (_applyingAutoLayout || _suppressManualLayoutCapture || !IsLoaded) return;
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

    public void SyncToTaskbar()
    {
        if (_visibilityAnimating) return;

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
        if (msg == NativeMethods.WM_ENTERSIZEMOVE)
        {
            _sizingWindow = true;
            ClearResizeHoverFx(immediate: true);
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_EXITSIZEMOVE)
        {
            _sizingWindow = false;
            UpdateHoverState(ResizeZone.None);
            return IntPtr.Zero;
        }

        if (msg == WM_NCMOUSELEAVE)
        {
            _trackingNcMouseLeave = false;
            UpdateHoverState(ResizeZone.None);
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_MOUSELEAVE)
        {
            _trackingMouseLeave = false;
            UpdateHoverState(ResizeZone.None);
            return IntPtr.Zero;
        }

        if (msg != WM_NCHITTEST) return IntPtr.Zero;

        var point = PointFromScreen(GetScreenPointFromLParam(lParam));

        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return IntPtr.Zero;

        var borderH = ScaledBorder(ResizeBorderHorizontal);
        var borderBottom = ScaledBorder(ResizeBorderBottom);
        var zone = GetResizeZone(point, width, height, borderH, borderBottom);

        // NC resize zones never raise WPF MouseMove — drive grip FX from hit-test.
        if (!_sizingWindow && !_draggingWindow)
            UpdateHoverState(zone, point);

        switch (zone)
        {
            case ResizeZone.BottomLeft:
                TrackNcMouseLeave(hwnd);
                handled = true;
                return (IntPtr)HTBOTTOMLEFT;
            case ResizeZone.BottomRight:
                TrackNcMouseLeave(hwnd);
                handled = true;
                return (IntPtr)HTBOTTOMRIGHT;
            case ResizeZone.Left:
                TrackNcMouseLeave(hwnd);
                handled = true;
                return (IntPtr)HTLEFT;
            case ResizeZone.Right:
                TrackNcMouseLeave(hwnd);
                handled = true;
                return (IntPtr)HTRIGHT;
            case ResizeZone.Bottom:
                TrackNcMouseLeave(hwnd);
                handled = true;
                return (IntPtr)HTBOTTOM;
            case ResizeZone.TitleBar:
                TrackClientMouseLeave(hwnd);
                handled = false;
                return (IntPtr)HTCLIENT;
            default:
                handled = false;
                TrackClientMouseLeave(hwnd);
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

        borderH = Math.Min(borderH, Math.Max(4, (width - 24) / 2.0));
        var titleH = GetTitleBarHeight();
        borderBottom = Math.Min(borderBottom, Math.Max(4, height - titleH - 8));

        if (_visibilityMode == OverlayVisibilityMode.Compact)
        {
            if (point.Y <= titleH && !IsInteractiveElement(point))
                return ResizeZone.TitleBar;
            return ResizeZone.None;
        }

        var onLeft = point.X <= borderH;
        var onRight = point.X >= width - borderH;
        var onBottom = point.Y >= height - borderBottom;

        if (onBottom && onLeft) return ResizeZone.BottomLeft;
        if (onBottom && onRight) return ResizeZone.BottomRight;
        if (onLeft) return ResizeZone.Left;
        if (onRight) return ResizeZone.Right;
        if (onBottom) return ResizeZone.Bottom;

        if (point.Y <= titleH && !IsInteractiveElement(point))
            return ResizeZone.TitleBar;

        return ResizeZone.None;
    }

    private double GetTitleBarHeight() =>
        TitleBarBorder.ActualHeight > 0 ? TitleBarBorder.ActualHeight : 28;

    private bool CanShowResizeGrips() =>
        _visibilityMode == OverlayVisibilityMode.Expanded
        && !_visibilityAnimating
        && !_sizingWindow
        && ResizeGripLayer.Visibility == Visibility.Visible;

    private void UpdateHoverState(ResizeZone zone, Point? point = null)
    {
        if (_draggingWindow) return;

        if (!point.HasValue)
        {
            _lastInTitleBarBand = false;
            _lastOverChromeButton = false;
        }
        else
        {
            var inTitleBar = IsInTitleBarBand(point.Value);
            var overChromeButton = IsTitleBarChromeButton(point.Value);
            var zoneChanged = zone != _activeGripZone;
            var chromeChanged = inTitleBar != _lastInTitleBarBand
                || overChromeButton != _lastOverChromeButton;

            if (!zoneChanged && zone != ResizeZone.None && !chromeChanged)
                return;

            _lastInTitleBarBand = inTitleBar;
            _lastOverChromeButton = overChromeButton;
        }

        var gripZoneChanged = zone != _activeGripZone;
        _activeGripZone = zone;

        if ((gripZoneChanged || zone == ResizeZone.None) && !_sizingWindow)
        {
            if (zone == ResizeZone.None)
                ClearResizeGripVisuals(immediate: true);
            else if (CanShowResizeGrips())
                SetResizeGripVisibility(zone);
            else
                ClearResizeGripVisuals(immediate: true);
        }

        ApplyResizeHoverChrome(zone, point);
    }

    private void ApplyResizeHoverChrome(ResizeZone zone, Point? point)
    {
        if (_sizingWindow || _draggingWindow) return;

        var inTitleBar = point.HasValue && IsInTitleBarBand(point.Value);
        SetTitleBarHover(inTitleBar);
        SyncResizeCursor(zone, point);
    }

    private void SyncResizeCursor(ResizeZone zone, Point? point)
    {
        var overChromeButton = point.HasValue && IsTitleBarChromeButton(point.Value);

        if (zone == _lastCursorZone
            && overChromeButton == _lastChromeButtonCursor
            && !IsResizeEdge(zone))
        {
            return;
        }

        _lastCursorZone = zone;
        _lastChromeButtonCursor = overChromeButton;

        // NC resize bands: Win32 sets cursor from HTLEFT/HTRIGHT/etc — clear WPF overrides.
        if (IsResizeEdge(zone))
        {
            Mouse.OverrideCursor = null;
            Cursor = null;
            return;
        }

        Mouse.OverrideCursor = overChromeButton
            ? Cursors.Hand
            : zone == ResizeZone.TitleBar
                ? Cursors.SizeAll
                : null;
        Cursor = null;
    }

    private static bool IsResizeEdge(ResizeZone zone) =>
        zone is ResizeZone.Left or ResizeZone.Right or ResizeZone.Bottom
            or ResizeZone.BottomLeft or ResizeZone.BottomRight;

    private bool IsInTitleBarBand(Point point)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return false;
        if (_visibilityMode == OverlayVisibilityMode.Hidden) return false;
        return point.Y >= 0 && point.Y <= GetTitleBarHeight();
    }

    private bool IsTitleBarChromeButton(Point point) =>
        InputHitTest(point) is DependencyObject hit && IsDescendantOfChromeButton(hit);

    private bool IsDescendantOfChromeButton(DependencyObject? source)
    {
        var current = source;
        while (current != null)
        {
            if (ReferenceEquals(current, CollapseButton) || ReferenceEquals(current, HideButton))
                return true;
            if (ReferenceEquals(current, TitleBarBorder))
                return false;
            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private bool _titleBarHovered;

    private void SetTitleBarHover(bool hovered)
    {
        if (_titleBarHovered == hovered) return;
        _titleBarHovered = hovered;

        TitleBarBorder.Background = hovered
            ? (_visibilityMode == OverlayVisibilityMode.Compact
                ? TitleBarHoverCompact
                : TitleBarHoverExpanded)
            : (_visibilityMode == OverlayVisibilityMode.Compact
                ? CompactBackground
                : ExpandedTitleBackground);
    }

    private void ClearResizeHoverFx(bool immediate = false)
    {
        _activeGripZone = ResizeZone.None;
        _lastInTitleBarBand = false;
        _lastOverChromeButton = false;
        _lastCursorZone = (ResizeZone)(-1);
        _lastChromeButtonCursor = false;
        ClearResizeGripVisuals(immediate);
        SetTitleBarHover(false);
        Mouse.OverrideCursor = null;
        Cursor = null;
    }

    private void ClearResizeGripVisuals(bool immediate)
    {
        SetGripOpacity(LeftGrip, false, immediate);
        SetGripOpacity(RightGrip, false, immediate);
        SetGripOpacity(BottomGrip, false, immediate);
        SetGripOpacity(BottomLeftCornerGrip, false, immediate);
        SetGripOpacity(BottomRightCornerGrip, false, immediate);
    }

    private void SetResizeGripVisibility(ResizeZone zone)
    {
        SetGripOpacity(LeftGrip, zone is ResizeZone.Left or ResizeZone.BottomLeft, immediate: false);
        SetGripOpacity(RightGrip, zone is ResizeZone.Right or ResizeZone.BottomRight, immediate: false);
        SetGripOpacity(BottomGrip,
            zone is ResizeZone.Bottom or ResizeZone.BottomLeft or ResizeZone.BottomRight,
            immediate: false);
        SetGripOpacity(BottomLeftCornerGrip, zone == ResizeZone.BottomLeft, immediate: false);
        SetGripOpacity(BottomRightCornerGrip, zone == ResizeZone.BottomRight, immediate: false);
    }

    private static void SetGripOpacity(UIElement element, bool visible, bool immediate)
    {
        var target = visible ? 1.0 : 0.0;
        element.BeginAnimation(UIElement.OpacityProperty, null);

        if (immediate)
        {
            element.Opacity = target;
            return;
        }

        if (!visible && element.Opacity <= 0.001)
        {
            element.Opacity = 0;
            return;
        }

        if (visible && element.Opacity >= 0.999)
        {
            element.Opacity = 1;
            return;
        }

        var anim = new DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(visible ? 90 : 70),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop,
        };
        anim.Completed += (_, _) => element.Opacity = target;
        element.BeginAnimation(UIElement.OpacityProperty, anim);
    }

    private void TrackClientMouseLeave(IntPtr hwnd)
    {
        if (_trackingMouseLeave) return;
        _trackingMouseLeave = true;
        _trackingNcMouseLeave = false;

        var tme = new NativeMethods.TRACKMOUSEEVENT
        {
            cbSize = Marshal.SizeOf<NativeMethods.TRACKMOUSEEVENT>(),
            dwFlags = NativeMethods.TME_LEAVE,
            hwndTrack = hwnd,
        };
        NativeMethods.TrackMouseEvent(ref tme);
    }

    private void TrackNcMouseLeave(IntPtr hwnd)
    {
        if (_trackingNcMouseLeave) return;
        _trackingNcMouseLeave = true;
        _trackingMouseLeave = false;

        var tme = new NativeMethods.TRACKMOUSEEVENT
        {
            cbSize = Marshal.SizeOf<NativeMethods.TRACKMOUSEEVENT>(),
            dwFlags = NativeMethods.TME_LEAVE | NativeMethods.TME_NONCLIENT,
            hwndTrack = hwnd,
        };
        NativeMethods.TrackMouseEvent(ref tme);
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

    private static Cursor ResolveCursor(ResizeZone zone) => zone switch
    {
        ResizeZone.BottomLeft => Cursors.SizeNESW,
        ResizeZone.BottomRight => Cursors.SizeNWSE,
        ResizeZone.Left or ResizeZone.Right => Cursors.SizeWE,
        ResizeZone.Bottom => Cursors.SizeNS,
        ResizeZone.TitleBar => Cursors.SizeAll,
        _ => Cursors.Arrow,
    };

    private void UpdateGripDashSizes()
    {
        if (!IsLoaded) return;

        var edgeW = ScaledBorder(ResizeBorderHorizontal);
        var edgeH = ScaledBorder(ResizeBorderBottom);
        var titleH = GetTitleBarHeight();
        var bodyH = Math.Max(1, ActualHeight - titleH);

        LeftGrip.Width = edgeW;
        RightGrip.Width = edgeW;
        LeftGrip.Margin = new Thickness(0, titleH, 0, 0);
        RightGrip.Margin = new Thickness(0, titleH, 0, 0);
        BottomGrip.Height = edgeH;
        BottomGrip.Margin = new Thickness(edgeW, 0, edgeW, 0);

        var dashV = Math.Clamp(bodyH * 0.42, 32, 96);
        var dashH = Math.Clamp((ActualWidth - edgeW * 2) * 0.38, 32, 120);
        LeftGripDash.X1 = edgeW / 2;
        LeftGripDash.X2 = edgeW / 2;
        LeftGripDash.Y2 = dashV;
        RightGripDash.X1 = edgeW / 2;
        RightGripDash.X2 = edgeW / 2;
        RightGripDash.Y2 = dashV;
        BottomGripDash.Y1 = edgeH / 2;
        BottomGripDash.Y2 = edgeH / 2;
        BottomGripDash.X2 = dashH;
    }

    private bool IsInteractiveElement(Point point)
    {
        if (InputHitTest(point) is not DependencyObject hit) return false;

        var current = hit;
        while (current != null)
        {
            if (current is Button or ButtonBase or TextBoxBase or ComboBox
                or ScrollBar or Thumb or RepeatButton)
                return true;
            if (current is FrameworkElement { Tag: string }) return true;
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
                var height = _visibilityMode == OverlayVisibilityMode.Compact
                    ? CompactBarHeight
                    : Math.Max(DefaultPanelHeight, taskbarHeight);
                Left = topLeft.X;
                Top = topLeft.Y + taskbarHeight - height;
                Width = width;
                Height = Math.Max(_visibilityMode == OverlayVisibilityMode.Compact ? CompactBarHeight : MinHeight, height);
                return;
            }

            var scale = NativeMethods.GetDpiScale(_taskbarService.TrayWindowHandle);
            var tbHeight = rect.Height / scale;
            var tbWidth = rect.Width / scale;
            var panelHeight = _visibilityMode == OverlayVisibilityMode.Compact
                ? CompactBarHeight
                : Math.Max(DefaultPanelHeight, tbHeight);
            Left = rect.Left / scale;
            Top = rect.Top / scale + tbHeight - panelHeight;
            Width = Math.Max(MinWidth, tbWidth / 2);
            Height = Math.Max(
                _visibilityMode == OverlayVisibilityMode.Compact ? CompactBarHeight : MinHeight,
                panelHeight);
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
            var height = _visibilityMode == OverlayVisibilityMode.Compact
                ? CompactBarHeight
                : Math.Max(DefaultPanelHeight, fallbackTaskbarHeight);
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
        if (_visibilityAnimating) return;

        if (++_refreshCount % 30 == 0)
            _ = RefreshDiscoveryIndexAsync();

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

        if (!_appDiscovery.IsIndexReady)
        {
            RenderGroupsLoadingState();
            return;
        }

        var taskbarButtons = _taskbarService.GetTaskbarButtons();
        var onTaskbar = new HashSet<string>(
            taskbarButtons.Select(b => b.ProcessName),
            StringComparer.OrdinalIgnoreCase);
        var taskbarTitles = taskbarButtons
            .GroupBy(b => b.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Title, StringComparer.OrdinalIgnoreCase);

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

            var detectedCount = 0;
            var chipEntries = new List<(string ProcessName, AppPresence Presence, DiscoveredApp? Discovered, string? TaskbarTitle)>();

            foreach (var processName in group.ProcessNames)
            {
                var discovered = _appDiscovery.TryResolve(processName);
                var onTaskbarNow = onTaskbar.Contains(processName);
                taskbarTitles.TryGetValue(processName, out var taskbarTitle);

                var presence = ResolvePresence(processName, onTaskbarNow, discovered);
                if (presence == AppPresence.NotFound) continue;

                if (presence is AppPresence.Running or AppPresence.Installed)
                    detectedCount++;

                chipEntries.Add((processName, presence, discovered, taskbarTitle));
            }

            var header = new TextBlock
            {
                Text = group.Name,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = accent,
                Margin = new Thickness(0, 0, 0, 4),
            };
            ToolTipService.SetToolTip(header, MakeTooltip(
                $"{group.Name}\n{detectedCount} detected on this PC · {group.ProcessNames.Count} in config · {group.GapAfter}px gap after group"));
            stack.Children.Add(header);

            if (chipEntries.Count == 0)
            {
                stack.Children.Add(MakeHintText(
                    "No matching apps on this PC — use Add App or install an app from this group"));
            }
            else
            {
                var wrap = new WrapPanel { Orientation = Orientation.Horizontal };
                foreach (var (processName, presence, discovered, taskbarTitle) in chipEntries)
                {
                    wrap.Children.Add(BuildAppChip(processName, presence, accent, group, discovered, taskbarTitle));
                }
                stack.Children.Add(wrap);
            }

            card.Child = stack;
            GroupsPanel.Children.Add(card);
        }
    }

    private void RenderGroupsLoadingState()
    {
        var panel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 28, 0, 28),
        };

        panel.Children.Add(new TextBlock
        {
            Text = "Searching for apps…",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Color.FromArgb(0xCC, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 6),
        });

        panel.Children.Add(new TextBlock
        {
            Text = "Scanning installed programs on this PC",
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xFF, 0xFF)),
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        GroupsPanel.Children.Add(panel);
    }

    private static AppPresence ResolvePresence(string processName, bool onTaskbar, DiscoveredApp? discovered)
    {
        if (onTaskbar || IsProcessRunning(processName))
            return AppPresence.Running;
        if (discovered != null)
            return AppPresence.Installed;
        return AppPresence.NotFound;
    }

    private static bool IsProcessRunning(string processName)
    {
        try
        {
            return Process.GetProcessesByName(processName).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private Border BuildAppChip(
        string processName,
        AppPresence presence,
        Brush accent,
        TaskbarGroup group,
        DiscoveredApp? discovered,
        string? taskbarTitle = null)
    {
        var label = discovered?.DisplayName ?? HumanizeProcessName(processName);
        var running = presence == AppPresence.Running;
        var idleBg = presence switch
        {
            AppPresence.Running => new SolidColorBrush(Color.FromArgb(0x33, 0x5B, 0x8C, 0xFF)),
            AppPresence.Installed => new SolidColorBrush(Color.FromArgb(0x28, 0xFF, 0xFF, 0xFF)),
            _ => new SolidColorBrush(Color.FromArgb(0x1A, 0xFF, 0xFF, 0xFF)),
        };
        var hoverBg = presence switch
        {
            AppPresence.Running => new SolidColorBrush(Color.FromArgb(0x55, 0x5B, 0x8C, 0xFF)),
            AppPresence.Installed => new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF)),
            _ => new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
        };
        var idleBorder = running
            ? accent
            : presence == AppPresence.Installed
                ? new SolidColorBrush(Color.FromArgb(0x88, 0xFF, 0xB3, 0x4D))
                : new SolidColorBrush(Color.FromArgb(0x44, 0xFF, 0xFF, 0xFF));

        var chip = new Border
        {
            Tag = processName,
            Background = idleBg,
            BorderBrush = idleBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0, 0, 6, 4),
            Cursor = Cursors.Hand,
            ToolTip = MakeTooltip(BuildChipTooltip(processName, label, presence, discovered, taskbarTitle)),
            ContextMenu = BuildChipContextMenu(processName, group, label),
        };

        var row = new StackPanel { Orientation = Orientation.Horizontal };
        if (running)
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
        else if (presence == AppPresence.Installed)
        {
            row.Children.Add(new Border
            {
                Width = 6,
                Height = 6,
                CornerRadius = new CornerRadius(3),
                Background = Brushes.Transparent,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xB3, 0x4D)),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 5, 0),
            });
        }

        row.Children.Add(new TextBlock
        {
            Text = label,
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10,
            Foreground = presence == AppPresence.Installed
                ? new SolidColorBrush(Color.FromArgb(0xDD, 0xFF, 0xFF, 0xFF))
                : Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        });

        chip.Child = row;
        chip.MouseEnter += (_, _) => chip.Background = hoverBg;
        chip.MouseLeave += (_, _) => chip.Background = idleBg;
        chip.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (chip.ContextMenu != null && chip.ContextMenu.IsOpen) return;
            FocusOrLaunchApp(processName);
        };
        return chip;
    }

    private ContextMenu BuildChipContextMenu(string processName, TaskbarGroup group, string label)
    {
        var menu = new ContextMenu
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 11,
            Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(2),
        };

        var removeItem = new MenuItem
        {
            Header = $"Remove \"{label}\" from {group.Name}",
            Foreground = Brushes.White,
        };
        removeItem.Click += (_, _) => RemoveAppFromGroup(processName, group);
        menu.Items.Add(removeItem);
        return menu;
    }

    private void RemoveAppFromGroup(string processName, TaskbarGroup group)
    {
        if (_config == null) return;

        var matches = group.ProcessNames
            .Where(p => p.Equals(processName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0) return;

        foreach (var match in matches)
            group.ProcessNames.Remove(match);

        if (!_config.Groups.Any(g =>
                g.ProcessNames.Any(p => p.Equals(processName, StringComparison.OrdinalIgnoreCase))))
        {
            _config.KnownExePaths.Remove(processName);
            _appDiscovery.UnregisterKnownApp(processName);
            }

            if (!_configService.Save(_config))
            {
            MessageBox.Show(
                "Failed to save settings. Please check that the config directory is accessible.",
                "Save Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            }
            ConfigChanged?.Invoke(_config);
            Refresh();
            }

    private void FocusOrLaunchApp(string processName)
    {
        if (_taskbarService.TryFocusApp(processName)) return;

        // Prevent launching a second instance if the process is already running
        if (Process.GetProcessesByName(processName).Length > 0) return;

        _ = LaunchAppAsync(processName);
    }

    private async Task LaunchAppAsync(string processName)
    {
        try
        {
            var app = _appDiscovery.TryResolve(processName);
            if (app == null)
            {
                var apps = await _appDiscovery.SearchAsync(processName);
                app = apps.FirstOrDefault(a =>
                    a.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
            }

            if (app == null || !File.Exists(app.ExePath)) return;

            Process.Start(new ProcessStartInfo(app.ExePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TaskSplit] Launch failed for {processName}: {ex.Message}");
        }
    }

    private static string BuildChipTooltip(
        string processName,
        string label,
        AppPresence presence,
        DiscoveredApp? discovered,
        string? taskbarTitle)
    {
        var lines = new List<string> { label, $"{processName}.exe" };

        switch (presence)
        {
            case AppPresence.Running:
                lines.Add("Running");
                if (!string.IsNullOrWhiteSpace(taskbarTitle))
                    lines.Add(taskbarTitle);
                lines.Add("Click to switch · right-click to remove");
                break;
            case AppPresence.Installed:
                lines.Add("Installed on this PC");
                if (discovered != null)
                    lines.Add($"Found via {discovered.Source}");
                lines.Add("Click to launch · right-click to remove");
                break;
            default:
                lines.Add("Not found on this PC");
                break;
        }

        return string.Join('\n', lines);
    }

    private static ToolTip MakeTooltip(string text) => new()
    {
        Content = text,
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 11,
        Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2E)),
        Foreground = Brushes.White,
        BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(8, 4, 8, 4),
    };

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
        if (_config == null || _manualLayout || _visibilityMode == OverlayVisibilityMode.Compact
            || ActualHeight > TaskbarStripMaxHeight) return;

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
