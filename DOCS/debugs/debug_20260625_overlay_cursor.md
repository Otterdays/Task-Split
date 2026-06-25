<!-- PRESERVATION RULE: Never delete or replace content. Append or annotate only. -->
# Debug: Overlay "always clickable" cursor + non-interactive app chips

**Date:** 2026-06-25  
**Severity:** Critical UX  
**Status:** Fixed

## Symptom

Moving the mouse over the groups panel showed a **Hand cursor everywhere** (entire `ScrollViewer` area), implying everything was clickable. App chips looked active (accent border + green dot) but had **no click handler** — users could not focus/switch apps from the overlay.

## Root cause

1. `TaskbarOverlay.OnMouseMove` set `Window.Cursor = Cursors.Hand` when `IsInteractiveElement` matched **`ScrollViewer`** — inherited by all children in the scroll area.
2. `BuildAppChip` rendered plain `Border` elements with running-state styling only; no hover or click behavior.

## Fix

1. Window cursor override limited to resize/title zones only (`Cursor = null` in client area so per-control cursors apply).
2. Removed `ScrollViewer` / `ListBox` from `IsInteractiveElement` hit-test list.
3. App chips: `Cursor = Hand`, hover background brighten, `MouseLeftButtonUp` → `TaskbarService.TryFocusApp`.
4. `TryFocusApp`: taskbar button `PostMessage` click when HWND exists; else `SetForegroundWindow` on process main window.

## Files touched

- `Views/TaskbarOverlay.xaml.cs`
- `Services/TaskbarService.cs`
- `Win32/NativeMethods.cs`

## Verify

1. Rebuild and run overlay (restart TaskSplit from tray if exe was locked).
2. Arrow cursor over empty panel background; Hand only on **+ Add App** and individual app chips.
3. Hover chip → slightly brighter background (not stuck).
4. Click **running** app chip (green dot) → window comes to foreground.
5. Click **stopped** app chip → app launches if exe found in discovery index.

## Follow-up fix (same day)

Chip clicks still did nothing because Win11 UIA buttons have `HWnd = 0` and `MainWindowHandle` is empty for Electron apps. Added UIA `InvokePattern`, physical taskbar click, and `EnumWindows` foreground activation.
