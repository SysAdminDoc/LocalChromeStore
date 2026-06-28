using LocalChromeStore.Models;
using LocalChromeStore.Services;
using LocalChromeStore.Services.Cdp;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class BrowserLaunchManagerTests
{
    private static BrowserInfo Chrome(int major = 120) => new()
    {
        Kind = BrowserKind.Chrome,
        DisplayName = "Chrome",
        ExecutablePath = @"C:\chrome.exe",
        MajorVersion = major
    };

    private static BrowserLaunchPlan Plan(bool loadsExtensions, string? tempProfile = null, params string[] warnings) => new()
    {
        Browser = Chrome(),
        Arguments = new[] { "--load-extension=C:\\e" },
        ExtensionCount = 1,
        Strategy = loadsExtensions ? LaunchStrategy.CommandLineLoad : LaunchStrategy.CdpLoadUnpacked,
        LoadsExtensions = loadsExtensions,
        TemporaryProfilePath = tempProfile,
        Warnings = warnings
    };

    [Fact]
    public void EmptySet_Sentinel_PromptsToInstall()
    {
        var outcome = BrowserLaunchManager.EmptySet(isSentinel: true, loadSetName: null);

        Assert.False(outcome.Launched);
        Assert.Contains("Install at least one extension", outcome.StatusText);
        Assert.Contains("No extensions installed yet", outcome.Log.Single());
    }

    [Fact]
    public void EmptySet_NamedSet_NamesTheSet()
    {
        var outcome = BrowserLaunchManager.EmptySet(isSentinel: false, loadSetName: "Dev");

        Assert.False(outcome.Launched);
        Assert.Contains("load set 'Dev'", outcome.StatusText);
        Assert.Contains("Load set 'Dev' has no installed extensions", outcome.Log.Single());
    }

    [Fact]
    public void DescribeLaunch_LoadsExtensions_ReportsLoadedCountAndCommand()
    {
        var (status, log) = BrowserLaunchManager.DescribeLaunch(Plan(loadsExtensions: true), 1, isSentinel: true, null);

        Assert.Equal("Launched Chrome with 1 extension(s) (all installed).", status);
        Assert.Contains(log, l => l.StartsWith("Load strategy -"));
        Assert.Contains("Launched Chrome with 1 extension(s) loaded (all installed).", log);
        Assert.Contains(log, l => l.StartsWith("Launch command:"));
    }

    [Fact]
    public void DescribeLaunch_CannotLoad_WarnsExtensionsWontLoad()
    {
        var (status, log) = BrowserLaunchManager.DescribeLaunch(Plan(loadsExtensions: false), 2, isSentinel: true, null);

        Assert.Equal("Launched Chrome, but it cannot load extensions from the command line.", status);
        Assert.Contains("Launched Chrome without loading 2 extension(s) - see warning above.", log);
    }

    [Fact]
    public void DescribeLaunch_NamedSet_UsesSetLabel()
    {
        var (status, _) = BrowserLaunchManager.DescribeLaunch(Plan(loadsExtensions: true), 1, isSentinel: false, "Dev");

        Assert.Equal("Launched Chrome with 1 extension(s) (load set 'Dev').", status);
    }

    [Fact]
    public void DescribeLaunch_SurfacesWarningsAndTempProfile()
    {
        var (_, log) = BrowserLaunchManager.DescribeLaunch(
            Plan(loadsExtensions: true, tempProfile: @"C:\tmp\profile", warnings: "be careful"),
            1, isSentinel: true, null);

        Assert.Contains("! be careful", log);
        Assert.Contains(@"Temporary browser profile: C:\tmp\profile", log);
    }

    [Fact]
    public void DisplayCommandForPlan_CdpStrategy_AddsPipeFlags()
    {
        var plan = new BrowserLaunchPlan
        {
            Browser = Chrome(142),
            Arguments = new[] { "--user-data-dir=C:\\profile", "https://example.test" },
            ExtensionCount = 1,
            Strategy = LaunchStrategy.CdpLoadUnpacked,
            LoadsExtensions = false
        };

        var command = BrowserLaunchManager.DisplayCommandForPlan(plan);

        Assert.Contains("--remote-debugging-pipe", command);
        Assert.Contains("--enable-unsafe-extension-debugging", command);
        Assert.Contains("--user-data-dir=C:\\profile", command);
        Assert.Contains("https://example.test", command);
    }

    [Fact]
    public async Task LaunchAsync_CdpStrategy_LoadsViaCdpAndLogsExtensionId()
    {
        var root = Path.Combine(Path.GetTempPath(), "LocalChromeStore.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var ext = Path.Combine(root, "ext");
            Directory.CreateDirectory(ext);
            var fake = new FakeCdpLoader(new CdpLoadResult(
                true,
                1,
                1,
                "all extensions loaded via CDP",
                new[] { new CdpLoadAttempt(ext, true, "abcdefghijklmnopabcdefghijklmnop", "loaded") }));
            var manager = CreateManager(root, fake);

            var outcome = await manager.LaunchAsync(
                Chrome(142),
                new[] { Installed("owner", "one", ext) },
                launchUrl: "https://example.test/start",
                useTemporaryProfile: true,
                isSentinel: true,
                loadSetName: null);

            Assert.True(outcome.Launched);
            Assert.Contains("--remote-debugging-pipe", string.Join("\n", outcome.Log));
            Assert.Contains("abcdefghijklmnopabcdefghijklmnop", string.Join("\n", outcome.Log));
            Assert.Equal(new[] { ext }, fake.ExtensionPaths);
            Assert.Contains(fake.ExtraArgs, a => a.StartsWith("--user-data-dir=", StringComparison.Ordinal));
            Assert.Contains("https://example.test/start", fake.ExtraArgs);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task LaunchAsync_CdpStrategy_FailureLogsExactErrorAndFallback()
    {
        var root = Path.Combine(Path.GetTempPath(), "LocalChromeStore.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var ext = Path.Combine(root, "ext");
            Directory.CreateDirectory(ext);
            var fake = new FakeCdpLoader(new CdpLoadResult(
                false,
                0,
                1,
                "loaded 0/1 extensions via CDP",
                new[] { new CdpLoadAttempt(ext, false, null, "Cannot load extension with file or directory name _metadata") }));
            var manager = CreateManager(root, fake);

            var outcome = await manager.LaunchAsync(
                Chrome(142),
                new[] { Installed("owner", "one", ext) },
                launchUrl: null,
                useTemporaryProfile: true,
                isSentinel: true,
                loadSetName: null);

            Assert.False(outcome.Launched);
            var log = string.Join("\n", outcome.Log);
            Assert.Contains("Cannot load extension with file or directory name _metadata", log);
            Assert.Contains("Fallback: use Chrome for Testing", log);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static BrowserLaunchManager CreateManager(string root, ICdpExtensionLoader cdpLoader)
    {
        var appData = Path.Combine(root, "appdata");
        var localData = Path.Combine(root, "localdata");
        var settings = new SettingsService(appData, localData);
        var github = new GitHubService(settings);
        var extensions = new ExtensionService(settings, github);
        return new BrowserLaunchManager(new BrowserLauncher(extensions), cdpLoader);
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

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Test cleanup best effort only.
        }
    }

    private sealed class FakeCdpLoader : ICdpExtensionLoader
    {
        private readonly CdpLoadResult _result;

        public FakeCdpLoader(CdpLoadResult result) => _result = result;

        public IReadOnlyList<string> ExtensionPaths { get; private set; } = [];
        public IReadOnlyList<string> ExtraArgs { get; private set; } = [];

        public Task<CdpLoadResult> LaunchAndLoadAsync(
            string browserExePath,
            IReadOnlyList<string> extensionPaths,
            IReadOnlyList<string> extraArgs,
            CancellationToken ct = default)
        {
            ExtensionPaths = extensionPaths.ToList();
            ExtraArgs = extraArgs.ToList();
            return Task.FromResult(_result);
        }
    }
}
