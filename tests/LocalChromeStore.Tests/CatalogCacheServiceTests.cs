using LocalChromeStore.Models;
using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class CatalogCacheServiceTests : IDisposable
{
    private readonly string _dir;

    public CatalogCacheServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "LocalChromeStore.Tests", Guid.NewGuid().ToString("N"), "cache");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        var root = Path.GetDirectoryName(Path.GetDirectoryName(_dir))!;
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Load_ReturnsNullWhenNoCacheExists()
    {
        var svc = new CatalogCacheService(_dir);
        Assert.Null(svc.Load());
        Assert.False(svc.Exists);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var svc = new CatalogCacheService(_dir);
        var catalog = new List<ExtensionInfo>
        {
            new()
            {
                RepoOwner = "owner",
                RepoName = "repo",
                RepoUrl = "https://github.com/owner/repo",
                ManifestName = "My Ext",
                LatestVersion = "1.0.0"
            }
        };

        svc.Save(catalog);
        Assert.True(svc.Exists);

        var snapshot = svc.Load();
        Assert.NotNull(snapshot);
        Assert.Single(snapshot!.Extensions);
        Assert.Equal("My Ext", snapshot.Extensions[0].ManifestName);
        Assert.Equal("1.0.0", snapshot.Extensions[0].LatestVersion);
        Assert.Equal("owner", snapshot.Extensions[0].RepoOwner);
    }

    [Fact]
    public void Save_OverwritesPreviousCache()
    {
        var svc = new CatalogCacheService(_dir);
        svc.Save(new List<ExtensionInfo>
        {
            new() { RepoOwner = "a", RepoName = "first", RepoUrl = "u" }
        });
        svc.Save(new List<ExtensionInfo>
        {
            new() { RepoOwner = "b", RepoName = "second", RepoUrl = "u" },
            new() { RepoOwner = "c", RepoName = "third", RepoUrl = "u" }
        });

        var snapshot = svc.Load();
        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot!.Extensions.Count);
        Assert.Equal("second", snapshot.Extensions[0].RepoName);
    }

    [Fact]
    public void Load_RecoversCachedAtTimestamp()
    {
        var svc = new CatalogCacheService(_dir);
        svc.Save(new List<ExtensionInfo>
        {
            new() { RepoOwner = "o", RepoName = "r", RepoUrl = "u" }
        });

        var snapshot = svc.Load();
        Assert.NotNull(snapshot);
        var age = DateTime.UtcNow - snapshot!.CachedAtUtc;
        Assert.True(age.TotalSeconds < 5);
    }
}
