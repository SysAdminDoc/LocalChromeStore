using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace LocalChromeStore.Services;

public interface IChromeForTestingInstaller
{
    string InstallRoot { get; }

    Task<ChromeForTestingInstallResult> InstallLatestStableAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default);
}

public sealed class ChromeForTestingInstaller : IChromeForTestingInstaller
{
    public const string LastKnownGoodDownloadsUrl =
        "https://googlechromelabs.github.io/chrome-for-testing/last-known-good-versions-with-downloads.json";

    private readonly HttpClient _http;

    public ChromeForTestingInstaller(SettingsService settings, HttpClient? http = null)
    {
        InstallRoot = Path.Combine(settings.CacheDir, "chrome-for-testing");
        _http = http ?? new HttpClient();
    }

    public string InstallRoot { get; }

    public async Task<ChromeForTestingInstallResult> InstallLatestStableAsync(
        IProgress<string>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(InstallRoot);
        progress?.Report("Chrome for Testing: resolving latest Stable Windows build.");
        var package = await ResolveLatestStablePackageAsync(ct);

        var installDir = Path.Combine(InstallRoot, package.Version, package.Platform);
        var existingExe = FindChromeExecutable(installDir);
        if (existingExe is not null)
        {
            return new ChromeForTestingInstallResult(
                package.Version,
                package.Platform,
                package.Url,
                installDir,
                existingExe,
                AlreadyInstalled: true,
                DownloadBytes: null);
        }

        var stagingDir = Path.Combine(InstallRoot, ".staging-" + Guid.NewGuid().ToString("N"));
        var archivePath = Path.Combine(stagingDir, "chrome-for-testing.zip");
        var extractedDir = Path.Combine(stagingDir, "extracted");
        Directory.CreateDirectory(stagingDir);

        try
        {
            progress?.Report($"Chrome for Testing: downloading {package.Version} ({package.Platform}).");
            var downloaded = await DownloadAsync(package.Url, archivePath, progress, ct);

            progress?.Report("Chrome for Testing: extracting downloaded package.");
            ZipFile.ExtractToDirectory(archivePath, extractedDir);
            var extractedExe = FindChromeExecutable(extractedDir)
                ?? throw new InvalidDataException("Chrome for Testing package did not contain chrome.exe.");

            if (Directory.Exists(installDir))
                Directory.Delete(installDir, recursive: true);
            Directory.CreateDirectory(Path.GetDirectoryName(installDir)!);
            Directory.Move(extractedDir, installDir);

            var finalExe = Path.Combine(installDir, Path.GetRelativePath(extractedDir, extractedExe));
            progress?.Report($"Chrome for Testing: installed {package.Version} at {finalExe}.");
            return new ChromeForTestingInstallResult(
                package.Version,
                package.Platform,
                package.Url,
                installDir,
                finalExe,
                AlreadyInstalled: false,
                DownloadBytes: downloaded);
        }
        finally
        {
            TryDeleteDirectory(stagingDir);
        }
    }

    private async Task<ChromeForTestingPackage> ResolveLatestStablePackageAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync(LastKnownGoodDownloadsUrl, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

        if (!doc.RootElement.TryGetProperty("channels", out var channels)
            || !channels.TryGetProperty("Stable", out var stable)
            || !stable.TryGetProperty("version", out var versionElement)
            || !stable.TryGetProperty("downloads", out var downloads)
            || !downloads.TryGetProperty("chrome", out var chromeDownloads))
        {
            throw new InvalidDataException("Chrome for Testing metadata does not contain Stable Chrome downloads.");
        }

        var version = versionElement.GetString();
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidDataException("Chrome for Testing metadata does not contain a Stable version.");

        var platform = WindowsPlatform();
        foreach (var item in chromeDownloads.EnumerateArray())
        {
            var candidatePlatform = item.TryGetProperty("platform", out var platformElement)
                ? platformElement.GetString()
                : null;
            if (!string.Equals(candidatePlatform, platform, StringComparison.OrdinalIgnoreCase))
                continue;

            var url = item.TryGetProperty("url", out var urlElement)
                ? urlElement.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(url))
                break;

            return new ChromeForTestingPackage(version, platform, url);
        }

        throw new InvalidDataException($"Chrome for Testing metadata did not include a {platform} Chrome download.");
    }

    private async Task<long> DownloadAsync(
        string url,
        string destinationPath,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var total = response.Content.Headers.ContentLength;
        var nextPercent = 10;
        await using var source = await response.Content.ReadAsStreamAsync(ct);
        await using var target = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);

        var buffer = new byte[128 * 1024];
        long written = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, ct);
            if (read == 0) break;
            await target.WriteAsync(buffer.AsMemory(0, read), ct);
            written += read;

            if (total is > 0)
            {
                var percent = (int)(written * 100 / total.Value);
                if (percent >= nextPercent)
                {
                    progress?.Report($"Chrome for Testing: downloaded {Math.Min(percent, 100)}%.");
                    nextPercent += 10;
                }
            }
        }

        return written;
    }

    private static string WindowsPlatform() => Environment.Is64BitOperatingSystem ? "win64" : "win32";

    private static string? FindChromeExecutable(string root)
    {
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateFiles(root, "chrome.exe", SearchOption.AllDirectories)
            .OrderBy(p => p.Length)
            .FirstOrDefault();
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup; a failed install should still surface the root failure.
        }
    }

    private sealed record ChromeForTestingPackage(string Version, string Platform, string Url);
}

public sealed record ChromeForTestingInstallResult(
    string Version,
    string Platform,
    string DownloadUrl,
    string InstallDirectory,
    string ExecutablePath,
    bool AlreadyInstalled,
    long? DownloadBytes);
