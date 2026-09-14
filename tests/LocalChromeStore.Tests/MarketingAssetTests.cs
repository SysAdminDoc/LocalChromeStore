using System.Buffers.Binary;
using System.Text.RegularExpressions;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class MarketingAssetTests
{
    [Fact]
    public void Readme_HasOneEvergreenHeroAtTheTop()
    {
        var root = FindRepositoryRoot();
        var readme = File.ReadAllText(Path.Combine(root, "README.md"));
        var firstLine = File.ReadLines(Path.Combine(root, "README.md")).First();

        Assert.Equal("![LocalChromeStore hero](assets/marketing/hero.png)", firstLine);
        Assert.Single(Regex.Matches(readme, Regex.Escape("assets/marketing/hero.png")));

        var heroSvg = File.ReadAllText(Path.Combine(root, "assets", "marketing", "hero.svg"));
        Assert.DoesNotMatch(@"(?i)\bv?\d+\.\d+\.\d+\b", heroSvg);
    }

    [Theory]
    [InlineData("assets/marketing/hero.png", 1600, 900)]
    [InlineData("assets/marketing/social-preview.png", 1280, 640)]
    [InlineData("assets/screenshots/catalog.png", 1680, 1080)]
    [InlineData("assets/screenshots/settings.png", 1680, 1260)]
    public void PublishedMarketingImage_HasExpectedDimensions(
        string relativePath,
        int expectedWidth,
        int expectedHeight)
    {
        var path = Path.Combine(FindRepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        var (width, height) = ReadPngDimensions(path);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md"))
                && Directory.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the LocalChromeStore repository root.");
    }

    private static (int Width, int Height) ReadPngDimensions(string path)
    {
        Span<byte> header = stackalloc byte[24];
        using var stream = File.OpenRead(path);
        stream.ReadExactly(header);

        ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
        Assert.True(header[..8].SequenceEqual(pngSignature), $"{path} is not a PNG file.");

        return (
            BinaryPrimitives.ReadInt32BigEndian(header[16..20]),
            BinaryPrimitives.ReadInt32BigEndian(header[20..24]));
    }
}
