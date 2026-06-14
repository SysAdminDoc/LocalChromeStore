using System.IO;
using LocalChromeStore.Models;
using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

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
}
