using LocalChromeStore.Models;
using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class BrowserLaunchManagerTests
{
    private static BrowserInfo Chrome() => new()
    {
        Kind = BrowserKind.Chrome,
        DisplayName = "Chrome",
        ExecutablePath = @"C:\chrome.exe",
        MajorVersion = 120
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
        Assert.Contains(log, l => l.StartsWith("Load strategy —"));
        Assert.Contains("Launched Chrome with 1 extension(s) loaded (all installed).", log);
        Assert.Contains(log, l => l.StartsWith("Launch command:"));
    }

    [Fact]
    public void DescribeLaunch_CannotLoad_WarnsExtensionsWontLoad()
    {
        var (status, log) = BrowserLaunchManager.DescribeLaunch(Plan(loadsExtensions: false), 2, isSentinel: true, null);

        Assert.Equal("Launched Chrome, but it cannot load extensions from the command line.", status);
        Assert.Contains("Launched Chrome without loading 2 extension(s) — see warning above.", log);
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
}
