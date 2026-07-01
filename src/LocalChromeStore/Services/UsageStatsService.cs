using System.IO;
using System.Text.Json;

namespace LocalChromeStore.Services;

public sealed class UsageStatsService
{
    private readonly string _path;
    private readonly object _lock = new();
    private UsageStats _stats;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public UsageStatsService(string cacheDir)
    {
        _path = Path.Combine(cacheDir, "usage-stats.json");
        _stats = Load();
    }

    public UsageStats Current { get { lock (_lock) return _stats; } }

    public void RecordRefresh(int extensionCount)
    {
        lock (_lock)
        {
            _stats.RefreshCount++;
            _stats.LastRefreshAt = DateTime.UtcNow;
            _stats.LastRefreshExtensionCount = extensionCount;
            Save();
        }
    }

    public void RecordInstall(string repoKey)
    {
        lock (_lock)
        {
            _stats.InstallCount++;
            _stats.LastInstallAt = DateTime.UtcNow;
            _stats.PerExtension.TryGetValue(repoKey, out var ext);
            ext.InstallCount++;
            ext.LastInstalledAt = DateTime.UtcNow;
            _stats.PerExtension[repoKey] = ext;
            Save();
        }
    }

    public void RecordUninstall(string repoKey)
    {
        lock (_lock)
        {
            _stats.UninstallCount++;
            _stats.PerExtension.TryGetValue(repoKey, out var ext);
            ext.UninstallCount++;
            _stats.PerExtension[repoKey] = ext;
            Save();
        }
    }

    public void RecordLaunch()
    {
        lock (_lock)
        {
            _stats.LaunchCount++;
            _stats.LastLaunchAt = DateTime.UtcNow;
            Save();
        }
    }

    public void RecordUpdate(string repoKey)
    {
        lock (_lock)
        {
            _stats.UpdateCount++;
            _stats.PerExtension.TryGetValue(repoKey, out var ext);
            ext.UpdateCount++;
            _stats.PerExtension[repoKey] = ext;
            Save();
        }
    }

    private UsageStats Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<UsageStats>(json, JsonOpts) ?? new();
            }
        }
        catch { /* corrupted — start fresh */ }
        return new();
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_stats, JsonOpts);
            SettingsService.WriteAtomic(_path, json);
        }
        catch { /* best-effort */ }
    }
}

public sealed class UsageStats
{
    public int RefreshCount { get; set; }
    public int InstallCount { get; set; }
    public int UninstallCount { get; set; }
    public int UpdateCount { get; set; }
    public int LaunchCount { get; set; }
    public DateTime? LastRefreshAt { get; set; }
    public DateTime? LastInstallAt { get; set; }
    public DateTime? LastLaunchAt { get; set; }
    public int LastRefreshExtensionCount { get; set; }
    public Dictionary<string, ExtensionUsageStats> PerExtension { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public struct ExtensionUsageStats
{
    public int InstallCount { get; set; }
    public int UninstallCount { get; set; }
    public int UpdateCount { get; set; }
    public DateTime? LastInstalledAt { get; set; }
}
