using System.IO;
using LocalChromeStore.Models;
using LocalChromeStore.Services.Cdp;

namespace LocalChromeStore.Services;

/// <summary>
/// Owns browser-launch decisions and the activity-log/status messages they produce, separated from
/// the view model's UI plumbing.
/// </summary>
public sealed class BrowserLaunchManager
{
    private readonly BrowserLauncher _launcher;
    private readonly ICdpExtensionLoader _cdpLoader;

    public BrowserLaunchManager(BrowserLauncher launcher, ICdpExtensionLoader? cdpLoader = null)
    {
        _launcher = launcher;
        _cdpLoader = cdpLoader ?? new CdpExtensionLoader();
    }

    /// <summary>The result of a launch attempt: the status line, the log lines, and whether it launched.</summary>
    public sealed record Outcome(string StatusText, IReadOnlyList<string> Log, bool Launched);

    /// <summary>Messages for when the active set resolves to no installed extensions.</summary>
    public static Outcome EmptySet(bool isSentinel, string? loadSetName)
    {
        var status = isSentinel
            ? "Install at least one extension before launching a browser session."
            : $"No extensions in load set '{loadSetName}' are currently installed. Install them or switch to 'All installed'.";
        var log = isSentinel
            ? "No extensions installed yet - install one before launching."
            : $"Load set '{loadSetName}' has no installed extensions - check installs or switch load set.";
        return new Outcome(status, new[] { log }, Launched: false);
    }

    /// <summary>
    /// Status + log lines describing a completed command-line launch plan: which strategy, whether
    /// extensions actually load on the target, the temporary profile, and the full command line.
    /// </summary>
    public static (string Status, List<string> Log) DescribeLaunch(
        BrowserLaunchPlan plan, int extensionCount, bool isSentinel, string? loadSetName)
    {
        var log = new List<string> { $"Load strategy - {plan.StrategyDescription}" };
        foreach (var warning in plan.Warnings) log.Add($"! {warning}");

        var setLabel = isSentinel ? "all installed" : $"load set '{loadSetName}'";
        string status;
        if (plan.LoadsExtensions)
        {
            status = $"Launched {plan.Browser.DisplayName} with {extensionCount} extension(s) ({setLabel}).";
            log.Add($"Launched {plan.Browser.DisplayName} with {extensionCount} extension(s) loaded ({setLabel}).");
        }
        else
        {
            status = $"Launched {plan.Browser.DisplayName}, but it cannot load extensions from the command line.";
            log.Add($"Launched {plan.Browser.DisplayName} without loading {extensionCount} extension(s) - see warning above.");
        }

        AddProfileLog(log, plan);
        log.Add($"Launch command: {DisplayCommandForPlan(plan)}");
        return (status, log);
    }

    public static string DisplayCommandForPlan(BrowserLaunchPlan plan)
    {
        if (plan.Strategy != LaunchStrategy.CdpLoadUnpacked || plan.ExtensionCount == 0)
            return plan.DisplayCommand;

        return BrowserLauncher.FormatCommandLine(
            plan.Browser.ExecutablePath,
            CdpProtocol.RequiredLaunchFlags.Concat(plan.Arguments));
    }

    /// <summary>
    /// Launches <paramref name="browser"/> with the resolved extension set. Branded Chrome 142+
    /// uses CDP <c>Extensions.loadUnpacked</c>; other Chromium-family browsers use the command-line
    /// strategy selected by <see cref="BrowserLauncher.ResolveStrategy"/>.
    /// </summary>
    public async Task<Outcome> LaunchAsync(
        BrowserInfo browser,
        IReadOnlyList<InstalledExtension> set,
        string? launchUrl,
        bool useTemporaryProfile,
        bool isSentinel,
        string? loadSetName,
        CancellationToken ct = default)
        => await LaunchAsync(
            browser,
            set,
            launchUrl,
            useTemporaryProfile ? BrowserProfileMode.Temporary : BrowserProfileMode.Default,
            isSentinel,
            loadSetName,
            ct);

    public async Task<Outcome> LaunchAsync(
        BrowserInfo browser,
        IReadOnlyList<InstalledExtension> set,
        string? launchUrl,
        BrowserProfileMode profileMode,
        bool isSentinel,
        string? loadSetName,
        CancellationToken ct = default)
    {
        if (set.Count == 0) return EmptySet(isSentinel, loadSetName);

        var profilePath = profileMode switch
        {
            BrowserProfileMode.Temporary => BrowserLauncher.CreateTemporaryProfileDirectory(),
            BrowserProfileMode.Persistent => BrowserLauncher.CreatePersistentProfileDirectory(browser, LoadSetKey(isSentinel, loadSetName), loadSetName: null),
            _ => null
        };
        var plan = BrowserLauncher.BuildLaunchPlan(browser, set, launchUrl, profileMode, profilePath);
        if (plan.Strategy == LaunchStrategy.CdpLoadUnpacked)
            return await LaunchViaCdpAsync(plan, set, isSentinel, loadSetName, ct);

        return LaunchViaCommandLine(browser, set, launchUrl, profileMode, profilePath, isSentinel, loadSetName);
    }

    private Outcome LaunchViaCommandLine(
        BrowserInfo browser,
        IReadOnlyList<InstalledExtension> set,
        string? launchUrl,
        BrowserProfileMode profileMode,
        string? profilePath,
        bool isSentinel,
        string? loadSetName)
    {
        var log = new List<string>();
        try
        {
            if (profileMode == BrowserProfileMode.Default && BrowserLauncher.IsBrowserRunning(browser))
                log.Add($"! {browser.DisplayName} is already running. Chromium forwards arguments to the existing " +
                    "window and drops --load-extension, so the extensions may not load. Close it first or choose an isolated profile mode.");

            var result = _launcher.Launch(browser, set, launchUrl, profileMode, profilePath);
            var (status, describeLog) = DescribeLaunch(result.Plan, set.Count, isSentinel, loadSetName);
            log.AddRange(describeLog);
            return new Outcome(status, log, Launched: true);
        }
        catch (Exception ex)
        {
            log.Add($"! Launch failed: {ex.Message}");
            return new Outcome($"Launch failed: {ex.Message}", log, Launched: false);
        }
    }

    private async Task<Outcome> LaunchViaCdpAsync(
        BrowserLaunchPlan plan,
        IReadOnlyList<InstalledExtension> set,
        bool isSentinel,
        string? loadSetName,
        CancellationToken ct)
    {
        var log = new List<string>
        {
            $"Load strategy - {plan.StrategyDescription}",
            "CDP loader selected: launching with --remote-debugging-pipe and Extensions.loadUnpacked."
        };
        foreach (var warning in plan.Warnings) log.Add($"! {warning}");
        AddProfileLog(log, plan);

        var extensionPaths = ResolveExtensionPaths(set);
        if (extensionPaths.Count == 0)
            return new Outcome("No installed extension folders exist on disk for this launch.", log, Launched: false);

        var command = DisplayCommandForPlan(plan);
        log.Add($"Launch command: {command}");

        if (BrowserLauncher.IsBrowserRunning(plan.Browser) && plan.ProfileMode == BrowserProfileMode.Default)
            log.Add($"! {plan.Browser.DisplayName} is already running. CDP pipe launch may attach to no usable pipe; close it first or choose an isolated profile mode.");

        try
        {
            var result = await _cdpLoader.LaunchAndLoadAsync(plan.Browser.ExecutablePath, extensionPaths, plan.Arguments, ct);
            foreach (var attempt in result.Attempts)
            {
                var prefix = attempt.Success ? "CDP loaded" : "! CDP failed";
                var id = string.IsNullOrWhiteSpace(attempt.ExtensionId) ? string.Empty : $" ({attempt.ExtensionId})";
                log.Add($"{prefix}: {attempt.ExtensionPath}{id} - {attempt.Detail}");
            }

            var setLabel = isSentinel ? "all installed" : $"load set '{loadSetName}'";
            if (result.Success)
            {
                log.Add($"CDP summary: {result.Detail} ({setLabel}).");
                return new Outcome(
                    $"Launched {plan.Browser.DisplayName} and loaded {result.Loaded}/{result.Total} extension(s) via CDP ({setLabel}).",
                    log,
                    Launched: true);
            }

            log.Add($"! CDP summary: {result.Detail}.");
            log.Add("Fallback: use Chrome for Testing, Brave/Chromium, a clean temp profile, or Enterprise Policy mode.");
            return new Outcome(
                $"CDP launch did not load all extensions in {plan.Browser.DisplayName}: {result.Detail}.",
                log,
                Launched: result.Loaded > 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.Add($"! CDP launch failed: {ex.Message}");
            log.Add("Fallback: use Chrome for Testing, Brave/Chromium, a clean temp profile, or Enterprise Policy mode.");
            return new Outcome($"CDP launch failed: {ex.Message}", log, Launched: false);
        }
    }

    private static List<string> ResolveExtensionPaths(IEnumerable<InstalledExtension> installed) =>
        installed
            .Select(e => e.InstallPath)
            .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static void AddProfileLog(List<string> log, BrowserLaunchPlan plan)
    {
        var profilePath = plan.ProfilePath ?? plan.TemporaryProfilePath;
        if (string.IsNullOrWhiteSpace(profilePath)) return;
        var label = plan.ProfileMode == BrowserProfileMode.Persistent
            ? "Persistent browser profile"
            : "Temporary browser profile";
        log.Add($"{label}: {profilePath}");
    }

    private static string? LoadSetKey(bool isSentinel, string? loadSetName)
    {
        if (isSentinel) return null;
        return string.IsNullOrWhiteSpace(loadSetName) ? "load-set" : loadSetName;
    }
}
