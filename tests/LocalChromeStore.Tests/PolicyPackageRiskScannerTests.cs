using LocalChromeStore.Models;
using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class PolicyPackageRiskScannerTests
{
    [Fact]
    public void Scan_SafeMv3Package_AllowsPolicyInstall()
    {
        using var temp = RiskPackage.Create("""
        {
          "manifest_version": 3,
          "name": "Safe",
          "version": "1.0.0",
          "background": { "service_worker": "worker.js" }
        }
        """);
        temp.Write("worker.js", "chrome.runtime.onInstalled.addListener(() => {});");
        var scanner = new PolicyPackageRiskScanner([]);

        var report = scanner.Scan(temp.Installed, ["abcdefghijklmnopabcdefghijklmnop"]);

        Assert.False(report.BlocksPolicyInstall);
        Assert.Empty(report.Findings);
        Assert.Equal("no blocking findings", report.Summary);
        Assert.Equal(3, report.ManifestVersion);
        Assert.Equal("abcdefghijklmnopabcdefghijklmnop", Assert.Single(report.DerivedExtensionIds));
    }

    [Fact]
    public void Scan_ManifestV2_BlocksPolicyInstall()
    {
        using var temp = RiskPackage.Create("""
        {
          "manifest_version": 2,
          "name": "MV2",
          "version": "1.0.0",
          "background": { "scripts": ["background.js"] }
        }
        """);
        var scanner = new PolicyPackageRiskScanner([]);

        var report = scanner.Scan(temp.Installed);

        Assert.True(report.BlocksPolicyInstall);
        Assert.Contains(report.Findings, f =>
            f.Severity == PolicyPackageRiskSeverity.Fail
            && f.Category == "Manifest"
            && f.Detail.Contains("Manifest V2", StringComparison.Ordinal));
    }

    [Fact]
    public void Scan_RemoteExecutableCodeAndEval_BlockPolicyInstall()
    {
        using var temp = RiskPackage.Create("""
        {
          "manifest_version": 3,
          "name": "Remote code",
          "version": "1.0.0",
          "background": { "service_worker": "worker.js" }
        }
        """);
        temp.Write("worker.js", """
        importScripts("https://cdn.example.test/runtime.js");
        const run = new Function("return 1");
        """);
        temp.Write("popup.html", """<script src="https://cdn.example.test/popup.js"></script>""");
        var scanner = new PolicyPackageRiskScanner([]);

        var report = scanner.Scan(temp.Installed);

        Assert.True(report.BlocksPolicyInstall);
        Assert.Contains(report.Findings, f => f.Category == "Remote executable code" && f.RelativePath == "worker.js");
        Assert.Contains(report.Findings, f => f.Category == "Remote executable code" && f.RelativePath == "popup.html");
        Assert.Contains(report.Findings, f => f.Category == "Dynamic code execution" && f.RelativePath == "worker.js");
    }

    [Fact]
    public void Scan_DangerousCsp_BlocksPolicyInstallAndWarnsOnWasmEval()
    {
        using var temp = RiskPackage.Create("""
        {
          "manifest_version": 3,
          "name": "CSP",
          "version": "1.0.0",
          "content_security_policy": {
            "extension_pages": "script-src 'self' 'unsafe-eval' https://cdn.example.test; object-src 'self'",
            "sandbox": "script-src 'self' 'wasm-unsafe-eval'"
          }
        }
        """);
        var scanner = new PolicyPackageRiskScanner([]);

        var report = scanner.Scan(temp.Installed);

        Assert.True(report.BlocksPolicyInstall);
        Assert.Contains(report.Findings, f => f.Category == "Content security policy" && f.Severity == PolicyPackageRiskSeverity.Fail);
        Assert.Contains(report.Findings, f => f.Category == "Content security policy" && f.Severity == PolicyPackageRiskSeverity.Warning);
    }

    [Fact]
    public void Scan_KnownMaliciousDerivedId_BlocksPolicyInstall()
    {
        using var temp = RiskPackage.Create("""
        {
          "manifest_version": 3,
          "name": "Known bad",
          "version": "1.0.0"
        }
        """);
        const string maliciousId = "abcdefghijklmnopabcdefghijklmnop";
        var scanner = new PolicyPackageRiskScanner([maliciousId]);

        var report = scanner.Scan(temp.Installed, [maliciousId]);

        Assert.True(report.BlocksPolicyInstall);
        Assert.Contains(report.Findings, f =>
            f.Category == "Known malicious extension ID"
            && f.Detail.Contains(maliciousId, StringComparison.Ordinal));
    }

    [Fact]
    public void Scan_OptionalFeedFile_BlocksKnownDerivedId()
    {
        using var temp = RiskPackage.Create("""
        {
          "manifest_version": 3,
          "name": "Known bad from file",
          "version": "1.0.0"
        }
        """);
        var feed = Path.Combine(temp.Root, "ids.txt");
        File.WriteAllText(feed, "# comment\nponmlkjihgfedcbaponmlkjihgfedcba\n");
        var scanner = new PolicyPackageRiskScanner([], [feed]);

        var report = scanner.Scan(temp.Installed, ["ponmlkjihgfedcbaponmlkjihgfedcba"]);

        Assert.True(report.BlocksPolicyInstall);
        Assert.Contains(report.Findings, f => f.Category == "Known malicious extension ID");
    }

    private sealed class RiskPackage : IDisposable
    {
        public string Root { get; }
        public string InstallRoot { get; }
        public InstalledExtension Installed { get; }

        private RiskPackage(string root, string manifest)
        {
            Root = root;
            InstallRoot = Path.Combine(root, "extension");
            Directory.CreateDirectory(InstallRoot);
            var manifestPath = Path.Combine(InstallRoot, "manifest.json");
            File.WriteAllText(manifestPath, manifest);
            Installed = new InstalledExtension
            {
                RepoOwner = "owner",
                RepoName = "sample",
                Version = "1.0.0",
                InstallPath = InstallRoot,
                ManifestPath = manifestPath,
                InstalledAt = DateTimeOffset.UtcNow,
                DisplayName = "Sample"
            };
        }

        public static RiskPackage Create(string manifest)
        {
            var root = Path.Combine(Path.GetTempPath(), "lcs-risk-" + Guid.NewGuid().ToString("N"));
            return new RiskPackage(root, manifest);
        }

        public void Write(string relativePath, string contents)
        {
            var path = Path.Combine(InstallRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, contents);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch { }
        }
    }
}
