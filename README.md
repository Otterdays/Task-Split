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
- 📐 **Dynamic Spacing**: Add custom pixel gaps between icon clusters.
- 🎨 **Overlay System**: Taskbar-aligned overlay with group labels and colored dividers.
- ➕ **Add App UI**: Search, browse, or scan the system for `.exe` files and add them to a group.
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

**From a terminal:**

```powershell
cd path\to\Task-Split
dotnet run
```

**Build a release exe:**

```powershell
dotnet build -c Release
# Output: bin\Release\net9.0-windows\TaskSplit.exe
```

---

## 🛠️ Usage

1.  **Run** `launch.bat` or `dotnet run` (or `TaskSplit.exe` after building).
2.  **Overlay** appears along the taskbar (or bottom of screen if auto-detect fails). Drag the center to move; drag edges to resize.
3.  **Add apps** — click **+ Add App** in the overlay, search or browse for an executable, pick a group, and confirm. Saves to `%AppData%\TaskSplit\config.json`.
4.  **Tray menu** — Show Overlay, Snap to Taskbar, Debug Overlay Info, Reset Taskbar Cache, Exit.
5.  **Manual config** — edit `%AppData%\TaskSplit\config.json` directly (process names e.g. `chrome`, `code`, `discord`).

> [!TIP]
> **Windows 11 Users:** For physical icon repositioning, [ExplorerPatcher](https://github.com/valinet/ExplorerPatcher) is highly recommended to restore the classic taskbar API functionality. Stock Win11 may return zero taskbar buttons; overlay positioning and dividers still work via `Shell_TrayWnd`.

---

## 🤝 Contributing

Contributions are welcome! Please follow our [STYLE_GUIDE](DOCS/STYLE_GUIDE.md) and use **Conventional Commits** for PRs.

## 📄 License

Distributed under the MIT License. See `LICENSE` for more information.
