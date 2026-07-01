using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using LocalChromeStore.Models;
using LocalChromeStore.Services.Crx;

namespace LocalChromeStore.Services;

public enum PolicyPackageRiskSeverity
{
    Info,
    Warning,
    Fail
}

public sealed record PolicyPackageRiskFinding(
    PolicyPackageRiskSeverity Severity,
    string Category,
    string Detail,
    string? RelativePath = null,
    int? Line = null)
{
    public string Location => RelativePath is null
        ? string.Empty
        : Line is { } line ? $"{RelativePath}:{line}" : RelativePath;
}

public sealed record PolicyPackageRiskReport(
    string ExtensionKey,
    string InstallPath,
    string ManifestPath,
    int? ManifestVersion,
    IReadOnlyList<string> DerivedExtensionIds,
    IReadOnlyList<PolicyPackageRiskFinding> Findings)
{
    public int FailCount => Findings.Count(f => f.Severity == PolicyPackageRiskSeverity.Fail);
    public int WarningCount => Findings.Count(f => f.Severity == PolicyPackageRiskSeverity.Warning);
    public bool BlocksPolicyInstall => FailCount > 0;
    public string Summary => BlocksPolicyInstall
        ? $"{FailCount} blocking finding(s), {WarningCount} warning(s)"
        : WarningCount > 0
            ? $"{WarningCount} warning(s), no blocking findings"
            : "no blocking findings";
}

public sealed class PolicyPackageRiskScanner
{
    private const int MaxTextBytes = 1024 * 1024;

    private static readonly string[] ScannedExtensions =
    [
        ".js", ".mjs", ".cjs", ".html", ".htm", ".json"
    ];

    private static readonly Regex ValidExtensionId = new("^[a-p]{32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly RiskPattern[] Patterns =
    [
        new(
            "Remote executable code",
            PolicyPackageRiskSeverity.Fail,
            new Regex(@"\bimportScripts\s*\(\s*['""]https?://", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "Imports executable JavaScript from a remote URL with importScripts()."),
        new(
            "Remote executable code",
            PolicyPackageRiskSeverity.Fail,
            new Regex(@"\bimport\s*\(\s*['""]https?://", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "Dynamically imports executable JavaScript from a remote URL."),
        new(
            "Remote executable code",
            PolicyPackageRiskSeverity.Fail,
            new Regex(@"<script[^>]+src\s*=\s*['""]https?://", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "Loads executable JavaScript from a remote script tag."),
        new(
            "Remote executable code",
            PolicyPackageRiskSeverity.Fail,
            new Regex(@"\bfetch\s*\(\s*['""]https?://[^'""]+\.(?:js|mjs|cjs|wasm)(?:[?#][^'""]*)?['""]", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "Fetches a remote JavaScript or WebAssembly payload."),
        new(
            "Remote executable code",
            PolicyPackageRiskSeverity.Fail,
            new Regex(@"\bWebAssembly\.(?:instantiate|compile|instantiateStreaming|compileStreaming)\s*\([^)]*fetch\s*\(\s*['""]https?://", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "Loads WebAssembly from a remote URL."),
        new(
            "Dynamic code execution",
            PolicyPackageRiskSeverity.Fail,
            new Regex(@"\beval\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            "Uses eval(), which violates Chrome extension policy and is unsafe for force-installed packages."),
        new(
            "Dynamic code execution",
            PolicyPackageRiskSeverity.Fail,
            new Regex(@"\bnew\s+Function\s*\(", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            "Creates executable code with new Function()."),
        new(
            "Dynamic code execution",
            PolicyPackageRiskSeverity.Fail,
            new Regex(@"\bset(?:Timeout|Interval)\s*\(\s*['""]", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            "Passes a string to setTimeout/setInterval, which is eval-like dynamic code execution."),

        // Obfuscation heuristics
        new(
            "Obfuscation",
            PolicyPackageRiskSeverity.Warning,
            new Regex(@"String\.fromCharCode\s*\(\s*(?:\d+\s*,\s*){8,}", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            "Long String.fromCharCode() chain suggests obfuscated code."),
        new(
            "Obfuscation",
            PolicyPackageRiskSeverity.Warning,
            new Regex(@"\batob\s*\(\s*['""][A-Za-z0-9+/=]{100,}['""]", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            "atob() with a large base64 payload may contain obfuscated executable code."),
        new(
            "Obfuscation",
            PolicyPackageRiskSeverity.Warning,
            new Regex(@"(?:\\x[0-9a-fA-F]{2}){16,}", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            "Long hex-escape sequence suggests obfuscated strings."),
        new(
            "Obfuscation",
            PolicyPackageRiskSeverity.Warning,
            new Regex(@"(?:\\u[0-9a-fA-F]{4}){12,}", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            "Long unicode-escape sequence suggests obfuscated strings."),

        // Secret leakage heuristics
        new(
            "Secret leakage",
            PolicyPackageRiskSeverity.Warning,
            new Regex(@"(?:api[_-]?key|apikey|api[_-]?secret)\s*[:=]\s*['""][A-Za-z0-9_\-]{20,}['""]", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "Possible hardcoded API key or secret."),
        new(
            "Secret leakage",
            PolicyPackageRiskSeverity.Warning,
            new Regex(@"(?:password|passwd|pwd)\s*[:=]\s*['""][^'""]{8,}['""]", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "Possible hardcoded password."),
        new(
            "Secret leakage",
            PolicyPackageRiskSeverity.Warning,
            new Regex(@"['""](?:sk|pk|rk)[-_](?:live|test|prod)[-_][A-Za-z0-9]{20,}['""]", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            "Possible hardcoded Stripe-style secret or publishable key."),
        new(
            "Secret leakage",
            PolicyPackageRiskSeverity.Warning,
            new Regex(@"['""]AIza[A-Za-z0-9_\\-]{35}['""]", RegexOptions.Compiled | RegexOptions.CultureInvariant),
            "Possible hardcoded Google API key (AIza prefix).")
    ];

    private readonly IReadOnlyList<string> _maliciousIdFeedPaths;
    private readonly IReadOnlySet<string> _builtInMaliciousIds;

    public PolicyPackageRiskScanner(SettingsService settings)
        : this(
            knownMaliciousIds: [],
            maliciousIdFeedPaths:
            [
                Path.Combine(settings.CacheDir, "policy-risk", "malicious-extension-ids.txt"),
                Path.Combine(AppContext.BaseDirectory, "policy-risk", "malicious-extension-ids.txt")
            ])
    {
    }

    public PolicyPackageRiskScanner(
        IEnumerable<string> knownMaliciousIds,
        IEnumerable<string>? maliciousIdFeedPaths = null)
    {
        _builtInMaliciousIds = NormalizeIds(knownMaliciousIds);
        _maliciousIdFeedPaths = maliciousIdFeedPaths?.ToArray() ?? [];
    }

    public PolicyPackageRiskReport Scan(
        InstalledExtension installed,
        IEnumerable<string>? policyExtensionIds = null)
    {
        ArgumentNullException.ThrowIfNull(installed);
        var installRoot = Path.GetFullPath(installed.InstallPath);
        var findings = new List<PolicyPackageRiskFinding>();
        var manifestPath = ResolveManifestPath(installed, installRoot, findings);
        int? manifestVersion = null;
        var derivedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in policyExtensionIds ?? [])
        {
            var normalized = NormalizeId(id);
            if (normalized is not null) derivedIds.Add(normalized);
        }

        if (!Directory.Exists(installRoot))
        {
            findings.Add(new PolicyPackageRiskFinding(
                PolicyPackageRiskSeverity.Fail,
                "Package structure",
                $"Installed extension directory was not found: {installRoot}"));
            return BuildReport(installed, installRoot, manifestPath, manifestVersion, derivedIds, findings);
        }

        if (manifestPath is not null && File.Exists(manifestPath))
            InspectManifest(installRoot, manifestPath, findings, derivedIds, out manifestVersion);

        ScanTextFiles(installRoot, findings);
        InspectKnownMaliciousIds(derivedIds, findings);
        return BuildReport(installed, installRoot, manifestPath, manifestVersion, derivedIds, findings);
    }

    public string FormatForPrompt(PolicyPackageRiskReport report)
    {
        var lines = new List<string> { $"Package-risk preflight: {report.Summary}." };
        if (report.DerivedExtensionIds.Count > 0)
            lines.Add($"Derived extension ID(s): {string.Join(", ", report.DerivedExtensionIds)}");

        foreach (var finding in report.Findings.Take(10))
        {
            var location = string.IsNullOrWhiteSpace(finding.Location) ? string.Empty : $" ({finding.Location})";
            lines.Add($"- {finding.Severity}: {finding.Category}{location} - {finding.Detail}");
        }

        if (report.Findings.Count > 10)
            lines.Add($"- +{report.Findings.Count - 10} more finding(s)");
        return string.Join(Environment.NewLine, lines);
    }

    private static PolicyPackageRiskReport BuildReport(
        InstalledExtension installed,
        string installRoot,
        string? manifestPath,
        int? manifestVersion,
        HashSet<string> derivedIds,
        List<PolicyPackageRiskFinding> findings) =>
        new(
            installed.Key,
            installRoot,
            manifestPath ?? Path.Combine(installRoot, "manifest.json"),
            manifestVersion,
            derivedIds.OrderBy(id => id, StringComparer.OrdinalIgnoreCase).ToArray(),
            findings
                .OrderByDescending(f => f.Severity)
                .ThenBy(f => f.Category, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.Line)
                .ToArray());

    private static string? ResolveManifestPath(
        InstalledExtension installed,
        string installRoot,
        List<PolicyPackageRiskFinding> findings)
    {
        var manifestPath = string.IsNullOrWhiteSpace(installed.ManifestPath)
            ? Path.Combine(installRoot, "manifest.json")
            : Path.GetFullPath(installed.ManifestPath);
        if (File.Exists(manifestPath)) return manifestPath;

        var fallback = Path.Combine(installRoot, "manifest.json");
        if (File.Exists(fallback)) return fallback;

        findings.Add(new PolicyPackageRiskFinding(
            PolicyPackageRiskSeverity.Fail,
            "Manifest",
            "Installed extension manifest.json was not found."));
        return manifestPath;
    }

    private static void InspectManifest(
        string installRoot,
        string manifestPath,
        List<PolicyPackageRiskFinding> findings,
        HashSet<string> derivedIds,
        out int? manifestVersion)
    {
        manifestVersion = null;
        var relativePath = SafeRelativePath(installRoot, manifestPath);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
        }
        catch (JsonException ex)
        {
            findings.Add(new PolicyPackageRiskFinding(
                PolicyPackageRiskSeverity.Fail,
                "Manifest",
                $"manifest.json could not be parsed: {ex.Message}",
                relativePath,
                ex.LineNumber.HasValue ? (int)ex.LineNumber.Value + 1 : null));
            return;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("manifest_version", out var mv) || !mv.TryGetInt32(out var version))
            {
                findings.Add(new PolicyPackageRiskFinding(
                    PolicyPackageRiskSeverity.Fail,
                    "Manifest",
                    "manifest_version is missing or not numeric.",
                    relativePath));
            }
            else
            {
                manifestVersion = version;
                if (version < 3)
                {
                    findings.Add(new PolicyPackageRiskFinding(
                        PolicyPackageRiskSeverity.Fail,
                        "Manifest",
                        $"Manifest V{version} cannot be force-installed safely on current Chromium builds; migrate to MV3 before policy install.",
                        relativePath));
                }
            }

            if (root.TryGetProperty("key", out var key) && key.ValueKind == JsonValueKind.String)
            {
                var id = TryDeriveManifestKeyId(key.GetString());
                if (id is not null) derivedIds.Add(id);
            }

            foreach (var csp in EnumerateContentSecurityPolicies(root))
                InspectContentSecurityPolicy(csp, relativePath, findings);
        }
    }

    private static IEnumerable<string> EnumerateContentSecurityPolicies(JsonElement root)
    {
        if (!root.TryGetProperty("content_security_policy", out var csp)) yield break;
        if (csp.ValueKind == JsonValueKind.String)
        {
            var value = csp.GetString();
            if (!string.IsNullOrWhiteSpace(value)) yield return value;
            yield break;
        }

        if (csp.ValueKind != JsonValueKind.Object) yield break;
        foreach (var property in csp.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                var value = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(value)) yield return value;
            }
        }
    }

    private static void InspectContentSecurityPolicy(
        string csp,
        string relativePath,
        List<PolicyPackageRiskFinding> findings)
    {
        if (csp.Contains("'unsafe-eval'", StringComparison.OrdinalIgnoreCase)
            || csp.Contains(" unsafe-eval", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new PolicyPackageRiskFinding(
                PolicyPackageRiskSeverity.Fail,
                "Content security policy",
                "CSP allows unsafe-eval, which enables dynamic code execution.",
                relativePath));
        }

        if (csp.Contains("'wasm-unsafe-eval'", StringComparison.OrdinalIgnoreCase)
            || csp.Contains(" wasm-unsafe-eval", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new PolicyPackageRiskFinding(
                PolicyPackageRiskSeverity.Warning,
                "Content security policy",
                "CSP allows wasm-unsafe-eval; review whether WebAssembly execution is expected.",
                relativePath));
        }

        if (Regex.IsMatch(csp, @"script-src[^;]*(?:http:|https:)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            findings.Add(new PolicyPackageRiskFinding(
                PolicyPackageRiskSeverity.Fail,
                "Content security policy",
                "CSP permits script-src from a remote HTTP(S) origin.",
                relativePath));
        }
    }

    private static string? TryDeriveManifestKeyId(string? manifestKey)
    {
        if (string.IsNullOrWhiteSpace(manifestKey)) return null;
        try
        {
            var spki = Convert.FromBase64String(manifestKey.Trim());
            return Crx3PackageService.DeriveExtensionId(spki);
        }
        catch
        {
            return null;
        }
    }

    private static void ScanTextFiles(string installRoot, List<PolicyPackageRiskFinding> findings)
    {
        EnumerationOptions options = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (var file in Directory.EnumerateFiles(installRoot, "*", options))
        {
            var extension = Path.GetExtension(file);
            if (!ScannedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                continue;

            FileInfo info;
            try { info = new FileInfo(file); }
            catch { continue; }

            var relativePath = SafeRelativePath(installRoot, file);
            if (info.Length > MaxTextBytes)
            {
                findings.Add(new PolicyPackageRiskFinding(
                    PolicyPackageRiskSeverity.Warning,
                    "Scanner coverage",
                    $"Text file is larger than {MaxTextBytes / 1024} KB and was skipped by policy preflight.",
                    relativePath));
                continue;
            }

            string text;
            try { text = File.ReadAllText(file); }
            catch (Exception ex)
            {
                findings.Add(new PolicyPackageRiskFinding(
                    PolicyPackageRiskSeverity.Warning,
                    "Scanner coverage",
                    $"Could not read text file: {ex.Message}",
                    relativePath));
                continue;
            }

            foreach (var pattern in Patterns)
            {
                var match = pattern.Regex.Match(text);
                if (!match.Success) continue;
                findings.Add(new PolicyPackageRiskFinding(
                    pattern.Severity,
                    pattern.Category,
                    pattern.Detail,
                    relativePath,
                    LineFor(text, match.Index)));
            }
        }
    }

    private void InspectKnownMaliciousIds(
        IReadOnlySet<string> derivedIds,
        List<PolicyPackageRiskFinding> findings)
    {
        if (derivedIds.Count == 0) return;
        var knownIds = LoadKnownMaliciousIds();
        if (knownIds.Count == 0) return;

        foreach (var id in derivedIds)
        {
            if (!knownIds.Contains(id)) continue;
            findings.Add(new PolicyPackageRiskFinding(
                PolicyPackageRiskSeverity.Fail,
                "Known malicious extension ID",
                $"{id} matches a configured malicious-extension ID feed."));
        }
    }

    private IReadOnlySet<string> LoadKnownMaliciousIds()
    {
        var ids = new HashSet<string>(_builtInMaliciousIds, StringComparer.OrdinalIgnoreCase);
        foreach (var path in _maliciousIdFeedPaths)
        {
            try
            {
                if (!File.Exists(path)) continue;
                foreach (var line in File.ReadLines(path))
                {
                    var candidate = NormalizeId(line.Split('#')[0]);
                    if (candidate is not null) ids.Add(candidate);
                }
            }
            catch
            {
                // Feed files are optional; diagnostics still show package-local findings.
            }
        }

        return ids;
    }

    private static IReadOnlySet<string> NormalizeIds(IEnumerable<string> ids)
    {
        var normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in ids)
        {
            var value = NormalizeId(id);
            if (value is not null) normalized.Add(value);
        }
        return normalized;
    }

    private static string? NormalizeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        var value = id.Trim().ToLowerInvariant();
        return ValidExtensionId.IsMatch(value) ? value : null;
    }

    private static int LineFor(string text, int index)
    {
        var line = 1;
        for (var i = 0; i < index && i < text.Length; i++)
            if (text[i] == '\n') line++;
        return line;
    }

    private static string SafeRelativePath(string root, string path)
    {
        try { return Path.GetRelativePath(root, path); }
        catch { return path; }
    }

    private sealed record RiskPattern(
        string Category,
        PolicyPackageRiskSeverity Severity,
        Regex Regex,
        string Detail);
}
