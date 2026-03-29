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
