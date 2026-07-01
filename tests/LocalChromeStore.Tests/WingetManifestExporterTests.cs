using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class WingetManifestExporterTests
{
    [Fact]
    public void Generate_ProducesValidYaml()
    {
        var yaml = WingetManifestExporter.Generate(
            packageId: "SysAdminDoc.TestApp",
            version: "1.2.3",
            publisher: "SysAdminDoc",
            packageName: "TestApp",
            description: "A test application.",
            license: "MIT",
            releaseUrl: "https://github.com/SysAdminDoc/TestApp/releases/tag/v1.2.3",
            assetUrl: "https://github.com/SysAdminDoc/TestApp/releases/download/v1.2.3/TestApp.zip",
            sha256: "abcdef1234567890abcdef1234567890abcdef1234567890abcdef1234567890");

        Assert.Contains("PackageIdentifier: SysAdminDoc.TestApp", yaml);
        Assert.Contains("PackageVersion: 1.2.3", yaml);
        Assert.Contains("Publisher: SysAdminDoc", yaml);
        Assert.Contains("InstallerType: zip", yaml);
        Assert.Contains("InstallerUrl: https://github.com/SysAdminDoc/TestApp/releases/download/v1.2.3/TestApp.zip", yaml);
        Assert.Contains("InstallerSha256: ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890ABCDEF1234567890", yaml);
        Assert.Contains("ManifestVersion: 1.6.0", yaml);
    }

    [Fact]
    public void Generate_OmitsSha256WhenNull()
    {
        var yaml = WingetManifestExporter.Generate(
            "Id", "1.0", "Pub", "Name", "Desc", "MIT",
            "https://example.com/release", "https://example.com/asset.zip");

        Assert.DoesNotContain("InstallerSha256", yaml);
    }

    [Fact]
    public void GenerateForLocalChromeStore_ProducesCorrectId()
    {
        var yaml = WingetManifestExporter.GenerateForLocalChromeStore("0.4.0",
            "https://github.com/SysAdminDoc/LocalChromeStore/releases/download/v0.4.0/LocalChromeStore-v0.4.0-win-x64.zip");

        Assert.Contains("PackageIdentifier: SysAdminDoc.LocalChromeStore", yaml);
        Assert.Contains("PackageVersion: 0.4.0", yaml);
        Assert.Contains("License: MIT", yaml);
    }
}
