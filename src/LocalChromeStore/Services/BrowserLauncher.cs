using System.Diagnostics;
using System.IO;
using LocalChromeStore.Models;

namespace LocalChromeStore.Services;

public sealed class BrowserLauncher
{
    private readonly ExtensionService _extensions;
    private const string TemporaryProfilePlaceholder = "<new temporary LocalChromeStore profile>";

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

    public BrowserLaunchResult Launch(
        BrowserInfo browser,
        IEnumerable<InstalledExtension>? overrideSet = null,
        string? launchUrl = null,
        bool useTemporaryProfile = false)
    {
        var temporaryProfilePath = useTemporaryProfile ? CreateTemporaryProfileDirectory() : null;
        var plan = BuildLaunchPlan(browser, overrideSet ?? _extensions.Installed, launchUrl, useTemporaryProfile, temporaryProfilePath);

        var psi = new ProcessStartInfo
        {
            FileName = browser.ExecutablePath,
            UseShellExecute = false,
        };
        foreach (var a in plan.Arguments) psi.ArgumentList.Add(a);
        return new BrowserLaunchResult(Process.Start(psi), plan);
    }

    public BrowserLaunchPlan BuildLaunchPlan(
        BrowserInfo browser,
        IEnumerable<InstalledExtension>? overrideSet = null,
        string? launchUrl = null,
        bool useTemporaryProfile = false)
        => BuildLaunchPlan(browser, overrideSet ?? _extensions.Installed, launchUrl, useTemporaryProfile, temporaryProfilePath: null);

    public static BrowserLaunchPlan BuildLaunchPlan(
        BrowserInfo browser,
        IEnumerable<InstalledExtension> installed,
        string? launchUrl = null,
        bool useTemporaryProfile = false,
        string? temporaryProfilePath = null)
    {
        var paths = installed
            .Select(e => e.InstallPath)
            .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var args = new List<string>();
        string? profilePath = null;
        if (useTemporaryProfile)
        {
            profilePath = string.IsNullOrWhiteSpace(temporaryProfilePath)
                ? TemporaryProfilePlaceholder
                : temporaryProfilePath;
            args.Add($"--user-data-dir={profilePath}");
            args.Add("--no-first-run");
            args.Add("--no-default-browser-check");
        }

        if (paths.Count > 0)
            args.Add($"--load-extension={string.Join(",", paths)}");

        if (!string.IsNullOrWhiteSpace(launchUrl))
            args.Add(launchUrl.Trim());

        return new BrowserLaunchPlan
        {
            Browser = browser,
            Arguments = args,
            ExtensionCount = paths.Count,
            TemporaryProfilePath = profilePath
        };
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

    public static string FormatCommandLine(string executablePath, IEnumerable<string> arguments)
    {
        var parts = new List<string> { QuoteForDisplay(executablePath) };
        parts.AddRange(arguments.Select(QuoteForDisplay));
        return string.Join(" ", parts);
    }

    private static string QuoteForDisplay(string value)
    {
        if (string.IsNullOrEmpty(value)) return "\"\"";
        var escaped = value.Replace("\"", "\\\"");
        return escaped.Any(char.IsWhiteSpace) || escaped.Contains(',') || escaped.Contains('<') || escaped.Contains('>')
            ? $"\"{escaped}\""
            : escaped;
    }

    private static string CreateTemporaryProfileDirectory()
    {
        var root = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LocalChromeStore",
            "profiles",
            "temp");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss-ffff"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ProgramFiles(string rel) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), rel);
    private static string ProgramFiles86(string rel) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), rel);
    private static string LocalAppData(string rel) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), rel);
}

public sealed class BrowserLaunchPlan
{
    public required BrowserInfo Browser { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public required int ExtensionCount { get; init; }
    public string? TemporaryProfilePath { get; init; }
    public string DisplayCommand => BrowserLauncher.FormatCommandLine(Browser.ExecutablePath, Arguments);
}

public sealed record BrowserLaunchResult(Process? Process, BrowserLaunchPlan Plan);
