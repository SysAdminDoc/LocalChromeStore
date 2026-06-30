using System.IO;
using LocalChromeStore.Models;
using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class ExtensionServiceTests
{
    private const string Hash = "d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2d2";

    [Theory]
    [InlineData(Hash, "LocalChromeStore.zip", Hash)]
    [InlineData(Hash + "  LocalChromeStore.zip", "LocalChromeStore.zip", Hash)]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  other.zip\n" + Hash + " *LocalChromeStore.zip", "LocalChromeStore.zip", Hash)]
    public void ParseExpectedSha256_AcceptsCommonSidecarShapes(string sidecar, string assetName, string expected)
    {
        Assert.Equal(expected, ExtensionService.ParseExpectedSha256(sidecar, assetName));
    }

    [Fact]
    public void ParseExpectedSha256_ReturnsNullWhenNoHashIsPresent()
    {
        Assert.Null(ExtensionService.ParseExpectedSha256("not a checksum", "LocalChromeStore.zip"));
    }

    [Theory]
    [InlineData("sha256:" + Hash)]
    [InlineData("SHA256:" + Hash)]
    [InlineData(" sha256:" + Hash + " ")]
    public void TryParseSha256Digest_AcceptsGitHubApiDigest(string digest)
    {
        Assert.True(ExtensionService.TryParseSha256Digest(digest, out var parsed));
        Assert.Equal(Hash, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(Hash)]
    [InlineData("sha512:" + Hash)]
    [InlineData("sha256:not-hex")]
    public void TryParseSha256Digest_RejectsUnsupportedOrInvalidDigest(string? digest)
    {
        Assert.False(ExtensionService.TryParseSha256Digest(digest, out var parsed));
        Assert.Equal(string.Empty, parsed);
    }

    [Fact]
    public async Task InstallAsync_LocalSource_LinksSourceFolderWithoutCopying()
    {
        var root = Path.Combine(Path.GetTempPath(), "LocalChromeStore.Tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        try
        {
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "manifest.json"),
                """
                {
                  "manifest_version": 3,
                  "name": "Linked Extension",
                  "version": "2.0.0",
                  "permissions": ["storage"]
                }
                """);

            var settings = new SettingsService(appDataRoot: Path.Combine(root, "appdata"), localAppDataRoot: Path.Combine(root, "localdata"));
            var github = new GitHubService(settings);
            var extensions = new ExtensionService(settings, github);
            var info = LocalSourceService.DiscoverOne(source)!;

            var installed = await extensions.InstallAsync(info);
            var manifest = settings.LoadManifest();

            Assert.Equal(Path.GetFullPath(source), installed.InstallPath);
            Assert.Equal(Path.Combine(Path.GetFullPath(source), "manifest.json"), installed.ManifestPath);
            Assert.Equal("local-source", installed.ChecksumSource);
            Assert.False(installed.ChecksumVerified);
            Assert.Equal("Linked Extension", installed.DisplayName);
            Assert.Equal(3, installed.ManifestVersionNumber);
            Assert.Equal(["storage"], installed.Permissions);
            Assert.Single(manifest.Extensions);
            Assert.Equal(Path.GetFullPath(source), manifest.Extensions[0].InstallPath);
            Assert.False(Directory.Exists(Path.Combine(settings.ExtensionsRoot, "local", info.RepoName)));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
