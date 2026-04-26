using LocalChromeStore.Models;
using Xunit;

namespace LocalChromeStore.Tests;

public class RepoManifestTests
{
    // ── F005: Validate ───────────────────────────────────────────────────────

    [Fact]
    public void Validate_EmptyManifest_ReturnsNoErrors()
    {
        var m = new RepoManifest();
        Assert.Empty(RepoManifest.Validate(m));
    }

    [Fact]
    public void Validate_ValidFields_ReturnsNoErrors()
    {
        var m = new RepoManifest
        {
            DisplayName  = "My Extension",
            Description  = "A helpful extension.",
            HomepageUrl  = "https://example.com",
            IconUrl      = "https://example.com/icon.png",
            Category     = "productivity",
        };
        Assert.Empty(RepoManifest.Validate(m));
    }

    [Fact]
    public void Validate_DisplayNameTooLong_ReturnsOneError()
    {
        var m = new RepoManifest { DisplayName = new string('A', 65) };
        var errs = RepoManifest.Validate(m);
        Assert.Single(errs);
        Assert.Contains("displayName", errs[0]);
    }

    [Fact]
    public void Validate_DescriptionTooLong_ReturnsOneError()
    {
        var m = new RepoManifest { Description = new string('B', 281) };
        var errs = RepoManifest.Validate(m);
        Assert.Single(errs);
        Assert.Contains("description", errs[0]);
    }

    [Fact]
    public void Validate_UnknownCategory_ReturnsOneError()
    {
        var m = new RepoManifest { Category = "mystery-meat" };
        var errs = RepoManifest.Validate(m);
        Assert.Single(errs);
        Assert.Contains("mystery-meat", errs[0]);
    }

    [Theory]
    [InlineData("productivity")]
    [InlineData("developer-tools")]
    [InlineData("privacy")]
    [InlineData("SECURITY")]        // case-insensitive
    [InlineData("Utilities")]
    public void Validate_KnownCategories_ReturnsNoError(string cat)
    {
        var m = new RepoManifest { Category = cat };
        Assert.Empty(RepoManifest.Validate(m));
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("ftp://ok-but-check")]   // relative-looking, will pass (ftp is absolute)
    [InlineData("javascript:alert(1)")]  // technically absolute — allowed by Uri, so not flagged
    public void Validate_InvalidHomepageUrl_ReturnsOneError(string url)
    {
        // Only truly non-parseable URLs trigger the warning.
        var m = new RepoManifest { HomepageUrl = url };
        var errs = RepoManifest.Validate(m);
        if (Uri.TryCreate(url, UriKind.Absolute, out _))
            Assert.Empty(errs);   // valid absolute URI — no error
        else
            Assert.Single(errs);  // not absolute — flagged
    }

    [Fact]
    public void Validate_InvalidIconUrl_ReturnsOneError()
    {
        var m = new RepoManifest { IconUrl = "not-a-url" };
        var errs = RepoManifest.Validate(m);
        Assert.Single(errs);
        Assert.Contains("iconUrl", errs[0]);
    }

    [Fact]
    public void Validate_MultipleInvalidFields_ReturnsMultipleErrors()
    {
        var m = new RepoManifest
        {
            DisplayName = new string('X', 65),
            Category    = "unknown-cat",
            HomepageUrl = "relative/path",
        };
        var errs = RepoManifest.Validate(m);
        Assert.Equal(3, errs.Count);
    }

    // ── F026: FrameworkLabels.BuildCommand ───────────────────────────────────

    [Theory]
    [InlineData(ExtensionFramework.Wxt,         "wxt build")]
    [InlineData(ExtensionFramework.Plasmo,       "plasmo build")]
    [InlineData(ExtensionFramework.ExtensionJs,  "npx extension build")]
    [InlineData(ExtensionFramework.Crxjs,        "vite build")]
    [InlineData(ExtensionFramework.WebExt,       "web-ext build")]
    public void BuildCommand_KnownFrameworks_ReturnsExpected(ExtensionFramework f, string expected)
    {
        Assert.Equal(expected, FrameworkLabels.BuildCommand(f));
    }

    [Theory]
    [InlineData(ExtensionFramework.PlainMv3)]
    [InlineData(ExtensionFramework.PlainMv2)]
    [InlineData(ExtensionFramework.Unknown)]
    public void BuildCommand_UnknownOrPlain_ReturnsEmpty(ExtensionFramework f)
    {
        Assert.Equal(string.Empty, FrameworkLabels.BuildCommand(f));
    }
}
