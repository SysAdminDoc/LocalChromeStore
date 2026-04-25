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
            ExecutablePath = Path.Combine(root, "Chrome App", "chrome.exe")
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
