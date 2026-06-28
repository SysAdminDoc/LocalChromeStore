using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalChromeStore.Models;
using LocalChromeStore.Services.Cdp;

namespace LocalChromeStore.Services;

public sealed class BrowserConformanceService
{
    private const string FixtureVersion = "1.0.0";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SettingsService _settings;
    private readonly ICdpExtensionLoader _cdpLoader;
    private readonly IBrowserConformanceProcessLauncher _processLauncher;
    private readonly TimeSpan _probeDuration;

    public BrowserConformanceService(
        SettingsService settings,
        ICdpExtensionLoader? cdpLoader = null,
        IBrowserConformanceProcessLauncher? processLauncher = null,
        TimeSpan? probeDuration = null)
    {
        _settings = settings;
        _cdpLoader = cdpLoader ?? new CdpExtensionLoader(terminateBrowserOnDispose: true);
        _processLauncher = processLauncher ?? new BrowserConformanceProcessLauncher();
        _probeDuration = probeDuration ?? TimeSpan.FromSeconds(4);
    }

    public async Task<BrowserConformanceRun> RunAsync(IReadOnlyList<BrowserInfo> browsers, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_settings.LogsDir);
        var fixturePath = EnsureFixtureExtension();
        var generatedAt = DateTimeOffset.Now;
        var results = new List<BrowserConformanceBrowserResult>();

        foreach (var browser in browsers.DistinctBy(b => b.ExecutablePath, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await ProbeBrowserAsync(browser, fixturePath, ct));
        }

        var report = new BrowserConformanceReport(
            SchemaVersion: 1,
            GeneratedAt: generatedAt,
            MachineName: Environment.MachineName,
            FixturePath: fixturePath,
            Browsers: results);

        var baseName = $"browser-conformance-{generatedAt:yyyy-MM-dd-HHmmss}";
        var jsonPath = Path.Combine(_settings.LogsDir, baseName + ".json");
        var textPath = Path.Combine(_settings.LogsDir, baseName + ".txt");
        await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(report, JsonOptions), Encoding.UTF8, ct);
        await File.WriteAllTextAsync(textPath, FormatTextReport(report), Encoding.UTF8, ct);
        return new BrowserConformanceRun(report, jsonPath, textPath);
    }

    public static BrowserConformanceLatestReports FindLatestReports(string logsDir)
    {
        if (!Directory.Exists(logsDir))
            return new BrowserConformanceLatestReports(null, null);

        var json = Directory.EnumerateFiles(logsDir, "browser-conformance-*.json")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        var text = Directory.EnumerateFiles(logsDir, "browser-conformance-*.txt")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
        return new BrowserConformanceLatestReports(json, text);
    }

    private async Task<BrowserConformanceBrowserResult> ProbeBrowserAsync(BrowserInfo browser, string fixturePath, CancellationToken ct)
    {
        var tempProfile = CreateProbeProfileDirectory(browser);
        var fixture = new InstalledExtension
        {
            RepoOwner = "local",
            RepoName = "conformance-fixture",
            Version = FixtureVersion,
            InstallPath = fixturePath,
            ManifestPath = Path.Combine(fixturePath, "manifest.json"),
            InstalledAt = DateTimeOffset.Now,
            DisplayName = "LocalChromeStore Conformance Fixture",
            ManifestVersionNumber = 3
        };
        var plan = BrowserLauncher.BuildLaunchPlan(browser, new[] { fixture }, "about:blank", true, tempProfile);
        var effectiveArgs = plan.Strategy == LaunchStrategy.CdpLoadUnpacked
            ? CdpProtocol.RequiredLaunchFlags.Concat(plan.Arguments).ToArray()
            : plan.Arguments.ToArray();
        var displayCommand = BrowserLaunchManager.DisplayCommandForPlan(plan);

        if (plan.Strategy == LaunchStrategy.CdpLoadUnpacked)
        {
            var cdpResult = await _cdpLoader.LaunchAndLoadAsync(
                browser.ExecutablePath,
                new[] { fixturePath },
                plan.Arguments,
                ct);

            return BrowserConformanceBrowserResult.FromCdp(
                browser,
                plan,
                effectiveArgs,
                displayCommand,
                cdpResult);
        }

        var processResult = await _processLauncher.LaunchAsync(plan, _probeDuration, ct);
        return BrowserConformanceBrowserResult.FromProcess(
            browser,
            plan,
            effectiveArgs,
            displayCommand,
            processResult);
    }

    private string EnsureFixtureExtension()
    {
        var root = Path.Combine(_settings.CacheDir, "conformance-fixture", "fixture-extension");
        Directory.CreateDirectory(root);
        var manifest = """
        {
          "manifest_version": 3,
          "name": "LocalChromeStore Conformance Fixture",
          "version": "1.0.0",
          "description": "Minimal fixture used by LocalChromeStore browser loading checks.",
          "background": {
            "service_worker": "service-worker.js"
          }
        }
        """;
        File.WriteAllText(Path.Combine(root, "manifest.json"), manifest, Encoding.UTF8);
        File.WriteAllText(
            Path.Combine(root, "service-worker.js"),
            "chrome.runtime.onInstalled.addListener(() => console.log('LocalChromeStore conformance fixture installed'));\n",
            Encoding.UTF8);
        return root;
    }

    private string CreateProbeProfileDirectory(BrowserInfo browser)
    {
        var root = Path.Combine(_settings.CacheDir, "conformance-profiles");
        Directory.CreateDirectory(root);
        var safeName = string.Concat(browser.DisplayName.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
        var path = Path.Combine(root, $"{DateTime.Now:yyyyMMdd-HHmmss-ffff}-{safeName}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string FormatTextReport(BrowserConformanceReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("LocalChromeStore browser conformance report");
        sb.AppendLine($"Generated: {report.GeneratedAt:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine($"Machine: {report.MachineName}");
        sb.AppendLine($"Fixture: {report.FixturePath}");
        sb.AppendLine($"Browsers: {report.Browsers.Count}");
        sb.AppendLine();

        foreach (var browser in report.Browsers)
        {
            sb.AppendLine($"== {browser.DisplayName} ==");
            sb.AppendLine($"Path: {browser.ExecutablePath}");
            sb.AppendLine($"Version: {browser.BrowserVersion ?? "(unknown)"}");
            sb.AppendLine($"Strategy: {browser.Strategy}");
            sb.AppendLine($"Success: {browser.Success}");
            sb.AppendLine($"Detail: {browser.Detail}");
            sb.AppendLine($"Temp profile: {browser.TemporaryProfilePath}");
            sb.AppendLine($"Command: {browser.DisplayCommand}");
            sb.AppendLine($"Args: {string.Join(" ", browser.Arguments)}");
            foreach (var warning in browser.Warnings)
                sb.AppendLine($"Warning: {warning}");
            if (browser.CdpAttempts.Count > 0)
            {
                sb.AppendLine($"CDP: loaded {browser.CdpLoaded}/{browser.CdpTotal}");
                foreach (var attempt in browser.CdpAttempts)
                {
                    var id = string.IsNullOrWhiteSpace(attempt.ExtensionId) ? "(no id)" : attempt.ExtensionId;
                    sb.AppendLine($"CDP attempt: success={attempt.Success}; id={id}; detail={attempt.Detail}; path={attempt.ExtensionPath}");
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }
}

public interface IBrowserConformanceProcessLauncher
{
    Task<BrowserConformanceProcessResult> LaunchAsync(
        BrowserLaunchPlan plan,
        TimeSpan probeDuration,
        CancellationToken ct = default);
}

public sealed class BrowserConformanceProcessLauncher : IBrowserConformanceProcessLauncher
{
    public async Task<BrowserConformanceProcessResult> LaunchAsync(
        BrowserLaunchPlan plan,
        TimeSpan probeDuration,
        CancellationToken ct = default)
    {
        Process? process = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = plan.Browser.ExecutablePath,
                UseShellExecute = false
            };
            foreach (var arg in plan.Arguments)
                psi.ArgumentList.Add(arg);

            process = Process.Start(psi);
            if (process is null)
                return new BrowserConformanceProcessResult(false, null, false, null, "Process.Start returned null.");

            var exitedDuringProbe = await WaitForExitAsync(process, probeDuration, ct);
            int? exitCode = exitedDuringProbe ? process.ExitCode : null;
            var detail = exitedDuringProbe
                ? $"Browser process exited during the {probeDuration.TotalSeconds:0.#}s probe window."
                : $"Browser process stayed alive for the {probeDuration.TotalSeconds:0.#}s probe window and was closed.";

            if (!exitedDuringProbe)
                await CloseOrKillAsync(process, ct);

            return new BrowserConformanceProcessResult(true, process.Id, exitedDuringProbe, exitCode, detail);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new BrowserConformanceProcessResult(false, process?.Id, false, null, ex.Message);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static async Task<bool> WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken ct)
    {
        if (process.HasExited) return true;
        var waitTask = process.WaitForExitAsync(ct);
        var delayTask = Task.Delay(timeout, ct);
        return await Task.WhenAny(waitTask, delayTask) == waitTask;
    }

    private static async Task CloseOrKillAsync(Process process, CancellationToken ct)
    {
        try
        {
            if (!process.HasExited && process.CloseMainWindow())
            {
                var closed = await WaitForExitAsync(process, TimeSpan.FromSeconds(1), ct);
                if (closed) return;
            }
        }
        catch
        {
            // Fall through to Kill.
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
        catch
        {
            // Best-effort probe cleanup.
        }
    }
}

public sealed record BrowserConformanceRun(
    BrowserConformanceReport Report,
    string JsonPath,
    string TextPath);

public sealed record BrowserConformanceReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAt,
    string MachineName,
    string FixturePath,
    IReadOnlyList<BrowserConformanceBrowserResult> Browsers);

public sealed record BrowserConformanceLatestReports(string? JsonPath, string? TextPath);

public sealed record BrowserConformanceProcessResult(
    bool Started,
    int? ProcessId,
    bool ExitedDuringProbe,
    int? ExitCode,
    string Detail);

public sealed record BrowserConformanceBrowserResult(
    BrowserKind Kind,
    string DisplayName,
    string ExecutablePath,
    int? MajorVersion,
    string? BrowserVersion,
    LaunchStrategy Strategy,
    IReadOnlyList<string> Arguments,
    string DisplayCommand,
    string? TemporaryProfilePath,
    bool LoadsExtensions,
    bool Success,
    bool Launched,
    string Detail,
    IReadOnlyList<string> Warnings,
    int? ProcessId,
    bool? ProcessExitedDuringProbe,
    int? ProcessExitCode,
    int CdpLoaded,
    int CdpTotal,
    IReadOnlyList<CdpLoadAttempt> CdpAttempts)
{
    public static BrowserConformanceBrowserResult FromProcess(
        BrowserInfo browser,
        BrowserLaunchPlan plan,
        IReadOnlyList<string> effectiveArgs,
        string displayCommand,
        BrowserConformanceProcessResult processResult) =>
        new(
            browser.Kind,
            browser.DisplayName,
            browser.ExecutablePath,
            browser.MajorVersion,
            browser.ProductVersion ?? browser.MajorVersion?.ToString(),
            plan.Strategy,
            effectiveArgs,
            displayCommand,
            plan.TemporaryProfilePath,
            plan.LoadsExtensions,
            Success: processResult.Started && plan.LoadsExtensions,
            Launched: processResult.Started,
            processResult.Detail,
            plan.Warnings,
            processResult.ProcessId,
            processResult.ExitedDuringProbe,
            processResult.ExitCode,
            CdpLoaded: 0,
            CdpTotal: 0,
            CdpAttempts: []);

    public static BrowserConformanceBrowserResult FromCdp(
        BrowserInfo browser,
        BrowserLaunchPlan plan,
        IReadOnlyList<string> effectiveArgs,
        string displayCommand,
        CdpLoadResult cdpResult) =>
        new(
            browser.Kind,
            browser.DisplayName,
            browser.ExecutablePath,
            browser.MajorVersion,
            browser.ProductVersion ?? browser.MajorVersion?.ToString(),
            plan.Strategy,
            effectiveArgs,
            displayCommand,
            plan.TemporaryProfilePath,
            plan.LoadsExtensions,
            Success: cdpResult.Success,
            Launched: cdpResult.Attempts.Count > 0 || cdpResult.Loaded > 0,
            cdpResult.Detail,
            plan.Warnings,
            ProcessId: null,
            ProcessExitedDuringProbe: null,
            ProcessExitCode: null,
            cdpResult.Loaded,
            cdpResult.Total,
            cdpResult.Attempts);
}
