using System.IO;
using LocalChromeStore.Models;
using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class LocalSourceServiceTests
{
    [Fact]
    public void DiscoverOne_ReturnsExtensionInfoFromManifestRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "LocalChromeStore.Tests", Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "Local Dev Extension");
        try
        {
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "manifest.json"),
                """
                {
                  "manifest_version": 3,
                  "name": "Local Dev Extension",
                  "version": "1.2.3",
                  "description": "Local test",
                  "permissions": ["storage"],
                  "host_permissions": ["https://example.test/*"]
                }
                """);

            var info = LocalSourceService.DiscoverOne(source);

            Assert.NotNull(info);
            Assert.Equal("local", info.RepoOwner);
            Assert.StartsWith("local-dev-extension-", info.RepoName);
            Assert.Equal(Path.GetFullPath(source), info.LocalSourcePath);
            Assert.Equal(DiscoverySource.LocalSourceFolder, info.DiscoverySource);
            Assert.Equal(AssetKind.LocalFolder, info.AssetKind);
            Assert.Equal("Local Dev Extension", info.DisplayName);
            Assert.Equal("1.2.3", info.DisplayVersion);
            Assert.Equal(3, info.ManifestVersionNumber);
            Assert.Equal(ExtensionFramework.PlainMv3, info.Framework);
            Assert.Equal(["storage"], info.Permissions);
            Assert.Equal(["https://example.test/*"], info.HostPermissions);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
