using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalChromeStore.Models;

namespace LocalChromeStore.Services;

public sealed class LocalSourceService
{
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
                    log?.Report($"Local source discovered: {info.DisplayName} ({path})");
                }
                else
                {
                    log?.Report($"! Local source skipped: manifest.json not found at {path}");
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
        var root = NormalizePath(sourceFolder);
        if (!Directory.Exists(root)) return null;

        var manifestPath = Path.Combine(root, "manifest.json");
        if (!File.Exists(manifestPath)) return null;

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
            RepoDescription = "Local unpacked extension source folder.",
            LocalSourcePath = root,
            LatestVersion = ReadString(doc.RootElement, "version") ?? "0.0.0",
            PublishedAt = Directory.GetLastWriteTimeUtc(root),
            ManifestSourcePath = manifestPath,
            DiscoverySource = DiscoverySource.LocalSourceFolder,
            AssetKind = AssetKind.LocalFolder,
            AssetName = dirName,
            Topics = "local-source",
            Freshness = RepoFreshness.Fresh,
            RepoLastPushedAt = Directory.GetLastWriteTimeUtc(root)
        };

        EnrichFromManifest(info, doc.RootElement);
        DetectFramework(root, info);
        return info;
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
