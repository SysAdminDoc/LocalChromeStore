using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
    private readonly Dispatcher_LogSink _logSink;
    private AppSettings _settings;
    private bool _busy;
    private string _statusText = "Ready.";
    private string _searchText = string.Empty;
    private bool _showInstalledOnly;
    private BrowserInfo? _selectedBrowser;
    private string _githubUserInput = "";
    private string _githubTokenInput = "";

    public ObservableCollection<ExtensionCardViewModel> Extensions { get; } = new();
    public ICollectionView ExtensionsView { get; }
    public ObservableCollection<string> LogLines { get; } = new();
    public ObservableCollection<BrowserInfo> Browsers { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand SaveSettingsCommand { get; }
    public ICommand SaveAndRefreshCommand { get; }
    public ICommand LaunchBrowserCommand { get; }
    public ICommand LaunchInstalledOnlyCommand { get; }
    public ICommand OpenInstallDirCommand { get; }
    public ICommand ClearHiddenReposCommand { get; }
    public ICommand ClearLogCommand { get; }

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

        DetectBrowsers();
        Log($"LocalChromeStore v{App.ResourceAssembly.GetName().Version} ready.");
        Log($"Extensions install root: {_settingsService.ExtensionsRoot}");
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
            StatusText = $"Found {Extensions.Count} extension(s) — {InstalledCount} installed.";
            Log(StatusText);
        }
        catch (Exception ex)
        {
            StatusText = $"Refresh failed: {ex.Message}";
            Log($"! {ex}");
        }
        finally
        {
            Busy = false;
        }
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

    private void OpenInstallDir()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settingsService.ExtensionsRoot}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log($"! {ex.Message}"); }
    }

    private void Log(string line) => _logSink.Append(line);

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
