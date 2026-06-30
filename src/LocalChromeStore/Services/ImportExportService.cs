using System.Text.Json;
using LocalChromeStore.Models;

namespace LocalChromeStore.Services;

/// <summary>Serialized catalog snapshot plus the entry count, for the export status message.</summary>
public sealed record CatalogExport(string Json, int Count);

/// <summary>
/// What an environment-import target resolves to before any permission review:
/// already installed at the requested version, missing from discovery, present but with no
    /// installable release asset/local source, or installable.
/// </summary>
public enum ImportAction { Install, AlreadyCurrent, Missing, MissingAsset }

/// <summary>
/// Machine-readable export projections (F039). Builds the catalog JSON snapshot from the discovered
/// catalog and the installed set, WPF-free and unit-testable. The view model owns the file dialog and
/// write; environment manifest import/export lives in <see cref="EnvironmentManifestService"/>.
/// </summary>
public sealed class ImportExportService
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };

    /// <summary>
    /// Projects each discovered <see cref="ExtensionInfo"/> (merged with its installed record, if any)
    /// into a stable export entry and serializes the list as indented JSON.
    /// </summary>
    public static CatalogExport BuildCatalog(IEnumerable<ExtensionInfo> catalog, IReadOnlyList<InstalledExtension> installed)
    {
        var installedByKey = installed.ToDictionary(
            e => $"{e.RepoOwner}/{e.RepoName}",
            e => e,
            StringComparer.OrdinalIgnoreCase);

        var entries = catalog.Select(info =>
        {
            installedByKey.TryGetValue($"{info.RepoOwner}/{info.RepoName}", out var inst);
            var assetChange = ReleaseProvenance.CompareAssetSnapshot(info, inst);
            return new CatalogExportEntry(
                RepoOwner: info.RepoOwner,
                RepoName: info.RepoName,
                RepoUrl: info.RepoUrl,
                DisplayName: info.DisplayName,
                DisplayVersion: info.DisplayVersion,
                Framework: info.Framework.ToString(),
                ManifestVersion: info.ManifestVersionNumber,
                HasAsset: !string.IsNullOrEmpty(info.AssetUrl),
                HasInstallSource: !string.IsNullOrEmpty(info.AssetUrl) || !string.IsNullOrWhiteSpace(info.LocalSourcePath),
                LocalSourcePath: info.LocalSourcePath,
                AssetName: info.AssetName,
                AssetUrl: info.AssetUrl,
                AssetDigest: info.AssetDigest,
                AssetSizeBytes: info.AssetSizeBytes > 0 ? info.AssetSizeBytes : null,
                AssetId: info.AssetId,
                AssetContentType: info.AssetContentType,
                AssetUploader: info.AssetUploader,
                AssetCreatedAt: info.AssetCreatedAt,
                AssetUpdatedAt: info.AssetUpdatedAt,
                AssetDownloadCount: info.AssetDownloadCount,
                ReleasePublishedAt: info.PublishedAt,
                AssetChangedSinceInstall: assetChange.CanCompare ? assetChange.Changed : null,
                AssetChangeDetail: inst is null ? null : ReleaseProvenance.ChangeStatusLabel(assetChange),
                ChecksumUrl: info.ChecksumUrl,
                ChecksumName: info.ChecksumName,
                DiscoverySource: info.DiscoverySource.ToString(),
                HasRepoManifest: info.HasRepoManifest,
                HomepageUrl: info.HomepageUrl,
                Stars: info.Stars > 0 ? info.Stars : null,
                Freshness: info.Freshness.ToString(),
                IsArchived: info.IsArchived,
                Warnings: info.Warnings.Count > 0 ? info.Warnings : null,
                InstalledVersion: inst?.Version,
                InstalledAt: inst?.InstalledAt,
                ChecksumVerified: inst?.ChecksumVerified,
                ChecksumSource: inst?.ChecksumSource);
        }).ToList();

        return new CatalogExport(JsonSerializer.Serialize(entries, IndentedJson), entries.Count);
    }

    /// <summary>
    /// Classifies an environment-import target from the current install/catalog state, independent of
    /// the permission-review prompt (which only applies to an <see cref="ImportAction.Install"/>).
    /// A target already installed at the requested version is current; one not surfaced by discovery is
    /// missing; one without an installable ZIP/CRX asset or local source is asset-less; otherwise it is installable.
    /// </summary>
    public static ImportAction ClassifyImportTarget(InstalledExtension? existing, string targetVersion, bool hasCard, bool cardHasInstallSource)
    {
        if (existing is not null && existing.Version.Equals(targetVersion, StringComparison.OrdinalIgnoreCase))
            return ImportAction.AlreadyCurrent;
        if (!hasCard) return ImportAction.Missing;
        if (!cardHasInstallSource) return ImportAction.MissingAsset;
        return ImportAction.Install;
    }
}

// F039: catalog export schema.
public sealed record CatalogExportEntry(
    string RepoOwner,
    string RepoName,
    string RepoUrl,
    string DisplayName,
    string DisplayVersion,
    string Framework,
    int? ManifestVersion,
    bool HasAsset,
    bool HasInstallSource,
    string? LocalSourcePath,
    string? AssetName,
    string? AssetUrl,
    string? AssetDigest,
    long? AssetSizeBytes,
    long? AssetId,
    string? AssetContentType,
    string? AssetUploader,
    DateTimeOffset? AssetCreatedAt,
    DateTimeOffset? AssetUpdatedAt,
    long? AssetDownloadCount,
    DateTimeOffset? ReleasePublishedAt,
    bool? AssetChangedSinceInstall,
    string? AssetChangeDetail,
    string? ChecksumUrl,
    string? ChecksumName,
    string DiscoverySource,
    bool HasRepoManifest,
    string? HomepageUrl,
    int? Stars,
    string Freshness,
    bool IsArchived,
    List<string>? Warnings,
    string? InstalledVersion,
    DateTimeOffset? InstalledAt,
    bool? ChecksumVerified,
    string? ChecksumSource);
