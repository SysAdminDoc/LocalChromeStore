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

    private static ManifestRiskViewModel ViewModel(string? checksumUrl = null, string? assetDigest = null) =>
        new(new ExtensionInfo
        {
            RepoOwner = "owner",
            RepoName = "repo",
            RepoUrl = "https://github.com/owner/repo",
            AssetName = "repo.zip",
            AssetUrl = "https://example.test/repo.zip",
            AssetDigest = assetDigest,
            ChecksumUrl = checksumUrl,
            ChecksumName = checksumUrl is null ? null : "repo.zip.sha256.txt"
        }, onInstall: () => { }, onClose: () => { });
}
