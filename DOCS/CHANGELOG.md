<!-- PRESERVATION RULE: Never delete or replace content. Append or annotate only. -->
# Changelog: Task-Split Project

## [0.1.7] - 2026-06-25
### Added
- **Overlay tooltips** — app chips show process name, running state, taskbar window title, and click action; group headers show app count and gap; title bar and **+ Add App** have usage hints. Dark-themed tooltip styling matches overlay chrome.

## [0.1.6] - 2026-06-25
### Fixed
- **Overlay "always clickable" cursor** — `OnMouseMove` no longer forces `Hand` over the entire `ScrollViewer`; only resize edges and title bar override window cursor.
- **App chips not interactive** — chips now show hover highlight, Hand cursor on chip only, and click focuses the app.
- **App chip click did nothing** — `TryFocusApp` now uses Win11 UIA `InvokePattern`, simulated taskbar click at button screen coords, then `EnumWindows` + `ForceForegroundWindow` (Electron/multi-process apps no longer rely on `MainWindowHandle`).

### Added
- **Launch fallback** — clicking a chip for a non-running app attempts to start it via `AppDiscoveryService` exe lookup.

## [0.1.5] - 2026-06-25
### Added
- **Groups panel** on overlay — lists configured groups and app chips; green dot when app is on the taskbar.
- **Add App search sort** — default list sorted by recently added (registry install date, shortcut mtime, exe creation time); `DiscoveredApp.AddedAt`.
- **Resize edge visuals** — blue gradient + dashed midpoint line on hover (driven from `WM_NCHITTEST`, not WPF `MouseMove`).

### Fixed
- **Add App appeared to do nothing** — config saved but UI only drew off-screen taskbar dividers; groups panel always reflects `config.json`.
- **Overlay collapsed to taskbar strip** on snap — now opens at ≥220px height, bottom-aligned above taskbar.
- **Content vanished while dragging** — full UI rebuild on every `LocationChanged` removed; debounced refresh on resize only.
- **Could not click scroll bar / content** — `HTCAPTION` limited to title bar; client area returns `HTCLIENT`.
- **Add App Group combo** — black Segoe UI text on white (closed + dropdown); removed broken `SystemColors.WindowBrushKey` override.
- **Win11 tip in README** — stock Win11 buttons now discovered via UI Automation (see 0.1.4).

### Changed
- Snap width remains half of detected taskbar width; dividers only in thin strip mode when auto-snapped.
- Timer poll: manual layout refreshes groups only; auto-snap still runs full `SyncToTaskbar`.

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
