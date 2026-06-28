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
}
