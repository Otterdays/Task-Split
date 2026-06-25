// [TRACE: ARCHITECTURE.md]
// Indexes installed/running apps and searches the filesystem for matching exe files.

using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using TaskSplit.Models;

namespace TaskSplit.Services;

public class AppDiscoveryService
{
    private readonly object _lock = new();
    private List<DiscoveredApp>? _index;
    private readonly Dictionary<string, DiscoveredApp> _knownApps = new(StringComparer.OrdinalIgnoreCase);

    public void InvalidateIndex()
    {
        lock (_lock) _index = null;
    }

    /// <summary>Loads user-registered exe paths (from config) into the resolver.</summary>
    public void SetKnownExePaths(IReadOnlyDictionary<string, string> paths)
    {
        lock (_lock)
        {
            _knownApps.Clear();
            foreach (var (processName, exePath) in paths)
            {
                var app = FromExePath(exePath);
                if (app != null)
                    _knownApps[processName] = app;
            }
        }
    }

    public void RegisterKnownApp(DiscoveredApp app)
    {
        lock (_lock) _knownApps[app.ProcessName] = app;
    }

    public void UnregisterKnownApp(string processName)
    {
        lock (_lock) _knownApps.Remove(processName);
    }

    /// <summary>Builds the system app index if not already cached.</summary>
    public Task WarmIndexAsync(CancellationToken ct = default) => GetIndexAsync(ct);

    /// <summary>Rebuilds the index without clearing the cached copy first (avoids UI flicker).</summary>
    public async Task RefreshIndexAsync(CancellationToken ct = default)
    {
        var built = await Task.Run(() => BuildIndex(ct), ct).ConfigureAwait(false);
        lock (_lock) _index = built;
    }

    public bool IsIndexReady
    {
        get { lock (_lock) return _index != null; }
    }

    /// <summary>Looks up a process name in the warmed index (null if not installed / index not ready).</summary>
    public DiscoveredApp? TryResolve(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return null;

        lock (_lock)
        {
            if (_knownApps.TryGetValue(processName, out var known))
            {
                if (File.Exists(known.ExePath))
                    return known;
                _knownApps.Remove(processName);
            }

            if (_index == null) return null;
            return _index.FirstOrDefault(a =>
                a.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public async Task<IReadOnlyList<DiscoveredApp>> SearchAsync(string query, CancellationToken ct = default)
    {
        var index = await GetIndexAsync(ct).ConfigureAwait(false);
        var q = query.Trim();

        if (string.IsNullOrWhiteSpace(q))
            return SortByRecentThenName(index).Take(60).ToList();

        var ranked = RankFilter(index, q).Take(40).ToList();
        if (ranked.Count >= 8 || q.Length < 2)
            return ranked;

        var deep = await Task.Run(() => DeepFileSearch(q, ct), ct).ConfigureAwait(false);
        return SortByRecentThenName(MergeResults(ranked, deep)).Take(60).ToList();
    }

    public DiscoveredApp? FromExePath(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            return null;

        var processName = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
        var displayName = HumanizeProcessName(processName);
        return new DiscoveredApp(processName, displayName, exePath, "Manual browse", TryGetExeAddedAt(exePath));
    }

    /// <summary>Permanently deletes an indexed executable from disk (with safety guards).</summary>
    public DeleteExecutableResult TryDeleteExecutable(string exePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                return new DeleteExecutableResult(false, "File not found.");

            var fullPath = Path.GetFullPath(exePath);
            if (IsProtectedSystemPath(fullPath))
                return new DeleteExecutableResult(false, "Cannot delete files from protected Windows folders.");

            if (IsExecutableInUse(fullPath))
                return new DeleteExecutableResult(false, "This program is running. Close it first, then try again.");

            File.Delete(fullPath);
            RemoveFromCachedIndex(fullPath);
            return new DeleteExecutableResult(true, "File deleted.");
        }
        catch (UnauthorizedAccessException)
        {
            return new DeleteExecutableResult(false, "Access denied. Try running Task-Split as administrator.");
        }
        catch (IOException ex)
        {
            return new DeleteExecutableResult(false, ex.Message);
        }
        catch (Exception ex)
        {
            return new DeleteExecutableResult(false, ex.Message);
        }
    }

    private void RemoveFromCachedIndex(string fullPath)
    {
        lock (_lock)
        {
            _index?.RemoveAll(a =>
                a.ExePath.Equals(fullPath, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static bool IsProtectedSystemPath(string fullPath)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            Environment.GetFolderPath(Environment.SpecialFolder.SystemX86),
        };

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(root)) continue;
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsExecutableInUse(string fullPath)
    {
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                var modulePath = proc.MainModule?.FileName;
                if (modulePath != null
                    && fullPath.Equals(Path.GetFullPath(modulePath), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch
            {
                // Access denied for system processes
            }
            finally
            {
                proc.Dispose();
            }
        }

        return false;
    }

    private async Task<List<DiscoveredApp>> GetIndexAsync(CancellationToken ct)
    {
        List<DiscoveredApp>? cached;
        lock (_lock) cached = _index;

        if (cached != null) return cached;

        var built = await Task.Run(() => BuildIndex(ct), ct).ConfigureAwait(false);
        lock (_lock) _index = built;
        return built;
    }

    private static List<DiscoveredApp> BuildIndex(CancellationToken ct)
    {
        var map = new Dictionary<string, DiscoveredApp>(StringComparer.OrdinalIgnoreCase);

        void Add(string? exePath, string source, string? displayName = null, DateTime? addedAt = null)
        {
            if (string.IsNullOrWhiteSpace(exePath)) return;
            if (!exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return;
            if (!File.Exists(exePath)) return;

            var processName = Path.GetFileNameWithoutExtension(exePath).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(processName)) return;

            var resolvedAddedAt = addedAt ?? TryGetExeAddedAt(exePath);
            var app = new DiscoveredApp(
                processName,
                displayName ?? HumanizeProcessName(processName),
                exePath,
                source,
                resolvedAddedAt);

            if (map.TryGetValue(processName, out var existing))
            {
                var existingIsRunning = existing.AddedAt == null;
                var newIsRunning = resolvedAddedAt == null;

                if (existingIsRunning && !newIsRunning)
                {
                    // Keep the running-process entry; don't overwrite with a dated shortcut
                    return;
                }

                if (!existingIsRunning && newIsRunning)
                {
                    // Replace shortcut with the running-process entry
                    map[processName] = app;
                    return;
                }

                var useNew = resolvedAddedAt > (existing.AddedAt ?? DateTime.MinValue);
                map[processName] = existing with
                {
                    DisplayName = displayName ?? existing.DisplayName,
                    ExePath = useNew ? exePath : existing.ExePath,
                    Source = useNew ? source : existing.Source,
                    AddedAt = MaxDate(existing.AddedAt, resolvedAddedAt),
                };
                return;
            }

            map[processName] = app;
        }

        ct.ThrowIfCancellationRequested();
        AddRunningProcesses(Add);

        ct.ThrowIfCancellationRequested();
        AddStartMenuShortcuts(Add);

        ct.ThrowIfCancellationRequested();
        AddProgramDirectories(Add);

        ct.ThrowIfCancellationRequested();
        AddRegistryInstalls(Add);

        return SortByRecentThenName(map.Values).ToList();
    }

    private static void AddRunningProcesses(Action<string?, string, string?, DateTime?> add)
    {
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (proc.MainModule?.FileName is { } path)
                    add(path, "Running now", proc.ProcessName, null);
            }
            catch
            {
                // Access denied for system processes
            }
            finally
            {
                proc.Dispose();
            }
        }
    }

    private static void AddStartMenuShortcuts(Action<string?, string, string?, DateTime?> add)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
        };

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            ScanShortcuts(root, add, maxDepth: 5);
    }

    private static void ScanShortcuts(string root, Action<string?, string, string?, DateTime?> add, int maxDepth)
    {
        if (!Directory.Exists(root)) return;

        try
        {
            foreach (var lnk in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
            {
                if (lnk.Count(c => c == Path.DirectorySeparatorChar) -
                    root.Count(c => c == Path.DirectorySeparatorChar) > maxDepth)
                    continue;

                var target = ResolveShortcut(lnk);
                var displayName = Path.GetFileNameWithoutExtension(lnk);
                DateTime? addedAt = null;
                try { addedAt = File.GetLastWriteTimeUtc(lnk); } catch { }
                add(target, "Start menu", displayName, addedAt);
            }
        }
        catch
        {
            // Ignore inaccessible folders
        }
    }

    private static string? ResolveShortcut(string lnkPath)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return null;

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(lnkPath);
            string target = shortcut.TargetPath;
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch
        {
            return null;
        }
    }

    private static void AddProgramDirectories(Action<string?, string, string?, DateTime?> add)
    {
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        };

        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            ScanForExes(root, add, maxDepth: 3, source: "Program files");
    }

    private static void ScanForExes(
        string root,
        Action<string?, string, string?, DateTime?> add,
        int maxDepth,
        string source)
    {
        if (!Directory.Exists(root)) return;

        try
        {
            foreach (var exe in Directory.EnumerateFiles(root, "*.exe", SearchOption.AllDirectories))
            {
                var depth = exe.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length
                            - root.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length;
                if (depth > maxDepth) continue;

                add(exe, source, null, null);
            }
        }
        catch
        {
            // Ignore permission errors mid-scan
        }
    }

    private static void AddRegistryInstalls(Action<string?, string, string?, DateTime?> add)
    {
        ReadUninstallKey(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", add);
        ReadUninstallKey(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", add);
        ReadUninstallKey(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", add);
    }

    private static void ReadUninstallKey(RegistryKey root, string subPath, Action<string?, string, string?, DateTime?> add)
    {
        try
        {
            using var key = root.OpenSubKey(subPath);
            if (key == null) return;

            foreach (var subName in key.GetSubKeyNames())
            {
                using var sub = key.OpenSubKey(subName);
                if (sub == null) continue;

                var displayName = sub.GetValue("DisplayName") as string;
                if (string.IsNullOrWhiteSpace(displayName)) continue;

                var installDate = ParseInstallDate(sub);
                var installLocation = sub.GetValue("InstallLocation") as string;
                var displayIcon = sub.GetValue("DisplayIcon") as string;

                if (!string.IsNullOrWhiteSpace(displayIcon))
                {
                    var iconPath = displayIcon.Split(',')[0].Trim('"');
                    if (iconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        add(iconPath, "Registry", displayName, installDate);
                }

                if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
                {
                    try
                    {
                        var exe = Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly)
                            .FirstOrDefault();
                        add(exe, "Registry", displayName, installDate);
                    }
                    catch { }
                }
            }
        }
        catch
        {
            // Registry access may fail
        }
    }

    private static List<DiscoveredApp> DeepFileSearch(string query, CancellationToken ct)
    {
        var results = new List<DiscoveredApp>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pattern = $"*{SanitizePattern(query)}*.exe";
        var qLower = query.ToLowerInvariant();

        foreach (var root in GetSearchRoots())
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(root)) continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(
                             root,
                             pattern,
                             new EnumerationOptions
                             {
                                 RecurseSubdirectories = true,
                                 MaxRecursionDepth = 5,
                                 IgnoreInaccessible = true,
                                 AttributesToSkip = FileAttributes.System,
                             }))
                {
                    ct.ThrowIfCancellationRequested();
                    if (!seen.Add(file)) continue;

                    var processName = Path.GetFileNameWithoutExtension(file).ToLowerInvariant();
                    if (!processName.Contains(qLower, StringComparison.OrdinalIgnoreCase)
                        && !file.Contains(qLower, StringComparison.OrdinalIgnoreCase))
                        continue;

                    results.Add(new DiscoveredApp(
                        processName,
                        HumanizeProcessName(processName),
                        file,
                        "File search",
                        TryGetExeAddedAt(file)));

                    if (results.Count >= 30) return results;
                }
            }
            catch
            {
                // Continue other roots
            }
        }

        return results;
    }

    private static IEnumerable<string> GetSearchRoots()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
    }

    private static string SanitizePattern(string query) =>
        string.Concat(query.Where(c => !Path.GetInvalidFileNameChars().Contains(c)));

    private static IEnumerable<DiscoveredApp> RankFilter(IEnumerable<DiscoveredApp> apps, string query)
    {
        var q = query.ToLowerInvariant();
        return apps
            .Select(app => (app, score: Score(app, q)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenByDescending(x => x.app.AddedAt ?? DateTime.MinValue)
            .ThenBy(x => x.app.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.app);
    }

    private static int Score(DiscoveredApp app, string q)
    {
        var name = app.ProcessName.ToLowerInvariant();
        var display = app.DisplayName.ToLowerInvariant();
        var path = app.ExePath.ToLowerInvariant();

        if (name == q || display == q) return 100;
        if (name.StartsWith(q) || display.StartsWith(q)) return 80;
        if (name.Contains(q) || display.Contains(q)) return 60;
        if (path.Contains(q)) return 40;
        return 0;
    }

    private static List<DiscoveredApp> MergeResults(IEnumerable<DiscoveredApp> primary, IEnumerable<DiscoveredApp> extra)
    {
        var map = new Dictionary<string, DiscoveredApp>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in primary.Concat(extra))
        {
            if (map.TryGetValue(app.ProcessName, out var existing))
                map[app.ProcessName] = existing with { AddedAt = MaxDate(existing.AddedAt, app.AddedAt) };
            else
                map[app.ProcessName] = app;
        }

        return SortByRecentThenName(map.Values).ToList();
    }

    private static IEnumerable<DiscoveredApp> SortByRecentThenName(IEnumerable<DiscoveredApp> apps) =>
        apps.OrderByDescending(a => a.AddedAt ?? DateTime.MinValue)
            .ThenBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase);

    private static DateTime? MaxDate(DateTime? a, DateTime? b)
    {
        if (a is null) return b;
        if (b is null) return a;
        return a > b ? a : b;
    }

    private static DateTime? TryGetExeAddedAt(string path)
    {
        try
        {
            return File.GetCreationTimeUtc(path);
        }
        catch
        {
            return null;
        }
    }

    private static DateTime? ParseInstallDate(RegistryKey key)
    {
        if (key.GetValue("InstallDate") is not string text || text.Length != 8)
            return null;

        if (DateTime.TryParseExact(
                text,
                "yyyyMMdd",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out var date))
            return date.ToUniversalTime();

        return null;
    }

    private static string HumanizeProcessName(string processName) =>
        string.Join(' ',
            processName.Replace('_', ' ').Replace('-', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(w => w.Length <= 1
                    ? w.ToUpperInvariant()
                    : char.ToUpperInvariant(w[0]) + w[1..]));
}
