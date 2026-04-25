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
    public ICommand LaunchBrowserCommand { get; }
    public ICommand LaunchInstalledOnlyCommand { get; }
    public ICommand OpenInstallDirCommand { get; }
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
        SaveSettingsCommand = new RelayCommand(_ => SaveSettings());
        LaunchBrowserCommand = new RelayCommand(_ => LaunchBrowser(installedOnly: false), _ => SelectedBrowser != null);
        LaunchInstalledOnlyCommand = new RelayCommand(_ => LaunchBrowser(installedOnly: true), _ => SelectedBrowser != null && _extensions.Installed.Any());
        OpenInstallDirCommand = new RelayCommand(_ => OpenInstallDir());
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
                CommandManager.InvalidateRequerySuggested();
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
                ExtensionsView.Refresh();
        }
    }

    public bool ShowInstalledOnly
    {
        get => _showInstalledOnly;
        set
        {
            if (SetField(ref _showInstalledOnly, value))
                ExtensionsView.Refresh();
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
                _settingsService.Save(_settings);
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
                _settingsService.Save(_settings);
                OnPropertyChanged();
            }
        }
    }

    public int InstalledCount => _extensions.Installed.Count;
    public int AvailableCount => Extensions.Count;

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
                    info, _extensions, _github, _settingsService, Log, RefreshAfterChange));
            }
            ExtensionsView.Refresh();
            OnPropertyChanged(nameof(InstalledCount));
            OnPropertyChanged(nameof(AvailableCount));
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
        OnPropertyChanged(nameof(InstalledCount));
        CommandManager.InvalidateRequerySuggested();
    }

    private void SaveSettings()
    {
        _settings.GitHubUser = GitHubUserInput.Trim();
        _settings.GitHubToken = string.IsNullOrWhiteSpace(GitHubTokenInput) ? null : GitHubTokenInput.Trim();
        _settingsService.Save(_settings);
        Log("Settings saved.");
        StatusText = "Settings saved.";
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
    }

    private void LaunchBrowser(bool installedOnly)
    {
        if (SelectedBrowser is null) return;
        var set = installedOnly ? _extensions.Installed.ToList() : _extensions.Installed.ToList();
        if (set.Count == 0)
        {
            Log("No extensions installed yet — install one to load it.");
        }
        try
        {
            _launcher.Launch(SelectedBrowser, set);
            Log($"Launched {SelectedBrowser.DisplayName} with {set.Count} extension(s) loaded.");
        }
        catch (Exception ex)
        {
            Log($"! Launch failed: {ex.Message}");
        }
    }

    private void OpenInstallDir()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{_settingsService.ExtensionsRoot}\"") { UseShellExecute = true }); }
        catch (Exception ex) { Log($"! {ex.Message}"); }
    }

    private void Log(string line) => _logSink.Append(line);
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
