using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using LocalChromeStore.Models;
using LocalChromeStore.Services;

namespace LocalChromeStore.ViewModels;

public sealed class ExtensionCardViewModel : ViewModelBase
{
    private readonly ExtensionService _extensions;
    private readonly GitHubService _github;
    private readonly SettingsService _settings;
    private readonly Action<string> _log;
    private readonly Action _refreshParent;

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
        Action refreshParent)
    {
        Info = info;
        _extensions = extensions;
        _github = github;
        _settings = settings;
        _log = log;
        _refreshParent = refreshParent;
        _installed = extensions.Find(info.RepoOwner, info.RepoName);

        InstallCommand = new AsyncRelayCommand(InstallAsync, _ => CanInstall);
        UninstallCommand = new RelayCommand(_ => Uninstall(), _ => IsInstalled && !Busy);
        OpenRepoCommand = new RelayCommand(_ => OpenUrl(Info.RepoUrl));
        OpenInstallDirCommand = new RelayCommand(_ => OpenDir(), _ => IsInstalled);
        _ = LoadIconAsync();
    }

    public string Title => Info.DisplayName;
    public string Version => Info.DisplayVersion;
    public string Description => Info.DisplayDescription;
    public string RepoUrl => Info.RepoUrl;
    public string Repo => $"{Info.RepoOwner}/{Info.RepoName}";
    public string AssetSummary => Info.AssetUrl != null
        ? $"{Info.AssetName} • {FormatSize(Info.AssetSizeBytes)}"
        : "no release asset";
    public string Stars => Info.Stars > 0 ? $"★ {Info.Stars}" : string.Empty;
    public bool HasAsset => !string.IsNullOrEmpty(Info.AssetUrl);
    public bool IsInstalled => _installed != null;
    public bool CanInstall => HasAsset && !Busy;
    public string InstallButtonLabel => IsInstalled
        ? (string.Equals(_installed!.Version, Info.DisplayVersion, StringComparison.OrdinalIgnoreCase)
            ? "Reinstall" : $"Update → {Info.DisplayVersion}")
        : "Install";
    public string StatusBadge => IsInstalled
        ? $"Installed • v{_installed!.Version}"
        : (HasAsset ? "Available" : "No release");

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

    private async Task InstallAsync(object? _)
    {
        if (!HasAsset) return;
        Busy = true;
        try
        {
            BusyMessage = "Downloading...";
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
            RaiseAllChanged();
            _refreshParent();
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
    }

    private void Uninstall()
    {
        Busy = true;
        try
        {
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
