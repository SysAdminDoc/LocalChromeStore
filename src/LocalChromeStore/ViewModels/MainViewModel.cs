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

namespace LocalChromeStore.ViewModels;

public sealed class MainViewModel : ViewModelBase
{
    private readonly SettingsService _settingsService;
    private readonly GitHubService _github;
    private readonly ExtensionService _extensions;
    private readonly BrowserLauncher _launcher;
    private readonly BrowserLaunchManager _launchManager;
    private readonly LoadSetManager _loadSets;
    private readonly PolicyPackageService _policyPackages;
    private readonly PolicyInstallService _policyInstaller;
    private readonly PolicyEnrollmentService _policyEnrollment = new();
    private readonly IDialogService _dialogs;
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
    private bool _selfUpdateAvailable;
    private string _selfUpdateMessage = string.Empty;
    private string _selfUpdateUrl = string.Empty;

    private static readonly LoadSet SentinelLoadSet = LoadSetManager.CreateSentinel();

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
    public ICommand OpenPolicyPageCommand { get; }
    public ICommand ReviewPolicyReadinessCommand { get; }
    public ICommand ExportDiagnosticsCommand { get; }
    public ICommand ExportEnvironmentCommand { get; }
    public ICommand ImportEnvironmentCommand { get; }
    public ICommand ExportCatalogCommand { get; }
    public ICommand CopyLaunchArgumentsCommand { get; }
    public ICommand CreateLoadSetCommand { get; }
    public ICommand DeleteLoadSetCommand { get; }
    public ICommand RestoreHiddenRepoCommand { get; }
    public ICommand OpenSelfUpdateCommand { get; }
    public ICommand DismissSelfUpdateCommand { get; }

    public MainViewModel() : this(null) { }

    /// <summary>Test/DI seam: inject a fake <see cref="IDialogService"/> to run the view model headlessly.</summary>
    public MainViewModel(IDialogService? dialogs)
    {
        _dialogs = dialogs ?? new DialogService();
        _settingsService = new SettingsService();
        _github = new GitHubService(_settingsService);
        _extensions = new ExtensionService(_settingsService, _github);
        _launcher = new BrowserLauncher(_extensions);
        _launchManager = new BrowserLaunchManager(_launcher);
        _loadSets = new LoadSetManager(_settingsService);
        _policyPackages = new PolicyPackageService(_settingsService);
        _policyInstaller = new PolicyInstallService();
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
        LaunchBrowserCommand = new AsyncRelayCommand(_ => LaunchBrowserAsync(), _ => CanLaunchBrowser);
        LaunchInstalledOnlyCommand = new AsyncRelayCommand(_ => LaunchBrowserAsync(), _ => CanLaunchBrowser);
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
        OpenPolicyPageCommand = new RelayCommand(_ => OpenPolicyPage(), _ => SelectedBrowser != null);
        ReviewPolicyReadinessCommand = new RelayCommand(_ => ReviewPolicyReadiness(), _ => SelectedBrowser != null);
        ExportDiagnosticsCommand = new RelayCommand(_ => ExportDiagnostics());
        ExportEnvironmentCommand = new RelayCommand(_ => ExportEnvironment());
        ImportEnvironmentCommand = new AsyncRelayCommand(_ => ImportEnvironmentAsync(), _ => !Busy);
        ExportCatalogCommand = new RelayCommand(_ => ExportCatalog());
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
        OpenSelfUpdateCommand = new RelayCommand(_ => OpenSelfUpdate(), _ => SelfUpdateAvailable);
        DismissSelfUpdateCommand = new RelayCommand(_ => SelfUpdateAvailable = false, _ => SelfUpdateAvailable);

        DetectBrowsers();
        Log($"LocalChromeStore v{AssemblyVersion} ready.");
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

    /// <summary>
    /// App version (Major.Minor.Patch) read from this assembly. Uses the type's own assembly rather
    /// than <c>App.ResourceAssembly</c> so it resolves when the view model runs headlessly under tests.
    /// </summary>
    private static string AssemblyVersion =>
        typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>Footer label bound to the real assembly version so it never drifts from the build.</summary>
    public string AppVersionLabel => $"LocalChromeStore v{AssemblyVersion}";

    /// <summary>True when a newer LocalChromeStore release exists; drives the dismissible update banner.</summary>
    public bool SelfUpdateAvailable
    {
        get => _selfUpdateAvailable;
        private set
        {
            if (SetField(ref _selfUpdateAvailable, value))
                CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>Banner copy describing the available release.</summary>
    public string SelfUpdateMessage
    {
        get => _selfUpdateMessage;
        private set => SetField(ref _selfUpdateMessage, value);
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
                OnPropertyChanged(nameof(PolicyPageLabel));
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
    public string ExtensionsPageLabel => "Extensions";
    public string PolicyPageLabel => "Policy";
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
            return BrowserLaunchManager.DisplayCommandForPlan(plan);
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
        ApplyPolicyInstallAsync,
        RollbackPolicyInstallAsync,
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
            var confirm = _dialogs.Confirm(
                $"Update {installableCards.Count} installed extension(s)?\n\nLocalChromeStore will replace each local copy with the current catalog release asset. Existing installs remain registered if an update fails.{permissionReviewText}",
                "Update extensions",
                permissionReviewCards.Any(c => c.HasHighRiskUpdatePermissionExpansion) ? DialogIcon.Warning : DialogIcon.Question);
            if (!confirm) return;
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
            await MaybeLaunchAfterInstallAsync($"{updated} updated extension(s)");
    }

    private static string FormatPermissionReviewList(IReadOnlyList<ExtensionCardViewModel> cards)
    {
        var lines = cards.Take(6).Select(c => $"- {c.Repo}: {c.UpdatePermissionDiff.AddedSummary}").ToList();
        if (cards.Count > lines.Count)
            lines.Add($"- +{cards.Count - lines.Count} more");
        return string.Join(Environment.NewLine, lines);
    }

    private Task OnExtensionInstalledAsync() => MaybeLaunchAfterInstallAsync("an installed extension");

    private async Task MaybeLaunchAfterInstallAsync(string reason)
    {
        if (!_settings.LaunchBrowserAfterInstall) return;
        if (SelectedBrowser is null)
        {
            StatusText = "Install complete; launch after install skipped because no browser is selected.";
            Log("! Launch after install skipped: no supported Chromium browser is selected.");
            return;
        }

        Log($"Launch after install is enabled after {reason}.");
        await LaunchBrowserAsync();
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
        OnPropertyChanged(nameof(PolicyPageLabel));
        RefreshLaunchPreviewProperties();
    }

    private void HideExtension(ExtensionCardViewModel extension)
    {
        var message = extension.IsInstalled
            ? $"Hide {extension.Repo} from the catalog?\n\nIts installed local copy will remain on disk and can still be launched. Restore hidden repositories from Settings to manage it again."
            : $"Hide {extension.Repo} from future discovery?\n\nRestore hidden repositories from Settings if you want it back in the catalog.";

        if (!_dialogs.Confirm(message, "Hide repository")) return;

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

    private async Task LaunchBrowserAsync()
    {
        if (SelectedBrowser is null) return;
        var set = GetActiveLoadSetExtensions(_extensions.Installed);
        var isSentinel = LoadSetManager.IsSentinel(_selectedLoadSet);

        if (set.Count == 0)
        {
            ApplyLaunchOutcome(BrowserLaunchManager.EmptySet(isSentinel, _selectedLoadSet?.Name));
            return;
        }

        // Persist the launch options used for this session (the input setters already persist them;
        // this keeps the saved state consistent with what is actually launched).
        var launchUrl = NormalizeLaunchUrl(LaunchUrlInput);
        _settings.LaunchUrl = launchUrl;
        _settings.LaunchWithTemporaryProfile = LaunchWithTemporaryProfile;
        _settingsService.Save(_settings);

        ApplyLaunchOutcome(await _launchManager.LaunchAsync(
            SelectedBrowser, set, launchUrl, LaunchWithTemporaryProfile, isSentinel, _selectedLoadSet?.Name));
    }

    private void ApplyLaunchOutcome(BrowserLaunchManager.Outcome outcome)
    {
        StatusText = outcome.StatusText;
        foreach (var line in outcome.Log) Log(line);
    }

    private List<InstalledExtension> GetActiveLoadSetExtensions(IReadOnlyList<InstalledExtension> installed) =>
        LoadSetManager.ResolveActiveExtensions(_selectedLoadSet, installed);

    private void CopyLaunchArguments()
    {
        if (SelectedBrowser is null) return;
        try
        {
            _dialogs.SetClipboardText(LaunchPreview);
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

    private void OpenPolicyPage()
    {
        if (SelectedBrowser is null) return;
        try
        {
            _launcher.OpenPolicyPage(SelectedBrowser);
            var url = BrowserLauncher.PolicyPageUrl(SelectedBrowser.Kind);
            StatusText = $"Opened {url} in {SelectedBrowser.DisplayName}.";
            Log($"Opened {url} in {SelectedBrowser.DisplayName}.");
        }
        catch (Exception ex)
        {
            StatusText = $"Could not open policy page: {ex.Message}";
            Log($"! Could not open policy page: {ex.Message}");
        }
    }

    private void ReviewPolicyReadiness()
    {
        if (SelectedBrowser is null) return;

        var enrollment = _policyEnrollment.DetectCurrent();
        var support = PolicyEnrollmentService.EvaluateOffStoreForceInstall(enrollment);
        Log("Policy readiness:");
        if (PolicyInstallService.TryGetTarget(SelectedBrowser.Kind, out var target))
        {
            Log($"  Browser target: {target.DisplayName}");
            Log($"  Registry key: HKLM\\{target.RegistrySubKey}");
            Log($"  Native policy page: {target.PolicyPageUrl}");
        }
        else
        {
            Log($"  ! {SelectedBrowser.DisplayName} does not have a known policy install target in LocalChromeStore.");
        }

        Log($"  Domain joined: {enrollment.DomainJoined}");
        Log($"  Entra joined: {enrollment.EntraJoined}");
        Log($"  CBCM enrolled: {enrollment.CbcmEnrolled}");
        Log($"  Off-store force-install supported: {support.Supported}");
        Log($"  {support.Reason}");
        StatusText = support.Supported
            ? "Policy backend is ready for managed off-store force-install requests."
            : "Policy backend is available, but this machine is not enrolled for off-store force-install.";
    }

    private async Task ApplyPolicyInstallAsync(ExtensionCardViewModel card)
    {
        if (SelectedBrowser is not { } browser)
        {
            StatusText = "Select a browser before applying Enterprise Policy.";
            Log("! Select a browser before applying Enterprise Policy.");
            return;
        }
        if (card.Installed is not { } installed)
        {
            StatusText = "Install the extension before applying Enterprise Policy.";
            Log($"! Policy install skipped for {card.Repo}: extension is not installed locally.");
            return;
        }
        if (!PolicyInstallService.TryGetTarget(browser.Kind, out var target))
        {
            StatusText = $"{browser.DisplayName} does not have a known Enterprise Policy target.";
            Log($"! {browser.DisplayName} does not have a known Enterprise Policy target.");
            return;
        }

        var defaultCrxUrl = BuildDefaultPolicyUrl(installed, PolicyPackageService.DefaultCrxFileName(installed));
        if (!PromptPolicyUrl(
                "Policy CRX URL",
                $"Enter the public URL where {PolicyPackageService.DefaultCrxFileName(installed)} will be hosted after LocalChromeStore packages it.",
                defaultCrxUrl,
                out var crxUrl))
        {
            return;
        }

        var defaultUpdateUrl = BuildDefaultPolicyUrl(installed, "update.xml");
        if (!PromptPolicyUrl(
                "Policy update.xml URL",
                "Enter the public update.xml URL that the selected browser policy should use.",
                defaultUpdateUrl,
                out var updateXmlUrl))
        {
            return;
        }

        string? existingUpdateXmlPath = null;
        var generateUpdateXml = _dialogs.Confirm(
            "Generate update.xml from the CRX URL and installed manifest version?\n\nChoose No to copy an existing update.xml file into the local policy package folder instead.",
            "Policy update.xml",
            DialogIcon.Question);
        if (!generateUpdateXml)
        {
            existingUpdateXmlPath = _dialogs.OpenFile(
                "Select update.xml",
                "Update XML (*.xml)|*.xml|All files (*.*)|*.*",
                _settingsService.PolicyPackagesRoot,
                ".xml");
            if (string.IsNullOrWhiteSpace(existingUpdateXmlPath))
            {
                StatusText = "Policy install cancelled before selecting update.xml.";
                Log($"Policy install cancelled for {card.Repo}: no update.xml selected.");
                return;
            }
        }

        Busy = true;
        try
        {
            StatusText = $"Packaging {card.Title} for Enterprise Policy...";
            Log($"Policy package started for {card.Repo} targeting {target.DisplayName}.");
            var progress = new Progress<string>(Log);
            var package = await Task.Run(() => _policyPackages.Prepare(
                new PolicyPackageRequest(installed, crxUrl, updateXmlUrl, existingUpdateXmlPath),
                progress));
            var request = package.ToInstallRequest(browser.Kind);
            var enrollment = _policyEnrollment.DetectCurrent();
            var consentPrompt = PolicyInstallService.BuildConsentPrompt([request], enrollment) +
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"Local package folder:{Environment.NewLine}{package.PackageDirectory}{Environment.NewLine}{Environment.NewLine}" +
                $"Upload the CRX to:{Environment.NewLine}{package.CrxUrl.AbsoluteUri}{Environment.NewLine}{Environment.NewLine}" +
                $"Upload/update the feed at:{Environment.NewLine}{package.UpdateXmlUrl.AbsoluteUri}{Environment.NewLine}{Environment.NewLine}" +
                "Health checks will fail until those hosted URLs are reachable.";

            if (!_dialogs.Confirm(consentPrompt, "Apply Enterprise Policy", DialogIcon.Warning))
            {
                StatusText = "Policy install cancelled before writing HKLM policy.";
                Log($"Policy install cancelled for {card.Repo}: HKLM policy consent was not confirmed.");
                return;
            }

            var install = _policyInstaller.Install(request, consentConfirmed: true);
            Log($"Policy entry written: HKLM\\{install.Target.RegistrySubKey}\\{install.ValueName} = {install.PolicyEntry}");
            if (install.EdgeExtensionSettingsWritten)
                Log("Edge ExtensionSettings.override_update_url written for the same extension ID.");

            var report = await _policyInstaller.CheckHealthAsync(request);
            LogPolicyHealthReport(report);
            StatusText = report.Healthy
                ? $"Enterprise Policy install is healthy for {card.Title}."
                : $"Enterprise Policy was written for {card.Title}; health checks need attention.";
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusText = "Enterprise Policy write requires administrator elevation.";
            Log($"! Policy install requires administrator elevation to write HKLM: {ex.Message}");
        }
        catch (Exception ex)
        {
            StatusText = $"Policy install failed: {ex.Message}";
            Log($"! Policy install failed for {card.Repo}: {ex.Message}");
        }
        finally
        {
            Busy = false;
        }
    }

    private async Task RollbackPolicyInstallAsync(ExtensionCardViewModel card)
    {
        if (SelectedBrowser is not { } browser)
        {
            StatusText = "Select a browser before rolling back Enterprise Policy.";
            Log("! Select a browser before rolling back Enterprise Policy.");
            return;
        }
        if (card.Installed is not { } installed)
        {
            StatusText = "Install the extension before rolling back Enterprise Policy.";
            Log($"! Policy rollback skipped for {card.Repo}: extension is not installed locally.");
            return;
        }
        if (!PolicyInstallService.TryGetTarget(browser.Kind, out var target))
        {
            StatusText = $"{browser.DisplayName} does not have a known Enterprise Policy target.";
            Log($"! {browser.DisplayName} does not have a known Enterprise Policy target.");
            return;
        }
        if (!_policyPackages.TryDeriveExtensionId(installed, out var extensionId, out var keyPath))
        {
            StatusText = "No policy signing key exists for this extension.";
            Log($"! Policy rollback skipped for {card.Repo}: no CRX signing key found at {keyPath}. Use Policy first to package it.");
            return;
        }

        var confirm = _dialogs.Confirm(
            $"Rollback Enterprise Policy force-install for {card.Title}?\n\nThis removes only registry policy entries for extension ID {extensionId} under {target.DisplayName}. Local installed files, CRX packages, update.xml, and signing keys remain on disk.",
            "Rollback Enterprise Policy",
            DialogIcon.Warning);
        if (!confirm) return;

        Busy = true;
        try
        {
            var result = await Task.Run(() => _policyInstaller.Rollback(browser.Kind, [extensionId]));
            foreach (var valueName in result.RemovedValueNames)
                Log($"Policy rollback removed: HKLM\\{result.Target.RegistrySubKey}\\{valueName}");
            foreach (var removedId in result.RemovedExtensionSettings)
                Log($"Policy rollback removed Edge ExtensionSettings entry: {removedId}");

            if (result.RemovedValueNames.Count == 0 && result.RemovedExtensionSettings.Count == 0)
            {
                StatusText = $"No Enterprise Policy entries were found for {card.Title}.";
                Log($"Policy rollback found no registry entries for {card.Repo} ({extensionId}).");
            }
            else
            {
                StatusText = $"Enterprise Policy rollback completed for {card.Title}.";
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            StatusText = "Enterprise Policy rollback requires administrator elevation.";
            Log($"! Policy rollback requires administrator elevation to write HKLM: {ex.Message}");
        }
        catch (Exception ex)
        {
            StatusText = $"Policy rollback failed: {ex.Message}";
            Log($"! Policy rollback failed for {card.Repo}: {ex.Message}");
        }
        finally
        {
            Busy = false;
        }
    }

    private bool PromptPolicyUrl(string title, string message, Uri defaultUrl, out Uri url)
    {
        url = defaultUrl;
        var input = _dialogs.PromptText(title, message, defaultUrl.AbsoluteUri);
        if (input is null)
        {
            StatusText = "Policy install cancelled before entering a hosted URL.";
            Log("Policy install cancelled before entering a hosted URL.");
            return false;
        }

        if (!Uri.TryCreate(input.Trim(), UriKind.Absolute, out var parsed)
            || (parsed.Scheme != Uri.UriSchemeHttps && parsed.Scheme != Uri.UriSchemeHttp))
        {
            StatusText = "Policy URL must be an absolute http or https URL.";
            Log($"! Policy URL rejected: {input}");
            return false;
        }

        url = parsed;
        return true;
    }

    private static Uri BuildDefaultPolicyUrl(InstalledExtension installed, string fileName)
    {
        var owner = Uri.EscapeDataString(installed.RepoOwner);
        var repo = Uri.EscapeDataString(installed.RepoName);
        var version = Uri.EscapeDataString(installed.Version);
        var file = Uri.EscapeDataString(fileName);
        return new Uri($"https://example.com/localchromestore/{owner}/{repo}/{version}/{file}");
    }

    private void LogPolicyHealthReport(PolicyHealthReport report)
    {
        Log("Policy health checks:");
        foreach (var check in report.Checks)
        {
            var prefix = check.Status switch
            {
                PolicyHealthStatus.Pass => "+",
                PolicyHealthStatus.Warning => "!",
                _ => "!"
            };
            Log($"  {prefix} {check.Name}: {check.Detail}");
        }
    }

    private void OpenInstallDir()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settingsService.ExtensionsRoot}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log($"! {ex.Message}"); }
    }

    /// <summary>
    /// Non-blocking self-update check against LocalChromeStore's own GitHub releases (P3). Surfaces a
    /// dismissible banner when a newer build exists; failures are silent and best-effort. The app
    /// never downloads or installs itself — the banner only links to the release page.
    /// </summary>
    public async Task CheckForAppUpdateAsync()
    {
        var result = await _github.CheckForAppUpdateAsync(_settings, AssemblyVersion);
        if (!result.UpdateAvailable) return;

        _selfUpdateUrl = result.ReleaseUrl;
        SelfUpdateMessage =
            $"LocalChromeStore {result.LatestVersion} is available (you have v{AssemblyVersion}). Open the release page to download it.";
        SelfUpdateAvailable = true;
        Log($"A newer LocalChromeStore release is available: {result.LatestVersion} (current v{AssemblyVersion}).");
    }

    private void OpenSelfUpdate()
    {
        if (string.IsNullOrWhiteSpace(_selfUpdateUrl)) return;
        try
        {
            Process.Start(new ProcessStartInfo(_selfUpdateUrl) { UseShellExecute = true });
            Log($"Opened the LocalChromeStore release page: {_selfUpdateUrl}.");
        }
        catch (Exception ex)
        {
            Log($"! Could not open the release page: {ex.Message}");
        }
    }

    private void ExportDiagnostics()
    {
        var defaultName = $"LocalChromeStore-diagnostics-{DateTime.Now:yyyy-MM-dd-HHmm}.txt";
        var path = _dialogs.SaveFile("Export diagnostics", "Text file (*.txt)|*.txt|All files (*.*)|*.*",
            defaultName, _settingsService.LogsDir, ".txt");
        if (path is null) return;

        try
        {
            File.WriteAllText(path, BuildDiagnosticsBundle(), Encoding.UTF8);
            StatusText = $"Diagnostics exported to {path}.";
            Log($"Exported diagnostics bundle to {path}.");
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
        var path = _dialogs.SaveFile("Export environment", "LocalChromeStore environment (*.json)|*.json|All files (*.*)|*.*",
            defaultName, _settingsService.SettingsDir, ".json");
        if (path is null) return;

        try
        {
            var manifest = EnvironmentManifestService.Create(_settings, _extensions.Installed);
            EnvironmentManifestService.Save(path, manifest);
            StatusText = $"Environment exported to {path}.";
            Log($"Exported environment manifest with {manifest.Extensions.Count} extension(s) to {path}.");
        }
        catch (Exception ex)
        {
            StatusText = $"Environment export failed: {ex.Message}";
            Log($"! Environment export failed: {ex.Message}");
        }
    }

    // F039: machine-readable catalog snapshot.
    private void ExportCatalog()
    {
        var defaultName = $"LocalChromeStore-catalog-{DateTime.Now:yyyy-MM-dd-HHmm}.json";
        var path = _dialogs.SaveFile("Export catalog", "LocalChromeStore catalog (*.json)|*.json|All files (*.*)|*.*",
            defaultName, _settingsService.SettingsDir, ".json");
        if (path is null) return;

        try
        {
            var export = ImportExportService.BuildCatalog(Extensions.Select(c => c.Info), _extensions.Installed);
            File.WriteAllText(path, export.Json, Encoding.UTF8);
            StatusText = $"Catalog exported: {export.Count} extensions to {path}.";
            Log($"Exported catalog snapshot ({export.Count} extensions) to {path}.");
        }
        catch (Exception ex)
        {
            StatusText = $"Catalog export failed: {ex.Message}";
            Log($"! Catalog export failed: {ex.Message}");
        }
    }

    private async Task ImportEnvironmentAsync()
    {
        var path = _dialogs.OpenFile("Import environment", "LocalChromeStore environment (*.json)|*.json|All files (*.*)|*.*",
            _settingsService.SettingsDir, ".json");
        if (path is null) return;

        EnvironmentManifest manifest;
        try
        {
            manifest = EnvironmentManifestService.Load(path);
        }
        catch (Exception ex)
        {
            StatusText = $"Environment import failed: {ex.Message}";
            Log($"! Environment import failed: {ex.Message}");
            return;
        }

        var confirm = _dialogs.Confirm(
            $"Import {manifest.Extensions.Count} extension(s) from this environment manifest?\n\nLocalChromeStore will update discovery settings, refresh GitHub, and install any matching release assets that are missing or outdated. Existing local installs are not removed.",
            "Import environment");
        if (!confirm) return;

        Busy = true;
        try
        {
            StatusText = "Importing environment...";
            _settings = EnvironmentManifestService.ApplySettings(_settings, manifest);
            _settingsService.Save(_settings);
            SyncSettingsInputs();
            ReloadExtraOwnersFromSettings();
            Log($"Imported environment settings from {path}.");

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
            cards.TryGetValue(target.Key, out var card);
            var action = ImportExportService.ClassifyImportTarget(
                existing, target.Version, hasCard: card is not null, cardHasAsset: card?.HasAsset ?? false);

            switch (action)
            {
                case ImportAction.AlreadyCurrent:
                    alreadyCurrent++;
                    Log($"Import skip: {target.Key} is already installed at {target.Version}.");
                    continue;
                case ImportAction.Missing:
                    missing++;
                    Log($"! Import missing: {target.Key} was not returned by current GitHub discovery.");
                    continue;
                case ImportAction.MissingAsset:
                    missing++;
                    Log($"! Import missing asset: {target.Key} has no installable ZIP/CRX release asset.");
                    continue;
            }

            var resolvedCard = card!; // ImportAction.Install implies the card exists with an asset.
            if (!resolvedCard.Version.Equals(target.Version, StringComparison.OrdinalIgnoreCase))
                Log($"Import version note: {target.Key} requested {target.Version}; installing current catalog version {resolvedCard.Version}.");

            var permissionDiff = existing is not null
                ? PermissionDiff.Compare(existing, resolvedCard.Info)
                : PermissionDiff.Compare(target, resolvedCard.Info);
            if (permissionDiff.HasAdditions && !ConfirmEnvironmentImportPermissionExpansion(target, resolvedCard, permissionDiff, existing is null))
            {
                skippedForPermissionReview++;
                Log($"Import skip: {target.Key} needs permission review before installing current catalog version {resolvedCard.Version}.");
                continue;
            }

            await _extensions.InstallAsync(resolvedCard.Info, new Progress<string>(Log));
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
        var confirm = _dialogs.Confirm(
            $"Import {target.Key}?\n\nThe current catalog release ({card.Version}) adds extension access compared with {baseline}:\n\n{diff.FormatAddedForPrompt()}\n\nInstall the current catalog release anyway?",
            "Review import permissions",
            diff.HasHighRiskAdditions ? DialogIcon.Warning : DialogIcon.Question);
        if (confirm)
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
        sb.AppendLine($"Version: {AssemblyVersion}");
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

        sb.AppendLine("== Policy-mode readiness ==");
        try
        {
            var enrollment = _policyEnrollment.DetectCurrent();
            var support = PolicyEnrollmentService.EvaluateOffStoreForceInstall(enrollment);
            if (SelectedBrowser is not null && PolicyInstallService.TryGetTarget(SelectedBrowser.Kind, out var target))
            {
                sb.AppendLine($"  Browser target: {target.DisplayName}");
                sb.AppendLine($"  Registry key:   HKLM\\{target.RegistrySubKey}");
                sb.AppendLine($"  Policy page:    {target.PolicyPageUrl}");
            }
            else
            {
                sb.AppendLine($"  Browser target: {(SelectedBrowser is null ? "(none selected)" : "unsupported by policy backend")}");
            }
            sb.AppendLine($"  Domain joined:  {enrollment.DomainJoined}");
            sb.AppendLine($"  Entra joined:   {enrollment.EntraJoined}");
            sb.AppendLine($"  CBCM enrolled:  {enrollment.CbcmEnrolled}");
            sb.AppendLine($"  Off-store force-install supported: {support.Supported}");
            sb.AppendLine($"    {support.Reason}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"  (enrollment probe failed: {ex.Message})");
        }
        sb.AppendLine();

        sb.AppendLine("== Installed extensions ==");
        if (_extensions.Installed.Count == 0) sb.AppendLine("  (none)");
        foreach (var inst in _extensions.Installed)
        {
            sb.AppendLine($"  {inst.RepoOwner}/{inst.RepoName}@{inst.Version}");
            sb.AppendLine($"    InstalledAt:      {inst.InstalledAt:yyyy-MM-dd HH:mm:ss zzz}");
            sb.AppendLine($"    InstallPath:      {inst.InstallPath}");
            sb.AppendLine($"    ChecksumVerified: {inst.ChecksumVerified}{(inst.ChecksumVerified ? $" ({inst.ChecksumAlgorithm}, {inst.ChecksumSource ?? "unknown source"})" : "")}");
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
            sb.AppendLine($"    Checksum:     {DescribeCatalogChecksum(info)}");
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
        foreach (var ls in _loadSets.LoadSaved())
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
        if (LoadSetManager.NameExists(LoadSets, name))
        {
            StatusText = $"A load set named '{name}' already exists.";
            Log($"! Load set '{name}' already exists — choose a different name.");
            return;
        }
        var newSet = LoadSetManager.Snapshot(name, _extensions.Installed);
        var keys = newSet.ExtensionKeys ?? new List<string>();
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

    private void PersistLoadSets() => _loadSets.Save(LoadSets);

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

    private static string DescribeCatalogChecksum(ExtensionInfo info)
    {
        if (!string.IsNullOrEmpty(info.ChecksumUrl))
            return $"sidecar ({info.ChecksumName ?? "checksum asset"})";
        return ExtensionService.TryParseSha256Digest(info.AssetDigest, out _)
            ? $"GitHub API digest ({info.AssetDigest})"
            : "unverified";
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
