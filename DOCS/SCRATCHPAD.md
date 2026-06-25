<!-- PRESERVATION RULE: Never delete or replace content. Append or annotate only. -->
# SCRATCHPAD: Taskbar Enhancement Tool (Task-Split)

## ACTIVE TASKS
- [x] Project Scaffolding (WPF, .NET 9.0 Windows)
- [x] Core Config Models & JSON Persistence
- [x] Win32 P/Invokes for Taskbar Manipulation
- [x] Taskbar Button Discovery Service
- [x] **WPF Overlay UI (Visual Dividers)**
- [x] Overlay positioning fixes (DPI, z-order, Win11 HWND fallbacks, debug diagnostics)
- [x] Overlay drag/resize + manual layout lock
- [x] **Add App dialog** + `AppDiscoveryService` (exe scan/search)
- [ ] Taskbar Physical Spacing Integration
- [ ] Full configuration UI (group editor, gap editor)
- [x] System Tray App Lifecycle

## ROADMAP
### Phase 1: MVP (Complete ✅)
- [x] Basic process-to-group mapping
- [x] Transparent overlay showing group names/separators
- [x] Simple system tray icon (toggle, settings, exit)

### Phase 2: Enhanced Functionality
- [ ] Physical spacing logic (Windows 10/Patched 11)
- [x] Add-app flow (search, browse, system scan) — partial config UI
- [ ] Drag-and-drop group management
- [ ] Custom gap sizes per group

### Phase 3: Polish & UX
- [ ] Auto-hide integration
- [ ] Dark/Light mode synchronization
- [ ] Multi-monitor support

---
## RECENT ACTIONS
- [x] 2026-06-25: **Fix** Win11 taskbar button discovery — UIA fallback; overlay dividers now appear after Add App when app is on taskbar.
- [x] 2026-06-25: Product name locked to **Task-Split** (user-facing); `DOCS/FEATURES.md` added for roadmap ideas.
- [x] 2026-06-25: `AppDiscoveryService` + `AddAppDialog` — running apps, Start Menu, Program Files, registry, deep `*.exe` file search; **+ Add App** button on overlay.
- [x] 2026-06-25: Overlay UX — solid panel, Task-Split title bar, drag/resize, manual layout lock, Snap to Taskbar, debug log (`%AppData%\TaskSplit\debug.log`).
- [x] 2026-06-25: Added `launch.bat` (dev launcher; prepends `%ProgramFiles%\dotnet` for Explorer stale-PATH after fresh SDK install).
- [x] 2026-06-25: Restored missing `Win32/NativeMethods.cs`; fixed `TaskbarOverlay.xaml` xmlns (`2006` not `2000`).
- [x] 2026-06-25: Verified `dotnet run` on fresh PC (.NET 9 SDK via winget).
- [x] 2026-03-29: Git repository initialized; `.gitignore` added; remote `origin` → https://github.com/Otterdays/Task-Split (initial push).
- [x] 2026-03-21 07:25:00: Scaffolding project and core services done.
- [x] 2026-03-21 07:29:00: Added DOCS directory and project metadata.
