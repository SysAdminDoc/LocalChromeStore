using LocalChromeStore.Models;
using LocalChromeStore.Services;
using LocalChromeStore.Services.Cdp;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class BrowserConformanceServiceTests
{
    [Fact]
    public async Task RunAsync_CommandLineBrowser_WritesFixtureAndReports()
    {
        var root = TestRoot();
        try
        {
            var settings = Settings(root);
            var processLauncher = new FakeProcessLauncher(new BrowserConformanceProcessResult(
                Started: true,
                ProcessId: 1234,
                ExitedDuringProbe: false,
                ExitCode: null,
                Detail: "probe stayed alive"));
            var service = new BrowserConformanceService(
                settings,
                cdpLoader: new FakeCdpLoader(CdpLoadResult.Skipped("not used")),
                processLauncher,
                probeDuration: TimeSpan.Zero);
            var browser = Browser(BrowserKind.Chrome, "Chrome", 120, root);

            var run = await service.RunAsync(new[] { browser });

            var result = Assert.Single(run.Report.Browsers);
            Assert.True(File.Exists(run.JsonPath));
            Assert.True(File.Exists(run.TextPath));
            Assert.True(File.Exists(Path.Combine(run.Report.FixturePath, "manifest.json")));
            Assert.Equal(LaunchStrategy.CommandLineLoad, result.Strategy);
            Assert.True(result.Success);
            Assert.True(result.Launched);
            Assert.Equal("120.0.1", result.BrowserVersion);
            Assert.Contains(result.Arguments, a => a.StartsWith("--user-data-dir=", StringComparison.Ordinal));
            Assert.Contains(result.Arguments, a => a.StartsWith("--load-extension=", StringComparison.Ordinal));
            Assert.Contains("CommandLineLoad", File.ReadAllText(run.JsonPath));
            Assert.NotNull(processLauncher.LastPlan);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task RunAsync_CdpBrowser_RecordsExtensionIdsAndEffectivePipeArgs()
    {
        var root = TestRoot();
        try
        {
            var settings = Settings(root);
            var cdp = new FakeCdpLoader(new CdpLoadResult(
                true,
                1,
                1,
                "all extensions loaded via CDP",
                new[] { new CdpLoadAttempt(@"C:\fixture", true, "abcdefghijklmnopabcdefghijklmnop", "loaded") }));
            var service = new BrowserConformanceService(
                settings,
                cdpLoader: cdp,
                processLauncher: new ThrowingProcessLauncher(),
                probeDuration: TimeSpan.Zero);
            var browser = Browser(BrowserKind.Chrome, "Google Chrome", 142, root);

            var run = await service.RunAsync(new[] { browser });

            var result = Assert.Single(run.Report.Browsers);
            Assert.Equal(LaunchStrategy.CdpLoadUnpacked, result.Strategy);
            Assert.True(result.Success);
            Assert.Contains("--remote-debugging-pipe", result.Arguments);
            Assert.Contains("--enable-unsafe-extension-debugging", result.Arguments);
            Assert.Contains(cdp.ExtraArgs, a => a.StartsWith("--user-data-dir=", StringComparison.Ordinal));
            Assert.Equal("abcdefghijklmnopabcdefghijklmnop", Assert.Single(result.CdpAttempts).ExtensionId);
            Assert.Contains("abcdefghijklmnopabcdefghijklmnop", File.ReadAllText(run.TextPath));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ChromeForTestingDetection_FindsCachedExecutablesAndUsesCommandLineStrategy()
    {
        var root = TestRoot();
        try
        {
            var exeDir = Path.Combine(root, "chrome-for-testing", "win64", "chrome-win64");
            Directory.CreateDirectory(exeDir);
            var exe = Path.Combine(exeDir, "chrome.exe");
            File.WriteAllText(exe, string.Empty);

            var found = BrowserLauncher.FindChromeForTestingExecutables(new[] { root });

            Assert.Contains(exe, found, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(LaunchStrategy.CommandLineLoad, BrowserLauncher.ResolveStrategy(BrowserKind.ChromeForTesting, 150));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void FindLatestReports_ReturnsNewestJsonAndTextReports()
    {
        var root = TestRoot();
        try
        {
            var logs = Path.Combine(root, "logs");
            Directory.CreateDirectory(logs);
            var older = Path.Combine(logs, "browser-conformance-2026-01-01-010101.json");
            var newer = Path.Combine(logs, "browser-conformance-2026-01-02-010101.json");
            var text = Path.Combine(logs, "browser-conformance-2026-01-02-010101.txt");
            File.WriteAllText(older, "{}");
            File.WriteAllText(newer, "{}");
            File.WriteAllText(text, "report");
            File.SetLastWriteTimeUtc(older, DateTime.UtcNow.AddMinutes(-2));
            File.SetLastWriteTimeUtc(newer, DateTime.UtcNow.AddMinutes(-1));
            File.SetLastWriteTimeUtc(text, DateTime.UtcNow);

            var latest = BrowserConformanceService.FindLatestReports(logs);

            Assert.Equal(newer, latest.JsonPath);
            Assert.Equal(text, latest.TextPath);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static SettingsService Settings(string root) =>
        new(Path.Combine(root, "appdata"), Path.Combine(root, "localdata"));

    private static BrowserInfo Browser(BrowserKind kind, string name, int major, string root) => new()
    {
        Kind = kind,
        DisplayName = name,
        ExecutablePath = Path.Combine(root, name.Replace(' ', '-') + ".exe"),
        MajorVersion = major,
        ProductVersion = $"{major}.0.1"
    };

    private static string TestRoot() =>
        Path.Combine(Path.GetTempPath(), "LocalChromeStore.Tests", Guid.NewGuid().ToString("N"));

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

    private sealed class FakeProcessLauncher : IBrowserConformanceProcessLauncher
    {
        private readonly BrowserConformanceProcessResult _result;

        public FakeProcessLauncher(BrowserConformanceProcessResult result) => _result = result;

        public BrowserLaunchPlan? LastPlan { get; private set; }

        public Task<BrowserConformanceProcessResult> LaunchAsync(
            BrowserLaunchPlan plan,
            TimeSpan probeDuration,
            CancellationToken ct = default)
        {
            LastPlan = plan;
            return Task.FromResult(_result);
        }
    }

    private sealed class ThrowingProcessLauncher : IBrowserConformanceProcessLauncher
    {
        public Task<BrowserConformanceProcessResult> LaunchAsync(
            BrowserLaunchPlan plan,
            TimeSpan probeDuration,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("Process launcher should not be used for CDP probes.");
    }

    private sealed class FakeCdpLoader : ICdpExtensionLoader
    {
        private readonly CdpLoadResult _result;

        public FakeCdpLoader(CdpLoadResult result) => _result = result;

        public IReadOnlyList<string> ExtraArgs { get; private set; } = [];

        public Task<CdpLoadResult> LaunchAndLoadAsync(
            string browserExePath,
            IReadOnlyList<string> extensionPaths,
            IReadOnlyList<string> extraArgs,
            CancellationToken ct = default)
        {
            ExtraArgs = extraArgs.ToList();
            return Task.FromResult(_result);
        }
    }
}
