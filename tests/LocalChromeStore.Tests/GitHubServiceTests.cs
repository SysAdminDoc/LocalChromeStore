using LocalChromeStore.Services;
using Octokit;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class GitHubServiceTests
{
    [Theory]
    [InlineData(AccountType.Organization, GitHubService.OwnerListing.Organization)]
    [InlineData(AccountType.User, GitHubService.OwnerListing.User)]
    [InlineData(AccountType.Bot, GitHubService.OwnerListing.User)]
    [InlineData(null, GitHubService.OwnerListing.User)]
    public void ResolveOwnerListing_UsesOrgListingForOrganizationsOnly(AccountType? type, GitHubService.OwnerListing expected)
    {
        Assert.Equal(expected, GitHubService.ResolveOwnerListing(type));
    }

    [Fact]
    public void AppVersion_ReflectsAssemblyVersion_NotHardcodedZeroOne()
    {
        // The footer/UA drift bug shipped "0.1.0" forever; assert it tracks the real assembly.
        var expected = typeof(GitHubService).Assembly.GetName().Version?.ToString(3);
        Assert.Equal(expected, GitHubService.AppVersion);
        Assert.NotEqual("0.1.0", GitHubService.AppVersion);
    }
}
