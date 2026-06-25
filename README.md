# 🛠️ Task-Split ⚒️

[![Build Status](https://img.shields.io/badge/Build-Success-brightgreen)](https://github.com/Otterdays/Task-Split)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%2F11-informational)](https://www.microsoft.com/en-us/windows)
[![Framework: .NET 9.0](https://img.shields.io/badge/Framework-.NET%209.0-blueviolet)](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)

**Task-Split** is a lightweight Windows enhancement tool that allows you to break your taskbar icons into named groups and add beautiful visual dividers or physical spacing between them. It fixes the "cluttered taskbar" problem by bringing organization back to your workflow.

---

## 🏗️ The Vision

The Windows 11 taskbar, while modern, removed the ability to effectively group and separate running applications. **Task-Split** aims to restore that functionality with a modern, non-invasive approach.

### Key Features
- 🏷️ **Visual Logic Groups**: Assign apps (e.g., "Work," "Social," "Browser") to named groups.
- 📋 **Groups Panel**: Overlay lists your groups and apps; green dot = running/pinned on taskbar.
- 📐 **Dynamic Spacing**: Add custom pixel gaps between icon clusters (overlay dividers when snapped to taskbar strip).
- 🎨 **Overlay System**: Floating panel or taskbar-aligned strip with group labels and colored dividers.
- ➕ **Add App UI**: Search (newest first), browse, or scan the system for `.exe` files and add them to a group.
- 🧩 **Zero-Invasion**: Runs as a lightweight system tray app; no shell replacement required.

---

## 🛤️ ROADMAP

### Phase 1: MVP (✅ Complete)
- [x] Project Scaffolding (.NET 9.0 WPF)
- [x] Core JSON Persistence & Configuration System
- [x] Win32 P/Invokes for Taskbar Discovery
- [x] WPF Overlay UI (Visual Dividers)
- [x] System Tray Lifecycle Management

... (See [SCRATCHPAD](DOCS/SCRATCHPAD.md) for task tracking · [FEATURES](DOCS/FEATURES.md) for planned work)

---

## 📋 Prerequisites

- **Windows 10 or 11**
- **[.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)** (required to build and run from source)

Install the SDK on a fresh PC:

```powershell
winget install Microsoft.DotNet.SDK.9
```

After installing, **log out and back in** (or reboot) if `dotnet` is not found in a new terminal. Double-clicking `launch.bat` works without a restart — it prepends the standard install path.

---

## 🚀 Getting Started

**Easiest (Windows):** double-click [`launch.bat`](launch.bat) in the project folder.

**Build a release exe:** double-click [`build.bat`](build.bat) — output at `BuiltExe\TaskSplit.exe`.

**From a terminal:**

```powershell
cd path\to\Task-Split
dotnet run
```

**Build a release exe:**

```powershell
dotnet build -c Release
# Output: BuiltExe\TaskSplit.exe
```

---

## 🛠️ Usage

1.  **Run** `launch.bat` or `dotnet run` (or `TaskSplit.exe` after building).
2.  **Overlay** opens above the taskbar (~220px tall, half taskbar width). **Drag the title bar** to move; **drag left/right/bottom edges** to resize (blue glow + dashed hint on hover). Title bar **`─`** collapses to a slim accent bar (stays on screen); **`×`** hides fully. Double-click the compact bar to expand.
3.  **Groups panel** — shows your groups and apps below **+ Add App**. Scroll if needed. **Green dot** = running; **amber ring** = installed but not running. **Right-click** a chip to remove it from the group.
4.  **Add apps** — click **+ Add App**, search (newest installs first) or browse for an `.exe`, pick a group, confirm. **Right-click** a listed app → **Delete from system…** to permanently remove junk installers (e.g. stray `setup.exe` files). Saves to `%AppData%\TaskSplit\config.json`.
5.  **Tray menu** — **Show Overlay** (checked when visible), **Compact bar** (checked when collapsed to title strip), Snap to Taskbar, Debug Overlay Info, Reset Taskbar Cache, Exit. **Double-click** the tray icon to restore and snap the overlay.
6.  **Manual config** — edit `%AppData%\TaskSplit\config.json` (process names e.g. `chrome`, `code`, `cursor`).

### First-run default groups

On first launch (no config file yet), Task-Split seeds **hardcoded starter groups** — not apps discovered from your PC:

| Group | Apps (process names) |
|-------|-------------------------|
| Work | `code`, `devenv` |
| Browser | `chrome`, `firefox`, `msedge` |
| Chat | `discord`, `slack`, `teams` |

These names are written to `%AppData%\TaskSplit\config.json` automatically. **Only apps detected on your PC appear as chips** (Start Menu, Program Files, registry, or running processes). Chips show in the overlay even if you don't use those apps; they only group on the taskbar when the process is running. Remove or edit groups in config, or use **+ Add App** to build your own layout.

> [!TIP]
> **Windows 11:** Taskbar buttons are discovered via **UI Automation** when classic HWND enumeration finds none. Check **Debug Overlay Info** — `Taskbar buttons` should be > 0. For physical icon repositioning, [ExplorerPatcher](https://github.com/valinet/ExplorerPatcher) may still help classic spacing APIs.

---

## 🤝 Contributing

Contributions are welcome! Please follow our [STYLE_GUIDE](DOCS/STYLE_GUIDE.md) and use **Conventional Commits** for PRs.

## 📄 License

Distributed under the MIT License. See `LICENSE` for more information.
