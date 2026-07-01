using System.Text.Json;
using LocalChromeStore.Models;
using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class ManifestFixtureTests : IDisposable
{
    private readonly string _fixturesDir;
    private readonly string _tempDir;

    public ManifestFixtureTests()
    {
        _fixturesDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Fixtures");
        _tempDir = Path.Combine(Path.GetTempPath(), "LocalChromeStore.Tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private ExtensionInfo DiscoverFixture(string fixtureName)
    {
        var dir = Path.Combine(_tempDir, Path.GetFileNameWithoutExtension(fixtureName));
        Directory.CreateDirectory(dir);
        File.Copy(Path.Combine(_fixturesDir, fixtureName), Path.Combine(dir, "manifest.json"));
        var info = LocalSourceService.DiscoverOne(dir);
        Assert.NotNull(info);
        return info!;
    }

    [Fact]
    public void Mv3Full_ParsesAllFields()
    {
        var info = DiscoverFixture("mv3-full.json");

        Assert.Equal("Full MV3 Extension", info.ManifestName);
        Assert.Equal("2.1.0", info.ManifestVersion);
        Assert.Equal("A complete MV3 manifest exercising all parsed fields.", info.ManifestDescription);
        Assert.Equal(3, info.ManifestVersionNumber);

        Assert.Equal(5, info.Permissions.Count);
        Assert.Contains("storage", info.Permissions);
        Assert.Contains("scripting", info.Permissions);
        Assert.Equal(2, info.OptionalPermissions.Count);
        Assert.Equal(2, info.HostPermissions.Count);
        Assert.Single(info.OptionalHostPermissions);
        Assert.Equal("<all_urls>", info.OptionalHostPermissions[0]);

        Assert.Equal("options.html", info.OptionsPage);
        Assert.Equal("devtools.html", info.DevtoolsPage);
    }

    [Fact]
    public void Mv2Legacy_PromotesHostPermissions()
    {
        var info = DiscoverFixture("mv2-legacy.json");

        Assert.Equal(2, info.ManifestVersionNumber);
        Assert.Equal("Legacy MV2 Extension", info.ManifestName);

        // MV2 host-like patterns should be promoted from permissions to HostPermissions
        Assert.DoesNotContain("https://*.example.com/*", info.Permissions);
        Assert.DoesNotContain("http://localhost/*", info.Permissions);
        Assert.Contains("https://*.example.com/*", info.HostPermissions);
        Assert.Contains("http://localhost/*", info.HostPermissions);

        // Non-host permissions stay
        Assert.Contains("tabs", info.Permissions);
        Assert.Contains("storage", info.Permissions);

        Assert.Equal("options.html", info.OptionsPage);
        Assert.Null(info.DevtoolsPage);
    }

    [Fact]
    public void Mv3Minimal_HandlesMissingFields()
    {
        var info = DiscoverFixture("mv3-minimal.json");

        Assert.Equal("Minimal MV3", info.ManifestName);
        Assert.Equal("0.0.1", info.ManifestVersion);
        Assert.Null(info.ManifestDescription);
        Assert.Equal(3, info.ManifestVersionNumber);

        Assert.Empty(info.Permissions);
        Assert.Empty(info.OptionalPermissions);
        Assert.Empty(info.HostPermissions);
        Assert.Empty(info.OptionalHostPermissions);
        Assert.Null(info.OptionsPage);
        Assert.Null(info.DevtoolsPage);
    }

    [Fact]
    public void HighRisk_IdentifiesHighRiskPermissions()
    {
        var info = DiscoverFixture("high-risk.json");

        Assert.Equal(3, info.ManifestVersionNumber);
        Assert.Contains("debugger", info.Permissions);
        Assert.Contains("management", info.Permissions);
        Assert.Contains("proxy", info.Permissions);

        // Host permissions
        Assert.Contains("<all_urls>", info.HostPermissions);

        // Aggregate risk should be High
        var risk = PermissionCatalog.Aggregate(
            info.Permissions.Select(p => PermissionCatalog.Describe(p))
                .Concat(info.HostPermissions.Select(h => PermissionCatalog.DescribeHost(h))));
        Assert.Equal(PermissionRisk.High, risk);
    }

    [Fact]
    public void EdgeCases_HandlesQuirkyManifest()
    {
        var info = DiscoverFixture("edge-cases.json");

        // Whitespace name should be preserved as-is (trimming is the manifest's responsibility)
        Assert.Equal("  Whitespace Name  ", info.ManifestName);
        Assert.Equal("1.0.0-beta", info.ManifestVersion);
        // Empty string description should be treated as null
        Assert.Null(info.ManifestDescription);

        // Empty permissions array
        Assert.Empty(info.Permissions);
        // Host patterns
        Assert.Equal(2, info.HostPermissions.Count);

        // Options page from options_ui
        Assert.Equal("settings/options.html", info.OptionsPage);
    }

    [Fact]
    public void AllFixtures_DiscoverWithoutThrow()
    {
        foreach (var fixture in Directory.GetFiles(_fixturesDir, "*.json"))
        {
            var dir = Path.Combine(_tempDir, "all-" + Path.GetFileNameWithoutExtension(fixture));
            Directory.CreateDirectory(dir);
            File.Copy(fixture, Path.Combine(dir, "manifest.json"));
            var info = LocalSourceService.DiscoverOne(dir);
            Assert.NotNull(info);
            Assert.False(string.IsNullOrEmpty(info!.ManifestName));
        }
    }
}
