using System.Net;
using LocalChromeStore.Models;
using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class CustomUpdateFeedSourceTests
{
    private static readonly Uri FeedUrl = new("https://catalog.example.test/extensions.json");

    [Fact]
    public async Task DiscoverAsync_ParsesHostedFeedAndRetainsIntegrityMetadata()
    {
        var handler = new StaticHandler();
        handler.Content = """
        {
          "schemaVersion": 1,
          "extensions": [
            {
              "owner": "ExampleOrg",
              "name": "Sample",
              "displayName": "Sample Extension",
              "version": "2.4.0",
              "description": "Hosted outside GitHub discovery.",
              "url": "https://github.com/ExampleOrg/Sample",
              "assetUrl": "https://cdn.example.test/sample-2.4.0.zip",
              "assetName": "sample-2.4.0.zip",
              "assetDigest": "sha256:abc",
              "checksumUrl": "https://cdn.example.test/sample.sha256",
              "checksumName": "sample.sha256"
            }
          ]
        }
        """;

        var source = new CustomUpdateFeedSource(new HttpClient(handler));
        var results = await source.DiscoverAsync(new AppSettings { CustomUpdateFeedUrl = FeedUrl.AbsoluteUri });

        var extension = Assert.Single(results);
        Assert.Equal("ExampleOrg", extension.RepoOwner);
        Assert.Equal("Sample Extension", extension.DisplayName);
        Assert.Equal("2.4.0", extension.DisplayVersion);
        Assert.Equal(DiscoverySource.CustomUpdateFeed, extension.DiscoverySource);
        Assert.Equal(AssetKind.Zip, extension.AssetKind);
        Assert.Equal("sha256:abc", extension.AssetDigest);
        Assert.Equal("https://cdn.example.test/sample.sha256", extension.ChecksumUrl);
    }

    [Fact]
    public async Task DiscoverAsync_FiltersPrereleasesUnlessEnabled()
    {
        var handler = new StaticHandler
        {
            Content = """
            { "extensions": [
              { "owner": "Org", "name": "Stable", "version": "1.0.0" },
              { "owner": "Org", "name": "Preview", "version": "2.0.0-beta", "isPrerelease": true }
            ] }
            """
        };
        var source = new CustomUpdateFeedSource(new HttpClient(handler));

        var stable = await source.DiscoverAsync(new AppSettings { CustomUpdateFeedUrl = FeedUrl.AbsoluteUri });
        var all = await source.DiscoverAsync(new AppSettings
        {
            CustomUpdateFeedUrl = FeedUrl.AbsoluteUri,
            ReleaseChannel = ReleaseChannel.IncludePrereleases
        });

        Assert.Equal("Stable", Assert.Single(stable).RepoName);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, e => e.IsPrerelease);
    }

    [Theory]
    [InlineData("http://catalog.example.test/extensions.json")]
    [InlineData("file:///C:/extensions.json")]
    [InlineData("not-a-url")]
    public async Task DiscoverAsync_RejectsNonHttpsFeed(string url)
    {
        var source = new CustomUpdateFeedSource(new HttpClient(new StaticHandler()));
        var log = new List<string>();

        var results = await source.DiscoverAsync(
            new AppSettings { CustomUpdateFeedUrl = url },
            new ImmediateProgress<string>(log.Add));

        Assert.Empty(results);
        Assert.Contains(log, line => line.Contains("HTTPS", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class StaticHandler : HttpMessageHandler
    {
        public string Content { get; set; } = "{ \"extensions\": [] }";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Content)
            });
    }

    private sealed class ImmediateProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }
}
