using System.Text.Json;
using LocalChromeStore.Models;
using LocalChromeStore.Services;
using Octokit;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class GitHubServiceTests
{
    [Theory]
    [InlineData(AccountType.Organization, GitHubService.OwnerListing.Organization)]
    [InlineData(AccountType.User, GitHubService.OwnerListing.User)]
    [InlineData(AccountType.Bot, GitHubService.OwnerListing.User)]
    [InlineData(null, GitHubService.OwnerListing.User)]
    public void ResolveOwnerListing_UsesOrgListingForOrganizationsOnly(AccountType? type, GitHubService.OwnerListing expected)
    {
        Assert.Equal(expected, GitHubService.ResolveOwnerListing(type));
    }

    [Fact]
    public void AppVersion_ReflectsAssemblyVersion_NotHardcodedZeroOne()
    {
        // The footer/UA drift bug shipped "0.1.0" forever; assert it tracks the real assembly.
        var expected = typeof(GitHubService).Assembly.GetName().Version?.ToString(3);
        Assert.Equal(expected, GitHubService.AppVersion);
        Assert.NotEqual("0.1.0", GitHubService.AppVersion);
    }

    [Theory]
    [InlineData("manifest.json")]
    [InlineData(".output/chrome-mv3/manifest.json")]
    [InlineData("build/chrome-mv3-prod/manifest.json")]
    [InlineData("dist/manifest.json")]
    [InlineData("extension/manifest.json")]
    [InlineData("public/manifest.json")]
    public void ManifestProbePaths_IncludeCommonFrameworkOutputs(string path)
    {
        Assert.Contains(path, GitHubService.ManifestProbePaths);
    }

    [Theory]
    [InlineData("0.3.0", "v0.4.0", true)]   // newer tag, leading v tolerated
    [InlineData("0.3.0", "0.4.0", true)]    // newer tag, no v
    [InlineData("0.3.0", "v0.3.0", false)]  // same version, format-insensitive
    [InlineData("0.3.0", "v0.2.9", false)]  // older release never prompts
    [InlineData("0.3.0", "v0.3.0-beta", false)] // prerelease of current ranks below it
    public void EvaluateSelfUpdate_FlagsOnlyStrictlyNewerReleases(string current, string tag, bool expected)
    {
        var info = GitHubService.EvaluateSelfUpdate(current, tag, "https://github.com/SysAdminDoc/LocalChromeStore/releases/tag/" + tag);
        Assert.Equal(expected, info.UpdateAvailable);
        Assert.Equal(tag, info.LatestVersion);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EvaluateSelfUpdate_MissingTag_IsNone(string? tag)
    {
        Assert.Same(SelfUpdateInfo.None, GitHubService.EvaluateSelfUpdate("0.3.0", tag, "url"));
    }

    [Fact]
    public void TryFindReleaseAssetDigest_ReadsMatchingAssetDigest()
    {
        using var doc = JsonDocument.Parse(
            """
            [
              { "name": "other.zip", "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" },
              {
                "id": 12345,
                "name": "LocalChromeStore.zip",
                "digest": "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                "size": 2048,
                "content_type": "application/zip",
                "uploader": { "login": "SysAdminDoc" },
                "created_at": "2026-06-29T18:00:00Z",
                "updated_at": "2026-06-29T18:05:00Z",
                "download_count": 7
              }
            ]
            """);

        var digest = GitHubService.TryFindReleaseAssetDigest(doc.RootElement, "localchromestore.zip");
        var provenance = GitHubService.TryFindReleaseAssetProvenance(doc.RootElement, "localchromestore.zip");

        Assert.Equal("sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", digest);
        Assert.NotNull(provenance);
        Assert.Equal(12345, provenance.Id);
        Assert.Equal(2048, provenance.SizeBytes);
        Assert.Equal("application/zip", provenance.ContentType);
        Assert.Equal("SysAdminDoc", provenance.Uploader);
        Assert.Equal(DateTimeOffset.Parse("2026-06-29T18:00:00Z"), provenance.CreatedAt);
        Assert.Equal(DateTimeOffset.Parse("2026-06-29T18:05:00Z"), provenance.UpdatedAt);
        Assert.Equal(7, provenance.DownloadCount);
    }

    [Fact]
    public void TryFindReleaseAssetDigest_MissingOrDigestlessAsset_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("""[{ "name": "LocalChromeStore.zip" }]""");

        Assert.Null(GitHubService.TryFindReleaseAssetDigest(doc.RootElement, "LocalChromeStore.zip"));
        Assert.Null(GitHubService.TryFindReleaseAssetDigest(doc.RootElement, "missing.zip"));
    }
}
