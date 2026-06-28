using LocalChromeStore.Models;
using LocalChromeStore.Services;
using LocalChromeStore.Services.Crx;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class PolicyPackageServiceTests
{
    [Fact]
    public void Prepare_CreatesStableSignedCrxAndGeneratedUpdateXml()
    {
        using var temp = TempProject.Create();
        var installed = temp.CreateInstalledExtension("1.2.3");
        var service = new PolicyPackageService(temp.Settings);
        var crxUrl = new Uri("https://updates.example.test/sample-1.2.3.crx");
        var updateUrl = new Uri("https://updates.example.test/update.xml");

        var first = service.Prepare(new PolicyPackageRequest(installed, crxUrl, updateUrl));
        var second = service.Prepare(new PolicyPackageRequest(installed, crxUrl, updateUrl));

        Assert.True(File.Exists(first.PrivateKeyPath));
        Assert.True(File.Exists(first.CrxPath));
        Assert.True(File.Exists(first.UpdateXmlPath));
        Assert.Equal("1.2.3", first.ManifestVersion);
        Assert.Equal(first.Package.ExtensionId, second.Package.ExtensionId);
        Assert.Equal(first.PrivateKeyPath, second.PrivateKeyPath);

        var verification = Crx3PackageService.VerifyPackage(File.ReadAllBytes(first.CrxPath));
        Assert.True(verification.SignatureValid);
        Assert.True(verification.ExtensionIdMatchesPublicKey);
        Assert.Equal(first.Package.ExtensionId, verification.ExtensionId);

        var updateXml = File.ReadAllText(first.UpdateXmlPath);
        Assert.Contains($"appid=\"{first.Package.ExtensionId}\"", updateXml);
        Assert.Contains($"codebase=\"{crxUrl.AbsoluteUri}\"", updateXml);
        Assert.Contains("version=\"1.2.3\"", updateXml);
        Assert.True(service.TryDeriveExtensionId(installed, out var derivedId, out _));
        Assert.Equal(first.Package.ExtensionId, derivedId);
    }

    [Fact]
    public void Prepare_CopiesSelectedUpdateXmlIntoPolicyPackage()
    {
        using var temp = TempProject.Create();
        var installed = temp.CreateInstalledExtension("2.0.0");
        var service = new PolicyPackageService(temp.Settings);
        var selectedUpdateXml = Path.Combine(temp.Root, "selected-update.xml");
        File.WriteAllText(selectedUpdateXml, "<gupdate protocol=\"2.0\" />");

        var result = service.Prepare(new PolicyPackageRequest(
            installed,
            new Uri("https://updates.example.test/sample.crx"),
            new Uri("https://updates.example.test/update.xml"),
            selectedUpdateXml));

        Assert.Equal("<gupdate protocol=\"2.0\" />", File.ReadAllText(result.UpdateXmlPath));
        Assert.Equal(Path.Combine(result.PackageDirectory, "update.xml"), result.UpdateXmlPath);
    }

    private sealed class TempProject : IDisposable
    {
        public string Root { get; }
        public SettingsService Settings { get; }

        private TempProject(string root)
        {
            Root = root;
            Settings = new SettingsService(
                appDataRoot: Path.Combine(root, "roaming"),
                localAppDataRoot: Path.Combine(root, "local"));
        }

        public static TempProject Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "lcs-policy-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TempProject(root);
        }

        public InstalledExtension CreateInstalledExtension(string version)
        {
            var installRoot = Path.Combine(Root, "extension");
            Directory.CreateDirectory(installRoot);
            var manifestPath = Path.Combine(installRoot, "manifest.json");
            File.WriteAllText(
                manifestPath,
                $$"""
                {
                  "manifest_version": 3,
                  "name": "Sample",
                  "version": "{{version}}"
                }
                """);
            File.WriteAllText(Path.Combine(installRoot, "worker.js"), "chrome.runtime.onInstalled.addListener(() => {});");

            return new InstalledExtension
            {
                RepoOwner = "owner",
                RepoName = "sample",
                Version = "v" + version,
                InstallPath = installRoot,
                ManifestPath = manifestPath,
                InstalledAt = DateTimeOffset.UtcNow,
                DisplayName = "Sample"
            };
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch { }
        }
    }
}
