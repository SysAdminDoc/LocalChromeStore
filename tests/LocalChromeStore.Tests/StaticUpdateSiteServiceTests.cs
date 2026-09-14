using System.Xml.Linq;
using LocalChromeStore.Models;
using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class StaticUpdateSiteServiceTests
{
    [Fact]
    public void Publish_CopiesCrxAndGeneratesPublicUpdateXml()
    {
        using var temp = TempProject.Create();
        var installed = temp.CreateInstalledExtension("1.2.3");
        var package = new PolicyPackageService(temp.Settings).Prepare(new PolicyPackageRequest(
            installed,
            new Uri("https://old.example.test/sample.crx"),
            new Uri("https://old.example.test/update.xml")));
        var siteRoot = Path.Combine(temp.Root, "site");

        var result = new StaticUpdateSiteService().Publish(
            package,
            siteRoot,
            new Uri("https://pages.example.test/store/"));

        Assert.True(File.Exists(result.CrxPath));
        Assert.True(File.Exists(result.UpdateXmlPath));
        Assert.Equal("https://pages.example.test/store/owner/sample/v1.2.3/sample-v1.2.3.crx", result.CrxUrl.AbsoluteUri);
        Assert.Equal("https://pages.example.test/store/owner/sample/update.xml", result.UpdateXmlUrl.AbsoluteUri);

        var xml = XDocument.Load(result.UpdateXmlPath);
        XNamespace ns = "http://www.google.com/update2/response";
        Assert.Equal(result.CrxUrl.AbsoluteUri, xml.Root?.Element(ns + "app")?.Element(ns + "updatecheck")?.Attribute("codebase")?.Value);
    }

    [Fact]
    public async Task PublishCatalog_WritesConsumableCustomFeed()
    {
        using var temp = TempProject.Create();
        var installed = temp.CreateInstalledExtension("2.0.0");
        var package = new PolicyPackageService(temp.Settings).Prepare(new PolicyPackageRequest(
            installed,
            new Uri("https://old.example.test/sample.crx"),
            new Uri("https://old.example.test/update.xml")));
        var siteRoot = Path.Combine(temp.Root, "site");

        var result = new StaticUpdateSiteService().PublishCatalog(
            [package],
            siteRoot,
            new Uri("https://pages.example.test/"));
        var settings = new AppSettings { CustomUpdateFeedUrl = result.FeedUrl.AbsoluteUri };
        using var http = new HttpClient(new FileHttpHandler(siteRoot));

        var feed = new CustomUpdateFeedSource(http);
        var entries = await feed.DiscoverAsync(settings);

        var entry = Assert.Single(entries);
        Assert.Equal(installed.RepoName, entry.RepoName);
        Assert.Equal(AssetKind.Crx, entry.AssetKind);
        Assert.Equal($"sha256:{package.Package.PackageSha256}", entry.AssetDigest);
    }

    [Fact]
    public void Publish_RejectsNonHttpsBaseUrl()
    {
        using var temp = TempProject.Create();
        var installed = temp.CreateInstalledExtension("1.0.0");
        var package = new PolicyPackageService(temp.Settings).Prepare(new PolicyPackageRequest(
            installed,
            new Uri("https://old.example.test/sample.crx"),
            new Uri("https://old.example.test/update.xml")));

        Assert.Throws<ArgumentException>(() => new StaticUpdateSiteService().Publish(
            package,
            Path.Combine(temp.Root, "site"),
            new Uri("http://pages.example.test/")));
    }

    private sealed class TempProject : IDisposable
    {
        public string Root { get; }
        public SettingsService Settings { get; }

        private TempProject(string root)
        {
            Root = root;
            Settings = new SettingsService(
                appDataRoot: Path.Combine(root, "roaming"),
                localAppDataRoot: Path.Combine(root, "local"));
        }

        public static TempProject Create()
        {
            var root = Path.Combine(Path.GetTempPath(), "lcs-site-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new TempProject(root);
        }

        public InstalledExtension CreateInstalledExtension(string version)
        {
            var installRoot = Path.Combine(Root, "extension");
            Directory.CreateDirectory(installRoot);
            var manifestPath = Path.Combine(installRoot, "manifest.json");
            File.WriteAllText(manifestPath, $$"""
                {
                  "manifest_version": 3,
                  "name": "Sample",
                  "version": "{{version}}"
                }
                """);
            File.WriteAllText(Path.Combine(installRoot, "worker.js"), "chrome.runtime.onInstalled.addListener(() => {});");
            return new InstalledExtension
            {
                RepoOwner = "owner",
                RepoName = "sample",
                Version = "v" + version,
                InstallPath = installRoot,
                ManifestPath = manifestPath,
                InstalledAt = DateTimeOffset.UtcNow,
                DisplayName = "Sample",
                RepoUrl = "https://github.com/owner/sample"
            };
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch { }
        }
    }

    private sealed class FileHttpHandler(string siteRoot) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new InvalidOperationException();
            var relative = uri.AbsolutePath.TrimStart('/');
            var path = Path.GetFullPath(Path.Combine(siteRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            var root = Path.GetFullPath(siteRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(File.ReadAllText(path))
            });
        }
    }
}
