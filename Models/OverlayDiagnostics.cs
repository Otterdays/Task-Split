// [TRACE: ARCHITECTURE.md]
namespace TaskSplit.Models;

public class OverlayDiagnostics
{
    public bool TrayFound { get; init; }
    public string TrayHwnd { get; init; } = "0x0";
    public string? TrayRectPhysical { get; init; }
    public bool TaskListFound { get; init; }
    public string TaskListHwnd { get; init; } = "0x0";
    public int ButtonCount { get; init; }
    public string OverlayPosition { get; init; } = "not positioned";
    public string OverlaySize { get; init; } = "0x0";
    public bool OverlayVisible { get; init; }
    public double DpiScale { get; init; } = 1.0;
    public string PositionMode { get; init; } = "unknown";
    public string TaskbarChildTree { get; init; } = "";

    public string ToReport() => string.Join(Environment.NewLine, new[]
    {
        "Task-Split Overlay Diagnostics",
        "─────────────────────────────",
        $"Tray HWND found:     {TrayFound} ({TrayHwnd})",
        $"Tray rect (physical): {TrayRectPhysical ?? "n/a"}",
        $"Task list found:     {TaskListFound} ({TaskListHwnd})",
        $"Taskbar buttons:     {ButtonCount}",
        $"Overlay visible:     {OverlayVisible}",
        $"Overlay position:    {OverlayPosition} ({PositionMode})",
        $"Overlay size (DIP):  {OverlaySize}",
        $"DPI scale:           {DpiScale:P0}",
        "",
        "Taskbar child tree (top levels):",
        string.IsNullOrWhiteSpace(TaskbarChildTree) ? "  (empty)" : TaskbarChildTree,
        "",
        "Common issues:",
        "• DPI mismatch — fixed via TransformFromDevice",
        "• Behind taskbar — fixed via HWND_TOPMOST SetWindowPos",
        "• Win11 hierarchy — MSTaskListWClass may be nested deeper",
        "• Auto-hide taskbar — rect may be 0-height when hidden",
        "• Taskbar on side/top — overlay follows Shell_TrayWnd rect",
    });
}
