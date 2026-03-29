// [TRACE: ARCHITECTURE.md]
// Service: discovers taskbar window handles and running app buttons.
// Approach: Shell_TrayWnd > ReBarWindow32 > MSTaskSwWClass > MSTaskListWClass
// NOTE: This path works on Win10 and Win11 with ExplorerPatcher.
//       On stock Win11, we can still get the taskbar rect for overlay positioning.

using System.Diagnostics;
using TaskSplit.Win32;

namespace TaskSplit.Services;

public record TaskbarButton(IntPtr HWnd, string ProcessName, string Title, NativeMethods.RECT Rect, int Order);

public class TaskbarService
{
    private IntPtr _trayWnd = IntPtr.Zero;
    private IntPtr _taskListWnd = IntPtr.Zero;

    /// <summary>Returns the screen rect of the primary taskbar.</summary>
    public NativeMethods.RECT? GetTaskbarRect()
    {
        var tray = GetTrayWindow();
        if (tray == IntPtr.Zero) return null;
        NativeMethods.GetWindowRect(tray, out var rect);
        return rect;
    }

    /// <summary>
    /// Returns buttonlike windows inside MSTaskListWClass.
    /// Falls back to an empty list on Win11 stock taskbar.
    /// </summary>
    public List<TaskbarButton> GetTaskbarButtons()
    {
        var buttons = new List<TaskbarButton>();

        var taskList = GetTaskListWindow();
        if (taskList == IntPtr.Zero) return buttons;

        int order = 0;
        NativeMethods.EnumChildWindows(taskList, (hWnd, _) =>
        {
            // Each direct child of MSTaskListWClass is a button group (one per app)
            var className = NativeMethods.GetClassName(hWnd);
            if (className is "MSTaskListWClass" or "MSTask" or "Button"
                || className.StartsWith("MSTask", StringComparison.Ordinal))
            {
                return true; // skip nested
            }

            if (!NativeMethods.IsWindowVisible(hWnd)) return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            var procName = GetProcessName(pid);

            NativeMethods.GetWindowRect(hWnd, out var rect);
            var title = NativeMethods.GetWindowText(hWnd);

            buttons.Add(new TaskbarButton(hWnd, procName, title, rect, order++));
            return true;
        }, IntPtr.Zero);

        return buttons;
    }

    /// <summary>Returns all visible, non-system windows on the taskbar (from EnumWindows).</summary>
    public List<TaskbarButton> GetVisibleWindows()
    {
        var results = new List<TaskbarButton>();
        int order = 0;

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;

            var title = NativeMethods.GetWindowText(hWnd);
            if (string.IsNullOrWhiteSpace(title)) return true;

            var className = NativeMethods.GetClassName(hWnd);
            // Skip shell/taskbar/system windows
            if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd"
                or "Progman" or "WorkerW" or "DV2ControlHost") return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            var procName = GetProcessName(pid);
            NativeMethods.GetWindowRect(hWnd, out var rect);

            results.Add(new TaskbarButton(hWnd, procName, title, rect, order++));
            return true;
        }, IntPtr.Zero);

        return results;
    }

    private IntPtr GetTrayWindow()
    {
        if (_trayWnd == IntPtr.Zero)
            _trayWnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
        return _trayWnd;
    }

    private IntPtr GetTaskListWindow()
    {
        if (_taskListWnd != IntPtr.Zero) return _taskListWnd;

        var tray = GetTrayWindow();
        if (tray == IntPtr.Zero) return IntPtr.Zero;

        // Drill: Shell_TrayWnd > ReBarWindow32 > MSTaskSwWClass > MSTaskListWClass
        var rebar = NativeMethods.FindWindowEx(tray, IntPtr.Zero, "ReBarWindow32", null);
        if (rebar == IntPtr.Zero) return IntPtr.Zero;

        var taskSw = NativeMethods.FindWindowEx(rebar, IntPtr.Zero, "MSTaskSwWClass", null);
        if (taskSw == IntPtr.Zero) return IntPtr.Zero;

        _taskListWnd = NativeMethods.FindWindowEx(taskSw, IntPtr.Zero, "MSTaskListWClass", null);
        return _taskListWnd;
    }

    private static string GetProcessName(uint pid)
    {
        try
        {
            return Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant();
        }
        catch
        {
            return "unknown";
        }
    }
}
