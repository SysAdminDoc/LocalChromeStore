using System.IO;

namespace LocalChromeStore.Services;

public sealed class LocalSourceWatcher : IDisposable
{
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly Action<string> _onChanged;
    private readonly object _lock = new();
    private DateTime _lastNotify = DateTime.MinValue;

    public LocalSourceWatcher(Action<string> onChanged)
    {
        _onChanged = onChanged;
    }

    public void Watch(IEnumerable<string> folders)
    {
        StopAll();
        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                var watcher = new FileSystemWatcher(folder)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    Filter = "manifest.json",
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };
                watcher.Changed += OnFileChanged;
                watcher.Created += OnFileChanged;
                _watchers.Add(watcher);
            }
            catch
            {
                // Some paths may not be watchable (network shares, etc.)
            }
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        lock (_lock)
        {
            if ((DateTime.UtcNow - _lastNotify).TotalSeconds < 3) return;
            _lastNotify = DateTime.UtcNow;
        }
        var folder = Path.GetDirectoryName(e.FullPath) ?? e.FullPath;
        _onChanged($"Local source changed: {folder}. Click Refresh to reload the catalog.");
    }

    public void StopAll()
    {
        foreach (var w in _watchers)
        {
            w.EnableRaisingEvents = false;
            w.Dispose();
        }
        _watchers.Clear();
    }

    public void Dispose() => StopAll();
}
