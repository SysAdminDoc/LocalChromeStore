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

    public ObservableCollection<ExtensionCardViewModel> Extensions { get; } = new();
    public ICollectionView ExtensionsView { get; }
    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<BrowserInfo> Browsers { get; } = new();
    public ObservableCollection<string> ExtraOwners { get; } = new();

    public ICommand RefreshCommand { get; }
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
        ReloadExtraOwnersFromSettings();

        ExtensionsView = CollectionViewSource.GetDefaultView(Extensions);
        ExtensionsView.Filter = FilterExtension;
        ExtensionsView.SortDescriptions.Add(new SortDescription(nameof(ExtensionCardViewModel.Title), ListSortDirection.Ascending));

        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => !Busy);
        SaveSettingsCommand = new RelayCommand(_ => { SaveSettings(); });
        SaveAndRefreshCommand = new AsyncRelayCommand(async _ =>
        {
            if (SaveSettings())
                await RefreshAsync();
        }, _ => !Busy);
        LaunchBrowserCommand = new RelayCommand(_ => LaunchBrowser(installedOnly: false), _ => CanLaunchBrowser);
        LaunchInstalledOnlyCommand = new RelayCommand(_ => LaunchBrowser(installedOnly: true), _ => SelectedBrowser != null && _extensions.Installed.Any());
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
    public int VisibleCount => ExtensionsView.Cast<object>().Count();
    public int HiddenRepoCount => _settings.HiddenRepos.Count;
    public bool HasInstalledExtensions => InstalledCount > 0;
    public bool HasHiddenRepos => HiddenRepoCount > 0;
    public bool CanLaunchBrowser => !Busy && SelectedBrowser != null && HasInstalledExtensions;
    public string RefreshButtonLabel => Busy ? "Refreshing..." : "Refresh";
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
            var infos = await _github.DiscoverAsync(_settings, logProgress);
            Extensions.Clear();
            foreach (var info in infos)
            {
                Extensions.Add(new ExtensionCardViewModel(
                    info, _extensions, _github, _settingsService, Log, RefreshAfterChange, HideExtension));
            }
            RefreshExtensionView();
            RefreshMetrics();
            ApplyServiceState(_github.LastState, infos.Count);
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
        // Persist current ExtraOwners ordering — already kept in sync with the ObservableCollection.
        _settings.ExtraOwners = ExtraOwners.ToList();
        _settingsService.Save(_settings);
        OnPropertyChanged(nameof(TopicFilter));
        Log("Settings saved locally.");
        StatusText = "Settings saved locally.";
        return true;
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

    private void LaunchBrowser(bool installedOnly)
    {
        if (SelectedBrowser is null) return;
        var set = installedOnly ? _extensions.Installed.ToList() : _extensions.Installed.ToList();
        if (set.Count == 0)
        {
            StatusText = "Install at least one extension before launching a browser session.";
            Log("No extensions installed yet — install one before launching.");
            return;
        }
        try
        {
            _launcher.Launch(SelectedBrowser, set);
            StatusText = $"Launched {SelectedBrowser.DisplayName} with {set.Count} extension(s).";
            Log($"Launched {SelectedBrowser.DisplayName} with {set.Count} extension(s) loaded.");
        }
        catch (Exception ex)
        {
            StatusText = $"Launch failed: {ex.Message}";
            Log($"! Launch failed: {ex.Message}");
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
        sb.AppendLine();

        sb.AppendLine("== Browsers detected ==");
        if (Browsers.Count == 0) sb.AppendLine("  (none)");
        foreach (var b in Browsers)
            sb.AppendLine($"  {b.Kind,-8} {b.DisplayName,-18} {b.ExecutablePath}");
        sb.AppendLine($"  Selected:      {SelectedBrowser?.DisplayName ?? "(none)"}");
        sb.AppendLine();

        sb.AppendLine("== Installed extensions ==");
        if (_extensions.Installed.Count == 0) sb.AppendLine("  (none)");
        foreach (var inst in _extensions.Installed)
        {
            sb.AppendLine($"  {inst.RepoOwner}/{inst.RepoName}@{inst.Version}");
            sb.AppendLine($"    InstalledAt:      {inst.InstalledAt:yyyy-MM-dd HH:mm:ss zzz}");
            sb.AppendLine($"    InstallPath:      {inst.InstallPath}");
            sb.AppendLine($"    ChecksumVerified: {inst.ChecksumVerified}{(inst.ChecksumVerified ? $" ({inst.ChecksumAlgorithm})" : "")}");
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
        OnPropertyChanged(nameof(HasInstalledExtensions));
        OnPropertyChanged(nameof(CanLaunchBrowser));
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateMessage));
        RefreshHiddenRepoProperties();
    }

    private void RefreshHiddenRepoProperties()
    {
        OnPropertyChanged(nameof(HiddenRepoCount));
        OnPropertyChanged(nameof(HasHiddenRepos));
        OnPropertyChanged(nameof(HiddenRepoSummary));
        CommandManager.InvalidateRequerySuggested();
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
