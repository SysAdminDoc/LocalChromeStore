using LocalChromeStore.Models;
using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class BrowserLauncherTests
{
    [Fact]
    public void BuildLaunchPlan_UsesArgumentListSafeValuesAndReadablePreview()
    {
        var root = Path.Combine(Path.GetTempPath(), "LocalChromeStore.Tests", Guid.NewGuid().ToString("N"));
        var firstExtension = Path.Combine(root, "Extension One");
        var secondExtension = Path.Combine(root, "ExtensionTwo");
        var profile = Path.Combine(root, "Profile One");
        Directory.CreateDirectory(firstExtension);
        Directory.CreateDirectory(secondExtension);

        var browser = new BrowserInfo
        {
            Kind = BrowserKind.Chrome,
            DisplayName = "Chrome",
            ExecutablePath = Path.Combine(root, "Chrome App", "chrome.exe"),
            MajorVersion = 120 // pre-137: plain --load-extension still works
        };

        var installed = new[]
        {
            Installed("owner", "one", firstExtension),
            Installed("owner", "two", secondExtension),
            Installed("owner", "missing", Path.Combine(root, "Missing Extension"))
        };

        var plan = BrowserLauncher.BuildLaunchPlan(
            browser,
            installed,
            launchUrl: "https://example.test/start",
            useTemporaryProfile: true,
            temporaryProfilePath: profile);

        Assert.Equal(2, plan.ExtensionCount);
        Assert.Contains($"--user-data-dir={profile}", plan.Arguments);
        Assert.Contains("--no-first-run", plan.Arguments);
        Assert.Contains("--no-default-browser-check", plan.Arguments);
        Assert.Contains($"--load-extension={firstExtension},{secondExtension}", plan.Arguments);
        Assert.Equal("https://example.test/start", plan.Arguments[^1]);
        Assert.All(plan.Arguments, argument => Assert.DoesNotContain("\"", argument));
        Assert.Contains($"\"{browser.ExecutablePath}\"", plan.DisplayCommand);
        Assert.Contains($"\"--load-extension={firstExtension},{secondExtension}\"", plan.DisplayCommand);
    }

    [Theory]
    // Branded Chrome: 137 removed --load-extension, 142 removed the override workaround.
    [InlineData(BrowserKind.Chrome, 120, LaunchStrategy.CommandLineLoad)]
    [InlineData(BrowserKind.Chrome, 137, LaunchStrategy.CommandLineLoadWithOverride)]
    [InlineData(BrowserKind.Chrome, 141, LaunchStrategy.CommandLineLoadWithOverride)]
    [InlineData(BrowserKind.Chrome, 142, LaunchStrategy.CdpLoadUnpacked)]
    [InlineData(BrowserKind.Chrome, 150, LaunchStrategy.CdpLoadUnpacked)]
    [InlineData(BrowserKind.Chrome, null, LaunchStrategy.CdpLoadUnpacked)]
    // Unbranded Chromium and other forks still load with the override on 137+.
    [InlineData(BrowserKind.Brave, 120, LaunchStrategy.CommandLineLoad)]
    [InlineData(BrowserKind.Brave, 142, LaunchStrategy.CommandLineLoadWithOverride)]
    [InlineData(BrowserKind.Chromium, 150, LaunchStrategy.CommandLineLoadWithOverride)]
    [InlineData(BrowserKind.Edge, 142, LaunchStrategy.CommandLineLoadWithOverride)]
    [InlineData(BrowserKind.Edge, null, LaunchStrategy.CommandLineLoadWithOverride)]
    public void ResolveStrategy_SelectsByKindAndVersion(BrowserKind kind, int? major, LaunchStrategy expected)
    {
        Assert.Equal(expected, BrowserLauncher.ResolveStrategy(kind, major));
    }

    [Fact]
    public void BuildLaunchPlan_AddsOverrideFlagForChromium137Plus()
    {
        var root = Path.Combine(Path.GetTempPath(), "LocalChromeStore.Tests", Guid.NewGuid().ToString("N"));
        var ext = Path.Combine(root, "ext");
        Directory.CreateDirectory(ext);
        var brave = new BrowserInfo { Kind = BrowserKind.Brave, DisplayName = "Brave", ExecutablePath = Path.Combine(root, "brave.exe"), MajorVersion = 142 };

        var plan = BrowserLauncher.BuildLaunchPlan(brave, new[] { Installed("owner", "one", ext) });

        Assert.Equal(LaunchStrategy.CommandLineLoadWithOverride, plan.Strategy);
        Assert.Contains(BrowserLauncher.DisableLoadExtensionOverrideFlag, plan.Arguments);
        Assert.Contains($"--load-extension={ext}", plan.Arguments);
        Assert.True(plan.LoadsExtensions);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void BuildLaunchPlan_BrandedChrome142_OmitsLoadAndWarns()
    {
        var root = Path.Combine(Path.GetTempPath(), "LocalChromeStore.Tests", Guid.NewGuid().ToString("N"));
        var ext = Path.Combine(root, "ext");
        Directory.CreateDirectory(ext);
        var chrome = new BrowserInfo { Kind = BrowserKind.Chrome, DisplayName = "Google Chrome", ExecutablePath = Path.Combine(root, "chrome.exe"), MajorVersion = 142 };

        var plan = BrowserLauncher.BuildLaunchPlan(chrome, new[] { Installed("owner", "one", ext) });

        Assert.Equal(LaunchStrategy.CdpLoadUnpacked, plan.Strategy);
        Assert.DoesNotContain(plan.Arguments, a => a.StartsWith("--load-extension", StringComparison.Ordinal));
        Assert.False(plan.LoadsExtensions);
        Assert.NotEmpty(plan.Warnings);
    }

    private static InstalledExtension Installed(string owner, string repo, string path) => new()
    {
        RepoOwner = owner,
        RepoName = repo,
        Version = "1.0.0",
        InstallPath = path,
        ManifestPath = Path.Combine(path, "manifest.json"),
        InstalledAt = DateTimeOffset.UtcNow
    };
}
