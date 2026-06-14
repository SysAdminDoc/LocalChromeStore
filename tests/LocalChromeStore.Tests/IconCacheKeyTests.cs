using LocalChromeStore.Models;
using LocalChromeStore.ViewModels;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class IconCacheKeyTests
{
    private static ExtensionInfo Info(string? iconUrl, string? version) => new()
    {
        RepoOwner = "owner",
        RepoName = "repo",
        RepoUrl = "https://example.com",
        LatestVersion = version,
        IconUrl = iconUrl
    };

    [Fact]
    public void Key_IsStable_ForSameUrlAndVersion()
    {
        var a = ExtensionCardViewModel.IconCacheKey(Info("https://x/icon.png", "1.0.0"));
        var b = ExtensionCardViewModel.IconCacheKey(Info("https://x/icon.png", "1.0.0"));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Key_Changes_WhenVersionChanges()
    {
        var v1 = ExtensionCardViewModel.IconCacheKey(Info("https://x/icon.png", "1.0.0"));
        var v2 = ExtensionCardViewModel.IconCacheKey(Info("https://x/icon.png", "2.0.0"));
        Assert.NotEqual(v1, v2);
    }

    [Fact]
    public void Key_Changes_WhenIconUrlChanges()
    {
        var a = ExtensionCardViewModel.IconCacheKey(Info("https://x/old.png", "1.0.0"));
        var b = ExtensionCardViewModel.IconCacheKey(Info("https://x/new.png", "1.0.0"));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Key_IsAValidPngFilename_AndIncludesOwnerRepo()
    {
        var key = ExtensionCardViewModel.IconCacheKey(Info("https://x/icon.png", "1.0.0"));
        Assert.StartsWith("owner_repo_", key);
        Assert.EndsWith(".png", key);
        Assert.Equal(-1, key.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()));
    }
}
