using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace LocalChromeStore;

public partial class App : Application
{
    // Per-user scope: a portable ZIP can be launched twice, and two processes writing the same
    // JSON state files concurrently corrupts them. The mutex blocks the second writer; the event
    // lets the second instance ask the first to surface its window instead.
    private const string InstanceMutexName = @"Local\LocalChromeStore.SingleInstance";
    private const string ActivateEventName = @"Local\LocalChromeStore.Activate";

    private Mutex? _instanceMutex;
    private EventWaitHandle? _activateEvent;

    protected override void OnStartup(StartupEventArgs e)
    {
        _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out var isNew);
        if (!isNew)
        {
            // Another instance owns the state files — signal it to come forward and exit.
            try
            {
                if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var existing))
                {
                    existing.Set();
                    existing.Dispose();
                }
            }
            catch { /* best-effort — still exit so we never double-write state */ }
            // Exit immediately and reliably; Shutdown() during OnStartup (before the dispatcher
            // loop runs) can leave the second process alive long enough to double-write state.
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Environment.Exit(0);
            return;
        }

        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        ThreadPool.RegisterWaitForSingleObject(_activateEvent, (_, _) => Dispatcher.Invoke(SurfaceMainWindow),
            null, Timeout.Infinite, executeOnlyOnce: false);

        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            CrashLog.Write(args.ExceptionObject as Exception);
        base.OnStartup(e);
    }

    private void SurfaceMainWindow()
    {
        var window = MainWindow;
        if (window is null) return;
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Show();
        window.Activate();
        window.Topmost = true;
        window.Topmost = false; // brief toggle forces foreground without staying always-on-top
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activateEvent?.Dispose();
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLog.Write(e.Exception);
        MessageBox.Show(
            $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nDetails written to crash log.",
            "LocalChromeStore",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }
}

internal static class CrashLog
{
    public static void Write(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LocalChromeStore", "logs");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            File.WriteAllText(path, ex.ToString());
        }
        catch { /* swallow — last-ditch logger */ }
    }
}
