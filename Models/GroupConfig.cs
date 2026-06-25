// [TRACE: ARCHITECTURE.md]
// Models for persisted group configuration

using System.Collections.ObjectModel;
using System.ComponentModel;

namespace TaskSplit.Models;

/// <summary>One named group that groups specific process names together on the taskbar.</summary>
public class TaskbarGroup : INotifyPropertyChanged
{
    private string _name = "New Group";
    private string _color = "#5B8CFF";

    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Name
    {
        get => _name;
        set { _name = value; OnPropertyChanged(nameof(Name)); }
    }

    /// <summary>Hex color for the group divider line.</summary>
    public string Color
    {
        get => _color;
        set { _color = value; OnPropertyChanged(nameof(Color)); }
    }

    /// <summary>Process names (e.g. "chrome", "code") belonging to this group.</summary>
    public ObservableCollection<string> ProcessNames { get; set; } = [];

    /// <summary>Extra pixel gap to add after this group's last icon.</summary>
    public int GapAfter { get; set; } = 32;

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Root persisted config model.</summary>
public class AppConfig
{
    public List<TaskbarGroup> Groups { get; set; } = [];
    /// <summary>User-picked exe paths for process names (e.g. from Browse).</summary>
    public Dictionary<string, string> KnownExePaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool ShowGroupLabels { get; set; } = true;
    public bool ShowDividers { get; set; } = true;
    public bool AttemptPhysicalSpacing { get; set; } = true;
    public double OverlayOpacity { get; set; } = 0.85;
}
