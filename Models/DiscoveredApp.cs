// [TRACE: ARCHITECTURE.md]
namespace TaskSplit.Models;

public sealed record DiscoveredApp(
    string ProcessName,
    string DisplayName,
    string ExePath,
    string Source);
