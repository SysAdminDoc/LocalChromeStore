using System.IO;
using System.Text;
using System.Text.Json;
using LocalChromeStore.Models;
using LocalChromeStore.Services.Crx;

namespace LocalChromeStore.Services;

public sealed record StaticUpdateSiteResult(
    string SiteRoot,
    string CrxPath,
    string UpdateXmlPath,
    Uri CrxUrl,
    Uri UpdateXmlUrl);

public sealed record StaticUpdateSiteCatalogResult(
    string SiteRoot,
    string FeedPath,
    Uri FeedUrl,
    IReadOnlyList<StaticUpdateSiteResult> Packages);

/// <summary>
/// Publishes policy CRX artifacts and Chrome update manifests into a plain static directory.
/// The resulting directory can be deployed by GitHub Pages, object storage, or any other static
/// host; no GitHub API credentials or server-side runtime are required.
/// </summary>
public sealed class StaticUpdateSiteService
{
    public const string FeedFileName = "extensions.json";

    public StaticUpdateSiteResult Publish(
        PolicyPackageResult package,
        string siteRoot,
        Uri siteBaseUrl,
        IProgress<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        var root = ValidateSiteRoot(siteRoot);
        var baseUrl = ValidateSiteBaseUrl(siteBaseUrl);
        var owner = SafeSegment(package.Installed.RepoOwner, nameof(package.Installed.RepoOwner));
        var repo = SafeSegment(package.Installed.RepoName, nameof(package.Installed.RepoName));
        var version = SafeSegment(package.Installed.Version, nameof(package.Installed.Version));
        var fileName = SafeSegment(Path.GetFileName(package.CrxPath), nameof(package.CrxPath));

        if (!File.Exists(package.CrxPath))
            throw new FileNotFoundException("Policy CRX package was not found.", package.CrxPath);
        if (!Crx3PackageService.IsValidExtensionId(package.Package.ExtensionId))
            throw new InvalidOperationException("Policy package does not contain a valid Chrome extension ID.");

        var packageDirectory = SafeCombine(root, owner, repo, version);
        Directory.CreateDirectory(packageDirectory);
        var publicCrxPath = Path.Combine(packageDirectory, fileName);
        File.Copy(package.CrxPath, publicCrxPath, overwrite: true);

        var updateDirectory = SafeCombine(root, owner, repo);
        Directory.CreateDirectory(updateDirectory);
        var updateXmlPath = Path.Combine(updateDirectory, "update.xml");
        var crxUrl = PublicUri(baseUrl, owner, repo, version, fileName);
        var updateXmlUrl = PublicUri(baseUrl, owner, repo, "update.xml");
        WriteUtf8(updateXmlPath, UpdateXmlService.Create(package.Package.ExtensionId, crxUrl, package.ManifestVersion));

        log?.Report($"Published policy CRX: {publicCrxPath}");
        log?.Report($"Published update.xml: {updateXmlPath}");
        return new StaticUpdateSiteResult(root, publicCrxPath, updateXmlPath, crxUrl, updateXmlUrl);
    }

    public StaticUpdateSiteCatalogResult PublishCatalog(
        IEnumerable<PolicyPackageResult> packages,
        string siteRoot,
        Uri siteBaseUrl,
        IProgress<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var root = ValidateSiteRoot(siteRoot);
        var baseUrl = ValidateSiteBaseUrl(siteBaseUrl);
        var packageList = packages.ToList();
        var results = packageList
            .Select(package => Publish(package, root, baseUrl, log))
            .ToList();

        var entries = packageList.Zip(results, (package, result) => new CustomUpdateFeedEntry
        {
            Owner = package.Installed.RepoOwner,
            Name = package.Installed.RepoName,
            DisplayName = package.Installed.DisplayName,
            Version = package.ManifestVersion,
            Url = package.Installed.RepoUrl ?? $"https://github.com/{package.Installed.RepoOwner}/{package.Installed.RepoName}",
            AssetUrl = result.CrxUrl.AbsoluteUri,
            AssetName = Path.GetFileName(result.CrxPath),
            AssetDigest = $"sha256:{package.Package.PackageSha256}"
        }).ToList();
        var feedPath = Path.Combine(root, FeedFileName);
        var feed = new CustomUpdateFeedDocument { SchemaVersion = 1, Extensions = entries };
        WriteUtf8(feedPath, JsonSerializer.Serialize(feed, JsonOptions));
        var feedUrl = PublicUri(baseUrl, FeedFileName);
        log?.Report($"Published custom update feed: {feedPath}");
        return new StaticUpdateSiteCatalogResult(root, feedPath, feedUrl, results);
    }

    private static string ValidateSiteRoot(string siteRoot)
    {
        if (string.IsNullOrWhiteSpace(siteRoot))
            throw new ArgumentException("Static site root is required.", nameof(siteRoot));
        return Path.GetFullPath(siteRoot);
    }

    private static Uri ValidateSiteBaseUrl(Uri siteBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(siteBaseUrl);
        if (!siteBaseUrl.IsAbsoluteUri || siteBaseUrl.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Static update sites must use an absolute HTTPS base URL.", nameof(siteBaseUrl));
        return new Uri(siteBaseUrl.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
    }

    private static string SafeSegment(string value, string parameterName)
    {
        var segment = PolicyPackageService.SanitizePathSegment(value);
        if (string.IsNullOrWhiteSpace(value)
            || segment is "." or ".."
            || segment.Contains(Path.DirectorySeparatorChar)
            || segment.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException("Path segment is invalid.", parameterName);
        return segment;
    }

    private static string SafeCombine(string root, params string[] segments)
    {
        var candidate = Path.GetFullPath(Path.Combine([root, .. segments]));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            && !candidate.Equals(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Static update site path escaped its configured root.");
        return candidate;
    }

    private static Uri PublicUri(Uri baseUrl, params string[] segments)
    {
        var relative = string.Join('/', segments.Select(Uri.EscapeDataString));
        return new Uri(baseUrl, relative);
    }

    private static void WriteUtf8(string path, string contents)
    {
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temp, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
}
