<!-- PRESERVATION RULE: Never delete or replace content. Append or annotate only. -->
# Architecture Overview: TaskSplit

## System Structure
TaskSplit is a C# .NET 9.0 WPF application that uses Win32 API calls to discover and manipulate the Windows Taskbar's button hierarchy. It follows a Service-Model-View pattern for clean separation of concerns.

```mermaid
graph TD
    A[App Entry / Tray Icon] --> B[AppConfig Model]
    B --> C[ConfigService (JSON Persistence)]
    A --> D[TaskbarService (Win32 API)]
    D --> E[Shell_TrayWnd Discovery]
    E --> F[MSTaskListWClass Search]
    D --> G[Window Enumeration Fallback]
    A --> H[TaskbarOverlay UI]
    H --> I[Transparent WPF View]
    I --> J[Group Dividers & Labels]
```

## Key Technologies
- **WPF**: Used for the transparent overlay and the settings UI.
- **Win32 P/Invoke**: Required to find window handles (`HWND`) of the taskbar and its child windows (`MSTaskListWClass`).
- **Native IPC/DWM**: Allows the overlay window to stay pinned above the taskbar but below active windows.

## Discovery Logic
1.  **Find `Shell_TrayWnd`**: The root window for the primary Windows Taskbar.
2.  **Navigate Down**: Drill into `ReBarWindow32` > `MSTaskSwWClass` > `MSTaskListWClass`.
3.  **Enumerate Buttons**: Each direct child of `MSTaskListWClass` represents an app button cluster.
4.  **Reposition**: (Best-effort) Use `SetWindowPos` to add pixel offsets matching the configured gaps.
5.  **Overlay Sync**: The WPF window matches its dimensions to the taskbar rect and draws dividers at calculated offsets.

## Project Layout (2026-06-25)
| Path | Role |
|------|------|
| `App.xaml.cs` | Tray icon, overlay lifecycle, config load |
| `Services/TaskbarService.cs` | Taskbar HWND discovery & button enumeration |
| `Services/ConfigService.cs` | `%AppData%\TaskSplit\config.json` persistence |
| `Win32/NativeMethods.cs` | P/Invoke wrappers (`FindWindow`, `EnumChildWindows`, `RECT`, etc.) |
| `Views/TaskbarOverlay.xaml` | Transparent overlay (dividers & labels) |
| `launch.bat` | Windows dev launcher (`dotnet run`; prepends SDK path for Explorer sessions) |

---

## [AMENDED 2026-06-25]: Product name & extended layout

**Product name:** Task-Split (user-facing). Assembly/namespace remains `TaskSplit`.

Additional components since initial layout:

| Path | Role |
|------|------|
| `Services/AppDiscoveryService.cs` | System exe index + search/browse |
| `Views/AddAppDialog.xaml` | Add-app search UI |
| `Models/DiscoveredApp.cs` | Search result model |
| `Models/OverlayDiagnostics.cs` | Debug overlay report |
| `DOCS/FEATURES.md` | Prioritized roadmap & possible features |

Planned work: see [FEATURES.md](FEATURES.md).
