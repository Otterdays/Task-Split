<!-- PRESERVATION RULE: Never delete or replace content. Append or annotate only. -->
# My Thoughts: TaskSplit Design Rationale

## 2026-03-21
### Taskbar Limitation Strategy
The Windows 11 taskbar is notoriously difficult to modify without full process injection or OS-level patching. I decided to prioritize a **Visual Overlay** approach (WPF) as the primary grouping mechanism, with **Physical Spacing** via `SetWindowPos` as an optional "best-effort" enhancement for users with ExplorerPatcher or Windows 10.

This maximizes compatibility while offering a premium look and feel (labels, custom colors) that physical spacing alone cannot provide.

### Tech Stack Choices
- **.NET 9.0**: Uses the latest performance improvements and modern C# features.
- **WPF**: Chosen specifically for its superior support for layered, transparent windows and custom rendering, which is essential for a sleek taskbar overlay.
- **JSON Persistence**: Simple, readable, and easy to manually edit if needed.
