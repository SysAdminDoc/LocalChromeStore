namespace LocalChromeStore.Models;

public enum ExtensionFramework
{
    Unknown,
    Wxt,
    Plasmo,
    ExtensionJs,
    Crxjs,
    WebExt,
    PlainMv3,
    PlainMv2
}

public enum DiscoverySource
{
    Unknown,
    ReleaseZipAsset,
    ReleaseCrxAsset,
    RepoManifest
}

public enum AssetKind
{
    None,
    Zip,
    Crx
}

public enum RepoFreshness
{
    Unknown,
    Fresh,
    Aging,
    Stale,
    Archived
}

public enum GitHubServiceStatus
{
    Ok,
    Empty,
    Unauthorized,
    RateLimited,
    Forbidden,
    NetworkError,
    OwnerNotFound
}

public sealed class GitHubRateLimitInfo
{
    public int Limit { get; set; }
    public int Remaining { get; set; }
    public DateTimeOffset? Reset { get; set; }
    public bool Authenticated { get; set; }
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.Now;
}

public sealed class GitHubServiceState
{
    public GitHubServiceStatus Status { get; set; } = GitHubServiceStatus.Ok;
    public string? Detail { get; set; }
    public GitHubRateLimitInfo? RateLimit { get; set; }
}

public static class FrameworkLabels
{
    public static string Label(ExtensionFramework f) => f switch
    {
        ExtensionFramework.Wxt => "WXT",
        ExtensionFramework.Plasmo => "Plasmo",
        ExtensionFramework.ExtensionJs => "Extension.js",
        ExtensionFramework.Crxjs => "CRXJS",
        ExtensionFramework.WebExt => "web-ext",
        ExtensionFramework.PlainMv3 => "Plain MV3",
        ExtensionFramework.PlainMv2 => "Plain MV2",
        _ => "Unknown"
    };

    public static string DiscoveryLabel(DiscoverySource s) => s switch
    {
        DiscoverySource.ReleaseZipAsset => "GitHub release ZIP asset",
        DiscoverySource.ReleaseCrxAsset => "GitHub release CRX asset",
        DiscoverySource.RepoManifest => "manifest.json in repo source",
        _ => "Unknown"
    };

    public static string AssetLabel(AssetKind k) => k switch
    {
        AssetKind.Zip => "ZIP",
        AssetKind.Crx => "CRX",
        _ => "No asset"
    };

    public static string FreshnessLabel(RepoFreshness f) => f switch
    {
        RepoFreshness.Fresh => "Active",
        RepoFreshness.Aging => "Aging",
        RepoFreshness.Stale => "Stale",
        RepoFreshness.Archived => "Archived",
        _ => "Unknown"
    };
}
