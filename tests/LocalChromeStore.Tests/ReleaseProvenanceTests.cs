using LocalChromeStore.Models;
using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class ReleaseProvenanceTests
{
    [Fact]
    public void CompareAssetSnapshot_DetectsReuploadedAsset()
    {
        var info = Info("sha256:" + new string('b', 64), size: 2048, updatedAt: DateTimeOffset.UnixEpoch.AddMinutes(2));
        var installed = Installed("sha256:" + new string('a', 64), size: 1024, updatedAt: DateTimeOffset.UnixEpoch);

        var comparison = ReleaseProvenance.CompareAssetSnapshot(info, installed);

        Assert.True(comparison.CanCompare);
        Assert.True(comparison.Changed);
        Assert.Contains("digest changed", comparison.Reasons);
        Assert.Contains("size changed", comparison.Reasons);
        Assert.Contains("upload timestamp changed", comparison.Reasons);
        Assert.Contains("changed since install", ReleaseProvenance.CardSummary(info, installed));
    }

    [Fact]
    public void CompareAssetSnapshot_ReportsUnavailableForLegacyInstallSnapshot()
    {
        var info = Info("sha256:" + new string('b', 64), size: 2048, updatedAt: DateTimeOffset.UnixEpoch.AddMinutes(2));
        var installed = new InstalledExtension
        {
            RepoOwner = "owner",
            RepoName = "repo",
            Version = "1.0.0",
            InstallPath = "C:\\ext",
            ManifestPath = "C:\\ext\\manifest.json"
        };

        var comparison = ReleaseProvenance.CompareAssetSnapshot(info, installed);

        Assert.False(comparison.CanCompare);
        Assert.False(comparison.Changed);
        Assert.Contains("install snapshot unavailable", ReleaseProvenance.CardSummary(info, installed));
    }

    private static ExtensionInfo Info(string digest, long size, DateTimeOffset updatedAt) => new()
    {
        RepoOwner = "owner",
        RepoName = "repo",
        RepoUrl = "https://github.com/owner/repo",
        AssetUrl = "https://example.test/repo.zip",
        AssetName = "repo.zip",
        AssetDigest = digest,
        AssetSizeBytes = size,
        AssetId = 10,
        AssetUpdatedAt = updatedAt
    };

    private static InstalledExtension Installed(string digest, long size, DateTimeOffset updatedAt) => new()
    {
        RepoOwner = "owner",
        RepoName = "repo",
        Version = "1.0.0",
        InstallPath = "C:\\ext",
        ManifestPath = "C:\\ext\\manifest.json",
        AssetName = "repo.zip",
        AssetDigest = digest,
        AssetSizeBytes = size,
        AssetId = 10,
        AssetUpdatedAt = updatedAt
    };
}
