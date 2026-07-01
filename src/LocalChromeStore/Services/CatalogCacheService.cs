using System.IO;
using System.Text.Json;
using LocalChromeStore.Models;

namespace LocalChromeStore.Services;

public sealed class CatalogCacheService
{
    private readonly string _cachePath;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public CatalogCacheService(string cacheDir)
    {
        _cachePath = Path.Combine(cacheDir, "catalog-cache.json");
    }

    public void Save(IReadOnlyList<ExtensionInfo> catalog)
    {
        try
        {
            var snapshot = new CatalogSnapshot
            {
                CachedAtUtc = DateTime.UtcNow,
                Extensions = catalog.ToList()
            };
            var json = JsonSerializer.Serialize(snapshot, JsonOpts);
            SettingsService.WriteAtomic(_cachePath, json);
        }
        catch
        {
            // Best-effort caching — never crash the app.
        }
    }

    public CatalogSnapshot? Load()
    {
        try
        {
            foreach (var candidate in new[] { _cachePath, _cachePath + ".bak" })
            {
                if (!File.Exists(candidate)) continue;
                var json = File.ReadAllText(candidate);
                var snapshot = JsonSerializer.Deserialize<CatalogSnapshot>(json, JsonOpts);
                if (snapshot?.Extensions is { Count: > 0 })
                    return snapshot;
            }
        }
        catch
        {
            // Corrupted cache — ignore.
        }
        return null;
    }

    public bool Exists => File.Exists(_cachePath);
}

public sealed class CatalogSnapshot
{
    public DateTime CachedAtUtc { get; set; }
    public List<ExtensionInfo> Extensions { get; set; } = new();
}
