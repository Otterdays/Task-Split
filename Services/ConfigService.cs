// [TRACE: ARCHITECTURE.md]
// Persists AppConfig as JSON to %AppData%\TaskSplit\config.json

using System.IO;
using System.Text.Json;
using TaskSplit.Models;

namespace TaskSplit.Services;

public class ConfigService
{
    private static readonly string ConfigDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskSplit");

    private static readonly string ConfigPath = Path.Combine(ConfigDir, "config.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return CreateDefault();
            var json = File.ReadAllText(ConfigPath);
            return JsonSerializer.Deserialize<AppConfig>(json, JsonOpts) ?? CreateDefault();
        }
        catch
        {
            return CreateDefault();
        }
    }

    public void Save(AppConfig config)
    {
        try
        {
            Directory.CreateDirectory(ConfigDir);
            var json = JsonSerializer.Serialize(config, JsonOpts);
            File.WriteAllText(ConfigPath, json);
        }
        catch (Exception ex)
        {
            // Non-fatal — log to debug output
            System.Diagnostics.Debug.WriteLine($"[TaskSplit] Config save failed: {ex.Message}");
        }
    }

    private static AppConfig CreateDefault() => new()
    {
        Groups =
        [
            new() { Name = "Work", Color = "#5B8CFF", GapAfter = 32, ProcessNames = ["code", "devenv"] },
            new() { Name = "Browser", Color = "#FF7B72", GapAfter = 32, ProcessNames = ["chrome", "firefox", "msedge"] },
            new() { Name = "Chat", Color = "#56D364", GapAfter = 32, ProcessNames = ["discord", "slack", "teams"] },
        ]
    };
}
