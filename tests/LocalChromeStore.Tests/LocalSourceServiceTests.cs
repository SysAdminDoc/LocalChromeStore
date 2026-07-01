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

    [Theory]
    [InlineData(".output/chrome-mv3")]
    [InlineData("build/chrome-mv3-prod")]
    [InlineData("dist")]
    [InlineData("extension")]
    [InlineData("public")]
    public void DiscoverOne_ResolvesKnownFrameworkOutputFolder(string relativeOutput)
    {
        var root = Path.Combine(Path.GetTempPath(), "LocalChromeStore.Tests", Guid.NewGuid().ToString("N"));
        var project = Path.Combine(root, "Wxt Project");
        var output = Path.Combine(project, relativeOutput.Replace('/', Path.DirectorySeparatorChar));
        try
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(project, "package.json"),
                """
                {
                  "devDependencies": {
                    "wxt": "latest"
                  }
                }
                """);
            File.WriteAllText(Path.Combine(output, "manifest.json"),
                """
                {
                  "manifest_version": 3,
                  "name": "Built Extension",
                  "version": "0.0.5"
                }
                """);

            var resolution = LocalSourceService.ResolveSourceFolder(project);
            var info = LocalSourceService.DiscoverOne(project);

            Assert.NotNull(resolution);
            Assert.Equal(Path.GetFullPath(project), resolution.ConfiguredPath);
            Assert.Equal(Path.GetFullPath(output), resolution.ExtensionRoot);
            Assert.Equal(relativeOutput.Replace('/', Path.DirectorySeparatorChar), resolution.RelativePath);
            Assert.NotNull(info);
            Assert.Equal(Path.GetFullPath(project), info.RepoUrl);
            Assert.Equal(Path.GetFullPath(output), info.LocalSourcePath);
            Assert.Equal(Path.Combine(Path.GetFullPath(output), "manifest.json"), info.ManifestSourcePath);
            Assert.Equal("Built Extension", info.DisplayName);
            Assert.Equal("0.0.5", info.DisplayVersion);
            Assert.Equal(ExtensionFramework.Wxt, info.Framework);
            Assert.Equal("package.json references WXT", info.FrameworkEvidence);
            Assert.Equal($"Wxt Project / {relativeOutput}", info.AssetName);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
