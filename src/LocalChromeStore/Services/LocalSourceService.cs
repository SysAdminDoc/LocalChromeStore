using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalChromeStore.Models;

namespace LocalChromeStore.Services;

public sealed record LocalSourceResolution(string ConfiguredPath, string ExtensionRoot, string ManifestPath, string RelativePath);

public sealed class LocalSourceService
{
    private static readonly string[] CandidateManifestPaths =
    [
        "manifest.json",
        ".output/chrome-mv3/manifest.json",
        ".output/chrome-mv3-prod/manifest.json",
        ".output/chrome-mv3-dev/manifest.json",
        "build/chrome-mv3-prod/manifest.json",
        "build/chrome-mv3-dev/manifest.json",
        "src/manifest.json",
        "dist/manifest.json",
        "extension/manifest.json",
        "public/manifest.json"
    ];

    public IReadOnlyList<ExtensionInfo> Discover(IEnumerable<string> sourceFolders, IProgress<string>? log = null)
    {
        var results = new List<ExtensionInfo>();
        foreach (var raw in sourceFolders.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var path = NormalizePath(raw);
            try
            {
                var info = DiscoverOne(path);
                if (info is not null)
                {
                    results.Add(info);
                    var location = path.Equals(info.LocalSourcePath, StringComparison.OrdinalIgnoreCase)
                        ? path
                        : $"{path} -> {info.LocalSourcePath}";
                    log?.Report($"Local source discovered: {info.DisplayName} ({location})");
                }
                else
                {
                    log?.Report($"! Local source skipped: manifest.json not found at {path} or known build-output folders.");
                }
            }
            catch (Exception ex)
            {
                log?.Report($"! Local source skipped: {path} - {ex.Message}");
            }
        }

        return results;
    }

    public static ExtensionInfo? DiscoverOne(string sourceFolder)
    {
        var resolution = ResolveSourceFolder(sourceFolder);
        if (resolution is null) return null;

        var root = resolution.ConfiguredPath;
        var extensionRoot = resolution.ExtensionRoot;
        var manifestPath = resolution.ManifestPath;

        using var doc = JsonDocument.Parse(
            File.ReadAllText(manifestPath),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

        var dirName = new DirectoryInfo(root).Name;
        var repoName = $"{SanitizeRepoPart(dirName)}-{ShortHash(root)}";
        var info = new ExtensionInfo
        {
            RepoOwner = "local",
            RepoName = repoName,
            RepoUrl = root,
            RepoDescription = resolution.RelativePath == "."
                ? "Local unpacked extension source folder."
                : $"Local unpacked extension build output ({resolution.RelativePath}).",
            LocalSourcePath = extensionRoot,
            LatestVersion = ReadString(doc.RootElement, "version") ?? "0.0.0",
            PublishedAt = Directory.GetLastWriteTimeUtc(extensionRoot),
            ManifestSourcePath = manifestPath,
            DiscoverySource = DiscoverySource.LocalSourceFolder,
            AssetKind = AssetKind.LocalFolder,
            AssetName = resolution.RelativePath == "."
                ? dirName
                : $"{dirName} / {resolution.RelativePath.Replace('\\', '/')}",
            Topics = "local-source",
            Freshness = RepoFreshness.Fresh,
            RepoLastPushedAt = Directory.GetLastWriteTimeUtc(extensionRoot)
        };

        EnrichFromManifest(info, doc.RootElement);
        DetectFramework(root, info);
        if (info.Framework == ExtensionFramework.Unknown && !extensionRoot.Equals(root, StringComparison.OrdinalIgnoreCase))
            DetectFramework(extensionRoot, info);
        return info;
    }

    public static LocalSourceResolution? ResolveSourceFolder(string sourceFolder)
    {
        var root = NormalizePath(sourceFolder);
        if (!Directory.Exists(root)) return null;

        foreach (var relativeManifest in CandidateManifestPaths)
        {
            var manifestPath = Path.GetFullPath(Path.Combine(root, relativeManifest));
            if (!File.Exists(manifestPath)) continue;

            var extensionRoot = Path.GetDirectoryName(manifestPath)!;
            var relativeRoot = Path.GetDirectoryName(relativeManifest);
            var relativeLabel = string.IsNullOrWhiteSpace(relativeRoot)
                ? "."
                : relativeRoot.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            return new LocalSourceResolution(root, extensionRoot, manifestPath, relativeLabel);
        }

        return null;
    }

    private static void EnrichFromManifest(ExtensionInfo info, JsonElement root)
    {
        info.ManifestName = ReadString(root, "name");
        info.ManifestVersion = ReadString(root, "version");
        info.ManifestDescription = ReadString(root, "description");
        if (root.TryGetProperty("manifest_version", out var mv))
        {
            if (mv.ValueKind == JsonValueKind.Number && mv.TryGetInt32(out var mvNumber))
                info.ManifestVersionNumber = mvNumber;
            else if (mv.ValueKind == JsonValueKind.String && int.TryParse(mv.GetString(), out var mvParsed))
                info.ManifestVersionNumber = mvParsed;
        }

        AppendStringArray(root, "permissions", info.Permissions);
        AppendStringArray(root, "optional_permissions", info.OptionalPermissions);
        AppendStringArray(root, "host_permissions", info.HostPermissions);
        AppendStringArray(root, "optional_host_permissions", info.OptionalHostPermissions);

        if (root.TryGetProperty("options_page", out var optPage) && optPage.ValueKind == JsonValueKind.String)
            info.OptionsPage = optPage.GetString();
        else if (root.TryGetProperty("options_ui", out var optUi) && optUi.ValueKind == JsonValueKind.Object
            && optUi.TryGetProperty("page", out var optUiPage) && optUiPage.ValueKind == JsonValueKind.String)
            info.OptionsPage = optUiPage.GetString();
        if (root.TryGetProperty("devtools_page", out var devtools) && devtools.ValueKind == JsonValueKind.String)
            info.DevtoolsPage = devtools.GetString();

        if (info.ManifestVersionNumber == 2 && info.Permissions.Count > 0)
        {
            var hosts = info.Permissions.Where(LooksLikeHostPattern).ToList();
            foreach (var host in hosts)
            {
                info.Permissions.Remove(host);
                if (!info.HostPermissions.Contains(host, StringComparer.OrdinalIgnoreCase))
                    info.HostPermissions.Add(host);
            }
        }
    }

    private static void DetectFramework(string root, ExtensionInfo info)
    {
        var packageJson = Path.Combine(root, "package.json");
        if (File.Exists(packageJson))
        {
            var text = File.ReadAllText(packageJson);
            if (text.Contains("\"wxt\"", StringComparison.OrdinalIgnoreCase) || text.Contains("@wxt-dev", StringComparison.OrdinalIgnoreCase))
                SetFramework(info, ExtensionFramework.Wxt, "package.json references WXT");
            else if (text.Contains("plasmo", StringComparison.OrdinalIgnoreCase))
                SetFramework(info, ExtensionFramework.Plasmo, "package.json references Plasmo");
            else if (text.Contains("extension.js", StringComparison.OrdinalIgnoreCase) || text.Contains("\"extension\"", StringComparison.OrdinalIgnoreCase))
                SetFramework(info, ExtensionFramework.ExtensionJs, "package.json references Extension.js");
            else if (text.Contains("@crxjs", StringComparison.OrdinalIgnoreCase))
                SetFramework(info, ExtensionFramework.Crxjs, "package.json references CRXJS");
            else if (text.Contains("web-ext", StringComparison.OrdinalIgnoreCase))
                SetFramework(info, ExtensionFramework.WebExt, "package.json references web-ext");
        }

        if (info.Framework != ExtensionFramework.Unknown) return;
        if (info.ManifestVersionNumber == 3)
            SetFramework(info, ExtensionFramework.PlainMv3, "manifest_version: 3");
        else if (info.ManifestVersionNumber == 2)
            SetFramework(info, ExtensionFramework.PlainMv2, "manifest_version: 2");
    }

    private static void SetFramework(ExtensionInfo info, ExtensionFramework framework, string evidence)
    {
        info.Framework = framework;
        info.FrameworkEvidence = evidence;
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static void AppendStringArray(JsonElement root, string propertyName, List<string> target)
    {
        if (!root.TryGetProperty(propertyName, out var arr) || arr.ValueKind != JsonValueKind.Array) return;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) continue;
            var value = item.GetString();
            if (!string.IsNullOrWhiteSpace(value) && !target.Contains(value, StringComparer.OrdinalIgnoreCase))
                target.Add(value);
        }
    }

    private static bool LooksLikeHostPattern(string value) =>
        value.Contains("://", StringComparison.Ordinal) ||
        value.Equals("<all_urls>", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("*.", StringComparison.Ordinal);

    private static string NormalizePath(string value) =>
        Path.GetFullPath(Environment.ExpandEnvironmentVariables(value.Trim().Trim('"')));

    private static string ShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value.ToUpperInvariant()));
        return Convert.ToHexString(bytes, 0, 4).ToLowerInvariant();
    }

    private static string SanitizeRepoPart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        Span<char> buffer = stackalloc char[value.Length];
        var length = 0;
        foreach (var c in value)
            buffer[length++] = Array.IndexOf(invalid, c) >= 0 || char.IsWhiteSpace(c) ? '-' : char.ToLowerInvariant(c);
        var result = new string(buffer[..length]).Trim('-');
        return string.IsNullOrWhiteSpace(result) ? "source" : result;
    }
}
