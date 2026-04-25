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
    public long AssetSizeBytes { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? IconUrl { get; set; }
    public string? ManifestName { get; set; }
    public string? ManifestVersion { get; set; }
    public string? ManifestDescription { get; set; }
    public int Stars { get; set; }
    public string? Topics { get; set; }

    public string DisplayName => string.IsNullOrWhiteSpace(ManifestName) ? RepoName : ManifestName!;
    public string DisplayVersion => ManifestVersion ?? LatestVersion ?? "—";
    public string DisplayDescription =>
        !string.IsNullOrWhiteSpace(ManifestDescription) ? ManifestDescription! :
        !string.IsNullOrWhiteSpace(RepoDescription) ? RepoDescription! :
        "No description provided.";
}
