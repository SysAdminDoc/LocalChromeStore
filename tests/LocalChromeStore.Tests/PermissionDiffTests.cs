using LocalChromeStore.Models;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class PermissionDiffTests
{
    [Fact]
    public void Compare_DetectsAddedRequiredAndHostPermissions()
    {
        var installed = Installed(
            permissions: ["storage"],
            hostPermissions: ["https://example.com/*"]);
        var incoming = Incoming(
            permissions: ["storage", "tabs"],
            hostPermissions: ["https://example.com/*", "<all_urls>"]);

        var diff = PermissionDiff.Compare(installed, incoming);

        Assert.True(diff.HasAdditions);
        Assert.Contains(diff.Added, i => i.Kind == PermissionDiffKind.RequiredPermission && i.Value == "tabs");
        Assert.Contains(diff.Added, i => i.Kind == PermissionDiffKind.HostPermission && i.Value == "<all_urls>" && i.Risk == PermissionRisk.High);
    }

    [Fact]
    public void Compare_IgnoresCaseWhitespaceAndDuplicateValues()
    {
        var installed = Installed(
            permissions: [" Storage "],
            hostPermissions: ["HTTPS://EXAMPLE.COM/*"]);
        var incoming = Incoming(
            permissions: ["storage", "storage"],
            hostPermissions: ["https://example.com/*", " https://example.com/* "]);

        var diff = PermissionDiff.Compare(installed, incoming);

        Assert.False(diff.HasAdditions);
        Assert.False(diff.HasRemovals);
    }

    [Fact]
    public void Compare_TreatsOptionalToRequiredAsAnExpansion()
    {
        var installed = Installed(optionalPermissions: ["tabs"]);
        var incoming = Incoming(permissions: ["tabs"]);

        var diff = PermissionDiff.Compare(installed, incoming);

        Assert.True(diff.HasAdditions);
        Assert.Contains(diff.Added, i => i.Kind == PermissionDiffKind.RequiredPermission && i.Value == "tabs");
    }

    [Fact]
    public void Compare_DoesNotTreatRequiredToOptionalAsAnExpansion()
    {
        var installed = Installed(permissions: ["tabs"]);
        var incoming = Incoming(optionalPermissions: ["tabs"]);

        var diff = PermissionDiff.Compare(installed, incoming);

        Assert.False(diff.HasAdditions);
        Assert.False(diff.HasRemovals);
    }

    [Fact]
    public void Compare_DetectsRemovedPermissions()
    {
        var installed = Installed(
            permissions: ["tabs"],
            optionalHostPermissions: ["https://old.example/*"]);
        var incoming = Incoming(permissions: []);

        var diff = PermissionDiff.Compare(installed, incoming);

        Assert.False(diff.HasAdditions);
        Assert.True(diff.HasRemovals);
        Assert.Contains(diff.Removed, i => i.Kind == PermissionDiffKind.RequiredPermission && i.Value == "tabs");
        Assert.Contains(diff.Removed, i => i.Kind == PermissionDiffKind.OptionalHostPermission && i.Value == "https://old.example/*");
    }

    [Fact]
    public void Compare_EnvironmentSnapshotDetectsCurrentCatalogExpansion()
    {
        var snapshot = new EnvironmentExtensionSnapshot
        {
            RepoOwner = "owner",
            RepoName = "repo",
            Version = "1.0.0",
            Permissions = ["storage"],
            OptionalHostPermissions = ["https://old.example/*"]
        };
        var incoming = Incoming(
            permissions: ["storage", "history"],
            optionalHostPermissions: ["https://old.example/*", "https://new.example/*"]);

        var diff = PermissionDiff.Compare(snapshot, incoming);

        Assert.True(diff.HasAdditions);
        Assert.Contains(diff.Added, i => i.Kind == PermissionDiffKind.RequiredPermission && i.Value == "history");
        Assert.Contains(diff.Added, i => i.Kind == PermissionDiffKind.OptionalHostPermission && i.Value == "https://new.example/*");
    }

    private static InstalledExtension Installed(
        string[]? permissions = null,
        string[]? optionalPermissions = null,
        string[]? hostPermissions = null,
        string[]? optionalHostPermissions = null) => new()
        {
            RepoOwner = "owner",
            RepoName = "repo",
            Version = "1.0.0",
            InstallPath = @"C:\ext",
            ManifestPath = @"C:\ext\manifest.json",
            InstalledAt = DateTimeOffset.UtcNow,
            Permissions = permissions?.ToList() ?? [],
            OptionalPermissions = optionalPermissions?.ToList() ?? [],
            HostPermissions = hostPermissions?.ToList() ?? [],
            OptionalHostPermissions = optionalHostPermissions?.ToList() ?? []
        };

    private static ExtensionInfo Incoming(
        string[]? permissions = null,
        string[]? optionalPermissions = null,
        string[]? hostPermissions = null,
        string[]? optionalHostPermissions = null) => new()
        {
            RepoOwner = "owner",
            RepoName = "repo",
            RepoUrl = "https://github.com/owner/repo",
            ManifestVersion = "2.0.0",
            Permissions = permissions?.ToList() ?? [],
            OptionalPermissions = optionalPermissions?.ToList() ?? [],
            HostPermissions = hostPermissions?.ToList() ?? [],
            OptionalHostPermissions = optionalHostPermissions?.ToList() ?? []
        };
}
