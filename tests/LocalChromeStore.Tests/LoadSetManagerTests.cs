using LocalChromeStore.Models;
using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class LoadSetManagerTests
{
    private static InstalledExtension Ext(string owner, string repo) => new()
    {
        RepoOwner = owner,
        RepoName = repo,
        Version = "1.0.0",
        InstallPath = $@"C:\ext\{owner}\{repo}",
        ManifestPath = $@"C:\ext\{owner}\{repo}\manifest.json",
        InstalledAt = DateTimeOffset.UnixEpoch
    };

    [Fact]
    public void Sentinel_IsRecognizedById()
    {
        var sentinel = LoadSetManager.CreateSentinel();
        Assert.Equal(LoadSetManager.SentinelId, sentinel.Id);
        Assert.True(LoadSetManager.IsSentinel(sentinel));
        Assert.True(LoadSetManager.IsSentinel(null));
        Assert.False(LoadSetManager.IsSentinel(new LoadSet { Name = "named" }));
    }

    [Fact]
    public void ResolveActiveExtensions_SentinelOrNullKeys_ReturnsAll()
    {
        var installed = new[] { Ext("o", "a"), Ext("o", "b") };
        Assert.Equal(2, LoadSetManager.ResolveActiveExtensions(LoadSetManager.CreateSentinel(), installed).Count);
        Assert.Equal(2, LoadSetManager.ResolveActiveExtensions(null, installed).Count);
        Assert.Equal(2, LoadSetManager.ResolveActiveExtensions(
            new LoadSet { Name = "all", ExtensionKeys = null }, installed).Count);
    }

    [Fact]
    public void ResolveActiveExtensions_FiltersByKey_CaseInsensitive()
    {
        var installed = new[] { Ext("o", "a"), Ext("o", "b"), Ext("o", "c") };
        var set = new LoadSet { Name = "set", ExtensionKeys = new() { "O/A", "o/c", "o/missing" } };

        var result = LoadSetManager.ResolveActiveExtensions(set, installed);

        Assert.Equal(new[] { "o/a", "o/c" }, result.Select(e => e.Key));
    }

    [Fact]
    public void Snapshot_CapturesAllInstalledKeys_AndTrimsName()
    {
        var installed = new[] { Ext("o", "a"), Ext("o", "b") };

        var set = LoadSetManager.Snapshot("  My Set  ", installed);

        Assert.Equal("My Set", set.Name);
        Assert.Equal(new[] { "o/a", "o/b" }, set.ExtensionKeys);
    }

    [Fact]
    public void NameExists_IsCaseInsensitive_AndTrims()
    {
        var sets = new[] { new LoadSet { Name = "Dev" }, new LoadSet { Name = "QA" } };

        Assert.True(LoadSetManager.NameExists(sets, "  dev "));
        Assert.False(LoadSetManager.NameExists(sets, "prod"));
    }
}
