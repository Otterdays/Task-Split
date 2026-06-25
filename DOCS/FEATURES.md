<!-- PRESERVATION RULE: Never delete or replace content. Append or annotate only. -->
# Task-Split: Possible Features & Roadmap Ideas

**Product name:** Task-Split  
**Repo / assembly:** `Task-Split` / `TaskSplit` (namespace unchanged)  
**Last updated:** 2026-06-25

This document captures planned and candidate features to move Task-Split from prototype toward a shippable product. Items are grouped by impact and rough priority.

---

## Current state (baseline)

What exists today:

- System tray app with overlay toggle, snap, debug, exit
- Taskbar-aligned overlay (DPI-aware, drag/resize, manual layout lock)
- Win32 taskbar discovery (`Shell_TrayWnd`, `MSTaskListWClass` + Win11 fallbacks)
- Group config via JSON (`%AppData%\TaskSplit\config.json`)
- **First-run default groups** — `ConfigService.CreateDefault()` hardcodes Work / Browser / Chat with preset process names (`code`, `devenv`, `chrome`, `firefox`, `msedge`, `discord`, `slack`, `teams`); not per-user discovery
- **+ Add App** dialog with system scan + file search (`AppDiscoveryService`); right-click **Delete from system…** for junk `.exe` files
- `launch.bat` dev launcher

Known gaps:

- Dividers often empty on **stock Windows 11** (no classic taskbar button HWNDs)
- Settings = edit JSON + MessageBox
- Debug-style overlay chrome (solid panel, title bar) — not production-polished
- No installer, no start-with-Windows, no release exe workflow

---

## Tier 1 — High impact (do first)

### 1.1 Win11 grouping that actually works

**Problem:** Core promise (visual groups on taskbar) fails when `GetTaskbarButtons()` returns zero.

**Options:**

| Approach | Effort | Notes |
|----------|--------|-------|
| Document + detect | Low | Banner when buttons = 0; recommend ExplorerPatcher |
| UI Automation / Shell APIs | High | Read taskbar buttons without classic HWND tree |
| User-placed divider slots | Medium | Save X positions in config; no HWND dependency |
| Process-order heuristic | Medium | Map running apps to estimated icon positions |

**Success metric:** User on stock Win11 sees at least one divider or label aligned with their apps.

### 1.2 Production vs debug overlay modes

| Debug (current) | Production |
|-----------------|------------|
| Solid background, title bar | Transparent / minimal |
| Draggable, resizable | Locked to taskbar rect |
| Visible border | Subtle dividers only |
| Add App button visible | Optional or tray-only |

Config flag: `overlayMode: "debug" | "production"`.

### 1.3 Real Settings window

Replace MessageBox + JSON editing with a proper WPF window:

- List groups and apps per group (add / remove)
- Rename group, color picker, gap per group
- Create / delete groups
- Toggles: labels, dividers, overlay opacity, start with Windows
- Snap to taskbar + save manual bounds

### 1.4 Ship artifacts

- `build.bat` → single-file self-contained `TaskSplit.exe`
- GitHub Releases with attached binary
- Optional: Inno Setup installer or WinGet manifest

### 1.5 Start with Windows

- Toggle in settings
- Startup shortcut or `Run` registry key
- Single-instance guard (don't launch twice)

---

## Tier 2 — Polish & trust

### 2.1 Persist overlay layout

Save manual `Left`, `Top`, `Width`, `Height` to config so position survives restarts.

### 2.2 Custom application icon

Replace generic `SystemIcons.Application` with branded `.ico` for tray and exe.

### 2.3 First-run wizard

Short flow: pick groups → add 2 apps each → snap overlay → done.

### 2.4 README demo assets

Screenshots or GIF of before/after taskbar. Critical for GitHub discovery.

### 2.5 CI pipeline

GitHub Action: `dotnet build` on push/PR. Real build badge in README.

### 2.6 Unit tests (high-value targets)

- `ConfigService` load/save round-trip
- `AppDiscoveryService` search ranking
- `TaskbarService` tree probe (mocked HWND data)

---

## Tier 3 — Differentiation

### 3.1 Physical taskbar spacing

Actually nudge icon clusters via Win32 (`SetWindowPos` on button HWNDs). Best on Win10 / ExplorerPatcher Win11. Headline feature vs overlay-only tools.

### 3.2 Multi-monitor taskbars

Enumerate `Shell_SecondaryTrayWnd`; overlay per display or follow primary only (configurable).

### 3.3 Event-driven sync

Replace 2-second polling with window create/destroy hooks or shell events.

### 3.4 Layout profiles

Presets: "Work", "Gaming", "Streaming" — swap group sets instantly.

### 3.5 Auto-hide taskbar integration

Track collapsed/expanded taskbar rect; hide or shrink overlay when taskbar auto-hides.

### 3.6 Theme sync

Match Windows light/dark for overlay labels and debug chrome.

---

## Tier 4 — Nice to have

- Drag-and-drop apps between groups in overlay
- Import/export config JSON
- Global hotkey to toggle overlay
- Per-app rules (e.g. always group `chrome.exe` with profile suffixes)
- Notification when new untracked app appears on taskbar ("Add to group?")
- Localization (i18n)

---

## Suggested 2-week sprint order

1. Production overlay mode + persist layout  
2. Settings window (groups + apps)  
3. Win11 zero-button detection + fallback UX  
4. `build.bat` + GitHub Release + icon  
5. Start with Windows toggle  

---

## Naming convention (locked for now)

| Context | Name |
|---------|------|
| User-facing product | **Task-Split** |
| GitHub repo | `Task-Split` |
| C# namespace / assembly | `TaskSplit` |
| Config directory | `%AppData%\TaskSplit` (legacy path; may alias later) |
| Built executable | `TaskSplit.exe` |

---

## Links

- Active task tracking: [SCRATCHPAD](SCRATCHPAD.md)
- Architecture: [ARCHITECTURE](ARCHITECTURE.md)
- Changelog: [CHANGELOG](CHANGELOG.md)
