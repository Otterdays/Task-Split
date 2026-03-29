# 🛠️ TaskSplit ⚒️

[![Build Status](https://img.shields.io/badge/Build-Success-brightgreen)](https://github.com/Otterdays/Task-Split)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](https://opensource.org/licenses/MIT)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows%2010%2F11-informational)](https://www.microsoft.com/en-us/windows)
[![Framework: .NET 9.0](https://img.shields.io/badge/Framework-.NET%209.0-blueviolet)](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)

**TaskSplit** is a lightweight Windows enhancement tool that allows you to break your taskbar icons into named groups and add beautiful visual dividers or physical spacing between them. It fixes the "cluttered taskbar" problem by bringing organization and organization back to your workflow.

---

## 🏗️ The Vision

The Windows 11 taskbar, while modern, removed the ability to effectively group and separate running applications. **TaskSplit** aims to restore that functionality with a modern, non-invasive approach.

### Key Features
- 🏷️ **Visual Logic Groups**: Assign apps (e.g., "Work," "Social," "Browser") to named groups.
- 📐 **Dynamic Spacing**: Add custom pixel gaps between icon clusters.
- 🎨 **Overlay System**: Beautifully rendered group labels and colored dividers pinned to the taskbar.
- 🧩 **Zero-Invasion**: Runs as a lightweight system tray app; no shell replacement required.

---

## 🛤️ ROADMAP

### Phase 1: MVP (🏗️ In Progress)
- [x] Project Scaffolding (.NET 9.0 WPF)
- [x] Core JSON Persistence & Configuration System
- [x] Win32 P/Invokes for Taskbar Discovery
- [ ] **WPF Overlay UI (Visual Dividers)**
- [ ] System Tray Lifecycle Management

... (See [SCRATCHPAD](DOCS/SCRATCHPAD.md) for deeper task tracking)

---

## 🛠️ Usage

1.  **Run** `TaskSplit.exe`.
2.  **Right-click** the taskbar icon to open **Settings**.
3.  **Define Groups** using process names (e.g., `chrome`, `code`, `discord`).
4.  **Enjoy** a tidy, organized taskbar.

> [!TIP]
> **Windows 11 Users:** For physical icon repositioning, [ExplorerPatcher](https://github.com/valinet/ExplorerPatcher) is highly recommended to restore the classic taskbar API functionality.

---

## 🤝 Contributing

Contributions are welcome! Please follow our [STYLE_GUIDE](DOCS/STYLE_GUIDE.md) and use **Conventional Commits** for PRs.

## 📄 License

Distributed under the MIT License. See `LICENSE` for more information.
