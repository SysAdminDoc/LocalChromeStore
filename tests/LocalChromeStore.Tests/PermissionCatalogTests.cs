using LocalChromeStore.Models;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class PermissionCatalogTests
{
    // ── Describe: known permissions ──────────────────────────────────────────

    [Theory]
    [InlineData("webRequest", PermissionRisk.High)]
    [InlineData("webRequestBlocking", PermissionRisk.High)]
    [InlineData("cookies", PermissionRisk.High)]
    [InlineData("history", PermissionRisk.High)]
    [InlineData("debugger", PermissionRisk.High)]
    [InlineData("nativeMessaging", PermissionRisk.High)]
    [InlineData("downloads", PermissionRisk.High)]
    [InlineData("clipboardRead", PermissionRisk.High)]
    [InlineData("userScripts", PermissionRisk.High)]
    public void Describe_ReturnsHigh_ForBroadAccessPermissions(string permission, PermissionRisk expected)
    {
        var entry = PermissionCatalog.Describe(permission);
        Assert.Equal(expected, entry.Risk);
        Assert.False(entry.IsHostPermission);
    }

    [Theory]
    [InlineData("tabs", PermissionRisk.Medium)]
    [InlineData("scripting", PermissionRisk.Medium)]
    [InlineData("declarativeNetRequest", PermissionRisk.Medium)]
    [InlineData("sessions", PermissionRisk.Medium)]
    [InlineData("identity", PermissionRisk.Medium)]
    [InlineData("clipboardWrite", PermissionRisk.Medium)]
    [InlineData("webNavigation", PermissionRisk.Medium)]
    public void Describe_ReturnsMedium_ForPrivilegedPermissions(string permission, PermissionRisk expected)
    {
        var entry = PermissionCatalog.Describe(permission);
        Assert.Equal(expected, entry.Risk);
    }

    [Theory]
    [InlineData("storage", PermissionRisk.Low)]
    [InlineData("activeTab", PermissionRisk.Low)]
    [InlineData("sidePanel", PermissionRisk.Low)]
    public void Describe_ReturnsLow_ForMinorPermissions(string permission, PermissionRisk expected)
    {
        var entry = PermissionCatalog.Describe(permission);
        Assert.Equal(expected, entry.Risk);
    }

    [Theory]
    [InlineData("alarms", PermissionRisk.Informational)]
    [InlineData("contextMenus", PermissionRisk.Informational)]
    [InlineData("idle", PermissionRisk.Informational)]
    [InlineData("windows", PermissionRisk.Informational)]
    public void Describe_ReturnsInformational_ForBenignPermissions(string permission, PermissionRisk expected)
    {
        var entry = PermissionCatalog.Describe(permission);
        Assert.Equal(expected, entry.Risk);
    }

    [Fact]
    public void Describe_UnknownPermission_ReturnsInformationalWithDescription()
    {
        var entry = PermissionCatalog.Describe("someUnknownCustomPermission");
        Assert.Equal(PermissionRisk.Informational, entry.Risk);
        Assert.NotEmpty(entry.Description);
        Assert.False(entry.IsHostPermission);
    }

    [Fact]
    public void Describe_IsOptional_IsReflectedOnEntry()
    {
        var entry = PermissionCatalog.Describe("storage", isOptional: true);
        Assert.True(entry.IsOptional);
    }

    [Fact]
    public void Describe_LookupIsCaseInsensitive()
    {
        var lower = PermissionCatalog.Describe("webrequest");
        var upper = PermissionCatalog.Describe("WEBREQUEST");
        var canon = PermissionCatalog.Describe("webRequest");
        Assert.Equal(canon.Risk, lower.Risk);
        Assert.Equal(canon.Risk, upper.Risk);
    }

    // ── DescribeHost ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("<all_urls>")]
    [InlineData("https://*/*")]
    [InlineData("http://*/*")]
    public void DescribeHost_ReturnsHigh_ForUniversalHostPatterns(string host)
    {
        var entry = PermissionCatalog.DescribeHost(host);
        Assert.Equal(PermissionRisk.High, entry.Risk);
        Assert.True(entry.IsHostPermission);
    }

    [Theory]
    [InlineData("https://*.example.com/*")]
    [InlineData("http://*google.com/*")]
    public void DescribeHost_ReturnsMediumOrHigher_ForWildcardSubdomains(string host)
    {
        var entry = PermissionCatalog.DescribeHost(host);
        Assert.True(entry.Risk >= PermissionRisk.Medium);
        Assert.True(entry.IsHostPermission);
    }

    [Fact]
    public void DescribeHost_ReturnsLow_ForExactHost()
    {
        var entry = PermissionCatalog.DescribeHost("https://api.github.com/");
        Assert.Equal(PermissionRisk.Low, entry.Risk);
        Assert.True(entry.IsHostPermission);
    }

    [Fact]
    public void DescribeHost_ReturnsInformational_ForEmptyHost()
    {
        var entry = PermissionCatalog.DescribeHost(string.Empty);
        Assert.Equal(PermissionRisk.Informational, entry.Risk);
    }

    // ── Aggregate ────────────────────────────────────────────────────────────

    [Fact]
    public void Aggregate_EmptyList_ReturnsInformational()
    {
        Assert.Equal(PermissionRisk.Informational, PermissionCatalog.Aggregate([]));
    }

    [Fact]
    public void Aggregate_SingleHigh_ReturnsHigh()
    {
        var entries = new[]
        {
            PermissionCatalog.Describe("alarms"),
            PermissionCatalog.Describe("storage"),
            PermissionCatalog.Describe("webRequest"),
        };
        Assert.Equal(PermissionRisk.High, PermissionCatalog.Aggregate(entries));
    }

    [Fact]
    public void Aggregate_HighDominatesAll()
    {
        var entries = new[]
        {
            PermissionCatalog.Describe("cookies"),
            PermissionCatalog.Describe("tabs"),
            PermissionCatalog.Describe("storage"),
        };
        Assert.Equal(PermissionRisk.High, PermissionCatalog.Aggregate(entries));
    }

    [Fact]
    public void Aggregate_OnlyInformational_ReturnsInformational()
    {
        var entries = new[]
        {
            PermissionCatalog.Describe("alarms"),
            PermissionCatalog.Describe("contextMenus"),
        };
        Assert.Equal(PermissionRisk.Informational, PermissionCatalog.Aggregate(entries));
    }
}
