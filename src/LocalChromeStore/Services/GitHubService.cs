using System.IO;
using System.IO.Compression;
using System.Net;
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

    public GitHubServiceState LastState { get; private set; } = new();

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

        var state = new GitHubServiceState
        {
            Status = GitHubServiceStatus.Ok,
            RateLimit = new GitHubRateLimitInfo { Authenticated = !string.IsNullOrWhiteSpace(cfg.GitHubToken) }
        };

        var found = new List<ExtensionInfo>();
        bool anyOwnerSucceeded = false;
        foreach (var owner in owners)
        {
            log?.Report($"Listing repos for {owner}...");
            IReadOnlyList<Repository> repos;
            try
            {
                repos = await client.Repository.GetAllForUser(owner);
                anyOwnerSucceeded = true;
            }
            catch (RateLimitExceededException ex)
            {
                state.Status = GitHubServiceStatus.RateLimited;
                state.Detail = $"GitHub API rate limit exceeded for {owner}. Add a personal access token in Settings to raise the limit.";
                log?.Report($"  ! rate limit hit for {owner}: resets {ex.Reset.LocalDateTime:HH:mm:ss}");
                continue;
            }
            catch (AuthorizationException)
            {
                state.Status = GitHubServiceStatus.Unauthorized;
                state.Detail = "GitHub token rejected. Re-enter the token or clear it to fall back to public access.";
                log?.Report($"  ! auth rejected for {owner}: token invalid or scopes insufficient");
                continue;
            }
            catch (NotFoundException)
            {
                state.Status = GitHubServiceStatus.OwnerNotFound;
                state.Detail = $"GitHub user or organization '{owner}' could not be found.";
                log?.Report($"  ! owner not found: {owner}");
                continue;
            }
            catch (ForbiddenException ex)
            {
                state.Status = GitHubServiceStatus.Forbidden;
                state.Detail = $"GitHub denied the request for {owner}: {ex.Message}";
                log?.Report($"  ! forbidden for {owner}: {ex.Message}");
                continue;
            }
            catch (Octokit.ApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                state.Status = GitHubServiceStatus.Unauthorized;
                state.Detail = "GitHub token rejected. Re-enter the token or clear it to fall back to public access.";
                log?.Report($"  ! auth rejected for {owner}: {ex.Message}");
                continue;
            }
            catch (HttpRequestException ex)
            {
                state.Status = GitHubServiceStatus.NetworkError;
                state.Detail = $"Network error while contacting GitHub: {ex.Message}";
                log?.Report($"  ! network error for {owner}: {ex.Message}");
                continue;
            }
            catch (Exception ex)
            {
                state.Detail ??= ex.Message;
                log?.Report($"  ! {owner}: {ex.Message}");
                continue;
            }

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

        // Capture latest rate-limit data for the UI. Best-effort — never fail discovery on this.
        try
        {
            var rl = await client.RateLimit.GetRateLimits();
            var core = rl?.Resources?.Core;
            if (core != null && state.RateLimit != null)
            {
                state.RateLimit.Limit = core.Limit;
                state.RateLimit.Remaining = core.Remaining;
                state.RateLimit.Reset = core.Reset;
                state.RateLimit.CapturedAt = DateTimeOffset.Now;
            }
        }
        catch { /* ignore — rate-limit endpoint itself can fail under degraded states */ }

        if (anyOwnerSucceeded && found.Count == 0 && state.Status == GitHubServiceStatus.Ok)
        {
            state.Status = GitHubServiceStatus.Empty;
            state.Detail = "GitHub returned repos, but none of them looked like Chromium extensions.";
        }

        LastState = state;
        return found;
    }

    /// <summary>
    /// Finds a SHA256 sidecar asset in the same release. Recognises the shapes
    /// the project's own release workflow emits as well as the common conventions
    /// upstream projects use (`<asset>.sha256`, `<asset>.sha256.txt`, separate
    /// `SHA256SUMS` text files mentioning the asset name).
    /// </summary>
    private static ReleaseAsset? FindChecksumSidecar(IEnumerable<ReleaseAsset> assets, string assetName)
    {
        var direct = assets.FirstOrDefault(a =>
            a.Name.Equals(assetName + ".sha256", StringComparison.OrdinalIgnoreCase) ||
            a.Name.Equals(assetName + ".sha256.txt", StringComparison.OrdinalIgnoreCase) ||
            a.Name.Equals(assetName + ".SHA256SUMS", StringComparison.OrdinalIgnoreCase));
        if (direct != null) return direct;

        // Aggregate sidecars covering multiple artifacts.
        return assets.FirstOrDefault(a =>
            a.Name.EndsWith("SHA256SUMS", StringComparison.OrdinalIgnoreCase) ||
            a.Name.EndsWith("checksums.txt", StringComparison.OrdinalIgnoreCase) ||
            a.Name.EndsWith(".sha256sum", StringComparison.OrdinalIgnoreCase));
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
        AssetKind assetKind = AssetKind.None;
        ReleaseAsset? checksum = null;
        if (release != null)
        {
            asset = release.Assets
                .Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                            a.Name.EndsWith(".crx", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(a => a.Size)
                .FirstOrDefault();
            if (asset != null)
            {
                assetKind = asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ? AssetKind.Zip : AssetKind.Crx;
                checksum = FindChecksumSidecar(release.Assets, asset.Name);
            }
        }

        var manifestSourcePath = await FindManifestPathAsync(client, repo, ct);
        var hasManifest = asset != null || manifestSourcePath != null;
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
            PublishedAt = release?.PublishedAt,
            AssetKind = assetKind,
            DiscoverySource = asset != null
                ? (assetKind == AssetKind.Zip ? DiscoverySource.ReleaseZipAsset : DiscoverySource.ReleaseCrxAsset)
                : DiscoverySource.RepoManifest,
            ManifestSourcePath = asset != null ? null : manifestSourcePath,
            RepoLastPushedAt = repo.PushedAt ?? repo.UpdatedAt,
            IsArchived = repo.Archived,
            ChecksumUrl = checksum?.BrowserDownloadUrl,
            ChecksumName = checksum?.Name
        };

        info.Freshness = ClassifyFreshness(info.IsArchived, info.RepoLastPushedAt);
        AddFreshnessWarnings(info, release);

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

        // Framework detection — best-effort.
        try
        {
            await DetectFrameworkAsync(client, repo, info, ct);
        }
        catch (Exception ex)
        {
            log?.Report($"  ~ framework probe failed for {repo.Name}: {ex.Message}");
        }

        // F004/F005: probe localchromestore.json — repo author's catalog manifest.
        try
        {
            await ProbeRepoManifestAsync(client, repo, info, log, ct);
        }
        catch (Exception ex)
        {
            log?.Report($"  ~ localchromestore.json probe failed for {repo.Name}: {ex.Message}");
        }

        return info;
    }

    private static readonly string[] CommonManifestPaths = ["manifest.json", "extension/manifest.json", "src/manifest.json", "dist/manifest.json", "public/manifest.json"];

    private static async Task<string?> FindManifestPathAsync(GitHubClient client, Repository repo, CancellationToken ct)
    {
        foreach (var path in CommonManifestPaths)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var contents = await client.Repository.Content.GetAllContents(repo.Owner.Login, repo.Name, path);
                if (contents.Count > 0) return path;
            }
            catch (NotFoundException) { /* try next */ }
            catch { return null; }
        }
        return null;
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
        if (root.TryGetProperty("manifest_version", out var mvEl))
        {
            if (mvEl.ValueKind == JsonValueKind.Number && mvEl.TryGetInt32(out var mvNum))
                info.ManifestVersionNumber = mvNum;
            else if (mvEl.ValueKind == JsonValueKind.String && int.TryParse(mvEl.GetString(), out var mvParsed))
                info.ManifestVersionNumber = mvParsed;
        }

        // Permissions / host_permissions / optional_* — F009/F058/F059.
        AppendStringArray(root, "permissions", info.Permissions);
        AppendStringArray(root, "optional_permissions", info.OptionalPermissions);
        AppendStringArray(root, "host_permissions", info.HostPermissions);
        AppendStringArray(root, "optional_host_permissions", info.OptionalHostPermissions);

        // MV2 stores host patterns inline with permissions; promote any URL-like entries
        // into HostPermissions so the risk panel groups them sensibly.
        if (info.ManifestVersionNumber == 2 && info.Permissions.Count > 0)
        {
            var hosts = info.Permissions.Where(LooksLikeHostPattern).ToList();
            foreach (var h in hosts)
            {
                info.Permissions.Remove(h);
                if (!info.HostPermissions.Contains(h, StringComparer.OrdinalIgnoreCase))
                    info.HostPermissions.Add(h);
            }
        }

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

    private static void AppendStringArray(JsonElement root, string property, List<string> sink)
    {
        if (!root.TryGetProperty(property, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) continue;
            var v = item.GetString();
            if (string.IsNullOrWhiteSpace(v)) continue;
            if (!sink.Contains(v, StringComparer.OrdinalIgnoreCase)) sink.Add(v!);
        }
    }

    private static bool LooksLikeHostPattern(string permission)
    {
        if (string.IsNullOrWhiteSpace(permission)) return false;
        return permission == "<all_urls>"
            || permission.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || permission.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || permission.StartsWith("file://", StringComparison.OrdinalIgnoreCase)
            || permission.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase)
            || permission.StartsWith("ws://", StringComparison.OrdinalIgnoreCase)
            || permission.StartsWith("wss://", StringComparison.OrdinalIgnoreCase)
            || permission.StartsWith("*://", StringComparison.OrdinalIgnoreCase);
    }

    private async Task DetectFrameworkAsync(GitHubClient client, Repository repo, ExtensionInfo info, CancellationToken ct)
    {
        // Strategy 1 (cheap, high signal): read package.json dependencies/devDependencies.
        var pkg = await TryReadRepoFileAsync(client, repo, "package.json", ct);
        if (pkg != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(pkg, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
                var deps = CollectDependencyNames(doc.RootElement);
                if (TryClassifyDeps(deps, out var fw, out var evidence))
                {
                    info.Framework = fw;
                    info.FrameworkEvidence = evidence;
                    return;
                }
            }
            catch { /* malformed package.json — fall through to file probes */ }
        }

        // Strategy 2 (cheap-ish): probe the canonical config file for each framework.
        // We only do this when package.json was missing or did not classify.
        var configProbes = new (string Path, ExtensionFramework Fw, string Evidence)[]
        {
            ("wxt.config.ts",    ExtensionFramework.Wxt,         "wxt.config.ts present"),
            ("wxt.config.js",    ExtensionFramework.Wxt,         "wxt.config.js present"),
            ("plasmo.config.ts", ExtensionFramework.Plasmo,      "plasmo.config.ts present"),
            ("plasmo.config.js", ExtensionFramework.Plasmo,      "plasmo.config.js present"),
            ("extension.config.js", ExtensionFramework.ExtensionJs, "extension.config.js present"),
            ("extension.config.ts", ExtensionFramework.ExtensionJs, "extension.config.ts present"),
            ("web-ext-config.js", ExtensionFramework.WebExt,     "web-ext-config.js present")
        };
        foreach (var probe in configProbes)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var contents = await client.Repository.Content.GetAllContents(repo.Owner.Login, repo.Name, probe.Path);
                if (contents.Count > 0)
                {
                    info.Framework = probe.Fw;
                    info.FrameworkEvidence = probe.Evidence;
                    return;
                }
            }
            catch (NotFoundException) { /* keep probing */ }
            catch { break; }
        }

        // Strategy 3: fall back to plain-mv2 / plain-mv3 from manifest_version, if known.
        if (info.ManifestVersionNumber == 3)
        {
            info.Framework = ExtensionFramework.PlainMv3;
            info.FrameworkEvidence = "manifest_version: 3 (no framework markers found)";
        }
        else if (info.ManifestVersionNumber == 2)
        {
            info.Framework = ExtensionFramework.PlainMv2;
            info.FrameworkEvidence = "manifest_version: 2 (no framework markers found)";
        }
    }

    private static readonly JsonSerializerOptions RepoManifestJsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private async Task ProbeRepoManifestAsync(GitHubClient client, Repository repo,
        ExtensionInfo info, IProgress<string>? log, CancellationToken ct)
    {
        var json = await TryReadRepoFileAsync(client, repo, "localchromestore.json", ct);
        if (json is null) return;

        RepoManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<RepoManifest>(json, RepoManifestJsonOpts);
        }
        catch (JsonException ex)
        {
            info.Warnings.Add($"localchromestore.json: JSON parse error — {ex.Message}");
            return;
        }
        if (manifest is null) return;

        // F005: validate and surface issues as warnings (non-blocking).
        var validationErrors = RepoManifest.Validate(manifest);
        info.Warnings.AddRange(validationErrors);

        // Apply overrides — localchromestore.json takes precedence over
        // manifest.json / repo metadata for catalog-facing fields.
        if (!string.IsNullOrWhiteSpace(manifest.DisplayName))
            info.ManifestName = manifest.DisplayName;
        if (!string.IsNullOrWhiteSpace(manifest.Description))
            info.ManifestDescription = manifest.Description;
        if (!string.IsNullOrWhiteSpace(manifest.IconUrl) && validationErrors.All(e => !e.Contains("iconUrl")))
            info.IconUrl = manifest.IconUrl;
        if (!string.IsNullOrWhiteSpace(manifest.HomepageUrl) && validationErrors.All(e => !e.Contains("homepageUrl")))
            info.HomepageUrl = manifest.HomepageUrl;
        if (manifest.HideFromCatalog == true)
            info.Warnings.Add("localchromestore.json: repo author flagged this extension as hidden from catalog.");

        info.HasRepoManifest = true;
        log?.Report($"  + localchromestore.json found for {repo.Name}");
    }

    private async Task<string?> TryReadRepoFileAsync(GitHubClient client, Repository repo, string path, CancellationToken ct)
    {
        try
        {
            var contents = await client.Repository.Content.GetAllContents(repo.Owner.Login, repo.Name, path);
            return contents.FirstOrDefault()?.Content;
        }
        catch (NotFoundException) { return null; }
        catch { return null; }
    }

    private static IEnumerable<string> CollectDependencyNames(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) yield break;
        foreach (var key in new[] { "dependencies", "devDependencies", "peerDependencies", "optionalDependencies" })
        {
            if (!root.TryGetProperty(key, out var section) || section.ValueKind != JsonValueKind.Object) continue;
            foreach (var prop in section.EnumerateObject())
                yield return prop.Name;
        }
    }

    private static bool TryClassifyDeps(IEnumerable<string> deps, out ExtensionFramework fw, out string evidence)
    {
        var set = new HashSet<string>(deps, StringComparer.OrdinalIgnoreCase);
        if (set.Contains("wxt")) { fw = ExtensionFramework.Wxt; evidence = "package.json depends on `wxt`"; return true; }
        if (set.Contains("plasmo")) { fw = ExtensionFramework.Plasmo; evidence = "package.json depends on `plasmo`"; return true; }
        if (set.Any(d => d.StartsWith("@extension-js/", StringComparison.OrdinalIgnoreCase) || d.Equals("extension-js", StringComparison.OrdinalIgnoreCase) || d.Equals("extension", StringComparison.OrdinalIgnoreCase)))
        { fw = ExtensionFramework.ExtensionJs; evidence = "package.json depends on Extension.js packages"; return true; }
        if (set.Contains("@crxjs/vite-plugin")) { fw = ExtensionFramework.Crxjs; evidence = "package.json depends on `@crxjs/vite-plugin`"; return true; }
        if (set.Contains("web-ext")) { fw = ExtensionFramework.WebExt; evidence = "package.json depends on `web-ext`"; return true; }
        fw = ExtensionFramework.Unknown;
        evidence = string.Empty;
        return false;
    }

    private static RepoFreshness ClassifyFreshness(bool archived, DateTimeOffset? lastPushedAt)
    {
        if (archived) return RepoFreshness.Archived;
        if (!lastPushedAt.HasValue) return RepoFreshness.Unknown;
        var age = DateTimeOffset.Now - lastPushedAt.Value;
        if (age <= TimeSpan.FromDays(90)) return RepoFreshness.Fresh;
        if (age <= TimeSpan.FromDays(365)) return RepoFreshness.Aging;
        return RepoFreshness.Stale;
    }

    private static void AddFreshnessWarnings(ExtensionInfo info, Release? release)
    {
        if (info.IsArchived)
            info.Warnings.Add("Repository is archived on GitHub.");
        else if (info.Freshness == RepoFreshness.Stale)
            info.Warnings.Add("No commits in over a year.");
        else if (info.Freshness == RepoFreshness.Aging)
            info.Warnings.Add("No commits in over 90 days.");

        if (release == null)
            info.Warnings.Add("No GitHub release found — discovery is using repo source files.");
        else if (release.PublishedAt.HasValue && (DateTimeOffset.Now - release.PublishedAt.Value) > TimeSpan.FromDays(365))
            info.Warnings.Add("Latest GitHub release is over a year old.");
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

    public async Task<string?> TryDownloadTextAsync(string url, CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadAsStringAsync(ct);
        }
        catch { return null; }
    }
}
