<!-- PRESERVATION RULE: Never delete or replace content. Append or annotate only. -->
# Changelog: Task-Split Project

## [0.1.4] - 2026-06-25
### Fixed
- **Add App had no visible effect on overlay** — Win11 taskbar buttons are not child HWNDs; `TaskbarService` now falls back to UI Automation to enumerate `Taskbar.TaskListButtonAutomationPeer` items and map labels (e.g. "Cursor - 1 running window pinned") to process names. Debug log should show `Taskbar buttons: N` with N > 0.

## [0.1.3] - 2026-06-25
### Added
- `DOCS/FEATURES.md` — prioritized roadmap and possible features document.

### Changed
- User-facing product name standardized to **Task-Split** (overlay title bar, tray, README, diagnostics). C# namespace remains `TaskSplit`.

## [0.1.2] - 2026-06-25
### Added
- **+ Add App** button on overlay; `AddAppDialog` with group picker, live search, and Browse for `.exe`.
- `AppDiscoveryService` — indexes running processes, Start Menu shortcuts, Program Files, registry installs; deep filesystem search for `*query*.exe`.
- `Models/DiscoveredApp.cs`, `Models/OverlayDiagnostics.cs`.
- Overlay title bar branding (**Task-Split**), drag-to-move, edge/corner resize, manual layout lock.
- Tray items: Snap to Taskbar, Debug Overlay Info, Reset Taskbar Cache; debug log at `%AppData%\TaskSplit\debug.log`.

### Fixed
- Overlay invisible on fresh Win11/DPI setups — DPI-aware positioning, `HWND_TOPMOST`, Win11 recursive `MSTaskListWClass` search, fallback work-area placement.

### Changed
- README usage docs for Add App flow and overlay controls.

## [0.1.1] - 2026-06-25
### Added
- `launch.bat` — one-click dev launcher for Windows (handles stale PATH after SDK install).

### Fixed
- Restored `Win32/NativeMethods.cs` (was referenced but missing from repo).
- Corrected XAML presentation namespace in `Views/TaskbarOverlay.xaml`.

### Changed
- README: prerequisites, getting started, and usage aligned with current MVP.

## [0.1.0] - 2026-03-21
### Added
- Created initial project scaffolding (.NET 9.0 Windows WPF).
- Defined core configuration models for group management.
- Implemented `TaskbarService` for discovering taskbar child windows.
- Added Win32 `NativeMethods` for taskbar manipulation.
- Setup `ConfigService` for JSON persistence.
- Initialized documentation suite (SCRATCHPAD, SUMMARY, SBOM, etc.).
- Created a high-quality README for GitHub.
