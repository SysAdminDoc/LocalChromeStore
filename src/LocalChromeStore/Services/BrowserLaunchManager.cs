using LocalChromeStore.Models;

namespace LocalChromeStore.Services;

/// <summary>
/// Owns the browser-launch decision and the activity-log / status messages it produces, separated
/// from the view model's UI plumbing. The pure pieces — the empty-set messages
/// (<see cref="EmptySet"/>) and the post-launch description (<see cref="DescribeLaunch"/>) — are
/// unit-tested headlessly; <see cref="Launch"/> wires them around the real
/// <see cref="BrowserLauncher.Launch"/> (which starts the process).
/// </summary>
public sealed class BrowserLaunchManager
{
    private readonly BrowserLauncher _launcher;

    public BrowserLaunchManager(BrowserLauncher launcher) => _launcher = launcher;

    /// <summary>The result of a launch attempt: the status line, the log lines, and whether it launched.</summary>
    public sealed record Outcome(string StatusText, IReadOnlyList<string> Log, bool Launched);

    /// <summary>Messages for when the active set resolves to no installed extensions.</summary>
    public static Outcome EmptySet(bool isSentinel, string? loadSetName)
    {
        var status = isSentinel
            ? "Install at least one extension before launching a browser session."
            : $"No extensions in load set '{loadSetName}' are currently installed. Install them or switch to 'All installed'.";
        var log = isSentinel
            ? "No extensions installed yet — install one before launching."
            : $"Load set '{loadSetName}' has no installed extensions — check installs or switch load set.";
        return new Outcome(status, new[] { log }, Launched: false);
    }

    /// <summary>
    /// Status + log lines describing a completed launch plan: which strategy, whether extensions
    /// actually load on the target, the temporary profile (if any), and the full command line.
    /// </summary>
    public static (string Status, List<string> Log) DescribeLaunch(
        BrowserLaunchPlan plan, int extensionCount, bool isSentinel, string? loadSetName)
    {
        var log = new List<string> { $"Load strategy — {plan.StrategyDescription}" };
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
            log.Add($"Launched {plan.Browser.DisplayName} without loading {extensionCount} extension(s) — see warning above.");
        }

        if (!string.IsNullOrEmpty(plan.TemporaryProfilePath))
            log.Add($"Temporary browser profile: {plan.TemporaryProfilePath}");
        log.Add($"Launch command: {plan.DisplayCommand}");
        return (status, log);
    }

    /// <summary>
    /// Launches <paramref name="browser"/> with the resolved extension set and returns the messages
    /// to surface. Warns first if a non-temporary launch targets an already-running browser (Chromium
    /// forwards the args to the existing window and drops <c>--load-extension</c>).
    /// </summary>
    public Outcome Launch(
        BrowserInfo browser,
        IReadOnlyList<InstalledExtension> set,
        string? launchUrl,
        bool useTemporaryProfile,
        bool isSentinel,
        string? loadSetName)
    {
        if (set.Count == 0) return EmptySet(isSentinel, loadSetName);

        var log = new List<string>();
        try
        {
            if (!useTemporaryProfile && BrowserLauncher.IsBrowserRunning(browser))
                log.Add($"! {browser.DisplayName} is already running. Chromium forwards arguments to the existing " +
                    "window and drops --load-extension, so the extensions may not load. Close it first or enable 'Clean temp profile'.");

            var result = _launcher.Launch(browser, set, launchUrl, useTemporaryProfile);
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
}
