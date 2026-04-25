using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using LocalChromeStore.Models;
using Octokit;

namespace LocalChromeStore.Services;

public sealed class GitHubService
{
    private readonly SettingsService _settings;
    private readonly HttpClient _http;
    private GitHubClient? _client;
    private string? _activeToken;

    public GitHubService(SettingsService settings)
    {
        _settings = settings;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("LocalChromeStore/0.1");
    }

    private GitHubClient GetClient(AppSettings cfg)
    {
        if (_client != null && _activeToken == cfg.GitHubToken) return _client;
        var product = new ProductHeaderValue("LocalChromeStore", "0.1.0");
        var c = new GitHubClient(product);
        if (!string.IsNullOrWhiteSpace(cfg.GitHubToken))
            c.Credentials = new Credentials(cfg.GitHubToken);
        _client = c;
        _activeToken = cfg.GitHubToken;
        return c;
    }

    /// <summary>
    /// Discover candidate extensions across the configured user(s).
    /// A repo is considered an extension candidate if it has a manifest.json
    /// at the root, or in a common subfolder (extension/, src/, dist/), OR its
    /// latest release contains a .zip / .crx asset.
    /// </summary>
    public async Task<List<ExtensionInfo>> DiscoverAsync(AppSettings cfg, IProgress<string>? log = null, CancellationToken ct = default)
    {
        var client = GetClient(cfg);
        var owners = new List<string>();
        if (!string.IsNullOrWhiteSpace(cfg.GitHubUser)) owners.Add(cfg.GitHubUser.Trim());
        owners.AddRange(cfg.ExtraOwners.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()));
        owners = owners.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        var found = new List<ExtensionInfo>();
        foreach (var owner in owners)
        {
            log?.Report($"Listing repos for {owner}...");
            IReadOnlyList<Repository> repos;
            try { repos = await client.Repository.GetAllForUser(owner); }
            catch (Exception ex) { log?.Report($"  ! {owner}: {ex.Message}"); continue; }

            log?.Report($"  {repos.Count} repos returned");
            foreach (var repo in repos)
            {
                ct.ThrowIfCancellationRequested();
                if (repo.Archived) continue;
                if (cfg.HiddenRepos.Contains($"{repo.Owner.Login}/{repo.Name}", StringComparer.OrdinalIgnoreCase)) continue;

                if (cfg.UseTopicFilter && !string.IsNullOrWhiteSpace(cfg.TopicFilter))
                {
                    var topics = await SafeGetTopics(client, repo);
                    if (topics is null || !topics.Any(t => t.Equals(cfg.TopicFilter, StringComparison.OrdinalIgnoreCase)))
                        continue;
                }

                var info = await ProbeRepoAsync(client, repo, log, ct);
                if (info != null) found.Add(info);
            }
        }
        return found;
    }

    private static async Task<List<string>?> SafeGetTopics(GitHubClient client, Repository repo)
    {
        try
        {
            var topics = await client.Repository.GetAllTopics(repo.Id);
            return topics?.Names?.ToList();
        }
        catch { return null; }
    }

    private async Task<ExtensionInfo?> ProbeRepoAsync(GitHubClient client, Repository repo, IProgress<string>? log, CancellationToken ct)
    {
        Release? release = null;
        try { release = await client.Repository.Release.GetLatest(repo.Owner.Login, repo.Name); }
        catch (NotFoundException) { /* no releases */ }
        catch (Exception ex) { log?.Report($"  ! release {repo.Name}: {ex.Message}"); }

        ReleaseAsset? asset = null;
        if (release != null)
        {
            asset = release.Assets
                .Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                            a.Name.EndsWith(".crx", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(a => a.Size)
                .FirstOrDefault();
        }

        var hasManifest = asset != null || await RepoHasManifestAsync(client, repo, ct);
        if (!hasManifest) return null;

        var info = new ExtensionInfo
        {
            RepoOwner = repo.Owner.Login,
            RepoName = repo.Name,
            RepoUrl = repo.HtmlUrl,
            RepoDescription = repo.Description,
            Stars = repo.StargazersCount,
            LatestVersion = release?.TagName,
            AssetUrl = asset?.BrowserDownloadUrl,
            AssetName = asset?.Name,
            AssetSizeBytes = asset?.Size ?? 0,
            PublishedAt = release?.PublishedAt
        };

        // Try to enrich from manifest.json — best-effort, don't fail discovery if it errors.
        try
        {
            var manifestJson = await TryReadManifestAsync(client, repo, asset, ct);
            if (manifestJson != null) Enrich(info, manifestJson);
        }
        catch (Exception ex)
        {
            log?.Report($"  ~ manifest probe failed for {repo.Name}: {ex.Message}");
        }

        return info;
    }

    private static readonly string[] CommonManifestPaths = ["manifest.json", "extension/manifest.json", "src/manifest.json", "dist/manifest.json", "public/manifest.json"];

    private static async Task<bool> RepoHasManifestAsync(GitHubClient client, Repository repo, CancellationToken ct)
    {
        foreach (var path in CommonManifestPaths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var contents = await client.Repository.Content.GetAllContents(repo.Owner.Login, repo.Name, path);
                if (contents.Count > 0) return true;
            }
            catch (NotFoundException) { /* try next */ }
            catch { return false; }
        }
        return false;
    }

    private async Task<JsonDocument?> TryReadManifestAsync(GitHubClient client, Repository repo, ReleaseAsset? asset, CancellationToken ct)
    {
        // Strategy 1: ZIP asset → download → read manifest.json
        if (asset != null && asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = await _http.GetByteArrayAsync(asset.BrowserDownloadUrl, ct);
            using var ms = new MemoryStream(bytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
            var entry = zip.Entries.FirstOrDefault(e =>
                e.FullName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) ||
                e.FullName.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase));
            if (entry != null)
            {
                using var es = entry.Open();
                using var reader = new StreamReader(es);
                var json = await reader.ReadToEndAsync(ct);
                return JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            }
        }

        // Strategy 2: probe manifest.json paths in repo
        foreach (var path in CommonManifestPaths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var contents = await client.Repository.Content.GetAllContents(repo.Owner.Login, repo.Name, path);
                var c = contents.FirstOrDefault();
                if (c?.Content != null)
                    return JsonDocument.Parse(c.Content, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            }
            catch (NotFoundException) { /* try next */ }
            catch { return null; }
        }
        return null;
    }

    private static void Enrich(ExtensionInfo info, JsonDocument doc)
    {
        var root = doc.RootElement;
        if (root.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            info.ManifestName = name.GetString();
        if (root.TryGetProperty("version", out var ver) && ver.ValueKind == JsonValueKind.String)
            info.ManifestVersion = ver.GetString();
        if (root.TryGetProperty("description", out var desc) && desc.ValueKind == JsonValueKind.String)
            info.ManifestDescription = desc.GetString();

        // Icon: pick the largest available
        if (root.TryGetProperty("icons", out var icons) && icons.ValueKind == JsonValueKind.Object)
        {
            string? bestPath = null;
            int bestSize = 0;
            foreach (var prop in icons.EnumerateObject())
            {
                if (int.TryParse(prop.Name, out var size) && size > bestSize && prop.Value.ValueKind == JsonValueKind.String)
                {
                    bestSize = size;
                    bestPath = prop.Value.GetString();
                }
            }
            // Build raw URL — caller can fetch on demand.
            if (bestPath != null)
                info.IconUrl = $"https://raw.githubusercontent.com/{info.RepoOwner}/{info.RepoName}/HEAD/{bestPath.TrimStart('/')}";
        }
    }

    public async Task<byte[]> DownloadAssetAsync(string url, IProgress<long>? progress = null, CancellationToken ct = default)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        var total = resp.Content.Headers.ContentLength ?? -1L;
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buf = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await stream.ReadAsync(buf, ct)) > 0)
        {
            await ms.WriteAsync(buf.AsMemory(0, read), ct);
            readTotal += read;
            progress?.Report(readTotal);
        }
        return ms.ToArray();
    }

    public async Task<byte[]?> TryDownloadIconAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsByteArrayAsync(ct);
        }
        catch { return null; }
    }
}
