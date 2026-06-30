using System.IO.Compression;
using System.Net;
using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class ChromeForTestingInstallerTests
{
    [Fact]
    public async Task InstallLatestStableAsync_DownloadsAndExtractsWindowsChrome()
    {
        var root = TestRoot();
        try
        {
            var settings = Settings(root);
            var platform = CurrentWindowsPlatform();
            var version = "150.0.7871.24";
            var downloadUrl = "https://downloads.example/chrome.zip";
            var handler = new FakeChromeForTestingHandler(
                Metadata(version, platform, downloadUrl),
                downloadUrl,
                CreateChromeZip(platform));
            var installer = new ChromeForTestingInstaller(settings, new HttpClient(handler));
            var progress = new CollectingProgress();

            var result = await installer.InstallLatestStableAsync(progress);

            Assert.False(result.AlreadyInstalled);
            Assert.Equal(version, result.Version);
            Assert.Equal(platform, result.Platform);
            Assert.Equal(downloadUrl, result.DownloadUrl);
            Assert.True(File.Exists(result.ExecutablePath));
            Assert.Contains(Path.Combine(settings.CacheDir, "chrome-for-testing"), result.ExecutablePath);
            Assert.Equal(1, handler.ZipRequests);
            Assert.Contains(progress.Lines, line => line.Contains("downloading", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(progress.Lines, line => line.Contains("installed", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public async Task InstallLatestStableAsync_ReusesExistingChromeForTestingBuild()
    {
        var root = TestRoot();
        try
        {
            var settings = Settings(root);
            var platform = CurrentWindowsPlatform();
            var version = "150.0.7871.24";
            var downloadUrl = "https://downloads.example/chrome.zip";
            var existingDir = Path.Combine(settings.CacheDir, "chrome-for-testing", version, platform, $"chrome-{platform}");
            Directory.CreateDirectory(existingDir);
            var existingExe = Path.Combine(existingDir, "chrome.exe");
            File.WriteAllText(existingExe, "existing");
            var handler = new FakeChromeForTestingHandler(
                Metadata(version, platform, downloadUrl),
                downloadUrl,
                CreateChromeZip(platform));
            var installer = new ChromeForTestingInstaller(settings, new HttpClient(handler));

            var result = await installer.InstallLatestStableAsync();

            Assert.True(result.AlreadyInstalled);
            Assert.Equal(existingExe, result.ExecutablePath);
            Assert.Null(result.DownloadBytes);
            Assert.Equal(0, handler.ZipRequests);
        }
        finally
        {
            TryDelete(root);
        }
    }

    private static SettingsService Settings(string root) =>
        new(Path.Combine(root, "appdata"), Path.Combine(root, "localdata"));

    private static string CurrentWindowsPlatform() => Environment.Is64BitOperatingSystem ? "win64" : "win32";

    private static string Metadata(string version, string platform, string url) => $$"""
        {
          "channels": {
            "Stable": {
              "version": "{{version}}",
              "downloads": {
                "chrome": [
                  { "platform": "{{platform}}", "url": "{{url}}" }
                ]
              }
            }
          }
        }
        """;

    private static byte[] CreateChromeZip(string platform)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry($"chrome-{platform}/chrome.exe");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("fake chrome");
        }
        return ms.ToArray();
    }

    private static string TestRoot() =>
        Path.Combine(Path.GetTempPath(), "LocalChromeStore.Tests", Guid.NewGuid().ToString("N"));

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup only.
        }
    }

    private sealed class CollectingProgress : IProgress<string>
    {
        public List<string> Lines { get; } = [];

        public void Report(string value) => Lines.Add(value);
    }

    private sealed class FakeChromeForTestingHandler : HttpMessageHandler
    {
        private readonly string _metadata;
        private readonly string _downloadUrl;
        private readonly byte[] _zip;

        public FakeChromeForTestingHandler(string metadata, string downloadUrl, byte[] zip)
        {
            _metadata = metadata;
            _downloadUrl = downloadUrl;
            _zip = zip;
        }

        public int ZipRequests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString();
            if (string.Equals(url, ChromeForTestingInstaller.LastKnownGoodDownloadsUrl, StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_metadata)
                });
            }

            if (string.Equals(url, _downloadUrl, StringComparison.Ordinal))
            {
                ZipRequests++;
                var content = new ByteArrayContent(_zip);
                content.Headers.ContentLength = _zip.Length;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = content
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
