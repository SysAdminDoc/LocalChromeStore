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
    public void BuildLaunchPlan_PersistentProfile_AddsStableUserDataDir()
    {
        var root = Path.Combine(Path.GetTempPath(), "LocalChromeStore.Tests", Guid.NewGuid().ToString("N"));
        var ext = Path.Combine(root, "ext");
        var profile = Path.Combine(root, "profiles", "persistent", "chrome", "dev");
        Directory.CreateDirectory(ext);
        var chrome = new BrowserInfo { Kind = BrowserKind.Chrome, DisplayName = "Chrome", ExecutablePath = Path.Combine(root, "chrome.exe"), MajorVersion = 120 };

        var plan = BrowserLauncher.BuildLaunchPlan(
            chrome,
            new[] { Installed("owner", "one", ext) },
            launchUrl: null,
            profileMode: BrowserProfileMode.Persistent,
            browserProfilePath: profile);

        Assert.Equal(BrowserProfileMode.Persistent, plan.ProfileMode);
        Assert.Equal(profile, plan.ProfilePath);
        Assert.Null(plan.TemporaryProfilePath);
        Assert.Contains($"--user-data-dir={profile}", plan.Arguments);
        Assert.Contains("--no-first-run", plan.Arguments);
        Assert.Contains($"--load-extension={ext}", plan.Arguments);
    }

    [Fact]
    public void BuildPersistentProfilePath_IsStableForBrowserAndLoadSet()
    {
        var browser = new BrowserInfo
        {
            Kind = BrowserKind.ChromeForTesting,
            DisplayName = "Chrome for Testing",
            ExecutablePath = @"C:\Browser\chrome.exe",
            MajorVersion = 149
        };

        var path = BrowserLauncher.BuildPersistentProfilePath(@"C:\LocalData", browser, "Dev Tools", null);

        Assert.Equal(Path.Combine(@"C:\LocalData", "LocalChromeStore", "profiles", "persistent", "chromefortesting", "load-set-dev-tools"), path);
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

    [Fact]
    public void BrowserProcessOutputCapture_StreamsStdoutStderrAndExitCode()
    {
        var powershell = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell",
            "v1.0",
            "powershell.exe");
        Assert.True(File.Exists(powershell));
        var progress = new CollectingProgress();
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = powershell,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add("[Console]::Out.WriteLine('out-line'); [Console]::Error.WriteLine('err-line'); exit 7");
        var process = System.Diagnostics.Process.Start(psi) ?? throw new InvalidOperationException("Process did not start.");

        BrowserProcessOutputCapture.Attach(process, "test browser", progress);

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && !progress.Lines.Any(l => l.Contains("code 7", StringComparison.Ordinal)))
            Thread.Sleep(50);

        Assert.Contains(progress.Lines, l => l.Contains("Browser stdout (test browser): out-line", StringComparison.Ordinal));
        Assert.Contains(progress.Lines, l => l.Contains("! Browser stderr (test browser): err-line", StringComparison.Ordinal));
        Assert.Contains(progress.Lines, l => l.Contains("! Browser process exited (test browser) with code 7.", StringComparison.Ordinal));
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

    private sealed class CollectingProgress : IProgress<string>
    {
        private readonly object _lock = new();
        private readonly List<string> _lines = [];

        public IReadOnlyList<string> Lines
        {
            get
            {
                lock (_lock)
                {
                    return _lines.ToList();
                }
            }
        }

        public void Report(string value)
        {
            lock (_lock)
            {
                _lines.Add(value);
            }
        }
    }
}
