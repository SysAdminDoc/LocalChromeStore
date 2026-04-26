using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using LocalChromeStore.Models;
using LocalChromeStore.Services;
using Microsoft.Win32;

namespace LocalChromeStore.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly GitHubService _github;
    private readonly ExtensionService _extensions;
    private readonly BrowserLauncher _launcher;
    private readonly Dispatcher_LogSink _logSink;
    private AppSettings _settings;
    private bool _busy;
    private string _statusText = "Ready.";
    private string _searchText = string.Empty;
    private bool _showInstalledOnly;
    private BrowserInfo? _selectedBrowser;
    private string _githubUserInput = "";
    private string _githubTokenInput = "";
    private string _newOwnerInput = "";
    private string? _selectedExtraOwner;
    private string _rateLimitSummary = "Rate limit: not yet measured.";
    private string _githubStatusSummary = "GitHub: ready.";
    private bool _isDegraded;
    private string _launchUrlInput = string.Empty;
    private bool _launchWithTemporaryProfile;
    private LoadSet? _selectedLoadSet;
    private string _newLoadSetNameInput = string.Empty;

    private static readonly LoadSet SentinelLoadSet = new() { Id = "__all__", Name = "All installed" };

    public ObservableCollection<ExtensionCardViewModel> Extensions { get; } = new();
    public ICollectionView ExtensionsView { get; }
    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<BrowserInfo> Browsers { get; } = new();
    public ObservableCollection<string> ExtraOwners { get; } = new();
    public ObservableCollection<LoadSet> LoadSets { get; } = new();
    public ObservableCollection<string> HiddenRepos { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand UpdateAllCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand SaveAndRefreshCommand { get; }
    public ICommand LaunchBrowserCommand { get; }
    public ICommand LaunchInstalledOnlyCommand { get; }
    public ICommand OpenInstallDirCommand { get; }
    public ICommand ClearHiddenReposCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand AddExtraOwnerCommand { get; }
    public ICommand RemoveExtraOwnerCommand { get; }
    public ICommand OpenBrowserExtensionsPageCommand { get; }
    public ICommand ExportDiagnosticsCommand { get; }
    public ICommand ExportEnvironmentCommand { get; }
    public ICommand ImportEnvironmentCommand { get; }
    public ICommand CopyLaunchArgumentsCommand { get; }
    public ICommand CreateLoadSetCommand { get; }
    public ICommand DeleteLoadSetCommand { get; }
    public ICommand RestoreHiddenRepoCommand { get; }

    public MainViewModel()
    {
        _settingsService = new SettingsService();
        _github = new GitHubService(_settingsService);
        _extensions = new ExtensionService(_settingsService, _github);
        _launcher = new BrowserLauncher(_extensions);
        _settings = _settingsService.Load();
        _logSink = new Dispatcher_LogSink(LogLines);

        _githubUserInput = _settings.GitHubUser;
        _githubTokenInput = _settings.GitHubToken ?? string.Empty;
        _launchUrlInput = _settings.LaunchUrl ?? string.Empty;
        _launchWithTemporaryProfile = _settings.LaunchWithTemporaryProfile;
        ReloadExtraOwnersFromSettings();
        SyncHiddenReposCollection();
        ReloadLoadSetsFromService();

        ExtensionsView = CollectionViewSource.GetDefaultView(Extensions);
        ExtensionsView.Filter = FilterExtension;
        ExtensionsView.SortDescriptions.Add(new SortDescription(nameof(ExtensionCardViewModel.Title), ListSortDirection.Ascending));

        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => !Busy);
        UpdateAllCommand = new AsyncRelayCommand(
            _ => UpdateAvailableExtensionsAsync(confirmFirst: true, operationName: "Manual update"),
            _ => !Busy && HasInstallableUpdates);
        SaveSettingsCommand = new RelayCommand(_ => { SaveSettings(); });
        SaveAndRefreshCommand = new AsyncRelayCommand(async _ =>
        {
            if (SaveSettings())
                await RefreshAsync();
        }, _ => !Busy);
        LaunchBrowserCommand = new RelayCommand(_ => LaunchBrowser(), _ => CanLaunchBrowser);
        LaunchInstalledOnlyCommand = new RelayCommand(_ => LaunchBrowser(), _ => CanLaunchBrowser);
        OpenInstallDirCommand = new RelayCommand(_ => OpenInstallDir());
        ClearHiddenReposCommand = new AsyncRelayCommand(async _ =>
        {
            if (ClearHiddenRepos())
                await RefreshAsync();
        }, _ => HasHiddenRepos && !Busy);
        ClearLogCommand = new RelayCommand(_ => LogLines.Clear());
        AddExtraOwnerCommand = new RelayCommand(_ => AddExtraOwner(), _ => !string.IsNullOrWhiteSpace(NewOwnerInput));
        RemoveExtraOwnerCommand = new RelayCommand(o => RemoveExtraOwner(o as string ?? SelectedExtraOwner), o => (o as string ?? SelectedExtraOwner) is { Length: > 0 });
        OpenBrowserExtensionsPageCommand = new RelayCommand(_ => OpenBrowserExtensionsPage(), _ => SelectedBrowser != null);
        ExportDiagnosticsCommand = new RelayCommand(_ => ExportDiagnostics());
        ExportEnvironmentCommand = new RelayCommand(_ => ExportEnvironment());
        ImportEnvironmentCommand = new AsyncRelayCommand(_ => ImportEnvironmentAsync(), _ => !Busy);
        CopyLaunchArgumentsCommand = new RelayCommand(_ => CopyLaunchArguments(), _ => SelectedBrowser != null);
        CreateLoadSetCommand = new RelayCommand(_ => CreateLoadSet(),
            _ => !string.IsNullOrWhiteSpace(NewLoadSetNameInput) && _extensions.Installed.Any());
        DeleteLoadSetCommand = new RelayCommand(
            o => DeleteLoadSet(o as LoadSet ?? _selectedLoadSet),
            o => {
                var target = o as LoadSet ?? _selectedLoadSet;
                return target is not null && target.Id != SentinelLoadSet.Id;
            });
        RestoreHiddenRepoCommand = new RelayCommand(o => RestoreHiddenRepo(o as string), o => o is string { Length: > 0 });

        DetectBrowsers();
        Log($"LocalChromeStore v{App.ResourceAssembly.GetName().Version} ready.");
        Log($"Extensions install root: {_settingsService.ExtensionsRoot}");
        if (_settingsService.TokenWasMigratedFromPlaintext)
        {
            Log("Migrating legacy plaintext GitHub token to DPAPI on next save.");
            _settingsService.Save(_settings);
            Log("GitHub token re-saved under DPAPI for the current Windows user.");
        }
        Log($"Run Refresh to discover extensions for '{_settings.GitHubUser}'.");
    }

    public bool Busy
    {
        get => _busy;
        private set
        {
            if (SetField(ref _busy, value))
            {
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(RefreshButtonLabel));
                OnPropertyChanged(nameof(CanLaunchBrowser));
                OnPropertyChanged(nameof(HasHiddenRepos));
                OnPropertyChanged(nameof(HasInstallableUpdates));
                OnPropertyChanged(nameof(UpdateAllLabel));
                OnPropertyChanged(nameof(PermissionReviewUpdateCount));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetField(ref _searchText, value))
                RefreshExtensionView();
        }
    }

    public bool ShowInstalledOnly
    {
        get => _showInstalledOnly;
        set
        {
            if (SetField(ref _showInstalledOnly, value))
                RefreshExtensionView();
        }
    }

    public BrowserInfo? SelectedBrowser
    {
        get => _selectedBrowser;
        set
        {
            if (SetField(ref _selectedBrowser, value))
            {
                if (value != null)
                {
                    _settings.PreferredBrowserPath = value.ExecutablePath;
                    _settingsService.Save(_settings);
                }
                OnPropertyChanged(nameof(CanLaunchBrowser));
                OnPropertyChanged(nameof(BrowserSummary));
                OnPropertyChanged(nameof(ExtensionsPageLabel));
                RefreshLaunchPreviewProperties();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string GitHubUserInput
    {
        get => _githubUserInput;
        set => SetField(ref _githubUserInput, value);
    }

    public string GitHubTokenInput
    {
        get => _githubTokenInput;
        set => SetField(ref _githubTokenInput, value);
    }

    public string NewOwnerInput
    {
        get => _newOwnerInput;
        set
        {
            if (SetField(ref _newOwnerInput, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string? SelectedExtraOwner
    {
        get => _selectedExtraOwner;
        set
        {
            if (SetField(ref _selectedExtraOwner, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public string LaunchUrlInput
    {
        get => _launchUrlInput;
        set
        {
            if (SetField(ref _launchUrlInput, value))
            {
                _settings.LaunchUrl = NormalizeLaunchUrl(value);
                _settingsService.Save(_settings);
                RefreshLaunchPreviewProperties();
            }
        }
    }

    public bool LaunchWithTemporaryProfile
    {
        get => _launchWithTemporaryProfile;
        set
        {
            if (SetField(ref _launchWithTemporaryProfile, value))
            {
                _settings.LaunchWithTemporaryProfile = value;
                _settingsService.Save(_settings);
                RefreshLaunchPreviewProperties();
            }
        }
    }

    public LoadSet? SelectedLoadSet
    {
        get => _selectedLoadSet;
        set
        {
            if (SetField(ref _selectedLoadSet, value))
            {
                OnPropertyChanged(nameof(ActiveLoadSetLabel));
                RefreshLaunchPreviewProperties();
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public string NewLoadSetNameInput
    {
        get => _newLoadSetNameInput;
        set
        {
            if (SetField(ref _newLoadSetNameInput, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool HasNamedLoadSets => LoadSets.Count > 1;

    public string ActiveLoadSetLabel => _selectedLoadSet is null || _selectedLoadSet.Id == SentinelLoadSet.Id
        ? "All installed extensions"
        : $"{_selectedLoadSet.Name} ({_selectedLoadSet.ExtensionKeys?.Count ?? 0} extension(s))";

    public bool LaunchBrowserAfterInstall
    {
        get => _settings.LaunchBrowserAfterInstall;
        set
        {
            if (_settings.LaunchBrowserAfterInstall != value)
            {
                _settings.LaunchBrowserAfterInstall = value;
                _settingsService.Save(_settings);
                OnPropertyChanged();
            }
        }
    }

    public bool AutoUpdateOnRefresh
    {
        get => _settings.AutoUpdateOnRefresh;
        set
        {
            if (_settings.AutoUpdateOnRefresh != value)
            {
                _settings.AutoUpdateOnRefresh = value;
                _settingsService.Save(_settings);
                OnPropertyChanged();
            }
        }
    }

    public bool UseTopicFilter
    {
        get => _settings.UseTopicFilter;
        set
        {
            if (_settings.UseTopicFilter != value)
            {
                _settings.UseTopicFilter = value;
                OnPropertyChanged();
            }
        }
    }

    public string TopicFilter
    {
        get => _settings.TopicFilter;
        set
        {
            if (_settings.TopicFilter != value)
            {
                _settings.TopicFilter = value;
                OnPropertyChanged();
            }
        }
    }

    public int InstalledCount => _extensions.Installed.Count;
    public int AvailableCount => Extensions.Count;
    public int UpdateAvailableCount => Extensions.Count(e => e.IsUpdateAvailable);
    public int InstallableUpdateCount => Extensions.Count(e => e.IsUpdateAvailable && e.HasAsset);
    public int PermissionReviewUpdateCount => Extensions.Count(e => e.IsUpdateAvailable && e.HasAsset && e.HasUpdatePermissionExpansion);
    public int VisibleCount => ExtensionsView.Cast<object>().Count();
    public int HiddenRepoCount => _settings.HiddenRepos.Count;
    public bool HasInstalledExtensions => InstalledCount > 0;
    public bool HasUpdates => UpdateAvailableCount > 0;
    public bool HasInstallableUpdates => InstallableUpdateCount > 0;
    public bool HasHiddenRepos => HiddenRepoCount > 0;
    public bool CanLaunchBrowser => !Busy && SelectedBrowser != null && HasInstalledExtensions;
    public string RefreshButtonLabel => Busy ? "Refreshing..." : "Refresh";
    public string UpdateAllLabel => InstallableUpdateCount == 0 ? "Update all" : $"Update all ({InstallableUpdateCount})";
    public string UpdateStatusSummary => UpdateAvailableCount == 0
        ? "No installed extensions have newer catalog versions."
        : InstallableUpdateCount == UpdateAvailableCount
            ? $"{UpdateAvailableCount} installed extension(s) can be updated.{PermissionReviewSuffix}"
            : $"{InstallableUpdateCount} of {UpdateAvailableCount} update(s) have installable release assets.{PermissionReviewSuffix}";
    private string PermissionReviewSuffix => PermissionReviewUpdateCount == 0
        ? string.Empty
        : $" {PermissionReviewUpdateCount} add new permissions and require review.";
    public string BrowserSummary => Browsers.Count == 0
        ? "No supported Chromium browser detected."
        : $"{Browsers.Count} browser(s) detected.";
    public string HiddenRepoSummary => HiddenRepoCount == 0
        ? "No repositories are hidden from discovery."
        : $"{HiddenRepoCount} hidden repo(s) excluded from refresh.";
    public string RateLimitSummary
    {
        get => _rateLimitSummary;
        private set => SetField(ref _rateLimitSummary, value);
    }
    public string GitHubStatusSummary
    {
        get => _githubStatusSummary;
        private set => SetField(ref _githubStatusSummary, value);
    }
    public bool IsDegraded
    {
        get => _isDegraded;
        private set => SetField(ref _isDegraded, value);
    }
    public string ExtensionsPageLabel => SelectedBrowser is null
        ? "Open browser extensions"
        : $"Open {BrowserLauncher.ExtensionsPageUrl(SelectedBrowser.Kind)}";
    public string LaunchProfileSummary
    {
        get
        {
            var profilePart = LaunchWithTemporaryProfile
                ? "Clean profile: each launch uses a new isolated browser profile under LocalChromeStore."
                : "Default profile: launch uses the selected browser's normal profile.";
            if (_selectedLoadSet is not null && _selectedLoadSet.Id != SentinelLoadSet.Id)
                return $"{profilePart} Load set: {_selectedLoadSet.Name} ({_selectedLoadSet.ExtensionKeys?.Count ?? 0} extension(s)).";
            return profilePart;
        }
    }
    public string LaunchPreview
    {
        get
        {
            if (SelectedBrowser is null) return "Select a supported Chromium browser to preview launch arguments.";
            var extensions = GetActiveLoadSetExtensions(_extensions.Installed);
            var plan = _launcher.BuildLaunchPlan(
                SelectedBrowser,
                extensions,
                NormalizeLaunchUrl(LaunchUrlInput),
                LaunchWithTemporaryProfile);
            return plan.DisplayCommand;
        }
    }
    public bool ShowEmptyState => !Busy && VisibleCount == 0;
    public string EmptyStateTitle
    {
        get
        {
            if (AvailableCount == 0) return "No extensions discovered yet";
            if (ShowInstalledOnly) return "No installed extensions match this view";
            if (!string.IsNullOrWhiteSpace(SearchText)) return "No matching extensions";
            return "Nothing to show";
        }
    }
    public string EmptyStateMessage
    {
        get
        {
            if (AvailableCount == 0)
                return HiddenRepoCount == 0
                    ? "Refresh to scan the configured GitHub account for repos with a manifest.json or release ZIP/CRX."
                    : "Refresh scans the configured GitHub account while keeping hidden repositories excluded.";
            if (ShowInstalledOnly)
                return "Clear the installed-only filter or install an extension from the full catalog.";
            if (!string.IsNullOrWhiteSpace(SearchText))
                return "Try a different extension name, repository, or description keyword.";
            return "Adjust the filters or refresh the catalog.";
        }
    }

    private bool FilterExtension(object obj)
    {
        if (obj is not ExtensionCardViewModel vm) return false;
        if (ShowInstalledOnly && !vm.IsInstalled) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var q = SearchText.Trim();
        return vm.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
            || vm.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
            || vm.Repo.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private async Task RefreshAsync()
    {
        Busy = true;
        StatusText = "Discovering extensions...";
        try
        {
            var logProgress = new Progress<string>(Log);
            var infos = (await _github.DiscoverAsync(_settings, logProgress)).ToList();
            RebuildExtensionCards(infos);
            ApplyServiceState(_github.LastState, infos.Count);
            if (_settings.AutoUpdateOnRefresh && HasInstallableUpdates)
            {
                await UpdateAvailableExtensionsAsync(
                    catalogSnapshot: infos,
                    confirmFirst: false,
                    operationName: "Auto-update on refresh");
            }
        }
        catch (Exception ex)
        {
            StatusText = $"Refresh failed: {ex.Message}";
            GitHubStatusSummary = "GitHub: refresh failed.";
            IsDegraded = true;
            Log($"! {ex}");
        }
        finally
        {
            Busy = false;
        }
    }

    private void RebuildExtensionCards(IEnumerable<ExtensionInfo> infos)
    {
        Extensions.Clear();
        foreach (var info in infos)
            Extensions.Add(CreateExtensionCard(info));
        RefreshExtensionView();
        RefreshMetrics();
    }

    private ExtensionCardViewModel CreateExtensionCard(ExtensionInfo info) => new(
        info,
        _extensions,
        _github,
        _settingsService,
        Log,
        RefreshAfterChange,
        OnExtensionInstalledAsync,
        HideExtension);

    private void ApplyServiceState(GitHubServiceState state, int count)
    {
        // Rate-limit visibility — F072.
        if (state.RateLimit is { Limit: > 0 } rl)
        {
            var resetIn = rl.Reset.HasValue ? rl.Reset.Value - DateTimeOffset.Now : TimeSpan.Zero;
            var resetText = rl.Reset.HasValue && resetIn > TimeSpan.Zero
                ? $", resets in {Format(resetIn)}"
                : string.Empty;
            var auth = rl.Authenticated ? "authenticated" : "anonymous";
            RateLimitSummary = $"Rate limit: {rl.Remaining}/{rl.Limit} ({auth}{resetText}).";
        }
        else
        {
            RateLimitSummary = "Rate limit: GitHub did not return rate-limit headers.";
        }

        // Status / degraded state — F075.
        switch (state.Status)
        {
            case GitHubServiceStatus.Ok:
                GitHubStatusSummary = $"GitHub: connected ({(state.RateLimit?.Authenticated == true ? "token" : "public")}).";
                IsDegraded = false;
                StatusText = $"Found {count} extension(s) — {InstalledCount} installed.";
                Log(StatusText);
                break;
            case GitHubServiceStatus.Empty:
                GitHubStatusSummary = "GitHub: connected, but no extension-shaped repos were returned.";
                IsDegraded = false;
                StatusText = "No extension-shaped repos found for the configured owner(s).";
                Log(state.Detail ?? StatusText);
                break;
            case GitHubServiceStatus.Unauthorized:
                GitHubStatusSummary = "GitHub: token rejected (401). Falling back to anonymous access.";
                IsDegraded = true;
                StatusText = state.Detail ?? "GitHub token rejected.";
                Log($"! {state.Detail}");
                break;
            case GitHubServiceStatus.RateLimited:
                GitHubStatusSummary = "GitHub: rate limit exceeded.";
                IsDegraded = true;
                StatusText = state.Detail ?? "GitHub rate limit exceeded.";
                Log($"! {StatusText}");
                break;
            case GitHubServiceStatus.Forbidden:
                GitHubStatusSummary = "GitHub: forbidden (403).";
                IsDegraded = true;
                StatusText = state.Detail ?? "GitHub denied the request.";
                Log($"! {StatusText}");
                break;
            case GitHubServiceStatus.OwnerNotFound:
                GitHubStatusSummary = "GitHub: configured owner could not be found.";
                IsDegraded = true;
                StatusText = state.Detail ?? "GitHub owner not found.";
                Log($"! {StatusText}");
                break;
            case GitHubServiceStatus.NetworkError:
                GitHubStatusSummary = "GitHub: network error.";
                IsDegraded = true;
                StatusText = state.Detail ?? "Network error contacting GitHub.";
                Log($"! {StatusText}");
                break;
            default:
                GitHubStatusSummary = "GitHub: unknown state.";
                IsDegraded = true;
                StatusText = state.Detail ?? "Unknown GitHub state.";
                break;
        }
    }

    private static string Format(TimeSpan ts)
    {
        if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes}m";
        if (ts.TotalMinutes >= 1) return $"{(int)ts.TotalMinutes}m {ts.Seconds}s";
        return $"{(int)ts.TotalSeconds}s";
    }

    private void RefreshAfterChange()
    {
        RefreshExtensionView();
        RefreshMetrics();
        CommandManager.InvalidateRequerySuggested();
    }

    private bool SaveSettings()
    {
        var user = GitHubUserInput.Trim();
        var topic = TopicFilter.Trim();
        if (string.IsNullOrWhiteSpace(user))
        {
            StatusText = "Enter a GitHub user or organization before saving.";
            Log("! Settings were not saved: GitHub user / org is required.");
            return false;
        }

        if (UseTopicFilter && string.IsNullOrWhiteSpace(topic))
        {
            StatusText = "Enter a topic filter or turn off topic filtering.";
            Log("! Settings were not saved: topic filter is blank.");
            return false;
        }

        _settings.GitHubUser = user;
        _settings.GitHubToken = string.IsNullOrWhiteSpace(GitHubTokenInput) ? null : GitHubTokenInput.Trim();
        _settings.TopicFilter = topic;
        _settings.LaunchUrl = NormalizeLaunchUrl(LaunchUrlInput);
        _settings.LaunchWithTemporaryProfile = LaunchWithTemporaryProfile;
        _settings.LaunchBrowserAfterInstall = LaunchBrowserAfterInstall;
        _settings.AutoUpdateOnRefresh = AutoUpdateOnRefresh;
        // Persist current ExtraOwners ordering — already kept in sync with the ObservableCollection.
        _settings.ExtraOwners = ExtraOwners.ToList();
        _settingsService.Save(_settings);
        OnPropertyChanged(nameof(TopicFilter));
        SyncSettingsInputs();
        Log("Settings saved locally.");
        StatusText = "Settings saved locally.";
        return true;
    }

    private async Task UpdateAvailableExtensionsAsync(
        IReadOnlyList<ExtensionInfo>? catalogSnapshot = null,
        bool confirmFirst = false,
        string operationName = "Update")
    {
        var updateCards = Extensions.Where(c => c.IsUpdateAvailable).ToList();
        var installableCards = updateCards.Where(c => c.HasAsset).ToList();
        if (updateCards.Count == 0)
        {
            StatusText = "No extension updates are available.";
            Log("No extension updates are available.");
            return;
        }

        if (installableCards.Count == 0)
        {
            StatusText = "Updates were detected, but none have installable release assets.";
            Log("Updates were detected, but none have installable release assets.");
            return;
        }

        var skippedPermissionReview = 0;
        var skippedNoAsset = updateCards.Count - installableCards.Count;
        if (!confirmFirst)
        {
            var permissionReviewCards = installableCards.Where(c => c.HasUpdatePermissionExpansion).ToList();
            if (permissionReviewCards.Count > 0)
            {
                foreach (var card in permissionReviewCards)
                    Log($"! {operationName} skipped {card.Repo}: update adds permissions or host access ({card.UpdatePermissionDiff.AddedSummary}). Use manual update to review.");

                skippedPermissionReview = permissionReviewCards.Count;
                installableCards = installableCards.Where(c => !c.HasUpdatePermissionExpansion).ToList();
                if (installableCards.Count == 0)
                {
                    var reviewSummary = $"{operationName} skipped {skippedPermissionReview} update(s) that add permissions or host access. Use Update all or the card update button to review.";
                    StatusText = reviewSummary;
                    Log(reviewSummary);
                    return;
                }
            }
        }

        if (confirmFirst)
        {
            var permissionReviewCards = installableCards.Where(c => c.HasUpdatePermissionExpansion).ToList();
            var permissionReviewText = permissionReviewCards.Count == 0
                ? string.Empty
                : $"{Environment.NewLine}{Environment.NewLine}Permission changes needing approval:{Environment.NewLine}{FormatPermissionReviewList(permissionReviewCards)}";
            var confirm = MessageBox.Show(
                $"Update {installableCards.Count} installed extension(s)?\n\nLocalChromeStore will replace each local copy with the current catalog release asset. Existing installs remain registered if an update fails.{permissionReviewText}",
                "Update extensions",
                MessageBoxButton.YesNo,
                permissionReviewCards.Any(c => c.HasHighRiskUpdatePermissionExpansion) ? MessageBoxImage.Warning : MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;
            foreach (var card in permissionReviewCards)
                Log($"Permission expansion approved for {card.Repo}: {card.UpdatePermissionDiff.AddedSummary}.");
        }

        var catalog = catalogSnapshot ?? Extensions.Select(c => c.Info).ToList();
        var updated = 0;
        var failed = 0;
        StatusText = $"{operationName}: updating {installableCards.Count} extension(s)...";
        Log($"{operationName}: updating {installableCards.Count} extension(s).");

        foreach (var card in installableCards)
        {
            try
            {
                StatusText = $"Updating {card.Repo}...";
                await _extensions.InstallAsync(card.Info, new Progress<string>(Log));
                updated++;
            }
            catch (Exception ex)
            {
                failed++;
                Log($"! {operationName} failed for {card.Repo}: {ex.Message}");
            }
        }

        _extensions.Reload();
        RebuildExtensionCards(catalog);
        var summary = $"{operationName} summary: {updated} updated, {failed} failed";
        if (skippedNoAsset > 0)
            summary += $", {skippedNoAsset} skipped without installable assets";
        if (skippedPermissionReview > 0)
            summary += $", {skippedPermissionReview} skipped for permission review";
        summary += ".";
        Log(summary);
        StatusText = summary;

        if (updated > 0)
            MaybeLaunchAfterInstall($"{updated} updated extension(s)");
    }

    private static string FormatPermissionReviewList(IReadOnlyList<ExtensionCardViewModel> cards)
    {
        var lines = cards.Take(6).Select(c => $"- {c.Repo}: {c.UpdatePermissionDiff.AddedSummary}").ToList();
        if (cards.Count > lines.Count)
            lines.Add($"- +{cards.Count - lines.Count} more");
        return string.Join(Environment.NewLine, lines);
    }

    private Task OnExtensionInstalledAsync()
    {
        MaybeLaunchAfterInstall("an installed extension");
        return Task.CompletedTask;
    }

    private void MaybeLaunchAfterInstall(string reason)
    {
        if (!_settings.LaunchBrowserAfterInstall) return;
        if (SelectedBrowser is null)
        {
            StatusText = "Install complete; launch after install skipped because no browser is selected.";
            Log("! Launch after install skipped: no supported Chromium browser is selected.");
            return;
        }

        Log($"Launch after install is enabled after {reason}.");
        LaunchBrowser();
    }

    private void DetectBrowsers()
    {
        Browsers.Clear();
        foreach (var b in _launcher.Detect()) Browsers.Add(b);
        if (!string.IsNullOrEmpty(_settings.PreferredBrowserPath))
            SelectedBrowser = Browsers.FirstOrDefault(b =>
                string.Equals(b.ExecutablePath, _settings.PreferredBrowserPath, StringComparison.OrdinalIgnoreCase));
        SelectedBrowser ??= Browsers.FirstOrDefault();
        Log(Browsers.Count == 0 ? "! No supported browsers detected." : $"Detected browsers: {string.Join(", ", Browsers.Select(b => b.DisplayName))}");
        OnPropertyChanged(nameof(BrowserSummary));
        OnPropertyChanged(nameof(CanLaunchBrowser));
        OnPropertyChanged(nameof(ExtensionsPageLabel));
        RefreshLaunchPreviewProperties();
    }

    private void HideExtension(ExtensionCardViewModel extension)
    {
        var message = extension.IsInstalled
            ? $"Hide {extension.Repo} from the catalog?\n\nIts installed local copy will remain on disk and can still be launched. Restore hidden repositories from Settings to manage it again."
            : $"Hide {extension.Repo} from future discovery?\n\nRestore hidden repositories from Settings if you want it back in the catalog.";

        var confirm = MessageBox.Show(
            message,
            "Hide repository",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        if (!_settings.HiddenRepos.Contains(extension.Repo, StringComparer.OrdinalIgnoreCase))
        {
            _settings.HiddenRepos.Add(extension.Repo);
            _settings.HiddenRepos.Sort(StringComparer.OrdinalIgnoreCase);
            _settingsService.Save(_settings);
        }

        Extensions.Remove(extension);
        RefreshExtensionView();
        RefreshMetrics();
        RefreshHiddenRepoProperties();
        StatusText = $"{extension.Repo} hidden from discovery.";
        Log($"Hidden {extension.Repo} from discovery.");
    }

    private bool ClearHiddenRepos()
    {
        if (_settings.HiddenRepos.Count == 0) return false;
        var count = _settings.HiddenRepos.Count;
        _settings.HiddenRepos.Clear();
        _settingsService.Save(_settings);
        RefreshHiddenRepoProperties();
        StatusText = $"Restored {count} hidden repo(s).";
        Log($"Restored {count} hidden repo(s) to discovery.");
        return true;
    }

    private void LaunchBrowser()
    {
        if (SelectedBrowser is null) return;
        var set = GetActiveLoadSetExtensions(_extensions.Installed);
        if (set.Count == 0)
        {
            var isSentinel = _selectedLoadSet is null || _selectedLoadSet.Id == SentinelLoadSet.Id;
            StatusText = isSentinel
                ? "Install at least one extension before launching a browser session."
                : $"No extensions in load set '{_selectedLoadSet!.Name}' are currently installed. Install them or switch to 'All installed'.";
            Log(isSentinel
                ? "No extensions installed yet — install one before launching."
                : $"Load set '{_selectedLoadSet!.Name}' has no installed extensions — check installs or switch load set.");
            return;
        }
        try
        {
            var launchUrl = NormalizeLaunchUrl(LaunchUrlInput);
            _settings.LaunchUrl = launchUrl;
            _settings.LaunchWithTemporaryProfile = LaunchWithTemporaryProfile;
            _settingsService.Save(_settings);

            var result = _launcher.Launch(SelectedBrowser, set, launchUrl, LaunchWithTemporaryProfile);
            var setLabel = _selectedLoadSet is null || _selectedLoadSet.Id == SentinelLoadSet.Id
                ? "all installed"
                : $"load set '{_selectedLoadSet.Name}'";
            StatusText = $"Launched {SelectedBrowser.DisplayName} with {set.Count} extension(s) ({setLabel}).";
            Log($"Launched {SelectedBrowser.DisplayName} with {set.Count} extension(s) loaded ({setLabel}).");
            if (!string.IsNullOrEmpty(result.Plan.TemporaryProfilePath))
                Log($"Temporary browser profile: {result.Plan.TemporaryProfilePath}");
            Log($"Launch command: {result.Plan.DisplayCommand}");
        }
        catch (Exception ex)
        {
            StatusText = $"Launch failed: {ex.Message}";
            Log($"! Launch failed: {ex.Message}");
        }
    }

    private List<InstalledExtension> GetActiveLoadSetExtensions(IReadOnlyList<InstalledExtension> installed)
    {
        if (_selectedLoadSet is null || _selectedLoadSet.Id == SentinelLoadSet.Id || _selectedLoadSet.ExtensionKeys is null)
            return installed.ToList();
        var keys = _selectedLoadSet.ExtensionKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return installed.Where(e => keys.Contains(e.Key)).ToList();
    }

    private void CopyLaunchArguments()
    {
        if (SelectedBrowser is null) return;
        try
        {
            Clipboard.SetText(LaunchPreview);
            StatusText = "Launch command copied to clipboard.";
            Log("Copied launch command to clipboard.");
        }
        catch (Exception ex)
        {
            StatusText = $"Could not copy launch command: {ex.Message}";
            Log($"! Could not copy launch command: {ex.Message}");
        }
    }

    private void OpenBrowserExtensionsPage()
    {
        if (SelectedBrowser is null) return;
        try
        {
            _launcher.OpenExtensionsPage(SelectedBrowser);
            var url = BrowserLauncher.ExtensionsPageUrl(SelectedBrowser.Kind);
            StatusText = $"Opened {url} in {SelectedBrowser.DisplayName}.";
            Log($"Opened {url} in {SelectedBrowser.DisplayName}.");
        }
        catch (Exception ex)
        {
            StatusText = $"Could not open extensions page: {ex.Message}";
            Log($"! Could not open extensions page: {ex.Message}");
        }
    }

    private void OpenInstallDir()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settingsService.ExtensionsRoot}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log($"! {ex.Message}"); }
    }

    private void ExportDiagnostics()
    {
        var defaultName = $"LocalChromeStore-diagnostics-{DateTime.Now:yyyy-MM-dd-HHmm}.txt";
        var dlg = new SaveFileDialog
        {
            FileName = defaultName,
            DefaultExt = ".txt",
            Filter = "Text file (*.txt)|*.txt|All files (*.*)|*.*",
            InitialDirectory = _settingsService.LogsDir
        };
        var owner = Application.Current?.MainWindow;
        var result = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
        if (result != true) return;

        try
        {
            File.WriteAllText(dlg.FileName, BuildDiagnosticsBundle(), Encoding.UTF8);
            StatusText = $"Diagnostics exported to {dlg.FileName}.";
            Log($"Exported diagnostics bundle to {dlg.FileName}.");
        }
        catch (Exception ex)
        {
            StatusText = $"Diagnostics export failed: {ex.Message}";
            Log($"! Diagnostics export failed: {ex.Message}");
        }
    }

    private void ExportEnvironment()
    {
        var defaultName = $"LocalChromeStore-environment-{DateTime.Now:yyyy-MM-dd-HHmm}.json";
        var dlg = new SaveFileDialog
        {
            FileName = defaultName,
            DefaultExt = ".json",
            Filter = "LocalChromeStore environment (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = _settingsService.SettingsDir
        };
        var owner = Application.Current?.MainWindow;
        var result = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
        if (result != true) return;

        try
        {
            var manifest = EnvironmentManifestService.Create(_settings, _extensions.Installed);
            EnvironmentManifestService.Save(dlg.FileName, manifest);
            StatusText = $"Environment exported to {dlg.FileName}.";
            Log($"Exported environment manifest with {manifest.Extensions.Count} extension(s) to {dlg.FileName}.");
        }
        catch (Exception ex)
        {
            StatusText = $"Environment export failed: {ex.Message}";
            Log($"! Environment export failed: {ex.Message}");
        }
    }

    private async Task ImportEnvironmentAsync()
    {
        var dlg = new OpenFileDialog
        {
            DefaultExt = ".json",
            Filter = "LocalChromeStore environment (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = _settingsService.SettingsDir,
            CheckFileExists = true
        };
        var owner = Application.Current?.MainWindow;
        var result = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
        if (result != true) return;

        EnvironmentManifest manifest;
        try
        {
            manifest = EnvironmentManifestService.Load(dlg.FileName);
        }
        catch (Exception ex)
        {
            StatusText = $"Environment import failed: {ex.Message}";
            Log($"! Environment import failed: {ex.Message}");
            return;
        }

        var confirm = MessageBox.Show(
            $"Import {manifest.Extensions.Count} extension(s) from this environment manifest?\n\nLocalChromeStore will update discovery settings, refresh GitHub, and install any matching release assets that are missing or outdated. Existing local installs are not removed.",
            "Import environment",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        Busy = true;
        try
        {
            StatusText = "Importing environment...";
            _settings = EnvironmentManifestService.ApplySettings(_settings, manifest);
            _settingsService.Save(_settings);
            SyncSettingsInputs();
            ReloadExtraOwnersFromSettings();
            Log($"Imported environment settings from {dlg.FileName}.");

            await RefreshCatalogForImportAsync();
            await InstallEnvironmentTargetsAsync(manifest);
            await RefreshCatalogForImportAsync();

            StatusText = $"Environment import complete: {manifest.Extensions.Count} target extension(s) processed.";
        }
        catch (Exception ex)
        {
            StatusText = $"Environment import failed: {ex.Message}";
            Log($"! Environment import failed: {ex}");
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task RefreshCatalogForImportAsync()
    {
        StatusText = "Refreshing catalog for import...";
        var infos = await _github.DiscoverAsync(_settings, new Progress<string>(Log));
        RebuildExtensionCards(infos);
        ApplyServiceState(_github.LastState, infos.Count);
    }

    private async Task InstallEnvironmentTargetsAsync(EnvironmentManifest manifest)
    {
        var cards = Extensions.ToDictionary(c => c.Repo, StringComparer.OrdinalIgnoreCase);
        var installed = 0;
        var alreadyCurrent = 0;
        var skippedForPermissionReview = 0;
        var missing = 0;
        foreach (var target in manifest.Extensions)
        {
            var existing = _extensions.Find(target.RepoOwner, target.RepoName);
            if (existing is not null && existing.Version.Equals(target.Version, StringComparison.OrdinalIgnoreCase))
            {
                alreadyCurrent++;
                Log($"Import skip: {target.Key} is already installed at {target.Version}.");
                continue;
            }

            if (!cards.TryGetValue(target.Key, out var card))
            {
                missing++;
                Log($"! Import missing: {target.Key} was not returned by current GitHub discovery.");
                continue;
            }

            if (!card.HasAsset)
            {
                missing++;
                Log($"! Import missing asset: {target.Key} has no installable ZIP/CRX release asset.");
                continue;
            }

            if (!card.Version.Equals(target.Version, StringComparison.OrdinalIgnoreCase))
                Log($"Import version note: {target.Key} requested {target.Version}; installing current catalog version {card.Version}.");

            var permissionDiff = existing is not null
                ? PermissionDiff.Compare(existing, card.Info)
                : PermissionDiff.Compare(target, card.Info);
            if (permissionDiff.HasAdditions && !ConfirmEnvironmentImportPermissionExpansion(target, card, permissionDiff, existing is null))
            {
                skippedForPermissionReview++;
                Log($"Import skip: {target.Key} needs permission review before installing current catalog version {card.Version}.");
                continue;
            }

            await _extensions.InstallAsync(card.Info, new Progress<string>(Log));
            installed++;
        }

        _extensions.Reload();
        var summary = $"Environment import summary: {installed} installed, {alreadyCurrent} already current, {missing} missing";
        if (skippedForPermissionReview > 0)
            summary += $", {skippedForPermissionReview} skipped for permission review";
        Log(summary + ".");
        RefreshAfterChange();
    }

    private bool ConfirmEnvironmentImportPermissionExpansion(
        EnvironmentExtensionSnapshot target,
        ExtensionCardViewModel card,
        PermissionDiff diff,
        bool comparedWithImportedSnapshot)
    {
        var baseline = comparedWithImportedSnapshot
            ? $"the exported {target.Version} environment snapshot"
            : "the local installed copy";
        var confirm = MessageBox.Show(
            $"Import {target.Key}?\n\nThe current catalog release ({card.Version}) adds extension access compared with {baseline}:\n\n{diff.FormatAddedForPrompt()}\n\nInstall the current catalog release anyway?",
            "Review import permissions",
            MessageBoxButton.YesNo,
            diff.HasHighRiskAdditions ? MessageBoxImage.Warning : MessageBoxImage.Question);
        if (confirm == MessageBoxResult.Yes)
        {
            Log($"Import permission expansion approved for {target.Key}: {diff.AddedSummary}.");
            return true;
        }

        return false;
    }

    private string BuildDiagnosticsBundle()
    {
        var sb = new StringBuilder();
        sb.AppendLine("LocalChromeStore diagnostics bundle");
        sb.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Version: {App.ResourceAssembly.GetName().Version}");
        sb.AppendLine($"OS: {Environment.OSVersion}");
        sb.AppendLine($".NET: {Environment.Version}");
        sb.AppendLine();

        sb.AppendLine("== Settings paths ==");
        sb.AppendLine($"  Settings file:    {_settingsService.SettingsPath}");
        sb.AppendLine($"  Installed manifest: {_settingsService.ManifestPath}");
        sb.AppendLine($"  Extensions root:  {_settingsService.ExtensionsRoot}");
        sb.AppendLine($"  Icon cache:       {_settingsService.IconCacheDir}");
        sb.AppendLine($"  Log directory:    {_settingsService.LogsDir}");
        sb.AppendLine();

        sb.AppendLine("== GitHub state ==");
        sb.AppendLine($"  Primary user:  {_settings.GitHubUser}");
        sb.AppendLine($"  Token present: {(string.IsNullOrEmpty(_settings.GitHubToken) ? "no" : "yes (DPAPI on disk)")}");
        sb.AppendLine($"  Extra owners:  {(ExtraOwners.Count == 0 ? "(none)" : string.Join(", ", ExtraOwners))}");
        sb.AppendLine($"  Status:        {GitHubStatusSummary}");
        sb.AppendLine($"  Rate limit:    {RateLimitSummary}");
        sb.AppendLine($"  Topic filter:  {(_settings.UseTopicFilter ? _settings.TopicFilter : "(disabled)")}");
        sb.AppendLine($"  Hidden repos:  {_settings.HiddenRepos.Count}");
        sb.AppendLine($"  Auto-update on refresh: {_settings.AutoUpdateOnRefresh}");
        sb.AppendLine($"  Launch after install:   {_settings.LaunchBrowserAfterInstall}");
        sb.AppendLine();

        sb.AppendLine("== Browsers detected ==");
        if (Browsers.Count == 0) sb.AppendLine("  (none)");
        foreach (var b in Browsers)
            sb.AppendLine($"  {b.Kind,-8} {b.DisplayName,-18} {b.ExecutablePath}");
        sb.AppendLine($"  Selected:      {SelectedBrowser?.DisplayName ?? "(none)"}");
        sb.AppendLine($"  Launch URL:    {NormalizeLaunchUrl(LaunchUrlInput) ?? "(none)"}");
        sb.AppendLine($"  Temp profile:  {LaunchWithTemporaryProfile}");
        sb.AppendLine($"  Launch command preview: {LaunchPreview}");
        sb.AppendLine();

        sb.AppendLine("== Installed extensions ==");
        if (_extensions.Installed.Count == 0) sb.AppendLine("  (none)");
        foreach (var inst in _extensions.Installed)
        {
            sb.AppendLine($"  {inst.RepoOwner}/{inst.RepoName}@{inst.Version}");
            sb.AppendLine($"    InstalledAt:      {inst.InstalledAt:yyyy-MM-dd HH:mm:ss zzz}");
            sb.AppendLine($"    InstallPath:      {inst.InstallPath}");
            sb.AppendLine($"    ChecksumVerified: {inst.ChecksumVerified}{(inst.ChecksumVerified ? $" ({inst.ChecksumAlgorithm})" : "")}");
            sb.AppendLine($"    ManifestVersion:  {(inst.ManifestVersionNumber.HasValue ? "MV" + inst.ManifestVersionNumber : "unknown")}");
            sb.AppendLine($"    Permissions:      {inst.Permissions.Count + inst.OptionalPermissions.Count} ({inst.HostPermissions.Count + inst.OptionalHostPermissions.Count} host)");
        }
        sb.AppendLine();

        sb.AppendLine("== Discovered catalog ==");
        if (Extensions.Count == 0) sb.AppendLine("  (none — refresh first)");
        foreach (var ext in Extensions)
        {
            var info = ext.Info;
            sb.AppendLine($"  {info.RepoOwner}/{info.RepoName}");
            sb.AppendLine($"    Framework:    {FrameworkLabels.Label(info.Framework)}");
            sb.AppendLine($"    Source:       {FrameworkLabels.DiscoveryLabel(info.DiscoverySource)}{(info.ManifestSourcePath is null ? "" : $" ({info.ManifestSourcePath})")}");
            sb.AppendLine($"    Asset:        {FrameworkLabels.AssetLabel(info.AssetKind)}{(info.AssetName is null ? "" : $" — {info.AssetName}")}");
            sb.AppendLine($"    Manifest ver: {(info.ManifestVersionNumber.HasValue ? "MV" + info.ManifestVersionNumber : "unknown")}");
            sb.AppendLine($"    Freshness:    {FrameworkLabels.FreshnessLabel(info.Freshness)}{(info.IsArchived ? " (archived)" : "")}");
            sb.AppendLine($"    Permissions:  {info.Permissions.Count + info.OptionalPermissions.Count} ({info.HostPermissions.Count + info.OptionalHostPermissions.Count} host)");
            if (ext.IsUpdateAvailable && ext.UpdatePermissionDiff.HasAdditions)
                sb.AppendLine($"    Update adds:  {ext.UpdatePermissionDiff.AddedSummary}");
            sb.AppendLine($"    Checksum:     {(string.IsNullOrEmpty(info.ChecksumUrl) ? "no sidecar" : info.ChecksumName)}");
            if (info.Warnings.Count > 0)
                sb.AppendLine($"    Warnings:     {string.Join(" | ", info.Warnings)}");
        }
        sb.AppendLine();

        sb.AppendLine("== Activity log ==");
        if (LogLines.Count == 0) sb.AppendLine("  (empty)");
        foreach (var line in LogLines) sb.AppendLine("  " + line);

        return sb.ToString();
    }

    private void Log(string line) => _logSink.Append(line);

    public bool HasExtraOwners => ExtraOwners.Count > 0;

    private void ReloadExtraOwnersFromSettings()
    {
        ExtraOwners.Clear();
        foreach (var o in _settings.ExtraOwners.Where(o => !string.IsNullOrWhiteSpace(o)))
            ExtraOwners.Add(o.Trim());
        OnPropertyChanged(nameof(HasExtraOwners));
    }

    private void SyncSettingsInputs()
    {
        GitHubUserInput = _settings.GitHubUser;
        GitHubTokenInput = _settings.GitHubToken ?? string.Empty;
        LaunchUrlInput = _settings.LaunchUrl ?? string.Empty;
        LaunchWithTemporaryProfile = _settings.LaunchWithTemporaryProfile;
        OnPropertyChanged(nameof(LaunchBrowserAfterInstall));
        OnPropertyChanged(nameof(AutoUpdateOnRefresh));
        OnPropertyChanged(nameof(UseTopicFilter));
        OnPropertyChanged(nameof(TopicFilter));
    }

    private void AddExtraOwner()
    {
        var owner = NewOwnerInput.Trim();
        if (string.IsNullOrWhiteSpace(owner)) return;
        if (string.Equals(owner, _settings.GitHubUser, StringComparison.OrdinalIgnoreCase))
        {
            StatusText = $"'{owner}' is already the primary owner.";
            Log($"! Skipped adding '{owner}': already primary owner.");
            NewOwnerInput = string.Empty;
            return;
        }
        if (ExtraOwners.Any(o => o.Equals(owner, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = $"'{owner}' is already in the extra owners list.";
            Log($"! Skipped adding '{owner}': already in extra owners.");
            NewOwnerInput = string.Empty;
            return;
        }
        ExtraOwners.Add(owner);
        _settings.ExtraOwners = ExtraOwners.ToList();
        _settingsService.Save(_settings);
        OnPropertyChanged(nameof(HasExtraOwners));
        NewOwnerInput = string.Empty;
        StatusText = $"Added '{owner}' to extra owners.";
        Log($"Added extra owner '{owner}'. Run Refresh to discover its repos.");
    }

    private void RemoveExtraOwner(string? owner)
    {
        if (string.IsNullOrWhiteSpace(owner)) return;
        var match = ExtraOwners.FirstOrDefault(o => o.Equals(owner, StringComparison.OrdinalIgnoreCase));
        if (match is null) return;
        ExtraOwners.Remove(match);
        _settings.ExtraOwners = ExtraOwners.ToList();
        _settingsService.Save(_settings);
        OnPropertyChanged(nameof(HasExtraOwners));
        if (string.Equals(SelectedExtraOwner, match, StringComparison.OrdinalIgnoreCase))
            SelectedExtraOwner = null;
        StatusText = $"Removed '{match}' from extra owners.";
        Log($"Removed extra owner '{match}'.");
    }

    private void ReloadLoadSetsFromService()
    {
        LoadSets.Clear();
        LoadSets.Add(SentinelLoadSet);
        foreach (var ls in _settingsService.LoadLoadSets())
            LoadSets.Add(ls);
        _selectedLoadSet = SentinelLoadSet;
        OnPropertyChanged(nameof(SelectedLoadSet));
        OnPropertyChanged(nameof(HasNamedLoadSets));
        OnPropertyChanged(nameof(ActiveLoadSetLabel));
    }

    private void SyncHiddenReposCollection()
    {
        HiddenRepos.Clear();
        foreach (var r in _settings.HiddenRepos)
            HiddenRepos.Add(r);
    }

    private void CreateLoadSet()
    {
        var name = NewLoadSetNameInput.Trim();
        if (string.IsNullOrWhiteSpace(name) || !_extensions.Installed.Any()) return;
        if (LoadSets.Any(ls => ls.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            StatusText = $"A load set named '{name}' already exists.";
            Log($"! Load set '{name}' already exists — choose a different name.");
            return;
        }
        var keys = _extensions.Installed.Select(e => e.Key).ToList();
        var newSet = new LoadSet { Name = name, ExtensionKeys = keys };
        LoadSets.Add(newSet);
        PersistLoadSets();
        SelectedLoadSet = newSet;
        NewLoadSetNameInput = string.Empty;
        OnPropertyChanged(nameof(HasNamedLoadSets));
        StatusText = $"Load set '{name}' created with {keys.Count} extension(s).";
        Log($"Created load set '{name}' with {keys.Count} extension(s): {string.Join(", ", keys)}.");
    }

    private void DeleteLoadSet(LoadSet? target = null)
    {
        target ??= _selectedLoadSet;
        if (target is null || target.Id == SentinelLoadSet.Id) return;
        var name = target.Name;
        LoadSets.Remove(target);
        PersistLoadSets();
        if (_selectedLoadSet?.Id == target.Id)
            SelectedLoadSet = SentinelLoadSet;
        OnPropertyChanged(nameof(HasNamedLoadSets));
        StatusText = $"Load set '{name}' deleted.";
        Log($"Deleted load set '{name}'.");
    }

    private void PersistLoadSets()
    {
        _settingsService.SaveLoadSets(LoadSets.Where(ls => ls.Id != SentinelLoadSet.Id));
    }

    private void RestoreHiddenRepo(string? repoKey)
    {
        if (string.IsNullOrWhiteSpace(repoKey)) return;
        var match = _settings.HiddenRepos.FirstOrDefault(r => r.Equals(repoKey, StringComparison.OrdinalIgnoreCase));
        if (match is null) return;
        _settings.HiddenRepos.Remove(match);
        _settings.HiddenRepos.Sort(StringComparer.OrdinalIgnoreCase);
        _settingsService.Save(_settings);
        RefreshHiddenRepoProperties();
        StatusText = $"Restored '{match}' to discovery.";
        Log($"Restored '{match}' to discovery. Refresh to show it in the catalog.");
    }

    private void RefreshExtensionView()
    {
        ExtensionsView.Refresh();
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateMessage));
    }

    private void RefreshMetrics()
    {
        OnPropertyChanged(nameof(InstalledCount));
        OnPropertyChanged(nameof(AvailableCount));
        OnPropertyChanged(nameof(UpdateAvailableCount));
        OnPropertyChanged(nameof(InstallableUpdateCount));
        OnPropertyChanged(nameof(PermissionReviewUpdateCount));
        OnPropertyChanged(nameof(HasInstalledExtensions));
        OnPropertyChanged(nameof(HasUpdates));
        OnPropertyChanged(nameof(HasInstallableUpdates));
        OnPropertyChanged(nameof(CanLaunchBrowser));
        OnPropertyChanged(nameof(UpdateAllLabel));
        OnPropertyChanged(nameof(UpdateStatusSummary));
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateMessage));
        RefreshLaunchPreviewProperties();
        RefreshHiddenRepoProperties();
        CommandManager.InvalidateRequerySuggested();
    }

    private void RefreshHiddenRepoProperties()
    {
        SyncHiddenReposCollection();
        OnPropertyChanged(nameof(HiddenRepoCount));
        OnPropertyChanged(nameof(HasHiddenRepos));
        OnPropertyChanged(nameof(HiddenRepoSummary));
        CommandManager.InvalidateRequerySuggested();
    }

    private void RefreshLaunchPreviewProperties()
    {
        OnPropertyChanged(nameof(LaunchPreview));
        OnPropertyChanged(nameof(LaunchProfileSummary));
        OnPropertyChanged(nameof(ActiveLoadSetLabel));
    }

    private static string? NormalizeLaunchUrl(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

internal sealed class Dispatcher_LogSink
{
    private readonly ObservableCollection<string> _sink;
    private const int MaxLines = 500;

    public Dispatcher_LogSink(ObservableCollection<string> sink) { _sink = sink; }

    public void Append(string line)
    {
        var stamped = $"[{DateTime.Now:HH:mm:ss}] {line}";
        if (Application.Current?.Dispatcher.CheckAccess() == true)
            DoAppend(stamped);
        else
            Application.Current?.Dispatcher.BeginInvoke(new Action(() => DoAppend(stamped)));
    }

    private void DoAppend(string line)
    {
        _sink.Add(line);
        while (_sink.Count > MaxLines) _sink.RemoveAt(0);
    }
}
