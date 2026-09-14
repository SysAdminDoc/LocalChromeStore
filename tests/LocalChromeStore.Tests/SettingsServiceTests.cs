using System.IO;
using LocalChromeStore.Models;
using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

[Collection("Serialized temp file system")]
public sealed class SettingsServiceTests
{
    private static string NewRoot() =>
        Path.Combine(Path.GetTempPath(), "LocalChromeStore.Tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void WriteAtomic_CreatesFile_WhenNoneExists()
    {
        var dir = NewRoot();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "state.json");

        SettingsService.WriteAtomic(path, "{\"a\":1}");

        Assert.True(File.Exists(path));
        Assert.Equal("{\"a\":1}", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.False(File.Exists(path + ".bak"));
    }

    [Fact]
    public void Constructor_UsesIsolatedDataRootFromEnvironment()
    {
        var root = NewRoot();
        var previous = Environment.GetEnvironmentVariable(SettingsService.DataRootEnvironmentVariable);
        try
        {
            Environment.SetEnvironmentVariable(SettingsService.DataRootEnvironmentVariable, root);

            var service = new SettingsService();

            Assert.Equal(Path.Combine(root, "Roaming", "LocalChromeStore"), service.SettingsDir);
            Assert.Equal(Path.Combine(root, "Local", "LocalChromeStore", "cache"), service.CacheDir);
        }
        finally
        {
            Environment.SetEnvironmentVariable(SettingsService.DataRootEnvironmentVariable, previous);
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void WriteAtomic_KeepsPriorContentInBackup_OnOverwrite()
    {
        var dir = NewRoot();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "state.json");

        SettingsService.WriteAtomic(path, "FIRST");
        SettingsService.WriteAtomic(path, "SECOND");

        Assert.Equal("SECOND", File.ReadAllText(path));
        Assert.Equal("FIRST", File.ReadAllText(path + ".bak"));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Load_RecoversFromBackup_WhenPrimaryIsCorrupt()
    {
        var root = NewRoot();
        var svc = new SettingsService(appDataRoot: root, localAppDataRoot: root);

        // First good save, then a second save so a .bak exists holding the first good copy.
        svc.Save(new AppSettings { GitHubUser = "alice" });
        svc.Save(new AppSettings { GitHubUser = "bob" });

        // Simulate a truncating crash on the live file.
        File.WriteAllText(svc.SettingsPath, "{ this is not valid json");

        var recovered = new SettingsService(appDataRoot: root, localAppDataRoot: root).Load();
        Assert.Equal("alice", recovered.GitHubUser); // from the .bak written by the second save
    }

    [Fact]
    public void Load_ReportsSchemaMismatchAndRecoversFromBackup()
    {
        var root = NewRoot();
        var svc = new SettingsService(appDataRoot: root, localAppDataRoot: root);
        svc.Save(new AppSettings { GitHubUser = "alice" });
        svc.Save(new AppSettings { GitHubUser = "bob" });
        File.WriteAllText(svc.SettingsPath, "{\"unrelated\":true}");

        var recovered = new SettingsService(appDataRoot: root, localAppDataRoot: root);
        var loaded = recovered.Load();

        Assert.Equal("alice", loaded.GitHubUser);
        Assert.Contains("backup", recovered.LastSettingsLoadWarning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SaveManifest_RoundTrips_AndKeepsBackup()
    {
        var root = NewRoot();
        var svc = new SettingsService(appDataRoot: root, localAppDataRoot: root);

        var m1 = new InstalledExtensionsManifest();
        m1.Extensions.Add(new InstalledExtension { RepoOwner = "o", RepoName = "r", Version = "1.0.0", InstallPath = "p", ManifestPath = "m" });
        svc.SaveManifest(m1);

        var m2 = new InstalledExtensionsManifest();
        m2.Extensions.Add(new InstalledExtension { RepoOwner = "o", RepoName = "r", Version = "2.0.0", InstallPath = "p", ManifestPath = "m" });
        svc.SaveManifest(m2);

        Assert.True(File.Exists(svc.ManifestPath + ".bak"));
        Assert.Equal("2.0.0", svc.LoadManifest().Extensions[0].Version);
    }

    [Fact]
    public void Load_MigratesLegacyTemporaryProfileFlagToProfileMode()
    {
        var root = NewRoot();
        var svc = new SettingsService(appDataRoot: root, localAppDataRoot: root);
        File.WriteAllText(svc.SettingsPath,
            """
            {
              "GitHubUser": "alice",
              "LaunchWithTemporaryProfile": true
            }
            """);

        var loaded = svc.Load();

        Assert.True(loaded.LaunchWithTemporaryProfile);
        Assert.Equal(BrowserProfileMode.Temporary, loaded.LaunchProfileMode);
    }

    [Fact]
    public void Save_PersistsProfileModeAndLegacyFlag()
    {
        var root = NewRoot();
        var svc = new SettingsService(appDataRoot: root, localAppDataRoot: root);

        svc.Save(new AppSettings { GitHubUser = "alice", LaunchProfileMode = BrowserProfileMode.Persistent });
        var json = File.ReadAllText(svc.SettingsPath);
        var loaded = svc.Load();

        Assert.Contains("\"LaunchProfileMode\": 1", json);
        Assert.Contains("\"LaunchWithTemporaryProfile\": false", json);
        Assert.Equal(BrowserProfileMode.Persistent, loaded.LaunchProfileMode);
        Assert.False(loaded.LaunchWithTemporaryProfile);
    }

    [Fact]
    public void Save_PersistsDistinctLocalSourceFolders()
    {
        var root = NewRoot();
        var svc = new SettingsService(appDataRoot: root, localAppDataRoot: root);
        var source = Path.Combine(root, "Source");
        var other = Path.Combine(root, "Other");

        svc.Save(new AppSettings
        {
            GitHubUser = "alice",
            LocalSourceFolders = [$" {source} ", source.ToUpperInvariant(), other]
        });

        var loaded = svc.Load();

        Assert.Equal([source, other], loaded.LocalSourceFolders);
    }
}
