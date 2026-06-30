using LocalChromeStore.Models;
using LocalChromeStore.ViewModels;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class ManifestRiskViewModelTests
{
    [Fact]
    public void ChecksumLabel_ReportsSidecarDigestAndUnverifiedStates()
    {
        Assert.Contains("SHA256 sidecar present", ViewModel(checksumUrl: "https://example.test/a.zip.sha256.txt").ChecksumLabel);
        Assert.Contains("GitHub API SHA256 digest present", ViewModel(assetDigest: "sha256:" + new string('a', 64)).ChecksumLabel);
        Assert.Contains("no SHA256 sidecar or GitHub API digest", ViewModel().ChecksumLabel);
    }

    [Fact]
    public void ReleaseProvenanceLabel_ReportsAssetUploadAndChangeStatus()
    {
        var installed = new InstalledExtension
        {
            RepoOwner = "owner",
            RepoName = "repo",
            Version = "1.0.0",
            InstallPath = "C:\\ext",
            ManifestPath = "C:\\ext\\manifest.json",
            AssetName = "repo.zip",
            AssetDigest = "sha256:" + new string('a', 64),
            AssetSizeBytes = 100,
            AssetUpdatedAt = DateTimeOffset.UnixEpoch
        };

        var vm = ViewModel(
            assetDigest: "sha256:" + new string('b', 64),
            installed: installed);

        Assert.Contains("Provenance:", vm.ReleaseProvenanceLabel);
        Assert.Contains("changed since install", vm.ReleaseProvenanceLabel);
        Assert.Contains("Change reasons:", vm.ReleaseProvenanceTooltip);
    }

    private static ManifestRiskViewModel ViewModel(string? checksumUrl = null, string? assetDigest = null, InstalledExtension? installed = null) =>
        new(new ExtensionInfo
        {
            RepoOwner = "owner",
            RepoName = "repo",
            RepoUrl = "https://github.com/owner/repo",
            AssetName = "repo.zip",
            AssetUrl = "https://example.test/repo.zip",
            AssetDigest = assetDigest,
            AssetSizeBytes = 200,
            AssetUpdatedAt = DateTimeOffset.UnixEpoch.AddMinutes(1),
            ChecksumUrl = checksumUrl,
            ChecksumName = checksumUrl is null ? null : "repo.zip.sha256.txt"
        }, onInstall: () => { }, onClose: () => { }, installed: installed);
}
