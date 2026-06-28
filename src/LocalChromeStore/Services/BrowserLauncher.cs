using System.Diagnostics;
using System.IO;
using LocalChromeStore.Models;

namespace LocalChromeStore.Services;

/// <summary>
/// How an extension set can be loaded into a given browser build. The command-line
/// <c>--load-extension</c> path was progressively locked down across the Chromium family:
/// Chrome 137 (May 2025) removed it from branded Chrome; Chromium/Brave 137 disabled it by
/// default but allow re-enabling with <c>--disable-features=DisableLoadExtensionCommandLineSwitch</c>;
/// Chrome 142 (~Nov 2025) removed that workaround on branded Chrome entirely.
/// </summary>
public enum LaunchStrategy
{
    /// <summary>Plain <c>--load-extension</c> works (pre-137 builds, Chrome for Testing).</summary>
    CommandLineLoad,

    /// <summary><c>--load-extension</c> works only with the <c>--disable-features</c> override
    /// (unbranded Chromium / Brave / Edge / Vivaldi / Opera, 137+).</summary>
    CommandLineLoadWithOverride,

    /// <summary>No command-line load path remains (branded Chrome 142+). Extensions must be
    /// loaded via CDP <c>Extensions.loadUnpacked</c>, Chrome for Testing, or enterprise policy.</summary>
    CdpLoadUnpacked
}

public sealed class BrowserLauncher
{
    public const string DisableLoadExtensionOverrideFlag = "--disable-features=DisableLoadExtensionCommandLineSwitch";

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
        AddChromeForTesting(results);
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
                var version = TryReadVersion(c);
                list.Add(new BrowserInfo
                {
                    Kind = kind,
                    DisplayName = name,
                    ExecutablePath = c,
                    MajorVersion = version.Major,
                    ProductVersion = version.ProductVersion
                });
                return;
            }
        }
    }

    private static void AddChromeForTesting(List<BrowserInfo> list)
    {
        foreach (var path in FindChromeForTestingExecutables())
        {
            var version = TryReadVersion(path);
            var suffix = version.Major is { } major ? $" {major}" : string.Empty;
            list.Add(new BrowserInfo
            {
                Kind = BrowserKind.ChromeForTesting,
                DisplayName = $"Chrome for Testing{suffix}",
                ExecutablePath = path,
                MajorVersion = version.Major,
                ProductVersion = version.ProductVersion
            });
        }
    }

    public static IReadOnlyList<string> FindChromeForTestingExecutables(IEnumerable<string>? roots = null)
    {
        var searchRoots = roots?.Where(r => !string.IsNullOrWhiteSpace(r)).ToArray() ?? ChromeForTestingSearchRoots();
        var found = new List<string>();
        foreach (var root in searchRoots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Directory.Exists(root)) continue;
                foreach (var exe in Directory.EnumerateFiles(root, "chrome.exe", SearchOption.AllDirectories))
                {
                    if (IsChromeForTestingPath(exe))
                        found.Add(exe);
                }
            }
            catch
            {
                // Ignore unreadable cache folders; browser detection should never block startup.
            }
        }

        return found
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ChromeForTestingSearchRoots()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[]
        {
            ProgramFiles(@"Google\Chrome for Testing"),
            ProgramFiles86(@"Google\Chrome for Testing"),
            LocalAppData(@"Google\Chrome for Testing"),
            Path.Combine(userProfile, ".cache", "selenium", "chrome"),
            Path.Combine(userProfile, ".cache", "chrome-for-testing"),
            Path.Combine(userProfile, ".cache", "puppeteer", "chrome")
        };
    }

    private static bool IsChromeForTestingPath(string path)
    {
        var normalized = path.Replace('/', '\\');
        return normalized.Contains("Chrome for Testing", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("chrome-for-testing", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(@"\selenium\chrome\", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains(@"\puppeteer\chrome\", StringComparison.OrdinalIgnoreCase);
    }

    private static (int? Major, string? ProductVersion) TryReadVersion(string executablePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(executablePath);
            var version = string.IsNullOrWhiteSpace(info.ProductVersion)
                ? info.FileVersion
                : info.ProductVersion;
            // ProductMajorPart is the Chromium milestone (e.g. 142) for Chrome/Edge/Brave/etc.
            if (info.ProductMajorPart > 0) return (info.ProductMajorPart, version);
            if (info.FileMajorPart > 0) return (info.FileMajorPart, version);
            return (null, version);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Resolves how the given browser build will load extensions. See <see cref="LaunchStrategy"/>.
    /// When the version is unknown we assume the current stable behaviour for that family
    /// (branded Chrome → CDP-only; every other Chromium fork → command line with the override).
    /// </summary>
    public static LaunchStrategy ResolveStrategy(BrowserKind kind, int? majorVersion) => kind switch
    {
        // Chrome for Testing intentionally keeps automation and extension-load flags available.
        BrowserKind.ChromeForTesting => LaunchStrategy.CommandLineLoad,
        // Branded Chrome: 137 removed --load-extension, 142 removed the override workaround.
        BrowserKind.Chrome => majorVersion switch
        {
            null => LaunchStrategy.CdpLoadUnpacked,
            < 137 => LaunchStrategy.CommandLineLoad,
            < 142 => LaunchStrategy.CommandLineLoadWithOverride,
            _ => LaunchStrategy.CdpLoadUnpacked
        },
        // Unbranded Chromium and the other forks still honour --load-extension with the override
        // on 137+. (Edge's 142-tracking is unconfirmed; the override is harmless if not required.)
        _ => majorVersion switch
        {
            null => LaunchStrategy.CommandLineLoadWithOverride,
            < 137 => LaunchStrategy.CommandLineLoad,
            _ => LaunchStrategy.CommandLineLoadWithOverride
        }
    };

    /// <summary>
    /// True when at least one process is already running from the same browser executable.
    /// Launching into the default profile of a running browser causes Chromium to forward the
    /// command line to the existing process and silently drop <c>--load-extension</c>.
    /// </summary>
    public static bool IsBrowserRunning(BrowserInfo browser)
    {
        try
        {
            var name = Path.GetFileNameWithoutExtension(browser.ExecutablePath);
            if (string.IsNullOrEmpty(name)) return false;
            return Process.GetProcessesByName(name).Length > 0;
        }
        catch
        {
            return false;
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

        var strategy = ResolveStrategy(browser.Kind, browser.MajorVersion);
        var warnings = new List<string>();

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

        var loadsViaCommandLine = strategy is LaunchStrategy.CommandLineLoad or LaunchStrategy.CommandLineLoadWithOverride;

        if (strategy == LaunchStrategy.CommandLineLoadWithOverride)
            args.Add(DisableLoadExtensionOverrideFlag);

        if (paths.Count > 0 && loadsViaCommandLine)
            args.Add($"--load-extension={string.Join(",", paths)}");

        if (paths.Count > 0 && strategy == LaunchStrategy.CdpLoadUnpacked)
        {
            var version = browser.MajorVersion is { } v ? $" {v}" : "";
            warnings.Add(
                $"{browser.DisplayName}{version} no longer supports loading extensions from the command line " +
                "(branded Chrome removed --load-extension in 137 and its override in 142). LocalChromeStore will use " +
                "CDP Extensions.loadUnpacked with --remote-debugging-pipe; if CDP fails, use Chrome for Testing, " +
                "Brave/Chromium, a clean temporary profile, or enterprise policy mode instead.");
        }

        if (!string.IsNullOrWhiteSpace(launchUrl))
            args.Add(launchUrl.Trim());

        return new BrowserLaunchPlan
        {
            Browser = browser,
            Arguments = args,
            ExtensionCount = paths.Count,
            TemporaryProfilePath = profilePath,
            Strategy = strategy,
            LoadsExtensions = paths.Count == 0 || loadsViaCommandLine,
            Warnings = warnings
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

    // F068: browser policy quick link.
    public Process? OpenPolicyPage(BrowserInfo browser)
    {
        var url = PolicyPageUrl(browser.Kind);
        var psi = new ProcessStartInfo
        {
            FileName = browser.ExecutablePath,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add(url);
        return Process.Start(psi);
    }

    public static string PolicyPageUrl(BrowserKind kind) => kind switch
    {
        BrowserKind.Edge => "edge://policy",
        _ => "chrome://policy"
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

    public static string CreateTemporaryProfileDirectory()
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
    public LaunchStrategy Strategy { get; init; }

    /// <summary>False when the chosen strategy cannot load the requested extensions via the command line
    /// (branded Chrome 142+). The browser still launches; the extensions just will not be loaded.</summary>
    public bool LoadsExtensions { get; init; } = true;

    public IReadOnlyList<string> Warnings { get; init; } = [];
    public string DisplayCommand => BrowserLauncher.FormatCommandLine(Browser.ExecutablePath, Arguments);

    /// <summary>Human-readable explanation of the chosen <see cref="LaunchStrategy"/> for the launch log,
    /// including the browser version it was resolved from.</summary>
    public string StrategyDescription
    {
        get
        {
            var version = Browser.MajorVersion is { } v ? $"v{v}" : "unknown version";
            return Strategy switch
            {
                LaunchStrategy.CommandLineLoad =>
                    $"{Browser.DisplayName} ({version}): loads via plain --load-extension.",
                LaunchStrategy.CommandLineLoadWithOverride =>
                    $"{Browser.DisplayName} ({version}): loads via --load-extension with the DisableLoadExtensionCommandLineSwitch override (Chromium 137+).",
                LaunchStrategy.CdpLoadUnpacked =>
                    $"{Browser.DisplayName} ({version}): no command-line load path (branded Chrome 142+); use CDP/Chrome for Testing/Brave/policy mode.",
                _ => $"{Browser.DisplayName} ({version}): {Strategy}."
            };
        }
    }
}

public sealed record BrowserLaunchResult(Process? Process, BrowserLaunchPlan Plan);
