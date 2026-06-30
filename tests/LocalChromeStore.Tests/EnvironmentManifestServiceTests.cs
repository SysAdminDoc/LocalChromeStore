using LocalChromeStore.Models;
using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class EnvironmentManifestServiceTests
{
    [Fact]
    public void Create_DoesNotExportTokenAndIncludesInstalledSnapshots()
    {
        var settings = new AppSettings
        {
            GitHubUser = "primary",
            GitHubToken = "secret-token",
            UseTopicFilter = true,
            TopicFilter = "chrome-extension",
            ExtraOwners = ["extra", "primary"],
            LocalSourceFolders = [" C:\\Source\\A ", "c:\\source\\a", "D:\\Source\\B"],
            LaunchUrl = " https://example.test ",
            LaunchProfileMode = BrowserProfileMode.Persistent
        };
        var installed = new[]
        {
            new InstalledExtension
            {
                RepoOwner = "extra",
                RepoName = "Extension",
                Version = "1.2.3",
                InstallPath = "C:\\Extensions\\Extension",
                ManifestPath = "C:\\Extensions\\Extension\\manifest.json",
                InstalledAt = DateTimeOffset.UtcNow,
                DisplayName = "Extension",
                RepoUrl = "https://github.com/extra/Extension",
                ManifestVersionNumber = 3,
                Framework = ExtensionFramework.Wxt,
                Permissions = ["storage"],
                HostPermissions = ["https://example.test/*"],
                ChecksumVerified = true,
                ChecksumAlgorithm = "SHA256",
                ChecksumValue = new string('a', 64),
                ChecksumSource = "api-digest",
                AssetName = "Extension.zip",
                AssetDigest = "sha256:" + new string('a', 64),
                AssetSizeBytes = 1024,
                AssetId = 99,
                AssetContentType = "application/zip",
                AssetUploader = "extra",
                AssetCreatedAt = DateTimeOffset.UnixEpoch,
                AssetUpdatedAt = DateTimeOffset.UnixEpoch.AddMinutes(1),
                AssetDownloadCount = 3,
                ReleasePublishedAt = DateTimeOffset.UnixEpoch.AddMinutes(2)
            }
        };

        var manifest = EnvironmentManifestService.Create(settings, installed);
        var json = EnvironmentManifestService.ToJson(manifest);

        Assert.DoesNotContain("secret-token", json);
        Assert.Equal("primary", manifest.Settings.GitHubUser);
        Assert.Equal(["extra"], manifest.Settings.ExtraOwners);
        Assert.Equal(["C:\\Source\\A", "D:\\Source\\B"], manifest.Settings.LocalSourceFolders);
        Assert.Equal("https://example.test", manifest.Settings.LaunchUrl);
        Assert.Equal(BrowserProfileMode.Persistent, manifest.Settings.LaunchProfileMode);
        Assert.False(manifest.Settings.LaunchWithTemporaryProfile);
        Assert.Single(manifest.Extensions);
        Assert.Equal(3, manifest.Extensions[0].ManifestVersionNumber);
        Assert.Equal("api-digest", manifest.Extensions[0].ChecksumSource);
        Assert.Equal(99, manifest.Extensions[0].AssetId);
        Assert.Equal("Extension.zip", manifest.Extensions[0].AssetName);
        Assert.Equal("application/zip", manifest.Extensions[0].AssetContentType);
        Assert.Equal(3, manifest.Extensions[0].AssetDownloadCount);
        Assert.Equal(["storage"], manifest.Extensions[0].Permissions);
        Assert.Equal(["https://example.test/*"], manifest.Extensions[0].HostPermissions);
    }

    [Fact]
    public void ApplySettings_PreservesTokenAndUnhidesImportedRepos()
    {
        var current = new AppSettings
        {
            GitHubUser = "old",
            GitHubToken = "secret-token",
            PreferredBrowserPath = "C:\\Browser\\chrome.exe",
            HiddenRepos = ["primary/Imported", "someone/Other"],
            LocalSourceFolders = ["C:\\Source\\Current", "C:\\Source\\Shared"],
            LaunchBrowserAfterInstall = true,
            AutoUpdateOnRefresh = true
        };
        var manifest = new EnvironmentManifest
        {
            Settings = new EnvironmentSettingsSnapshot
            {
                GitHubUser = "primary",
                UseTopicFilter = false,
                TopicFilter = "chrome-extension",
                ExtraOwners = ["extra"],
                LocalSourceFolders = ["c:\\source\\shared", "D:\\Source\\Imported"],
                LaunchProfileMode = BrowserProfileMode.Persistent
            },
            Extensions =
            [
                new EnvironmentExtensionSnapshot
                {
                    RepoOwner = "primary",
                    RepoName = "Imported",
                    Version = "1.0.0"
                },
                new EnvironmentExtensionSnapshot
                {
                    RepoOwner = "extra",
                    RepoName = "Second",
                    Version = "2.0.0"
                }
            ]
        };

        var applied = EnvironmentManifestService.ApplySettings(current, manifest);

        Assert.Equal("primary", applied.GitHubUser);
        Assert.Equal("secret-token", applied.GitHubToken);
        Assert.Equal("C:\\Browser\\chrome.exe", applied.PreferredBrowserPath);
        Assert.True(applied.LaunchBrowserAfterInstall);
        Assert.True(applied.AutoUpdateOnRefresh);
        Assert.Equal(BrowserProfileMode.Persistent, applied.LaunchProfileMode);
        Assert.False(applied.LaunchWithTemporaryProfile);
        Assert.Equal(["extra"], applied.ExtraOwners);
        Assert.Equal(["C:\\Source\\Current", "c:\\source\\shared", "D:\\Source\\Imported"], applied.LocalSourceFolders);
        Assert.Equal(["someone/Other"], applied.HiddenRepos);
    }
}
