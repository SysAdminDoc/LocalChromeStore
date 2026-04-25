using System.IO;
using System.Text.Json;
using LocalChromeStore.Models;

namespace LocalChromeStore.Services;

public sealed class SettingsService
{
    public string SettingsDir { get; }
    public string SettingsPath { get; }
    public string ExtensionsRoot { get; }
    public string CacheDir { get; }
    public string LogsDir { get; }
    public string ManifestPath { get; }
    public string IconCacheDir { get; }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        SettingsDir = Path.Combine(appData, "LocalChromeStore");
        SettingsPath = Path.Combine(SettingsDir, "settings.json");
        ExtensionsRoot = Path.Combine(localAppData, "LocalChromeStore", "extensions");
        CacheDir = Path.Combine(localAppData, "LocalChromeStore", "cache");
        LogsDir = Path.Combine(localAppData, "LocalChromeStore", "logs");
        IconCacheDir = Path.Combine(CacheDir, "icons");
        ManifestPath = Path.Combine(SettingsDir, "installed.json");
        Directory.CreateDirectory(SettingsDir);
        Directory.CreateDirectory(ExtensionsRoot);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(IconCacheDir);
    }

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
        }
        catch { return new AppSettings(); }
    }

    public void Save(AppSettings settings)
    {
        var json = JsonSerializer.Serialize(settings, JsonOpts);
        File.WriteAllText(SettingsPath, json);
    }

    public InstalledExtensionsManifest LoadManifest()
    {
        if (!File.Exists(ManifestPath)) return new InstalledExtensionsManifest();
        try
        {
            var json = File.ReadAllText(ManifestPath);
            return JsonSerializer.Deserialize<InstalledExtensionsManifest>(json, JsonOpts)
                ?? new InstalledExtensionsManifest();
        }
        catch { return new InstalledExtensionsManifest(); }
    }

    public void SaveManifest(InstalledExtensionsManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, JsonOpts);
        File.WriteAllText(ManifestPath, json);
    }
}
