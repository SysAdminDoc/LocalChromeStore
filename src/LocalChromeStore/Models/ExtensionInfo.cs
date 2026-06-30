namespace LocalChromeStore.Models;

public sealed class ExtensionInfo
{
    public required string RepoOwner { get; init; }
    public required string RepoName { get; init; }
    public required string RepoUrl { get; init; }
    public string? RepoDescription { get; init; }
    public string? LatestVersion { get; set; }
    public string? AssetUrl { get; set; }
    public string? AssetName { get; set; }
    public string? LocalSourcePath { get; set; }
    public string? AssetDigest { get; set; }
    public long AssetSizeBytes { get; set; }
    public long? AssetId { get; set; }
    public string? AssetContentType { get; set; }
    public string? AssetUploader { get; set; }
    public DateTimeOffset? AssetCreatedAt { get; set; }
    public DateTimeOffset? AssetUpdatedAt { get; set; }
    public long? AssetDownloadCount { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? IconUrl { get; set; }
    public string? ManifestName { get; set; }
    public string? ManifestVersion { get; set; }
    public string? ManifestDescription { get; set; }
    public int Stars { get; set; }
    public string? Topics { get; set; }

    // Catalog explainability metadata
    public DiscoverySource DiscoverySource { get; set; } = DiscoverySource.Unknown;
    public string? ManifestSourcePath { get; set; }
    public AssetKind AssetKind { get; set; } = AssetKind.None;
    public ExtensionFramework Framework { get; set; } = ExtensionFramework.Unknown;
    public string? FrameworkEvidence { get; set; }
    public int? ManifestVersionNumber { get; set; }
    public DateTimeOffset? RepoLastPushedAt { get; set; }
    public RepoFreshness Freshness { get; set; } = RepoFreshness.Unknown;
    public bool IsArchived { get; set; }
    public List<string> Warnings { get; set; } = new();

    // F004: repo-supplied catalog manifest
    public bool HasRepoManifest { get; set; }
    public string? HomepageUrl { get; set; }

    // Trust + risk metadata
    public string? ChecksumUrl { get; set; }
    public string? ChecksumName { get; set; }
    public List<string> Permissions { get; set; } = new();
    public List<string> OptionalPermissions { get; set; } = new();
    public List<string> HostPermissions { get; set; } = new();
    public List<string> OptionalHostPermissions { get; set; } = new();

    public string DisplayName => string.IsNullOrWhiteSpace(ManifestName) ? RepoName : ManifestName!;
    public string DisplayVersion => ManifestVersion ?? LatestVersion ?? "—";
    public string DisplayDescription =>
        !string.IsNullOrWhiteSpace(ManifestDescription) ? ManifestDescription! :
        !string.IsNullOrWhiteSpace(RepoDescription) ? RepoDescription! :
        "No description provided.";
}
