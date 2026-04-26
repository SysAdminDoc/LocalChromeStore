using System.Text.Json;
using LocalChromeStore.Models;
using Xunit;

namespace LocalChromeStore.Tests;

/// <summary>
/// Tests for LoadSet model — JSON roundtrip and default values.
/// </summary>
public sealed class LoadSetSerializationTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    [Fact]
    public void LoadSet_DefaultId_IsNonEmptyGuid()
    {
        var set = new LoadSet { Name = "Test" };
        Assert.False(string.IsNullOrWhiteSpace(set.Id));
        Assert.True(Guid.TryParse(set.Id, out _));
    }

    [Fact]
    public void LoadSet_DefaultExtensionKeys_IsNull()
    {
        var set = new LoadSet { Name = "All installed" };
        Assert.Null(set.ExtensionKeys);
    }

    [Fact]
    public void LoadSet_RoundtripWithNullKeys_OmitsKeysInJson()
    {
        var original = new LoadSet { Name = "Slim" };
        var json = JsonSerializer.Serialize(original, JsonOpts);

        Assert.DoesNotContain("ExtensionKeys", json);
        var restored = JsonSerializer.Deserialize<LoadSet>(json, JsonOpts)!;
        Assert.Null(restored.ExtensionKeys);
        Assert.Equal(original.Name, restored.Name);
    }

    [Fact]
    public void LoadSet_RoundtripWithExtensionKeys_PreservesKeys()
    {
        var original = new LoadSet
        {
            Name = "Work",
            ExtensionKeys = ["owner1/repo1", "owner2/repo2"]
        };
        var json = JsonSerializer.Serialize(original, JsonOpts);
        var restored = JsonSerializer.Deserialize<LoadSet>(json, JsonOpts)!;

        Assert.Equal(original.Name, restored.Name);
        Assert.NotNull(restored.ExtensionKeys);
        Assert.Equal(2, restored.ExtensionKeys.Count);
        Assert.Contains("owner1/repo1", restored.ExtensionKeys);
        Assert.Contains("owner2/repo2", restored.ExtensionKeys);
    }

    [Fact]
    public void LoadSet_ListRoundtrip_PreservesAllSets()
    {
        var sets = new List<LoadSet>
        {
            new() { Name = "Alpha", ExtensionKeys = ["a/b"] },
            new() { Name = "Beta" },
        };

        var json = JsonSerializer.Serialize(sets, JsonOpts);
        var restored = JsonSerializer.Deserialize<List<LoadSet>>(json, JsonOpts)!;

        Assert.Equal(2, restored.Count);
        Assert.Equal("Alpha", restored[0].Name);
        Assert.Equal("Beta", restored[1].Name);
        Assert.Single(restored[0].ExtensionKeys!);
        Assert.Null(restored[1].ExtensionKeys);
    }

    [Fact]
    public void LoadSet_CreatedAt_IsPreservedInRoundtrip()
    {
        var now = DateTimeOffset.UtcNow;
        var original = new LoadSet { Name = "Timestamped", CreatedAt = now };
        var json = JsonSerializer.Serialize(original, JsonOpts);
        var restored = JsonSerializer.Deserialize<LoadSet>(json, JsonOpts)!;

        Assert.Equal(now.ToUnixTimeSeconds(), restored.CreatedAt.ToUnixTimeSeconds());
    }
}
