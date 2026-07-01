using System.Text.Json;
using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class JsonEventLogTests : IDisposable
{
    private readonly string _dir;

    public JsonEventLogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "LocalChromeStore.Tests", Guid.NewGuid().ToString("N"), "logs");
    }

    public void Dispose()
    {
        var root = Path.GetDirectoryName(_dir)!;
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void Write_CreatesJsonlFileWithValidJson()
    {
        var log = new JsonEventLog(_dir);
        log.Info(EventCategory.Install, "Installed Foo v1.0");

        var files = Directory.GetFiles(_dir, "events-*.jsonl");
        Assert.Single(files);

        var lines = File.ReadAllLines(files[0]);
        Assert.Single(lines);

        var doc = JsonDocument.Parse(lines[0]);
        Assert.Equal("info", doc.RootElement.GetProperty("level").GetString());
        Assert.Equal("install", doc.RootElement.GetProperty("category").GetString());
        Assert.Equal("Installed Foo v1.0", doc.RootElement.GetProperty("message").GetString());
        Assert.True(doc.RootElement.TryGetProperty("ts", out _));
    }

    [Fact]
    public void Write_WarnLevel_RecordsCorrectly()
    {
        var log = new JsonEventLog(_dir);
        log.Warn(EventCategory.Discovery, "! Repo skipped: no manifest");

        var files = Directory.GetFiles(_dir, "events-*.jsonl");
        var line = File.ReadAllLines(files[0])[0];
        var doc = JsonDocument.Parse(line);
        Assert.Equal("warn", doc.RootElement.GetProperty("level").GetString());
        Assert.Equal("discovery", doc.RootElement.GetProperty("category").GetString());
    }

    [Fact]
    public void Write_ErrorLevel_RecordsCorrectly()
    {
        var log = new JsonEventLog(_dir);
        log.Error(EventCategory.Launch, "Browser failed to start");

        var line = File.ReadAllLines(Directory.GetFiles(_dir, "events-*.jsonl")[0])[0];
        var doc = JsonDocument.Parse(line);
        Assert.Equal("error", doc.RootElement.GetProperty("level").GetString());
        Assert.Equal("launch", doc.RootElement.GetProperty("category").GetString());
    }

    [Fact]
    public void Write_WithMetadata_IncludesMetaObject()
    {
        var log = new JsonEventLog(_dir);
        log.Info(EventCategory.Install, "Installed Bar", new Dictionary<string, object?>
        {
            ["owner"] = "SysAdminDoc",
            ["repo"] = "Bar",
            ["version"] = "2.0.0"
        });

        var line = File.ReadAllLines(Directory.GetFiles(_dir, "events-*.jsonl")[0])[0];
        var doc = JsonDocument.Parse(line);
        Assert.True(doc.RootElement.TryGetProperty("meta", out var meta));
        Assert.Equal("SysAdminDoc", meta.GetProperty("owner").GetString());
        Assert.Equal("Bar", meta.GetProperty("repo").GetString());
        Assert.Equal("2.0.0", meta.GetProperty("version").GetString());
    }

    [Fact]
    public void Write_NullMetadata_OmitsMeta()
    {
        var log = new JsonEventLog(_dir);
        log.Info(EventCategory.General, "Simple message");

        var line = File.ReadAllLines(Directory.GetFiles(_dir, "events-*.jsonl")[0])[0];
        var doc = JsonDocument.Parse(line);
        Assert.False(doc.RootElement.TryGetProperty("meta", out _));
    }

    [Fact]
    public void Write_MultipleEvents_AppendsToSameFile()
    {
        var log = new JsonEventLog(_dir);
        log.Info(EventCategory.Install, "First");
        log.Warn(EventCategory.Update, "Second");
        log.Error(EventCategory.Launch, "Third");

        var files = Directory.GetFiles(_dir, "events-*.jsonl");
        Assert.Single(files);

        var lines = File.ReadAllLines(files[0]);
        Assert.Equal(3, lines.Length);

        foreach (var line in lines)
            Assert.True(IsValidJson(line));
    }

    [Theory]
    [InlineData("Installed Foo v1.0", EventCategory.Install)]
    [InlineData("! Install failed for X", EventCategory.Install)]
    [InlineData("Downloaded 100%", EventCategory.Install)]
    [InlineData("Uninstall complete", EventCategory.Uninstall)]
    [InlineData("Launching Chrome", EventCategory.Launch)]
    [InlineData("Browser exited with code 0", EventCategory.Launch)]
    [InlineData("CDP pipe connected", EventCategory.Launch)]
    [InlineData("Update available for Foo", EventCategory.Update)]
    [InlineData("Policy applied to HKLM", EventCategory.Policy)]
    [InlineData("Discovered 5 repos", EventCategory.Discovery)]
    [InlineData("Local source discovered: Foo", EventCategory.Discovery)]
    [InlineData("Settings saved", EventCategory.Settings)]
    [InlineData("Environment import complete", EventCategory.Import)]
    [InlineData("Catalog export saved", EventCategory.Export)]
    [InlineData("A newer version is available", EventCategory.SelfUpdate)]
    [InlineData("A newer LocalChromeStore release is available", EventCategory.SelfUpdate)]
    [InlineData("Something random", EventCategory.General)]
    public void ClassifyLogLine_CategorizesCorrectly(string line, EventCategory expected)
    {
        Assert.Equal(expected, JsonEventLog.ClassifyLogLine(line));
    }

    private static bool IsValidJson(string s)
    {
        try { JsonDocument.Parse(s); return true; }
        catch { return false; }
    }
}
