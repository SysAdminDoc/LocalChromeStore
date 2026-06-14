using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class VersionCompareTests
{
    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("v1.0.0", "1.0.0", 0)]      // leading v normalized
    [InlineData("V2.1", "2.1.0", 0)]        // unequal segment counts, zero-padded
    [InlineData("1.10", "1.2", 1)]          // numeric, not lexical: 10 > 2
    [InlineData("1.2", "1.10", -1)]
    [InlineData("v1.0", "1.0", 0)]
    [InlineData("2.0.0", "1.9.9", 1)]
    [InlineData("1.0.0", "2.0.0", -1)]
    [InlineData("1.0.0+build5", "1.0.0+build9", 0)] // build metadata ignored
    public void Compare_NormalizesAndComparesNumerically(string a, string b, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(VersionCompare.Compare(a, b)));
    }

    [Theory]
    [InlineData("1.0.0-beta", "1.0.0", -1)]    // prerelease < release
    [InlineData("1.0.0", "1.0.0-rc.1", 1)]
    [InlineData("1.0.0-alpha", "1.0.0-beta", -1)]
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha", 1)] // more identifiers = higher
    [InlineData("1.0.0-1", "1.0.0-alpha", -1)]      // numeric ranks below alphanumeric
    public void Compare_OrdersPrereleases(string a, string b, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(VersionCompare.Compare(a, b)));
    }

    [Theory]
    [InlineData("v1.1", "1.0", true)]
    [InlineData("1.0", "v1.0", false)]   // equal across v-prefix is not "newer"
    [InlineData("1.2", "1.10", false)]
    [InlineData("2.0.0", "2.0.0-rc1", true)]
    public void IsNewer_DetectsStrictUpgrade(string candidate, string current, bool expected)
    {
        Assert.Equal(expected, VersionCompare.IsNewer(candidate, current));
    }

    [Theory]
    [InlineData(null, null, 0)]
    [InlineData("", "1.0", -1)]
    [InlineData("1.0", "", 1)]
    public void Compare_HandlesEmptyAndNull(string? a, string? b, int expectedSign)
    {
        Assert.Equal(expectedSign, Math.Sign(VersionCompare.Compare(a, b)));
    }
}
