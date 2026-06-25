// [TRACE: ARCHITECTURE.md]
namespace TaskSplit.Models;

public sealed record DiscoveredApp(
    string ProcessName,
    string DisplayName,
    string ExePath,
    string Source,
    DateTime? AddedAt = null)
{
    public string AddedAtLabel =>
        AddedAt is { } date ? $" · added {date.ToLocalTime():MMM d, yyyy}" : "";
}
