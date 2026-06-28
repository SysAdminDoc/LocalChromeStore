namespace LocalChromeStore.Models;

public sealed class InstalledExtension
{
    public required string RepoOwner { get; set; }
    public required string RepoName { get; set; }
    public required string Version { get; set; }
    public required string InstallPath { get; set; }
    public required string ManifestPath { get; set; }
    public DateTimeOffset InstalledAt { get; set; }
    public bool ChecksumVerified { get; set; }
    public string? ChecksumAlgorithm { get; set; }
    public string? ChecksumValue { get; set; }
    public string? ChecksumSource { get; set; }
    public string? DisplayName { get; set; }
    public string? RepoUrl { get; set; }
    public int? ManifestVersionNumber { get; set; }
    public ExtensionFramework Framework { get; set; } = ExtensionFramework.Unknown;
    public List<string> Permissions { get; set; } = new();
    public List<string> OptionalPermissions { get; set; } = new();
    public List<string> HostPermissions { get; set; } = new();
    public List<string> OptionalHostPermissions { get; set; } = new();
    public string Key => $"{RepoOwner}/{RepoName}";
}

public sealed class InstalledExtensionsManifest
{
    public int Version { get; set; } = 1;
    public List<InstalledExtension> Extensions { get; set; } = new();
}
