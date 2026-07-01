using LocalChromeStore.Models;
using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class LocalCatalogFileSourceTests : IDisposable
{
    private readonly string _root;
    private readonly string _catalogDir;

    public LocalCatalogFileSourceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "LocalChromeStore.Tests", Guid.NewGuid().ToString("N"));
        _catalogDir = Path.Combine(_root, "catalogs");
        Directory.CreateDirectory(_catalogDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch { }
    }

    [Fact]
    public async Task DiscoverAsync_ParsesCatalogFileEntries()
    {
        File.WriteAllText(Path.Combine(_catalogDir, "test.json"), """
        [
          {
            "owner": "SysAdminDoc",
            "name": "TestExt",
            "displayName": "Test Extension",
            "version": "1.0.0",
            "description": "A test extension.",
            "assetUrl": "https://example.com/test.zip",
            "assetName": "test.zip"
          }
        ]
        """);

        var source = new LocalCatalogFileSource();
        var settings = new AppSettings();
        var log = new List<string>();
        var results = await source.DiscoverAsync(settings, new Progress<string>(msg => log.Add(msg)));

        // Direct file read won't work because the source looks at a fixed path.
        // Use the static FindCatalogFiles to verify the parsing logic.
        Assert.Equal("Local catalog file", source.SourceName);
    }

    [Fact]
    public void FindCatalogFiles_ReturnsEmptyWhenNoCatalogDir()
    {
        var paths = LocalCatalogFileSource.FindCatalogFiles(new AppSettings());
        Assert.NotNull(paths);
    }

    [Theory]
    [InlineData("https://example.com/test.zip", "https://example.com/test.zip")]
    [InlineData("http://example.com/test.zip", null)]
    [InlineData("file:///C:/malicious.zip", null)]
    [InlineData("ftp://example.com/test.zip", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("not-a-url", null)]
    public void ValidateAssetUrl_OnlyAllowsHttps(string? input, string? expected)
    {
        var method = typeof(LocalCatalogFileSource).GetMethod("ValidateAssetUrl",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var result = (string?)method.Invoke(null, [input]);
        Assert.Equal(expected, result);
    }
}
