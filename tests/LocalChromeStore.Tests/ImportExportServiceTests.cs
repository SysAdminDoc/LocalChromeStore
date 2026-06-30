using System.Text.Json;
using LocalChromeStore.Models;
using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class ImportExportServiceTests
{
    [Fact]
    public void BuildCatalog_ProjectsEntriesAndMergesInstalledState()
    {
        var infos = new[]
        {
            new ExtensionInfo
            {
                RepoOwner = "o", RepoName = "a", RepoUrl = "https://github.com/o/a",
                ManifestName = "Alpha", ManifestVersion = "2.0",
                AssetUrl = "https://x/a.zip", AssetName = "a.zip", AssetDigest = "sha256:" + new string('a', 64),
                AssetSizeBytes = 10, AssetId = 101, AssetContentType = "application/zip",
                AssetUploader = "owner", AssetCreatedAt = DateTimeOffset.UnixEpoch,
                AssetUpdatedAt = DateTimeOffset.UnixEpoch.AddMinutes(1), AssetDownloadCount = 4,
                PublishedAt = DateTimeOffset.UnixEpoch.AddMinutes(2), ChecksumName = "a.zip.sha256.txt"
            },
            new ExtensionInfo
            {
                RepoOwner = "o", RepoName = "b", RepoUrl = "https://github.com/o/b"
            },
            new ExtensionInfo
            {
                RepoOwner = "local", RepoName = "source-1234abcd", RepoUrl = @"C:\src\extension",
                ManifestName = "Local Source", ManifestVersion = "0.0.5",
                LocalSourcePath = @"C:\src\extension", DiscoverySource = DiscoverySource.LocalSourceFolder,
                AssetKind = AssetKind.LocalFolder, AssetName = "extension"
            }
        };
        var installed = new[]
        {
            new InstalledExtension
            {
                RepoOwner = "o", RepoName = "a", Version = "1.5",
                InstallPath = @"C:\ext\o\a", ManifestPath = @"C:\ext\o\a\manifest.json",
                ChecksumVerified = true, ChecksumSource = "api-digest", InstalledAt = DateTimeOffset.UnixEpoch,
                AssetName = "a.zip", AssetDigest = "sha256:" + new string('a', 64),
                AssetSizeBytes = 10, AssetId = 101, AssetUpdatedAt = DateTimeOffset.UnixEpoch.AddMinutes(1)
            }
        };

        var export = ImportExportService.BuildCatalog(infos, installed);

        Assert.Equal(3, export.Count);
        using var doc = JsonDocument.Parse(export.Json);
        var arr = doc.RootElement;
        Assert.Equal(3, arr.GetArrayLength());

        var a = arr[0];
        Assert.Equal("o", a.GetProperty("RepoOwner").GetString());
        Assert.Equal("Alpha", a.GetProperty("DisplayName").GetString());
        Assert.Equal("2.0", a.GetProperty("DisplayVersion").GetString());
        Assert.True(a.GetProperty("HasAsset").GetBoolean());
        Assert.True(a.GetProperty("HasInstallSource").GetBoolean());
        Assert.Equal("sha256:" + new string('a', 64), a.GetProperty("AssetDigest").GetString());
        Assert.Equal(101, a.GetProperty("AssetId").GetInt64());
        Assert.Equal("application/zip", a.GetProperty("AssetContentType").GetString());
        Assert.Equal("owner", a.GetProperty("AssetUploader").GetString());
        Assert.Equal(4, a.GetProperty("AssetDownloadCount").GetInt64());
        Assert.False(a.GetProperty("AssetChangedSinceInstall").GetBoolean());
        Assert.Equal("unchanged since install", a.GetProperty("AssetChangeDetail").GetString());
        Assert.Equal("a.zip.sha256.txt", a.GetProperty("ChecksumName").GetString());
        Assert.Equal("1.5", a.GetProperty("InstalledVersion").GetString());
        Assert.True(a.GetProperty("ChecksumVerified").GetBoolean());
        Assert.Equal("api-digest", a.GetProperty("ChecksumSource").GetString());

        var b = arr[1];
        Assert.Equal("b", b.GetProperty("RepoName").GetString());
        Assert.False(b.GetProperty("HasAsset").GetBoolean());
        Assert.False(b.GetProperty("HasInstallSource").GetBoolean());
        Assert.Equal(JsonValueKind.Null, b.GetProperty("InstalledVersion").ValueKind);

        var c = arr[2];
        Assert.Equal("source-1234abcd", c.GetProperty("RepoName").GetString());
        Assert.False(c.GetProperty("HasAsset").GetBoolean());
        Assert.True(c.GetProperty("HasInstallSource").GetBoolean());
        Assert.Equal(@"C:\src\extension", c.GetProperty("LocalSourcePath").GetString());
        Assert.Equal("LocalSourceFolder", c.GetProperty("DiscoverySource").GetString());
    }

    [Fact]
    public void BuildCatalog_EmptyCatalog_ProducesEmptyArray()
    {
        var export = ImportExportService.BuildCatalog(Array.Empty<ExtensionInfo>(), Array.Empty<InstalledExtension>());

        Assert.Equal(0, export.Count);
        using var doc = JsonDocument.Parse(export.Json);
        Assert.Equal(0, doc.RootElement.GetArrayLength());
    }

    private static InstalledExtension Installed(string version) => new()
    {
        RepoOwner = "o", RepoName = "a", Version = version,
        InstallPath = @"C:\ext\o\a", ManifestPath = @"C:\ext\o\a\manifest.json"
    };

    [Fact]
    public void ClassifyImportTarget_AlreadyInstalledAtVersion_IsAlreadyCurrent()
    {
        // Case-insensitive version match counts as current.
        Assert.Equal(ImportAction.AlreadyCurrent,
            ImportExportService.ClassifyImportTarget(Installed("1.0.0"), "1.0.0", hasCard: true, cardHasInstallSource: true));
    }

    [Fact]
    public void ClassifyImportTarget_NoCard_IsMissing()
    {
        Assert.Equal(ImportAction.Missing,
            ImportExportService.ClassifyImportTarget(existing: null, "1.0.0", hasCard: false, cardHasInstallSource: false));
    }

    [Fact]
    public void ClassifyImportTarget_CardWithoutInstallSource_IsMissingAsset()
    {
        Assert.Equal(ImportAction.MissingAsset,
            ImportExportService.ClassifyImportTarget(existing: null, "1.0.0", hasCard: true, cardHasInstallSource: false));
    }

    [Fact]
    public void ClassifyImportTarget_NewerOrDifferentVersionWithInstallSource_IsInstall()
    {
        // Installed but at a different version: install the catalog version.
        Assert.Equal(ImportAction.Install,
            ImportExportService.ClassifyImportTarget(Installed("0.9.0"), "1.0.0", hasCard: true, cardHasInstallSource: true));
        // Not installed at all: install.
        Assert.Equal(ImportAction.Install,
            ImportExportService.ClassifyImportTarget(existing: null, "1.0.0", hasCard: true, cardHasInstallSource: true));
    }
}
