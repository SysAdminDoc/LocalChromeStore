namespace LocalChromeStore.Models;

public sealed class EnvironmentManifest
{
    public int Version { get; set; } = 1;
    public string App { get; set; } = "LocalChromeStore";
    public DateTimeOffset ExportedAt { get; set; } = DateTimeOffset.UtcNow;
    public EnvironmentSettingsSnapshot Settings { get; set; } = new();
    public List<EnvironmentExtensionSnapshot> Extensions { get; set; } = new();
}

public sealed class EnvironmentSettingsSnapshot
{
    public string GitHubUser { get; set; } = "SysAdminDoc";
    public bool UseTopicFilter { get; set; }
    public string TopicFilter { get; set; } = "chrome-extension";
    public List<string> ExtraOwners { get; set; } = new();
    public string? LaunchUrl { get; set; }
    public bool LaunchWithTemporaryProfile { get; set; }
}

public sealed class EnvironmentExtensionSnapshot
{
    public required string RepoOwner { get; set; }
    public required string RepoName { get; set; }
    public required string Version { get; set; }
    public string? DisplayName { get; set; }
    public string? RepoUrl { get; set; }
    public int? ManifestVersionNumber { get; set; }
    public ExtensionFramework Framework { get; set; } = ExtensionFramework.Unknown;
    public bool ChecksumVerified { get; set; }
    public string? ChecksumAlgorithm { get; set; }
    public string? ChecksumValue { get; set; }
    public List<string> Permissions { get; set; } = new();
    public List<string> OptionalPermissions { get; set; } = new();
    public List<string> HostPermissions { get; set; } = new();
    public List<string> OptionalHostPermissions { get; set; } = new();

    public string Key => $"{RepoOwner}/{RepoName}";
}
