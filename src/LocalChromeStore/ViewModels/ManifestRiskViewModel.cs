using System.Collections.ObjectModel;
using System.Windows.Input;
using LocalChromeStore.Models;
using LocalChromeStore.Services;

namespace LocalChromeStore.ViewModels;

public sealed class ManifestRiskViewModel : ViewModelBase
{
    public ExtensionInfo Info { get; }
    private readonly InstalledExtension? _installed;

    public ObservableCollection<PermissionRow> Permissions { get; } = new();
    public ObservableCollection<PermissionRow> HostPermissions { get; } = new();

    public ICommand OpenRepoCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand CloseCommand { get; }

    public string Title => Info.DisplayName;
    public string Repo => $"{Info.RepoOwner}/{Info.RepoName}";
    public string ManifestVersionLabel => Info.ManifestVersionNumber.HasValue
        ? $"manifest_version: {Info.ManifestVersionNumber.Value}"
        : "manifest_version: unknown";
    public string FrameworkLabel => $"Framework: {FrameworkLabels.Label(Info.Framework)}";
    public string SourceLabel
    {
        get
        {
            var src = FrameworkLabels.DiscoveryLabel(Info.DiscoverySource);
            if (Info.DiscoverySource == DiscoverySource.LocalSourceFolder && !string.IsNullOrEmpty(Info.ManifestSourcePath))
                return $"Source: {src} ({Info.ManifestSourcePath})";
            if (Info.DiscoverySource == DiscoverySource.RepoManifest && !string.IsNullOrEmpty(Info.ManifestSourcePath))
                return $"Source: {src} ({Info.ManifestSourcePath})";
            return $"Source: {src}";
        }
    }
    public string AssetLabel => Info.AssetKind == AssetKind.None
        ? "Asset: none — no installable artifact yet."
        : $"Asset: {FrameworkLabels.AssetLabel(Info.AssetKind)} — {Info.AssetName}";
    public string ReleaseProvenanceLabel => $"Provenance: {ReleaseProvenance.CardSummary(Info, _installed)}";
    public string ReleaseProvenanceTooltip => ReleaseProvenance.Detail(Info, _installed);
    public string ChecksumLabel
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Info.LocalSourcePath))
                return "Checksum: local source folder linked directly; no release-asset checksum applies.";
            if (!string.IsNullOrEmpty(Info.ChecksumUrl))
                return $"Checksum: SHA256 sidecar present ({Info.ChecksumName}). Install will fail closed on mismatch.";
            return ExtensionService.TryParseSha256Digest(Info.AssetDigest, out _)
                ? "Checksum: GitHub API SHA256 digest present. Install will fail closed on mismatch."
                : "Checksum: no SHA256 sidecar or GitHub API digest in the release. Install proceeds without integrity verification.";
        }
    }

    public PermissionRisk OverallRisk { get; }
    public string OverallRiskLabel => OverallRisk switch
    {
        PermissionRisk.High => "High-risk permissions requested.",
        PermissionRisk.Medium => "Sensitive permissions requested.",
        PermissionRisk.Low => "Low-risk permissions only.",
        _ => "No permissions requested."
    };
    public bool HasNoPermissions => Permissions.Count == 0 && HostPermissions.Count == 0;

    public bool CanInstall => !string.IsNullOrEmpty(Info.AssetUrl) || !string.IsNullOrWhiteSpace(Info.LocalSourcePath);
    public bool _confirmed;
    public bool Confirmed
    {
        get => _confirmed;
        set => SetField(ref _confirmed, value);
    }

    public ManifestRiskViewModel(ExtensionInfo info, Action onInstall, Action onClose, InstalledExtension? installed = null)
    {
        Info = info;
        _installed = installed;

        foreach (var p in info.Permissions)
            Permissions.Add(PermissionRow.From(PermissionCatalog.Describe(p)));
        foreach (var p in info.OptionalPermissions)
            Permissions.Add(PermissionRow.From(PermissionCatalog.Describe(p, isOptional: true)));
        foreach (var h in info.HostPermissions)
            HostPermissions.Add(PermissionRow.From(PermissionCatalog.DescribeHost(h)));
        foreach (var h in info.OptionalHostPermissions)
            HostPermissions.Add(PermissionRow.From(PermissionCatalog.DescribeHost(h, isOptional: true)));

        var entries = info.Permissions.Select(p => PermissionCatalog.Describe(p))
            .Concat(info.OptionalPermissions.Select(p => PermissionCatalog.Describe(p, isOptional: true)))
            .Concat(info.HostPermissions.Select(h => PermissionCatalog.DescribeHost(h)))
            .Concat(info.OptionalHostPermissions.Select(h => PermissionCatalog.DescribeHost(h, isOptional: true)));
        OverallRisk = PermissionCatalog.Aggregate(entries);

        OpenRepoCommand = new RelayCommand(_ =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(info.RepoUrl) { UseShellExecute = true }); }
            catch { /* swallow — open-url failures are non-fatal */ }
        });
        InstallCommand = new RelayCommand(_ => onInstall(), _ => CanInstall);
        CloseCommand = new RelayCommand(_ => onClose());
    }
}

public sealed class PermissionRow
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required PermissionRisk Risk { get; init; }
    public required bool IsOptional { get; init; }
    public required bool IsHostPermission { get; init; }

    public string RiskLabel => Risk switch
    {
        PermissionRisk.High => "High",
        PermissionRisk.Medium => "Medium",
        PermissionRisk.Low => "Low",
        _ => "Info"
    };
    public string Suffix => IsOptional ? "  (optional)" : string.Empty;
    public bool IsHigh => Risk == PermissionRisk.High;
    public bool IsMedium => Risk == PermissionRisk.Medium;
    public bool IsLow => Risk == PermissionRisk.Low;
    public bool IsInformational => Risk == PermissionRisk.Informational;

    public static PermissionRow From(PermissionEntry entry) => new()
    {
        Name = entry.Name,
        Description = entry.Description,
        Risk = entry.Risk,
        IsOptional = entry.IsOptional,
        IsHostPermission = entry.IsHostPermission
    };
}
