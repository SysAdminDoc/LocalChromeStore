using System.Net.Http;
using System.Text.Json;
using LocalChromeStore.Models;

namespace LocalChromeStore.Services;

/// <summary>
/// Discovers extensions from a small, HTTPS-hosted JSON catalog. The feed is intentionally
/// independent of GitHub so an operator can publish a catalog from GitHub Pages, object storage,
/// or another static host without changing the discovery pipeline.
/// </summary>
public sealed class CustomUpdateFeedSource : IExtensionSource
{
    public const long MaxFeedBytes = 16L * 1024 * 1024;
    public string SourceName => "Custom update feed";

    private readonly HttpClient _http;

    public CustomUpdateFeedSource(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<IReadOnlyList<ExtensionInfo>> DiscoverAsync(
        AppSettings settings,
        IProgress<string>? log = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.CustomUpdateFeedUrl))
            return [];

        if (!TryValidateHttpsUrl(settings.CustomUpdateFeedUrl, out var feedUri))
        {
            log?.Report("! Custom update feed skipped: URL must be an absolute HTTPS URL.");
            return [];
        }

        try
        {
            using var response = await _http.GetAsync(feedUri, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                log?.Report($"! Custom update feed returned HTTP {(int)response.StatusCode}: {feedUri.AbsoluteUri}");
                return [];
            }

            if (response.Content.Headers.ContentLength > MaxFeedBytes)
            {
                log?.Report($"! Custom update feed exceeds the {MaxFeedBytes / (1024 * 1024)} MB safety limit.");
                return [];
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            if (json.Length > MaxFeedBytes)
            {
                log?.Report($"! Custom update feed exceeds the {MaxFeedBytes / (1024 * 1024)} MB safety limit.");
                return [];
            }

            var document = JsonSerializer.Deserialize<CustomUpdateFeedDocument>(json, JsonOptions);
            if (document?.Extensions is null)
            {
                log?.Report("! Custom update feed is empty or does not contain an extensions array.");
                return [];
            }

            var results = new List<ExtensionInfo>();
            foreach (var entry in document.Extensions)
            {
                if (string.IsNullOrWhiteSpace(entry.Owner) || string.IsNullOrWhiteSpace(entry.Name))
                    continue;
                if (entry.IsPrerelease && settings.ReleaseChannel != ReleaseChannel.IncludePrereleases)
                    continue;

                var assetUrl = ValidateHttpsUrl(entry.AssetUrl);
                var checksumUrl = ValidateHttpsUrl(entry.ChecksumUrl);
                var repoUrl = ValidateHttpsUrl(entry.Url)
                    ?? $"https://github.com/{entry.Owner.Trim()}/{entry.Name.Trim()}";
                var version = string.IsNullOrWhiteSpace(entry.Version) ? null : entry.Version.Trim();

                results.Add(new ExtensionInfo
                {
                    RepoOwner = entry.Owner.Trim(),
                    RepoName = entry.Name.Trim(),
                    RepoUrl = repoUrl,
                    ManifestName = NullIfBlank(entry.DisplayName),
                    ManifestVersion = version,
                    ManifestDescription = NullIfBlank(entry.Description),
                    LatestVersion = version,
                    AssetUrl = assetUrl,
                    AssetName = NullIfBlank(entry.AssetName),
                    AssetDigest = NullIfBlank(entry.AssetDigest),
                    ChecksumUrl = checksumUrl,
                    ChecksumName = NullIfBlank(entry.ChecksumName),
                    DiscoverySource = DiscoverySource.CustomUpdateFeed,
                    AssetKind = AssetKindFor(assetUrl),
                    Freshness = RepoFreshness.Unknown,
                    IsPrerelease = entry.IsPrerelease,
                    HomepageUrl = ValidateHttpsUrl(entry.HomepageUrl)
                });
                log?.Report($"Custom feed entry: {entry.Owner.Trim()}/{entry.Name.Trim()}@{version ?? "?"}");
            }

            log?.Report($"Loaded {results.Count} extension(s) from custom update feed: {feedUri.AbsoluteUri}");
            return results;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log?.Report($"! Could not read custom update feed {feedUri.AbsoluteUri}: {ex.Message}");
            return [];
        }
    }

    internal static string? ValidateHttpsUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        return TryValidateHttpsUrl(value, out var uri) ? uri.AbsoluteUri : null;
    }

    private static bool TryValidateHttpsUrl(string? value, out Uri uri)
    {
        if (Uri.TryCreate(value?.Trim(), UriKind.Absolute, out uri!)
            && uri.Scheme == Uri.UriSchemeHttps)
            return true;
        uri = null!;
        return false;
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AssetKind AssetKindFor(string? assetUrl) =>
        string.IsNullOrWhiteSpace(assetUrl)
            ? AssetKind.None
            : assetUrl.EndsWith(".crx", StringComparison.OrdinalIgnoreCase) ? AssetKind.Crx : AssetKind.Zip;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };
}

public sealed class CustomUpdateFeedDocument
{
    public int SchemaVersion { get; set; } = 1;
    public List<CustomUpdateFeedEntry> Extensions { get; set; } = [];
}

public sealed class CustomUpdateFeedEntry
{
    public string? Owner { get; set; }
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Version { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }
    public string? HomepageUrl { get; set; }
    public string? AssetUrl { get; set; }
    public string? AssetName { get; set; }
    public string? AssetDigest { get; set; }
    public string? ChecksumUrl { get; set; }
    public string? ChecksumName { get; set; }
    public bool IsPrerelease { get; set; }
}
