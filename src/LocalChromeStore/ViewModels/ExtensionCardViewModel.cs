using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LocalChromeStore.Models;
using LocalChromeStore.Services;
using LocalChromeStore.Views;

namespace LocalChromeStore.ViewModels;

public sealed class ExtensionCardViewModel : ViewModelBase
{
    private readonly ExtensionService _extensions;
    private readonly GitHubService _github;
    private readonly SettingsService _settings;
    private readonly Action<string> _log;
    private readonly Action _refreshParent;
    private readonly Func<Task>? _afterInstall;

    private bool _busy;
    private string? _busyMessage;
    private InstalledExtension? _installed;
    private BitmapImage? _icon;

    public ExtensionInfo Info { get; }

    public ExtensionCardViewModel(
        ExtensionInfo info,
        ExtensionService extensions,
        GitHubService github,
        SettingsService settings,
        Action<string> log,
        Action refreshParent,
        Func<Task>? afterInstall,
        Action<ExtensionCardViewModel> hideRepository)
    {
        Info = info;
        _extensions = extensions;
        _github = github;
        _settings = settings;
        _log = log;
        _refreshParent = refreshParent;
        _afterInstall = afterInstall;
        _installed = extensions.Find(info.RepoOwner, info.RepoName);

        InstallCommand = new AsyncRelayCommand(InstallAsync, _ => CanInstall);
        UninstallCommand = new RelayCommand(_ => Uninstall(), _ => IsInstalled && !Busy);
        OpenRepoCommand = new RelayCommand(_ => OpenUrl(Info.RepoUrl));
        OpenInstallDirCommand = new RelayCommand(_ => OpenDir(), _ => CanOpenInstallDir);
        HideRepositoryCommand = new RelayCommand(_ => hideRepository(this), _ => !Busy);
        InspectCommand = new RelayCommand(_ => InspectAsync(), _ => !Busy);
        OpenHomepageCommand = new RelayCommand(_ => OpenUrl(Info.HomepageUrl!), _ => HasHomepageUrl);
        CopyBuildCommandCommand = new RelayCommand(_ => Clipboard.SetText(BuildCommand), _ => HasBuildCommand);
        _ = LoadIconAsync();
    }

    public string Title => Info.DisplayName;
    public string Version => Info.DisplayVersion;
    public string Description => Info.DisplayDescription;
    public string RepoUrl => Info.RepoUrl;
    public string Repo => $"{Info.RepoOwner}/{Info.RepoName}";
    public string AssetSummary => Info.AssetUrl != null
        ? $"{Info.AssetName} • {FormatSize(Info.AssetSizeBytes)}"
        : "Add a ZIP or CRX release asset to enable install.";
    public string ReleaseSummary => Info.PublishedAt.HasValue
        ? $"Released {Info.PublishedAt.Value.LocalDateTime:MMM d, yyyy}"
        : "Release date unavailable";
    public string Stars => Info.Stars > 0 ? $"★ {Info.Stars}" : string.Empty;
    public bool HasAsset => !string.IsNullOrEmpty(Info.AssetUrl);
    public bool IsInstalled => _installed != null;
    public bool IsUpdateAvailable => IsInstalled
        && !string.Equals(_installed!.Version, Info.DisplayVersion, StringComparison.OrdinalIgnoreCase);
    public bool CanInstall => HasAsset && !Busy;
    public bool CanOpenInstallDir => IsInstalled && !Busy;
    public string InstallButtonLabel => IsInstalled
        ? (string.Equals(_installed!.Version, Info.DisplayVersion, StringComparison.OrdinalIgnoreCase)
            ? "Reinstall" : $"Update to {Info.DisplayVersion}")
        : (HasAsset ? "Install" : "Unavailable");
    public string StatusBadge => IsInstalled
        ? (IsUpdateAvailable ? "Update available" : "Installed")
        : (HasAsset ? "Ready to install" : "Release needed");
    public string InstalledDetail => IsInstalled
        ? $"Local version {_installed!.Version}"
        : "Not installed locally";
    public PermissionDiff UpdatePermissionDiff => _installed is null
        ? PermissionDiff.Empty
        : PermissionDiff.Compare(_installed, Info);
    public bool HasUpdatePermissionExpansion => IsUpdateAvailable && UpdatePermissionDiff.HasAdditions;
    public bool HasHighRiskUpdatePermissionExpansion => HasUpdatePermissionExpansion && UpdatePermissionDiff.HasHighRiskAdditions;

    // Catalog explainability ------------------------------------------------

    public string FrameworkBadge => FrameworkLabels.Label(Info.Framework);
    public bool HasFrameworkBadge => Info.Framework != ExtensionFramework.Unknown;

    public string ManifestVersionBadge => Info.ManifestVersionNumber switch
    {
        3 => "MV3",
        2 => "MV2",
        _ => string.Empty
    };
    public bool HasManifestVersionBadge => Info.ManifestVersionNumber is 2 or 3;
    public bool IsManifestV2 => Info.ManifestVersionNumber == 2;

    public string FreshnessBadge => Info.Freshness switch
    {
        RepoFreshness.Fresh => "Active",
        RepoFreshness.Aging => "Aging",
        RepoFreshness.Stale => "Stale",
        RepoFreshness.Archived => "Archived",
        _ => string.Empty
    };
    public bool HasFreshnessBadge => Info.Freshness is RepoFreshness.Aging or RepoFreshness.Stale or RepoFreshness.Archived;
    public bool IsFreshnessWarn => Info.Freshness is RepoFreshness.Aging;
    public bool IsFreshnessAlert => Info.Freshness is RepoFreshness.Stale or RepoFreshness.Archived;

    public string SourceSummary
    {
        get
        {
            var label = FrameworkLabels.DiscoveryLabel(Info.DiscoverySource);
            if (Info.DiscoverySource == DiscoverySource.RepoManifest && !string.IsNullOrEmpty(Info.ManifestSourcePath))
                return $"{label} ({Info.ManifestSourcePath})";
            return label;
        }
    }

    public string WhyShownDetail
    {
        get
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Repo: {Repo}");
            sb.AppendLine($"Discovery source: {FrameworkLabels.DiscoveryLabel(Info.DiscoverySource)}");
            if (Info.DiscoverySource == DiscoverySource.RepoManifest && !string.IsNullOrEmpty(Info.ManifestSourcePath))
                sb.AppendLine($"  Manifest path: {Info.ManifestSourcePath}");
            if (Info.AssetKind != AssetKind.None)
                sb.AppendLine($"Release asset: {FrameworkLabels.AssetLabel(Info.AssetKind)} ({Info.AssetName})");
            else
                sb.AppendLine("Release asset: none — install will be unavailable until the repo publishes a ZIP/CRX.");
            sb.AppendLine($"Detected framework: {FrameworkLabels.Label(Info.Framework)}");
            if (!string.IsNullOrEmpty(Info.FrameworkEvidence))
                sb.AppendLine($"  Evidence: {Info.FrameworkEvidence}");
            if (Info.ManifestVersionNumber.HasValue)
                sb.AppendLine($"Manifest version: MV{Info.ManifestVersionNumber.Value}");
            else
                sb.AppendLine("Manifest version: unknown (manifest could not be parsed).");
            if (HasBuildCommand)
                sb.AppendLine($"Build command: {BuildCommand}");
            if (HasRepoManifest)
                sb.AppendLine("Catalog manifest: localchromestore.json present.");
            if (!string.IsNullOrEmpty(Info.HomepageUrl))
                sb.AppendLine($"Homepage: {Info.HomepageUrl}");
            if (Info.RepoLastPushedAt.HasValue)
                sb.AppendLine($"Last push: {Info.RepoLastPushedAt.Value.LocalDateTime:yyyy-MM-dd} ({FrameworkLabels.FreshnessLabel(Info.Freshness)})");
            if (Info.IsArchived)
                sb.AppendLine("Archived: yes (read-only on GitHub).");
            if (Info.Warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Warnings:");
                foreach (var w in Info.Warnings) sb.AppendLine($"  • {w}");
            }
            // F049: release readiness checklist
            sb.AppendLine();
            sb.AppendLine("Release readiness:");
            sb.AppendLine($"  {(HasAsset ? "+" : "-")} Release asset (ZIP or CRX)");
            sb.AppendLine($"  {(!string.IsNullOrEmpty(Info.ChecksumUrl) ? "+" : "-")} SHA-256 checksum sidecar");
            sb.AppendLine($"  {(Info.ManifestVersionNumber == 3 ? "+" : "-")} Manifest V3");
            sb.AppendLine($"  {(Info.HasRepoManifest ? "+" : "-")} localchromestore.json catalog manifest");
            sb.AppendLine($"  {(Info.Freshness is RepoFreshness.Fresh or RepoFreshness.Aging && !Info.IsArchived ? "+" : "-")} Repository active within a year");
            var score = new[] { HasAsset, !string.IsNullOrEmpty(Info.ChecksumUrl), Info.ManifestVersionNumber == 3, Info.HasRepoManifest, Info.Freshness is RepoFreshness.Fresh or RepoFreshness.Aging && !Info.IsArchived }.Count(x => x);
            sb.Append($"  Score: {score}/5");
            return sb.ToString().TrimEnd();
        }
    }

    public bool HasWarnings => Info.Warnings.Count > 0;
    public string WarningSummary => Info.Warnings.Count == 0
        ? string.Empty
        : Info.Warnings.Count == 1 ? Info.Warnings[0] : $"{Info.Warnings.Count} warnings — see Why";

    // Trust + risk surfacing — F007/F009/F058/F059.
    public TrustTier Trust
    {
        get
        {
            if (_installed?.ChecksumVerified == true) return TrustTier.ChecksumVerified;
            if (!string.IsNullOrEmpty(Info.ChecksumUrl)) return TrustTier.ChecksumVerifiable;
            if (HasAsset) return TrustTier.ConfiguredRelease;
            return TrustTier.SourceOnly;
        }
    }
    public string TrustBadge => FrameworkLabels.TrustLabel(Trust);
    public bool IsTrustVerified => Trust == TrustTier.ChecksumVerified;
    public bool IsTrustVerifiable => Trust == TrustTier.ChecksumVerifiable;
    public bool IsTrustSourceOnly => Trust == TrustTier.SourceOnly;

    public int PermissionCount => Info.Permissions.Count + Info.OptionalPermissions.Count
        + Info.HostPermissions.Count + Info.OptionalHostPermissions.Count;
    public PermissionRisk MaxPermissionRisk =>
        PermissionCatalog.Aggregate(
            Info.Permissions.Select(p => PermissionCatalog.Describe(p))
                .Concat(Info.OptionalPermissions.Select(p => PermissionCatalog.Describe(p, isOptional: true)))
                .Concat(Info.HostPermissions.Select(h => PermissionCatalog.DescribeHost(h)))
                .Concat(Info.OptionalHostPermissions.Select(h => PermissionCatalog.DescribeHost(h, isOptional: true))));
    public bool HasHighRiskPermissions => MaxPermissionRisk == PermissionRisk.High;
    public string PermissionSummary
    {
        get
        {
            if (PermissionCount == 0) return "No permissions declared.";
            var parts = new List<string> { $"{PermissionCount} permission{(PermissionCount == 1 ? "" : "s")}" };
            if (HasHighRiskPermissions) parts.Add("includes high-risk");
            else if (MaxPermissionRisk == PermissionRisk.Medium) parts.Add("includes sensitive");
            return string.Join(" · ", parts);
        }
    }

    public BitmapImage? Icon
    {
        get => _icon;
        private set => SetField(ref _icon, value);
    }

    public bool Busy
    {
        get => _busy;
        private set
        {
            if (SetField(ref _busy, value))
            {
                OnPropertyChanged(nameof(CanInstall));
                OnPropertyChanged(nameof(CanOpenInstallDir));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string? BusyMessage
    {
        get => _busyMessage;
        private set => SetField(ref _busyMessage, value);
    }

    public ICommand InstallCommand { get; }
    public ICommand UninstallCommand { get; }
    public ICommand OpenRepoCommand { get; }
    public ICommand OpenInstallDirCommand { get; }
    public ICommand HideRepositoryCommand { get; }
    public ICommand InspectCommand { get; }
    // F004: repo manifest homepage link
    public ICommand OpenHomepageCommand { get; }
    // F026: copy build command to clipboard
    public ICommand CopyBuildCommandCommand { get; }

    // F004: localchromestore.json
    public bool HasRepoManifest => Info.HasRepoManifest;
    public bool HasHomepageUrl  => !string.IsNullOrEmpty(Info.HomepageUrl);

    // F026: build command dry-run
    public string BuildCommand    => FrameworkLabels.BuildCommand(Info.Framework);
    public bool   HasBuildCommand => !string.IsNullOrEmpty(BuildCommand);

    private void InspectAsync()
    {
        var owner = Application.Current?.MainWindow;
        if (owner is null) return;
        ManifestRiskWindow.Show(owner, Info, out var requested);
        if (requested && CanInstall)
            _ = InstallAsync(null);
    }

    private async Task InstallAsync(object? _)
    {
        if (!HasAsset) return;
        if (IsUpdateAvailable && !ConfirmUpdatePermissionExpansion()) return;

        var installed = false;
        Busy = true;
        try
        {
            BusyMessage = "Preparing download...";
            var bytesProgress = new Progress<long>(b =>
            {
                if (Info.AssetSizeBytes > 0)
                {
                    var pct = (int)Math.Min(100, b * 100L / Info.AssetSizeBytes);
                    BusyMessage = $"Downloading {pct}%";
                }
                else
                {
                    BusyMessage = $"Downloading {FormatSize(b)}";
                }
            });
            var logProgress = new Progress<string>(_log);
            _installed = await _extensions.InstallAsync(Info, logProgress, bytesProgress);
            BusyMessage = "Installed";
            RaiseAllChanged();
            _refreshParent();
            installed = true;
        }
        catch (Exception ex)
        {
            _log($"! Install failed for {Repo}: {ex.Message}");
        }
        finally
        {
            Busy = false;
            BusyMessage = null;
        }

        if (installed && _afterInstall is not null)
        {
            try { await _afterInstall(); }
            catch (Exception ex) { _log($"! Post-install action failed for {Repo}: {ex.Message}"); }
        }
    }

    private bool ConfirmUpdatePermissionExpansion()
    {
        var diff = UpdatePermissionDiff;
        if (!diff.HasAdditions) return true;

        var confirm = MessageBox.Show(
            $"Update {Title}?\n\nThis update adds extension access:\n\n{diff.FormatAddedForPrompt()}\n\nInstall this update anyway?",
            "Review update permissions",
            MessageBoxButton.YesNo,
            diff.HasHighRiskAdditions ? MessageBoxImage.Warning : MessageBoxImage.Question);
        if (confirm == MessageBoxResult.Yes)
        {
            _log($"Permission expansion approved for {Repo}: {diff.AddedSummary}.");
            return true;
        }

        _log($"Update cancelled for {Repo}: permission expansion was not approved.");
        return false;
    }

    private void Uninstall()
    {
        var confirm = MessageBox.Show(
            $"Remove the local copy of {Title}?\n\nThe GitHub repository and release assets are not changed.",
            "Uninstall extension",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirm != MessageBoxResult.Yes) return;

        Busy = true;
        try
        {
            BusyMessage = "Removing local copy...";
            var logProgress = new Progress<string>(_log);
            _extensions.Uninstall(Info.RepoOwner, Info.RepoName, logProgress);
            _installed = null;
            RaiseAllChanged();
            _refreshParent();
        }
        catch (Exception ex)
        {
            _log($"! Uninstall failed for {Repo}: {ex.Message}");
        }
        finally
        {
            Busy = false;
            BusyMessage = null;
        }
    }

    private void OpenDir()
    {
        if (_installed == null) return;
        if (!Directory.Exists(_installed.InstallPath)) return;
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_installed.InstallPath}\"") { UseShellExecute = true }); }
        catch (Exception ex) { _log($"! open dir failed: {ex.Message}"); }
    }

    private void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { _log($"! open url failed: {ex.Message}"); }
    }

    private async Task LoadIconAsync()
    {
        try
        {
            var cacheKey = $"{Info.RepoOwner}_{Info.RepoName}.png";
            var cachePath = Path.Combine(_settings.IconCacheDir, cacheKey);
            byte[]? bytes = null;
            if (File.Exists(cachePath))
                bytes = await File.ReadAllBytesAsync(cachePath);
            else if (!string.IsNullOrEmpty(Info.IconUrl))
            {
                bytes = await _github.TryDownloadIconAsync(Info.IconUrl);
                if (bytes != null) await File.WriteAllBytesAsync(cachePath, bytes);
            }
            if (bytes == null || bytes.Length == 0) return;

            var bmp = new BitmapImage();
            using var ms = new MemoryStream(bytes);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            Icon = bmp;
        }
        catch { /* ignore — fall back to placeholder */ }
    }

    public void RaiseAllChanged()
    {
        OnPropertyChanged(nameof(IsInstalled));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(InstallButtonLabel));
        OnPropertyChanged(nameof(StatusBadge));
        OnPropertyChanged(nameof(HasAsset));
        OnPropertyChanged(nameof(IsUpdateAvailable));
        OnPropertyChanged(nameof(UpdatePermissionDiff));
        OnPropertyChanged(nameof(HasUpdatePermissionExpansion));
        OnPropertyChanged(nameof(HasHighRiskUpdatePermissionExpansion));
        OnPropertyChanged(nameof(CanOpenInstallDir));
        OnPropertyChanged(nameof(InstalledDetail));
        OnPropertyChanged(nameof(Trust));
        OnPropertyChanged(nameof(TrustBadge));
        OnPropertyChanged(nameof(IsTrustVerified));
        OnPropertyChanged(nameof(IsTrustVerifiable));
        OnPropertyChanged(nameof(IsTrustSourceOnly));
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "?";
        string[] u = ["B", "KB", "MB", "GB"];
        double v = bytes;
        int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.##} {u[i]}";
    }
}
