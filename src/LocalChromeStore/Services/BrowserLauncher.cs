using System.Diagnostics;
using System.IO;
using LocalChromeStore.Models;

namespace LocalChromeStore.Services;

public sealed class BrowserLauncher
{
    private readonly ExtensionService _extensions;

    public BrowserLauncher(ExtensionService extensions)
    {
        _extensions = extensions;
    }

    public IReadOnlyList<BrowserInfo> Detect()
    {
        var results = new List<BrowserInfo>();
        TryAdd(results, BrowserKind.Chrome, "Google Chrome", new[]
        {
            ProgramFiles(@"Google\Chrome\Application\chrome.exe"),
            ProgramFiles86(@"Google\Chrome\Application\chrome.exe"),
            LocalAppData(@"Google\Chrome\Application\chrome.exe")
        });
        TryAdd(results, BrowserKind.Brave, "Brave", new[]
        {
            ProgramFiles(@"BraveSoftware\Brave-Browser\Application\brave.exe"),
            ProgramFiles86(@"BraveSoftware\Brave-Browser\Application\brave.exe"),
            LocalAppData(@"BraveSoftware\Brave-Browser\Application\brave.exe")
        });
        TryAdd(results, BrowserKind.Edge, "Microsoft Edge", new[]
        {
            ProgramFiles(@"Microsoft\Edge\Application\msedge.exe"),
            ProgramFiles86(@"Microsoft\Edge\Application\msedge.exe")
        });
        TryAdd(results, BrowserKind.Vivaldi, "Vivaldi", new[]
        {
            LocalAppData(@"Vivaldi\Application\vivaldi.exe"),
            ProgramFiles(@"Vivaldi\Application\vivaldi.exe")
        });
        TryAdd(results, BrowserKind.Opera, "Opera", new[]
        {
            LocalAppData(@"Programs\Opera\opera.exe")
        });
        TryAdd(results, BrowserKind.Chromium, "Chromium", new[]
        {
            ProgramFiles(@"Chromium\Application\chrome.exe"),
            LocalAppData(@"Chromium\Application\chrome.exe")
        });
        return results;
    }

    private static void TryAdd(List<BrowserInfo> list, BrowserKind kind, string name, string[] candidates)
    {
        foreach (var c in candidates.Where(p => !string.IsNullOrEmpty(p)))
        {
            if (File.Exists(c))
            {
                list.Add(new BrowserInfo { Kind = kind, DisplayName = name, ExecutablePath = c });
                return;
            }
        }
    }

    public Process? Launch(BrowserInfo browser, IEnumerable<InstalledExtension>? overrideSet = null, string? launchUrl = null)
    {
        var set = (overrideSet ?? _extensions.Installed).ToList();
        var paths = set.Select(e => e.InstallPath).Where(Directory.Exists).ToList();
        var args = new List<string>();
        if (paths.Count > 0)
        {
            // Quote each path; --load-extension wants a comma-separated list.
            var joined = string.Join(",", paths);
            args.Add($"--load-extension=\"{joined}\"");
        }
        if (!string.IsNullOrWhiteSpace(launchUrl))
            args.Add(launchUrl);

        var psi = new ProcessStartInfo
        {
            FileName = browser.ExecutablePath,
            UseShellExecute = false,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        return Process.Start(psi);
    }

    /// <summary>
    /// Opens the browser's native extension management page. Edge uses edge://extensions,
    /// every other Chromium-family browser routes chrome://extensions to its own page.
    /// </summary>
    public Process? OpenExtensionsPage(BrowserInfo browser)
    {
        var url = ExtensionsPageUrl(browser.Kind);
        var psi = new ProcessStartInfo
        {
            FileName = browser.ExecutablePath,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(url);
        return Process.Start(psi);
    }

    public static string ExtensionsPageUrl(BrowserKind kind) => kind switch
    {
        BrowserKind.Edge => "edge://extensions",
        _ => "chrome://extensions"
    };

    private static string ProgramFiles(string rel) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), rel);
    private static string ProgramFiles86(string rel) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), rel);
    private static string LocalAppData(string rel) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), rel);
}
