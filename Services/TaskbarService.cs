// [TRACE: ARCHITECTURE.md]
// Service: discovers taskbar window handles and running app buttons.
// Approach: Shell_TrayWnd > ReBarWindow32 > MSTaskSwWClass > MSTaskListWClass
// Win11 fallback: UI Automation (Taskbar.TaskListButtonAutomationPeer) when HWND enum finds nothing.

using System.Diagnostics;
using System.Text;
using System.Windows.Automation;
using TaskSplit.Models;
using TaskSplit.Win32;

namespace TaskSplit.Services;

public record TaskbarButton(
    IntPtr HWnd,
    string ProcessName,
    string Title,
    NativeMethods.RECT Rect,
    int Order,
    AutomationElement? AutomationElement = null);

public class TaskbarService
{
    private static readonly Dictionary<string, string> DisplayNameAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["File Explorer"] = "explorer",
        ["Microsoft Edge"] = "msedge",
        ["Windows Terminal"] = "windowsterminal",
        ["Visual Studio Code"] = "code",
        ["Visual Studio"] = "devenv",
    };

    private IntPtr _trayWnd = IntPtr.Zero;
    private IntPtr _taskListWnd = IntPtr.Zero;

    public IntPtr TrayWindowHandle => GetTrayWindow();

    /// <summary>Returns the screen rect of the primary taskbar (physical pixels).</summary>
    public NativeMethods.RECT? GetTaskbarRect()
    {
        var tray = GetTrayWindow();
        if (tray == IntPtr.Zero) return null;
        NativeMethods.GetWindowRect(tray, out var rect);
        if (rect.Width <= 0 || rect.Height <= 0) return null;
        return rect;
    }

    public List<TaskbarButton> GetTaskbarButtons()
    {
        var buttons = GetTaskbarButtonsViaHwnd();
        if (buttons.Count == 0)
            buttons = GetTaskbarButtonsViaAutomation();
        return buttons;
    }

    /// <summary>Brings an app to the foreground via taskbar button or process windows.</summary>
    public bool TryFocusApp(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return false;

        var matches = GetTaskbarButtons()
            .Where(b => ProcessNameMatches(b, processName))
            .ToList();

        foreach (var btn in matches)
        {
            if (TryInvokeTaskbarButton(btn))
            {
                Debug.WriteLine($"[TaskSplit] Focused {processName} via UIA invoke");
                return true;
            }

            if (TryClickTaskbarButton(btn))
            {
                Debug.WriteLine($"[TaskSplit] Focused {processName} via taskbar click");
                return true;
            }
        }

        if (TryFocusProcessWindows(processName))
        {
            Debug.WriteLine($"[TaskSplit] Focused {processName} via window enum");
            return true;
        }

        Debug.WriteLine($"[TaskSplit] Failed to focus {processName}");
        return false;
    }

    private static bool TryInvokeTaskbarButton(TaskbarButton btn)
    {
        if (btn.AutomationElement == null) return false;

        try
        {
            if (btn.AutomationElement.GetCurrentPattern(InvokePattern.Pattern) is InvokePattern invoke)
            {
                invoke.Invoke();
                return true;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TaskSplit] UIA invoke failed: {ex.Message}");
        }

        return false;
    }

    private static bool TryClickTaskbarButton(TaskbarButton btn)
    {
        if (btn.Rect.Width <= 0 || btn.Rect.Height <= 0) return false;

        try
        {
            var screenX = (btn.Rect.Left + btn.Rect.Right) / 2;
            var screenY = (btn.Rect.Top + btn.Rect.Bottom) / 2;
            return NativeMethods.SimulateScreenClick(screenX, screenY);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TaskSplit] Taskbar click failed: {ex.Message}");
            return false;
        }
    }

    private static bool TryFocusProcessWindows(string processName)
    {
        var pids = CollectProcessIds(processName);
        if (pids.Count == 0) return false;

        var hwnd = NativeMethods.FindBestWindowForProcesses(pids);
        return hwnd != IntPtr.Zero && NativeMethods.ForceForegroundWindow(hwnd);
    }

    private static HashSet<uint> CollectProcessIds(string processName)
    {
        var pids = new HashSet<uint>();
        foreach (var proc in Process.GetProcessesByName(processName))
        {
            try { pids.Add((uint)proc.Id); }
            catch { /* ignore */ }
            finally { proc.Dispose(); }
        }

        if (pids.Count > 0) return pids;

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
                    pids.Add((uint)proc.Id);
            }
            catch { /* ignore */ }
            finally { proc.Dispose(); }
        }

        return pids;
    }

    private List<TaskbarButton> GetTaskbarButtonsViaHwnd()
    {
        var buttons = new List<TaskbarButton>();
        var taskList = GetTaskListWindow();
        if (taskList == IntPtr.Zero) return buttons;

        EnumTaskbarButtonHwnds(taskList, buttons);
        return buttons;
    }

    private static void EnumTaskbarButtonHwnds(IntPtr parent, List<TaskbarButton> buttons)
    {
        NativeMethods.EnumChildWindows(parent, (hWnd, _) =>
        {
            var className = NativeMethods.GetClassName(hWnd);

            // Container nodes — recurse instead of treating as buttons.
            if (className is "MSTaskListWClass" or "MSTaskSwWClass" or "MSTask"
                || className.StartsWith("MSTask", StringComparison.Ordinal))
            {
                EnumTaskbarButtonHwnds(hWnd, buttons);
                return true;
            }

            if (!NativeMethods.IsWindowVisible(hWnd)) return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            var procName = GetProcessName(pid);
            NativeMethods.GetWindowRect(hWnd, out var rect);
            if (rect.Width <= 0 || rect.Height <= 0) return true;

            var title = NativeMethods.GetWindowText(hWnd);
            buttons.Add(new TaskbarButton(hWnd, procName, title, rect, buttons.Count));
            return true;
        }, IntPtr.Zero);
    }

    private static List<TaskbarButton> GetTaskbarButtonsViaAutomation()
    {
        var buttons = new List<TaskbarButton>();
        try
        {
            var tray = AutomationElement.RootElement.FindFirst(
                TreeScope.Children,
                new PropertyCondition(AutomationElement.ClassNameProperty, "Shell_TrayWnd"));
            if (tray == null) return buttons;

            var items = tray.FindAll(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ClassNameProperty, "Taskbar.TaskListButtonAutomationPeer"));

            var sorted = new List<(AutomationElement El, NativeMethods.RECT Rect)>();
            for (int i = 0; i < items.Count; i++)
            {
                var el = items[i];
                var bounds = el.Current.BoundingRectangle;
                if (bounds.Width <= 0 || bounds.Height <= 0) continue;

                var rect = new NativeMethods.RECT
                {
                    Left = (int)Math.Round(bounds.Left),
                    Top = (int)Math.Round(bounds.Top),
                    Right = (int)Math.Round(bounds.Right),
                    Bottom = (int)Math.Round(bounds.Bottom),
                };
                sorted.Add((el, rect));
            }

            sorted.Sort((a, b) => a.Rect.Left.CompareTo(b.Rect.Left));

            for (int i = 0; i < sorted.Count; i++)
            {
                var (el, rect) = sorted[i];
                var label = el.Current.Name ?? "";
                var processName = ResolveProcessNameFromLabel(label);
                buttons.Add(new TaskbarButton(IntPtr.Zero, processName, label, rect, i, el));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TaskSplit] UIA taskbar scan failed: {ex.Message}");
        }

        return buttons;
    }

    private static string ResolveProcessNameFromLabel(string label)
    {
        var displayName = ExtractDisplayName(label);
        if (string.IsNullOrWhiteSpace(displayName)) return "unknown";

        if (DisplayNameAliases.TryGetValue(displayName, out var alias))
            return alias;

        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                var pn = proc.ProcessName.ToLowerInvariant();
                if (displayName.Equals(proc.ProcessName, StringComparison.OrdinalIgnoreCase))
                    return pn;

                if (displayName.Equals(HumanizeProcessName(pn), StringComparison.OrdinalIgnoreCase))
                    return pn;

                if (proc.MainWindowTitle.StartsWith(displayName, StringComparison.OrdinalIgnoreCase))
                    return pn;
            }
            catch
            {
                // Access denied for system processes
            }
            finally
            {
                proc.Dispose();
            }
        }

        return displayName.Replace(" ", "").ToLowerInvariant();
    }

    private static string ExtractDisplayName(string label)
    {
        var name = label.Trim();
        if (name.EndsWith(" pinned", StringComparison.OrdinalIgnoreCase))
            name = name[..^7].TrimEnd();

        var dash = name.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
            name = name[..dash];

        return name.Trim();
    }

    private static bool ProcessNameMatches(TaskbarButton button, string processName)
    {
        if (button.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase))
            return true;

        var humanized = HumanizeProcessName(processName);
        if (HumanizeProcessName(button.ProcessName).Equals(humanized, StringComparison.OrdinalIgnoreCase))
            return true;

        return button.Title.Contains(humanized, StringComparison.OrdinalIgnoreCase)
            || button.Title.Contains(processName, StringComparison.OrdinalIgnoreCase);
    }

    private static string HumanizeProcessName(string processName) =>
        string.Join(' ',
            processName.Replace('_', ' ').Replace('-', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Length <= 1
                    ? w.ToUpperInvariant()
                    : char.ToUpperInvariant(w[0]) + w[1..]));

    public OverlayDiagnostics GetDiagnostics(
        double overlayLeft, double overlayTop, double overlayWidth, double overlayHeight,
        bool overlayVisible, string positionMode)
    {
        var tray = GetTrayWindow();
        var rect = GetTaskbarRect();
        var taskList = GetTaskListWindow();
        var dpi = NativeMethods.GetDpiScale(tray);

        return new OverlayDiagnostics
        {
            TrayFound = tray != IntPtr.Zero,
            TrayHwnd = FormatHwnd(tray),
            TrayRectPhysical = rect is { } r
                ? $"({r.Left},{r.Top})-({r.Right},{r.Bottom}) [{r.Width}x{r.Height}]"
                : null,
            TaskListFound = taskList != IntPtr.Zero,
            TaskListHwnd = FormatHwnd(taskList),
            ButtonCount = GetTaskbarButtons().Count,
            OverlayPosition = $"{overlayLeft:F0}, {overlayTop:F0}",
            OverlaySize = $"{overlayWidth:F0} x {overlayHeight:F0}",
            OverlayVisible = overlayVisible,
            DpiScale = dpi,
            PositionMode = positionMode,
            TaskbarChildTree = ProbeTaskbarTree(tray, maxDepth: 3),
        };
    }

    public void ResetCache() => (_trayWnd, _taskListWnd) = (IntPtr.Zero, IntPtr.Zero);

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

        var rebar = NativeMethods.FindWindowEx(tray, IntPtr.Zero, "ReBarWindow32", null);

        // Classic: ReBar > MSTaskSwWClass > MSTaskListWClass
        if (rebar != IntPtr.Zero)
        {
            var taskSw = NativeMethods.FindWindowEx(rebar, IntPtr.Zero, "MSTaskSwWClass", null);
            if (taskSw != IntPtr.Zero)
            {
                _taskListWnd = NativeMethods.FindWindowEx(taskSw, IntPtr.Zero, "MSTaskListWClass", null);
                if (_taskListWnd != IntPtr.Zero) return _taskListWnd;
            }

            // Win11: MSTaskListWClass sometimes direct under ReBar
            _taskListWnd = NativeMethods.FindWindowEx(rebar, IntPtr.Zero, "MSTaskListWClass", null);
            if (_taskListWnd != IntPtr.Zero) return _taskListWnd;
        }

        // Deep fallback: search entire tray subtree
        _taskListWnd = FindDescendant(tray, "MSTaskListWClass");
        return _taskListWnd;
    }

    private static IntPtr FindDescendant(IntPtr root, string className)
    {
        IntPtr found = IntPtr.Zero;
        NativeMethods.EnumChildWindows(root, (hwnd, _) =>
        {
            if (NativeMethods.GetClassName(hwnd) == className)
            {
                found = hwnd;
                return false;
            }

            var nested = FindDescendant(hwnd, className);
            if (nested != IntPtr.Zero)
            {
                found = nested;
                return false;
            }

            return true;
        }, IntPtr.Zero);

        return found;
    }

    private static string ProbeTaskbarTree(IntPtr tray, int maxDepth)
    {
        if (tray == IntPtr.Zero) return "";
        var sb = new StringBuilder();
        AppendChildren(tray, sb, 0, maxDepth);
        return sb.ToString().TrimEnd();
    }

    private static void AppendChildren(IntPtr parent, StringBuilder sb, int depth, int maxDepth)
    {
        if (depth > maxDepth) return;

        NativeMethods.EnumChildWindows(parent, (hwnd, _) =>
        {
            var indent = new string(' ', depth * 2);
            var cls = NativeMethods.GetClassName(hwnd);
            NativeMethods.GetWindowRect(hwnd, out var r);
            sb.AppendLine($"{indent}- {cls} [{r.Width}x{r.Height}] {FormatHwnd(hwnd)}");
            AppendChildren(hwnd, sb, depth + 1, maxDepth);
            return true;
        }, IntPtr.Zero);
    }

    private static string FormatHwnd(IntPtr hwnd) =>
        hwnd == IntPtr.Zero ? "0x0" : $"0x{hwnd.ToInt64():X}";

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
