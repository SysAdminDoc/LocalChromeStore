using LocalChromeStore.Models;
using LocalChromeStore.Services;
using LocalChromeStore.ViewModels;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class SmokeTests : IDisposable
{
    private readonly string _root;
    private readonly SettingsService _settings;
    private readonly FakeDialogService _dialogs;

    public SmokeTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "LocalChromeStore.Tests", Guid.NewGuid().ToString("N"));
        _settings = new SettingsService(
            appDataRoot: Path.Combine(_root, "appdata"),
            localAppDataRoot: Path.Combine(_root, "localappdata"));
        _dialogs = new FakeDialogService();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch { /* best-effort temp cleanup */ }
    }

    [Fact]
    public void Settings_CreatesFolderStructure()
    {
        Assert.True(Directory.Exists(_settings.SettingsDir));
        Assert.True(Directory.Exists(_settings.ExtensionsRoot));
        Assert.True(Directory.Exists(_settings.CacheDir));
        Assert.True(Directory.Exists(_settings.LogsDir));
        Assert.True(Directory.Exists(_settings.IconCacheDir));
        Assert.True(Directory.Exists(_settings.PolicyPackagesRoot));
        Assert.True(Directory.Exists(_settings.PolicyKeysDir));
    }

    [Fact]
    public void Settings_LoadDefaultsAndSave()
    {
        var defaults = _settings.Load();
        Assert.Equal("SysAdminDoc", defaults.GitHubUser);
        Assert.Equal("chrome-extension", defaults.TopicFilter);

        defaults.GitHubUser = "TestUser";
        _settings.Save(defaults);

        var reloaded = _settings.Load();
        Assert.Equal("TestUser", reloaded.GitHubUser);
    }

    [Fact]
    public void ExtensionService_NoInstalled_ReturnsEmpty()
    {
        var github = new GitHubService(_settings);
        var ext = new ExtensionService(_settings, github);
        Assert.Empty(ext.Installed);
        Assert.Null(ext.Find("any", "repo"));
    }

    [Fact]
    public void CatalogCache_IntegrationWithSettings()
    {
        var cache = new CatalogCacheService(_settings.CacheDir);
        Assert.False(cache.Exists);
        Assert.Null(cache.Load());

        cache.Save(new List<ExtensionInfo>
        {
            new() { RepoOwner = "o", RepoName = "r", RepoUrl = "u", ManifestName = "Test" }
        });

        Assert.True(cache.Exists);
        var loaded = cache.Load();
        Assert.NotNull(loaded);
        Assert.Single(loaded!.Extensions);
    }

    [Fact]
    public void JsonEventLog_IntegrationWithSettings()
    {
        var log = new JsonEventLog(_settings.LogsDir);
        log.Info(EventCategory.General, "Smoke test event");
        log.Warn(EventCategory.Discovery, "Test warning");

        var files = Directory.GetFiles(_settings.LogsDir, "events-*.jsonl");
        Assert.Single(files);
        var lines = File.ReadAllLines(files[0]);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public void LoadSetManager_EmptyStart()
    {
        var mgr = new LoadSetManager(_settings);
        Assert.Equal("All installed", LoadSetManager.CreateSentinel().Name);
    }

    [Fact]
    public void BrowserLauncher_Detect_DoesNotThrow()
    {
        var github = new GitHubService(_settings);
        var ext = new ExtensionService(_settings, github);
        var launcher = new BrowserLauncher(ext);
        var browsers = launcher.Detect();
        Assert.NotNull(browsers);
    }

    [Fact]
    public void PolicyEnrollment_DoesNotThrow()
    {
        var enrollment = new PolicyEnrollmentService();
        var state = enrollment.DetectCurrent();
        Assert.NotNull(state);
        var eval = PolicyEnrollmentService.EvaluateOffStoreForceInstall(state);
        Assert.NotNull(eval);
    }

    private sealed class FakeDialogService : IDialogService
    {
        public bool ConfirmResult { get; set; } = true;
        public string? PromptResult { get; set; }
        public bool Confirm(string message, string title, DialogIcon icon) => ConfirmResult;
        public void Alert(string message, string title, DialogIcon icon) { }
        public string? SaveFile(string title, string filter, string defaultFileName, string? initialDirectory, string? defaultExt) => null;
        public string? OpenFile(string title, string filter, string? initialDirectory, string? defaultExt) => null;
        public string? PromptText(string title, string message, string defaultValue) => PromptResult;
        public void SetClipboardText(string text) { }
    }
}
