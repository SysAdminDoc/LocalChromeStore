using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using LocalChromeStore.Models;

namespace LocalChromeStore.Services;

public sealed class ExtensionService
{
    private readonly SettingsService _settings;
    private readonly GitHubService _github;
    private InstalledExtensionsManifest _manifest;

    public ExtensionService(SettingsService settings, GitHubService github)
    {
        _settings = settings;
        _github = github;
        _manifest = settings.LoadManifest();
    }

    public IReadOnlyList<InstalledExtension> Installed => _manifest.Extensions;

    public InstalledExtension? Find(string repoOwner, string repoName)
        => _manifest.Extensions.FirstOrDefault(e =>
            e.RepoOwner.Equals(repoOwner, StringComparison.OrdinalIgnoreCase) &&
            e.RepoName.Equals(repoName, StringComparison.OrdinalIgnoreCase));

    public async Task<InstalledExtension> InstallAsync(ExtensionInfo info, IProgress<string>? log = null, IProgress<long>? bytes = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(info.AssetUrl))
            throw new InvalidOperationException("No release asset to install. The repo has no ZIP/CRX in its latest release yet.");

        log?.Report($"Downloading {info.AssetName} ({Format(info.AssetSizeBytes)})...");
        var data = await _github.DownloadAssetAsync(info.AssetUrl, bytes, ct);

        // F006 — verify SHA256 against sidecar before extraction. Fail closed on mismatch.
        bool checksumVerified = false;
        string? checksumValue = null;
        string? checksumSource = null;
        if (!string.IsNullOrEmpty(info.ChecksumUrl))
        {
            log?.Report($"Verifying checksum from {info.ChecksumName ?? "sidecar"}...");
            var sidecar = await _github.TryDownloadTextAsync(info.ChecksumUrl, ct);
            if (string.IsNullOrWhiteSpace(sidecar))
            {
                throw new InvalidOperationException("Refusing to install: checksum sidecar is present but could not be downloaded. Try again or remove the sidecar from the release.");
            }
            var expected = ParseExpectedSha256(sidecar, info.AssetName);
            if (expected is null)
            {
                throw new InvalidOperationException("Refusing to install: checksum sidecar is present but does not contain a SHA256 hash for this asset.");
            }
            var actual = Convert.ToHexStringLower(SHA256.HashData(data));
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Refusing to install: SHA256 mismatch.\nExpected: {expected}\nActual:   {actual}");
            }
            log?.Report($"Checksum OK (SHA256 {actual[..12]}…).");
            checksumVerified = true;
            checksumValue = actual;
            checksumSource = "sidecar";
        }
        else if (TryParseSha256Digest(info.AssetDigest, out var apiDigest))
        {
            log?.Report("Verifying checksum from GitHub release asset digest...");
            var actual = Convert.ToHexStringLower(SHA256.HashData(data));
            if (!string.Equals(actual, apiDigest, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Refusing to install: GitHub API digest mismatch.\nExpected: {apiDigest}\nActual:   {actual}");
            }
            log?.Report($"Checksum OK (GitHub API SHA256 {actual[..12]}…).");
            checksumVerified = true;
            checksumValue = actual;
            checksumSource = "api-digest";
        }

        var version = info.DisplayVersion.Replace('/', '_').Replace('\\', '_');
        var targetDir = Path.Combine(_settings.ExtensionsRoot, info.RepoOwner, info.RepoName, version);

        // Wipe any prior extraction at the same version path so we start clean.
        if (Directory.Exists(targetDir))
        {
            log?.Report($"Removing previous extraction at {targetDir}");
            Directory.Delete(targetDir, recursive: true);
        }
        Directory.CreateDirectory(targetDir);

        var assetExt = Path.GetExtension(info.AssetName ?? "").ToLowerInvariant();
        if (assetExt == ".zip")
        {
            log?.Report($"Extracting ZIP to {targetDir}");
            ExtractZip(data, targetDir);
        }
        else if (assetExt == ".crx")
        {
            log?.Report($"Stripping CRX header and extracting to {targetDir}");
            ExtractCrx(data, targetDir);
        }
        else
        {
            throw new InvalidOperationException($"Unsupported asset type: {info.AssetName}");
        }

        var manifestPath = LocateManifest(targetDir)
            ?? throw new InvalidOperationException("manifest.json not found in extracted asset.");
        var extensionRoot = Path.GetDirectoryName(manifestPath)!;

        // Replace any prior install row for this repo.
        _manifest.Extensions.RemoveAll(e =>
            e.RepoOwner.Equals(info.RepoOwner, StringComparison.OrdinalIgnoreCase) &&
            e.RepoName.Equals(info.RepoName, StringComparison.OrdinalIgnoreCase));

        var entry = new InstalledExtension
        {
            RepoOwner = info.RepoOwner,
            RepoName = info.RepoName,
            Version = info.DisplayVersion,
            InstallPath = extensionRoot,
            ManifestPath = manifestPath,
            InstalledAt = DateTimeOffset.UtcNow,
            ChecksumVerified = checksumVerified,
            ChecksumAlgorithm = checksumVerified ? "SHA256" : null,
            ChecksumValue = checksumValue,
            ChecksumSource = checksumSource,
            AssetName = info.AssetName,
            AssetDigest = !string.IsNullOrWhiteSpace(info.AssetDigest)
                ? info.AssetDigest
                : (checksumVerified && checksumValue is not null ? $"sha256:{checksumValue}" : null),
            AssetSizeBytes = info.AssetSizeBytes > 0 ? info.AssetSizeBytes : null,
            AssetId = info.AssetId,
            AssetContentType = info.AssetContentType,
            AssetUploader = info.AssetUploader,
            AssetCreatedAt = info.AssetCreatedAt,
            AssetUpdatedAt = info.AssetUpdatedAt,
            AssetDownloadCount = info.AssetDownloadCount,
            ReleasePublishedAt = info.PublishedAt,
            DisplayName = info.DisplayName,
            RepoUrl = info.RepoUrl,
            ManifestVersionNumber = info.ManifestVersionNumber,
            Framework = info.Framework,
            Permissions = info.Permissions.ToList(),
            OptionalPermissions = info.OptionalPermissions.ToList(),
            HostPermissions = info.HostPermissions.ToList(),
            OptionalHostPermissions = info.OptionalHostPermissions.ToList()
        };
        _manifest.Extensions.Add(entry);
        _settings.SaveManifest(_manifest);
        PruneOldVersions(info.RepoOwner, info.RepoName, version, log);
        log?.Report($"Installed {info.DisplayName} v{info.DisplayVersion}");
        return entry;
    }

    public static bool TryParseSha256Digest(string? digest, out string sha256)
    {
        sha256 = string.Empty;
        if (string.IsNullOrWhiteSpace(digest)) return false;
        var trimmed = digest.Trim();
        const string Prefix = "sha256:";
        if (!trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var value = trimmed[Prefix.Length..].Trim();
        if (value.Length != 64) return false;
        foreach (var c in value)
        {
            var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!isHex) return false;
        }
        sha256 = value.ToLowerInvariant();
        return true;
    }

    public void Uninstall(string repoOwner, string repoName, IProgress<string>? log = null)
    {
        var entry = Find(repoOwner, repoName);
        if (entry == null) return;

        var repoDir = Path.Combine(_settings.ExtensionsRoot, repoOwner, repoName);
        try
        {
            if (Directory.Exists(repoDir))
            {
                Directory.Delete(repoDir, recursive: true);
                log?.Report($"Removed {repoDir}");
            }
        }
        catch (Exception ex)
        {
            log?.Report($"! Failed to delete {repoDir}: {ex.Message}");
        }

        _manifest.Extensions.RemoveAll(e =>
            e.RepoOwner.Equals(repoOwner, StringComparison.OrdinalIgnoreCase) &&
            e.RepoName.Equals(repoName, StringComparison.OrdinalIgnoreCase));
        _settings.SaveManifest(_manifest);
        log?.Report($"Uninstalled {repoOwner}/{repoName}");
    }

    public void Reload() => _manifest = _settings.LoadManifest();

    private static void ExtractZip(byte[] data, string targetDir)
    {
        using var ms = new MemoryStream(data);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        // Detect a single top-level folder so we can flatten — many of the user's
        // release ZIPs ship as `<repo>-<version>/...`. Skip the wrapper if found.
        string? wrapper = null;
        var rootEntries = zip.Entries
            .Select(e => e.FullName.Replace('\\', '/'))
            .Where(n => n.Length > 0)
            .ToList();
        if (rootEntries.Count > 0)
        {
            var firstSegments = rootEntries.Select(n => n.Split('/').First()).Distinct().ToList();
            if (firstSegments.Count == 1 && rootEntries.All(n => n.StartsWith(firstSegments[0] + "/", StringComparison.Ordinal) || n == firstSegments[0]))
                wrapper = firstSegments[0];
        }

        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                continue;
            var rel = entry.FullName.Replace('\\', '/');
            if (wrapper != null && rel.StartsWith(wrapper + "/", StringComparison.Ordinal))
                rel = rel.Substring(wrapper.Length + 1);
            if (string.IsNullOrEmpty(rel)) continue;

            var dest = Path.GetFullPath(Path.Combine(targetDir, rel));
            // Zip-slip guard
            if (!dest.StartsWith(Path.GetFullPath(targetDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing to extract path outside target: {entry.FullName}");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using var es = entry.Open();
            using var fs = File.Create(dest);
            es.CopyTo(fs);
        }
    }

    private static void ExtractCrx(byte[] data, string targetDir)
    {
        // CRX2: 'Cr24', version=2, pubkeyLen, sigLen, then ZIP.
        // CRX3: 'Cr24', version=3, headerLen, then ZIP.
        if (data.Length < 16 || data[0] != 'C' || data[1] != 'r' || data[2] != '2' || data[3] != '4')
            throw new InvalidOperationException("Not a valid CRX file (magic mismatch).");

        int version = BitConverter.ToInt32(data, 4);
        int zipStart;
        if (version == 2)
        {
            int pubKeyLen = BitConverter.ToInt32(data, 8);
            int sigLen = BitConverter.ToInt32(data, 12);
            zipStart = 16 + pubKeyLen + sigLen;
        }
        else if (version == 3)
        {
            int headerLen = BitConverter.ToInt32(data, 8);
            zipStart = 12 + headerLen;
        }
        else throw new InvalidOperationException($"Unsupported CRX version: {version}");

        if (zipStart >= data.Length) throw new InvalidOperationException("CRX header indicates zero-length payload.");
        var zipBytes = new byte[data.Length - zipStart];
        Buffer.BlockCopy(data, zipStart, zipBytes, 0, zipBytes.Length);
        ExtractZip(zipBytes, targetDir);
    }

    private static string? LocateManifest(string root)
    {
        var direct = Path.Combine(root, "manifest.json");
        if (File.Exists(direct)) return direct;
        // Search one level deep (handles ZIPs with single wrapper folder we didn't strip).
        foreach (var sub in Directory.EnumerateDirectories(root))
        {
            var nested = Path.Combine(sub, "manifest.json");
            if (File.Exists(nested)) return nested;
        }
        // Fallback: deep search but bounded.
        return Directory.EnumerateFiles(root, "manifest.json", SearchOption.AllDirectories).FirstOrDefault();
    }

    private void PruneOldVersions(string owner, string repo, string keepVersion, IProgress<string>? log)
    {
        var repoDir = Path.Combine(_settings.ExtensionsRoot, owner, repo);
        if (!Directory.Exists(repoDir)) return;
        foreach (var dir in Directory.EnumerateDirectories(repoDir))
        {
            var name = Path.GetFileName(dir);
            if (name.Equals(keepVersion, StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                Directory.Delete(dir, recursive: true);
                log?.Report($"Pruned old version: {name}");
            }
            catch (Exception ex)
            {
                log?.Report($"! Could not prune {dir}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Pulls the SHA256 hex hash for a given asset out of a sidecar text file.
    /// Accepts both single-hash sidecars (`<hash>` or `<hash>  <filename>`) and
    /// multi-line SHA256SUMS-style files where the asset name is the disambiguator.
    /// </summary>
    internal static string? ParseExpectedSha256(string sidecar, string? assetName)
    {
        // Strip BOM and normalise newlines.
        sidecar = sidecar.Replace("\r\n", "\n");
        var lines = sidecar.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0) return null;

        // Single-line, hash-only.
        if (lines.Length == 1)
        {
            var only = lines[0].Trim();
            var hash = only.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return IsHexSha256(hash) ? hash : null;
        }

        // Multi-line SHA256SUMS — the line whose path matches the asset wins.
        if (!string.IsNullOrEmpty(assetName))
        {
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                var parts = line.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2) continue;
                var hash = parts[0].Trim();
                var name = parts[1].Trim().TrimStart('*'); // GNU sha256sum binary marker
                if (!IsHexSha256(hash)) continue;
                if (string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase) ||
                    name.EndsWith("/" + assetName, StringComparison.OrdinalIgnoreCase))
                    return hash;
            }
        }

        // Fallback: first hex SHA256 token in the file.
        foreach (var raw in lines)
        {
            var first = raw.Trim().Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (IsHexSha256(first)) return first;
        }
        return null;
    }

    private static bool IsHexSha256(string? value)
    {
        if (value == null || value.Length != 64) return false;
        foreach (var c in value)
        {
            var ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!ok) return false;
        }
        return true;
    }

    private static string Format(long bytes)
    {
        if (bytes <= 0) return "?";
        string[] units = ["B", "KB", "MB", "GB"];
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:0.##} {units[u]}";
    }
}
