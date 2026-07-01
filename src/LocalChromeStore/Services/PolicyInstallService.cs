using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using LocalChromeStore.Models;
using LocalChromeStore.Services.Crx;
using Microsoft.Win32;

namespace LocalChromeStore.Services;

public enum PolicyHealthStatus
{
    Pass,
    Warning,
    Fail
}

public sealed record PolicyBrowserTarget(
    BrowserKind BrowserKind,
    string DisplayName,
    string RegistrySubKey,
    string PolicyPageUrl);

public sealed record PolicyInstallRequest(
    BrowserKind BrowserKind,
    string ExtensionId,
    Uri UpdateXmlUrl,
    string? DisplayName = null);

public sealed record PolicyInstallResult(
    PolicyBrowserTarget Target,
    string ValueName,
    string PolicyEntry,
    bool EdgeExtensionSettingsWritten);

public sealed record PolicyRollbackResult(
    PolicyBrowserTarget Target,
    IReadOnlyList<string> RemovedValueNames,
    IReadOnlyList<string> RemovedExtensionSettings);

public sealed record PolicyHealthCheck(
    string Name,
    PolicyHealthStatus Status,
    string Detail);

public sealed record PolicyHealthReport(
    PolicyInstallRequest Request,
    PolicyBrowserTarget? Target,
    Uri? CrxUrl,
    IReadOnlyList<PolicyHealthCheck> Checks)
{
    public bool Healthy => Checks.All(c => c.Status != PolicyHealthStatus.Fail);
}

public interface IPolicyRegistry
{
    IReadOnlyDictionary<string, string> ReadStringValues(string subKey);
    void SetStringValue(string subKey, string valueName, string value);
    void DeleteValue(string subKey, string valueName);
}

public sealed class WindowsPolicyRegistry : IPolicyRegistry
{
    public IReadOnlyDictionary<string, string> ReadStringValues(string subKey)
    {
        using var key = Registry.LocalMachine.OpenSubKey(subKey, writable: false);
        if (key is null) return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in key.GetValueNames())
        {
            if (key.GetValue(name) is string value)
                values[name] = value;
        }
        return values;
    }

    public void SetStringValue(string subKey, string valueName, string value)
    {
        using var key = Registry.LocalMachine.CreateSubKey(subKey, writable: true)
            ?? throw new InvalidOperationException($"Could not open HKLM\\{subKey} for writing.");
        key.SetValue(valueName, value, RegistryValueKind.String);
    }

    public void DeleteValue(string subKey, string valueName)
    {
        using var key = Registry.LocalMachine.OpenSubKey(subKey, writable: true);
        key?.DeleteValue(valueName, throwOnMissingValue: false);
    }
}

public sealed class PolicyInstallService
{
    private readonly IPolicyRegistry _registry;
    private readonly HttpClient _http;
    private const string EdgeExtensionSettingsSubKey = @"SOFTWARE\Policies\Microsoft\Edge";
    private const string EdgeExtensionSettingsValueName = "ExtensionSettings";
    private static readonly JsonSerializerOptions EdgeExtensionSettingsJsonOptions = new() { WriteIndented = false };

    private static readonly IReadOnlyDictionary<BrowserKind, PolicyBrowserTarget> Targets =
        new Dictionary<BrowserKind, PolicyBrowserTarget>
        {
            [BrowserKind.Chrome] = new(
                BrowserKind.Chrome,
                "Google Chrome",
                @"SOFTWARE\Policies\Google\Chrome\ExtensionInstallForcelist",
                "chrome://policy"),
            [BrowserKind.Edge] = new(
                BrowserKind.Edge,
                "Microsoft Edge",
                @"SOFTWARE\Policies\Microsoft\Edge\ExtensionInstallForcelist",
                "edge://policy"),
            [BrowserKind.Brave] = new(
                BrowserKind.Brave,
                "Brave",
                @"SOFTWARE\Policies\BraveSoftware\Brave\ExtensionInstallForcelist",
                "chrome://policy"),
            [BrowserKind.Chromium] = new(
                BrowserKind.Chromium,
                "Chromium",
                @"SOFTWARE\Policies\Chromium\ExtensionInstallForcelist",
                "chrome://policy")
        };

    public PolicyInstallService(IPolicyRegistry? registry = null, HttpClient? http = null)
    {
        _registry = registry ?? new WindowsPolicyRegistry();
        _http = http ?? new HttpClient();
    }

    public static bool TryGetTarget(BrowserKind browserKind, out PolicyBrowserTarget target) =>
        Targets.TryGetValue(browserKind, out target!);

    public static IReadOnlyCollection<PolicyBrowserTarget> SupportedTargets => Targets.Values.ToList();

    public static string BuildPolicyEntry(string extensionId, Uri updateXmlUrl)
    {
        ValidateExtensionId(extensionId);
        ValidateUpdateUrl(updateXmlUrl);
        return $"{extensionId};{updateXmlUrl.AbsoluteUri}";
    }

    public static string BuildConsentPrompt(IEnumerable<PolicyInstallRequest> requests, EnrollmentState enrollment)
    {
        var requestList = requests.ToList();
        var support = PolicyEnrollmentService.EvaluateOffStoreForceInstall(enrollment);
        var lines = new List<string>
        {
            "Enable Enterprise Policy force-install for these extension(s)?",
            "",
            "LocalChromeStore will write HKLM browser policy registry values under ExtensionInstallForcelist. This requires elevation and affects the selected browser for every Windows user on this machine.",
            "",
            "The browser will fetch each self-hosted update.xml URL and then install the referenced CRX package. Use this only for extension packages and update feeds you control.",
            "",
            $"Enrollment readiness: {support.Reason}",
            "",
            "Policy entries:"
        };

        if (requestList.Count == 0)
        {
            lines.Add("- (none)");
        }
        else
        {
            foreach (var request in requestList)
            {
                var target = TryGetTarget(request.BrowserKind, out var t) ? t.DisplayName : request.BrowserKind.ToString();
                var name = string.IsNullOrWhiteSpace(request.DisplayName) ? request.ExtensionId : request.DisplayName;
                lines.Add($"- {target}: {name} ({request.ExtensionId}) -> {request.UpdateXmlUrl.AbsoluteUri}");
            }
        }

        lines.Add("");
        lines.Add("Rollback removes only the browser policy registry entries. Packaged CRX/update artifacts remain on disk or on the hosting service.");
        if (requestList.Any(r => r.BrowserKind == BrowserKind.Edge))
            lines.Add("Microsoft Edge also receives ExtensionSettings.override_update_url so self-hosted updates are not redirected to the Edge Add-ons store.");
        return string.Join(Environment.NewLine, lines);
    }

    public PolicyInstallResult Install(PolicyInstallRequest request, bool consentConfirmed)
    {
        if (!consentConfirmed)
            throw new InvalidOperationException("Policy install requires explicit user consent before writing HKLM force-install policy.");
        var target = RequireTarget(request.BrowserKind);
        var entry = BuildPolicyEntry(request.ExtensionId, request.UpdateXmlUrl);
        var values = _registry.ReadStringValues(target.RegistrySubKey);
        var existing = FindEntryValueName(values, request.ExtensionId);
        var valueName = existing ?? NextValueName(values.Keys);

        _registry.SetStringValue(target.RegistrySubKey, valueName, entry);
        bool edgeSettingsWritten = false;
        try
        {
            edgeSettingsWritten = request.BrowserKind == BrowserKind.Edge && WriteEdgeExtensionSettings(request);
        }
        catch
        {
            if (existing is null)
                _registry.DeleteValue(target.RegistrySubKey, valueName);
            throw;
        }
        return new PolicyInstallResult(target, valueName, entry, edgeSettingsWritten);
    }

    public PolicyRollbackResult Rollback(BrowserKind browserKind, IEnumerable<string> extensionIds)
    {
        var target = RequireTarget(browserKind);
        var idSet = extensionIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (idSet.Count == 0)
            return new PolicyRollbackResult(target, [], []);

        var removed = new List<string>();
        foreach (var (name, value) in _registry.ReadStringValues(target.RegistrySubKey))
        {
            var id = ParsePolicyEntry(value).ExtensionId;
            if (id is null || !idSet.Contains(id)) continue;
            _registry.DeleteValue(target.RegistrySubKey, name);
                removed.Add(name);
        }

        var removedEdgeSettings = browserKind == BrowserKind.Edge
            ? RemoveEdgeExtensionSettings(idSet)
            : [];
        return new PolicyRollbackResult(target, removed, removedEdgeSettings);
    }

    public async Task<PolicyHealthReport> CheckHealthAsync(PolicyInstallRequest request, CancellationToken ct = default)
    {
        var checks = new List<PolicyHealthCheck>();
        if (!TryGetTarget(request.BrowserKind, out var target))
        {
            checks.Add(new PolicyHealthCheck(
                "Browser policy target",
                PolicyHealthStatus.Fail,
                $"{request.BrowserKind} does not have a known LocalChromeStore policy registry target."));
            return new PolicyHealthReport(request, null, null, checks);
        }

        checks.Add(new PolicyHealthCheck(
            "Native policy page",
            PolicyHealthStatus.Pass,
            $"{target.DisplayName} exposes policy diagnostics at {target.PolicyPageUrl}."));

        checks.Add(Crx3PackageService.IsValidExtensionId(request.ExtensionId)
            ? new PolicyHealthCheck("Extension ID", PolicyHealthStatus.Pass, $"{request.ExtensionId} is a valid Chrome extension ID.")
            : new PolicyHealthCheck("Extension ID", PolicyHealthStatus.Fail, "Extension ID must be 32 characters using Chrome's a-p alphabet."));

        checks.Add(IsSupportedUpdateUrl(request.UpdateXmlUrl)
            ? new PolicyHealthCheck("Update URL", PolicyHealthStatus.Pass, request.UpdateXmlUrl.AbsoluteUri)
            : new PolicyHealthCheck("Update URL", PolicyHealthStatus.Fail, "Update XML URL must be an absolute http or https URL."));

        var expectedEntry = Crx3PackageService.IsValidExtensionId(request.ExtensionId) && IsSupportedUpdateUrl(request.UpdateXmlUrl)
            ? BuildPolicyEntry(request.ExtensionId, request.UpdateXmlUrl)
            : null;
        checks.Add(CheckRegistryState(target, request.ExtensionId, expectedEntry));
        if (request.BrowserKind == BrowserKind.Edge)
            checks.Add(CheckEdgeExtensionSettings(request));

        Uri? crxUrl = null;
        if (IsSupportedUpdateUrl(request.UpdateXmlUrl))
        {
            var xml = await TryDownloadTextAsync(request.UpdateXmlUrl, ct);
            if (xml is null)
            {
                checks.Add(new PolicyHealthCheck("Update XML", PolicyHealthStatus.Fail, $"Could not download {request.UpdateXmlUrl.AbsoluteUri}."));
            }
            else
            {
                var inspection = InspectUpdateXml(xml, request.ExtensionId);
                checks.Add(inspection.Check);
                crxUrl = inspection.CrxUrl;
            }
        }

        if (crxUrl is null)
        {
            checks.Add(new PolicyHealthCheck("CRX reachability", PolicyHealthStatus.Fail, "No CRX codebase URL was found in a valid update.xml."));
        }
        else
        {
            checks.Add(await CheckCrxReachabilityAsync(crxUrl, ct));
        }

        return new PolicyHealthReport(request, target, crxUrl, checks);
    }

    private PolicyHealthCheck CheckRegistryState(PolicyBrowserTarget target, string extensionId, string? expectedEntry)
    {
        var values = _registry.ReadStringValues(target.RegistrySubKey);
        var existingName = FindEntryValueName(values, extensionId);
        if (existingName is null)
        {
            return new PolicyHealthCheck(
                "Registry state",
                PolicyHealthStatus.Fail,
                $"HKLM\\{target.RegistrySubKey} has no force-install entry for {extensionId}.");
        }

        var value = values[existingName];
        if (expectedEntry is not null && string.Equals(value, expectedEntry, StringComparison.OrdinalIgnoreCase))
        {
            return new PolicyHealthCheck(
                "Registry state",
                PolicyHealthStatus.Pass,
                $"Value {existingName} matches {expectedEntry}.");
        }

        return new PolicyHealthCheck(
            "Registry state",
            PolicyHealthStatus.Fail,
            $"Value {existingName} is {value}, expected {expectedEntry ?? "a valid policy entry"}.");
    }

    private PolicyHealthCheck CheckEdgeExtensionSettings(PolicyInstallRequest request)
    {
        try
        {
            var values = _registry.ReadStringValues(EdgeExtensionSettingsSubKey);
            if (!values.TryGetValue(EdgeExtensionSettingsValueName, out var raw) || string.IsNullOrWhiteSpace(raw))
            {
                return new PolicyHealthCheck(
                    "Edge override_update_url",
                    PolicyHealthStatus.Fail,
                    $"HKLM\\{EdgeExtensionSettingsSubKey}\\{EdgeExtensionSettingsValueName} is missing.");
            }

            var root = ReadEdgeExtensionSettings(raw);
            if (root[request.ExtensionId] is not JsonObject extension)
            {
                return new PolicyHealthCheck(
                    "Edge override_update_url",
                    PolicyHealthStatus.Fail,
                    $"ExtensionSettings has no entry for {request.ExtensionId}.");
            }

            var overrideUpdateUrl = TryGetBoolean(extension, "override_update_url");
            var updateUrl = TryGetString(extension, "update_url");
            var installMode = TryGetString(extension, "installation_mode");
            if (overrideUpdateUrl == true
                && string.Equals(updateUrl, request.UpdateXmlUrl.AbsoluteUri, StringComparison.OrdinalIgnoreCase)
                && string.Equals(installMode, "force_installed", StringComparison.OrdinalIgnoreCase))
            {
                return new PolicyHealthCheck(
                    "Edge override_update_url",
                    PolicyHealthStatus.Pass,
                    $"ExtensionSettings forces {request.ExtensionId} to {request.UpdateXmlUrl.AbsoluteUri}.");
            }

            return new PolicyHealthCheck(
                "Edge override_update_url",
                PolicyHealthStatus.Fail,
                $"ExtensionSettings entry must set installation_mode=force_installed, update_url={request.UpdateXmlUrl.AbsoluteUri}, and override_update_url=true.");
        }
        catch (Exception ex)
        {
            return new PolicyHealthCheck(
                "Edge override_update_url",
                PolicyHealthStatus.Fail,
                $"Could not inspect Edge ExtensionSettings JSON: {ex.Message}");
        }
    }

    private async Task<PolicyHealthCheck> CheckCrxReachabilityAsync(Uri crxUrl, CancellationToken ct)
    {
        var response = await TrySendAsync(HttpMethod.Head, crxUrl, ct);
        if (response?.StatusCode == HttpStatusCode.MethodNotAllowed)
        {
            response.Dispose();
            response = await TrySendAsync(HttpMethod.Get, crxUrl, ct);
        }

        if (response is null)
        {
            return new PolicyHealthCheck(
                "CRX reachability",
                PolicyHealthStatus.Fail,
                $"Could not reach {crxUrl.AbsoluteUri}.");
        }

        using (response)
        {
            return response.IsSuccessStatusCode
                ? new PolicyHealthCheck("CRX reachability", PolicyHealthStatus.Pass, $"{crxUrl.AbsoluteUri} returned HTTP {(int)response.StatusCode}.")
                : new PolicyHealthCheck("CRX reachability", PolicyHealthStatus.Fail, $"{crxUrl.AbsoluteUri} returned HTTP {(int)response.StatusCode}.");
        }
    }

    private bool WriteEdgeExtensionSettings(PolicyInstallRequest request)
    {
        var values = _registry.ReadStringValues(EdgeExtensionSettingsSubKey);
        var root = values.TryGetValue(EdgeExtensionSettingsValueName, out var raw) && !string.IsNullOrWhiteSpace(raw)
            ? ReadEdgeExtensionSettings(raw)
            : new JsonObject();

        root[request.ExtensionId] = new JsonObject
        {
            ["installation_mode"] = "force_installed",
            ["update_url"] = request.UpdateXmlUrl.AbsoluteUri,
            ["override_update_url"] = true
        };
        _registry.SetStringValue(
            EdgeExtensionSettingsSubKey,
            EdgeExtensionSettingsValueName,
            root.ToJsonString(EdgeExtensionSettingsJsonOptions));
        return true;
    }

    private IReadOnlyList<string> RemoveEdgeExtensionSettings(IReadOnlySet<string> extensionIds)
    {
        var values = _registry.ReadStringValues(EdgeExtensionSettingsSubKey);
        if (!values.TryGetValue(EdgeExtensionSettingsValueName, out var raw) || string.IsNullOrWhiteSpace(raw))
            return [];

        var root = ReadEdgeExtensionSettings(raw);
        var removed = new List<string>();
        foreach (var extensionId in extensionIds)
        {
            if (root.Remove(extensionId))
                removed.Add(extensionId);
        }

        if (removed.Count == 0) return removed;
        if (root.Count == 0)
        {
            _registry.DeleteValue(EdgeExtensionSettingsSubKey, EdgeExtensionSettingsValueName);
        }
        else
        {
            _registry.SetStringValue(
                EdgeExtensionSettingsSubKey,
                EdgeExtensionSettingsValueName,
                root.ToJsonString(EdgeExtensionSettingsJsonOptions));
        }
        return removed;
    }

    private static JsonObject ReadEdgeExtensionSettings(string raw)
    {
        var node = JsonNode.Parse(raw);
        return node as JsonObject
            ?? throw new InvalidOperationException("Edge ExtensionSettings policy value must be a JSON object.");
    }

    private static string? TryGetString(JsonObject obj, string propertyName)
    {
        try { return obj[propertyName]?.GetValue<string>(); }
        catch (InvalidOperationException) { return null; }
    }

    private static bool? TryGetBoolean(JsonObject obj, string propertyName)
    {
        try { return obj[propertyName]?.GetValue<bool>(); }
        catch (InvalidOperationException) { return null; }
    }

    private async Task<string?> TryDownloadTextAsync(Uri uri, CancellationToken ct)
    {
        using var response = await TrySendAsync(HttpMethod.Get, uri, ct);
        if (response is null || !response.IsSuccessStatusCode) return null;
        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task<HttpResponseMessage?> TrySendAsync(HttpMethod method, Uri uri, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(method, uri);
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch
        {
            return null;
        }
    }

    private static (PolicyHealthCheck Check, Uri? CrxUrl) InspectUpdateXml(string xml, string expectedExtensionId)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            XNamespace ns = "http://www.google.com/update2/response";
            var root = doc.Root;
            if (root?.Name != ns + "gupdate")
                return (new PolicyHealthCheck("Update XML", PolicyHealthStatus.Fail, "Root element must be gupdate in the Google update namespace."), null);
            if (!string.Equals(root.Attribute("protocol")?.Value, "2.0", StringComparison.Ordinal))
                return (new PolicyHealthCheck("Update XML", PolicyHealthStatus.Fail, "gupdate protocol must be 2.0."), null);

            var app = root.Elements(ns + "app")
                .FirstOrDefault(e => string.Equals(e.Attribute("appid")?.Value, expectedExtensionId, StringComparison.OrdinalIgnoreCase));
            if (app is null)
                return (new PolicyHealthCheck("Update XML", PolicyHealthStatus.Fail, $"No app element matches {expectedExtensionId}."), null);

            var update = app.Element(ns + "updatecheck");
            var codebase = update?.Attribute("codebase")?.Value;
            var version = update?.Attribute("version")?.Value;
            if (string.IsNullOrWhiteSpace(codebase) || !Uri.TryCreate(codebase, UriKind.Absolute, out var crxUrl))
                return (new PolicyHealthCheck("Update XML", PolicyHealthStatus.Fail, "updatecheck codebase must be an absolute CRX URL."), null);
            if (string.IsNullOrWhiteSpace(version))
                return (new PolicyHealthCheck("Update XML", PolicyHealthStatus.Fail, "updatecheck version is required."), crxUrl);

            return (new PolicyHealthCheck("Update XML", PolicyHealthStatus.Pass, $"update.xml maps {expectedExtensionId} to {crxUrl.AbsoluteUri} version {version}."), crxUrl);
        }
        catch (Exception ex)
        {
            return (new PolicyHealthCheck("Update XML", PolicyHealthStatus.Fail, $"Could not parse update.xml: {ex.Message}"), null);
        }
    }

    private static PolicyBrowserTarget RequireTarget(BrowserKind browserKind) =>
        TryGetTarget(browserKind, out var target)
            ? target
            : throw new NotSupportedException($"{browserKind} does not have a known Enterprise Policy install target.");

    private static void ValidateExtensionId(string extensionId)
    {
        if (!Crx3PackageService.IsValidExtensionId(extensionId))
            throw new ArgumentException("Extension ID must be 32 characters using Chrome's a-p alphabet.", nameof(extensionId));
    }

    private static void ValidateUpdateUrl(Uri updateXmlUrl)
    {
        ArgumentNullException.ThrowIfNull(updateXmlUrl);
        if (!IsSupportedUpdateUrl(updateXmlUrl))
            throw new ArgumentException("Update XML URL must be an absolute http or https URL.", nameof(updateXmlUrl));
    }

    private static bool IsSupportedUpdateUrl(Uri uri) =>
        uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private static string? FindEntryValueName(IReadOnlyDictionary<string, string> values, string extensionId)
    {
        foreach (var (name, value) in values)
        {
            var parsed = ParsePolicyEntry(value);
            if (string.Equals(parsed.ExtensionId, extensionId, StringComparison.OrdinalIgnoreCase))
                return name;
        }
        return null;
    }

    private static (string? ExtensionId, string? UpdateUrl) ParsePolicyEntry(string value)
    {
        var parts = value.Split(';', 2, StringSplitOptions.TrimEntries);
        return parts.Length == 2 ? (parts[0], parts[1]) : (null, null);
    }

    private static string NextValueName(IEnumerable<string> existingNames)
    {
        var max = 0;
        foreach (var name in existingNames)
        {
            if (int.TryParse(name, out var n) && n > max)
                max = n;
        }
        return (max + 1).ToString();
    }
}
