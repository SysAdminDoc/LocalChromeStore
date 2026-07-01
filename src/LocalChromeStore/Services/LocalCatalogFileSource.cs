using System.IO;
using System.Text.Json;
using LocalChromeStore.Models;

namespace LocalChromeStore.Services;

public sealed class LocalCatalogFileSource : IExtensionSource
{
    public string SourceName => "Local catalog file";

    public Task<IReadOnlyList<ExtensionInfo>> DiscoverAsync(AppSettings settings, IProgress<string>? log = null, CancellationToken ct = default)
    {
        var results = new List<ExtensionInfo>();
        var catalogPaths = FindCatalogFiles(settings);

        foreach (var path in catalogPaths)
        {
            try
            {
                var json = File.ReadAllText(path);
                var entries = JsonSerializer.Deserialize<List<CatalogFileEntry>>(json, JsonOpts);
                if (entries is null) continue;

                foreach (var entry in entries)
                {
                    if (string.IsNullOrWhiteSpace(entry.Owner) || string.IsNullOrWhiteSpace(entry.Name))
                        continue;

                    var assetUrl = ValidateAssetUrl(entry.AssetUrl);

                    results.Add(new ExtensionInfo
                    {
                        RepoOwner = entry.Owner,
                        RepoName = entry.Name,
                        RepoUrl = entry.Url ?? $"https://github.com/{entry.Owner}/{entry.Name}",
                        ManifestName = entry.DisplayName,
                        ManifestVersion = entry.Version,
                        ManifestDescription = entry.Description,
                        LatestVersion = entry.Version,
                        AssetUrl = assetUrl,
                        AssetName = entry.AssetName,
                        DiscoverySource = DiscoverySource.RepoManifest,
                        AssetKind = !string.IsNullOrEmpty(assetUrl)
                            ? (assetUrl.EndsWith(".crx", StringComparison.OrdinalIgnoreCase) ? AssetKind.Crx : AssetKind.Zip)
                            : AssetKind.None,
                        Freshness = RepoFreshness.Unknown,
                    });

                    log?.Report($"Catalog file entry: {entry.Owner}/{entry.Name}@{entry.Version ?? "?"}");
                }

                log?.Report($"Loaded {entries.Count} extension(s) from catalog file: {path}");
            }
            catch (Exception ex)
            {
                log?.Report($"! Could not read catalog file {path}: {ex.Message}");
            }
        }

        return Task.FromResult<IReadOnlyList<ExtensionInfo>>(results);
    }

    public static IReadOnlyList<string> FindCatalogFiles(AppSettings settings)
    {
        var paths = new List<string>();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var catalogDir = Path.Combine(appData, "LocalChromeStore", "catalogs");
        if (Directory.Exists(catalogDir))
        {
            foreach (var file in Directory.EnumerateFiles(catalogDir, "*.json"))
                paths.Add(file);
        }

        var localCatalog = Path.Combine(AppContext.BaseDirectory, "catalogs");
        if (Directory.Exists(localCatalog))
        {
            foreach (var file in Directory.EnumerateFiles(localCatalog, "*.json"))
            {
                if (!paths.Contains(file, StringComparer.OrdinalIgnoreCase))
                    paths.Add(file);
            }
        }

        return paths;
    }

    private static string? ValidateAssetUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != "https") return null;
        return url;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };
}

public sealed class CatalogFileEntry
{
    public string? Owner { get; set; }
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }
    public string? AssetUrl { get; set; }
    public string? AssetName { get; set; }
}
