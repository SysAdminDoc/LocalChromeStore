namespace LocalChromeStore.Models;

public enum PermissionRisk
{
    Informational,
    Low,
    Medium,
    High
}

public sealed record PermissionEntry(string Name, PermissionRisk Risk, string Description, bool IsHostPermission = false, bool IsOptional = false);

/// <summary>
/// Risk classification for the most common Chrome extension permissions.
/// Sources: https://developer.chrome.com/docs/extensions/reference/permissions-list
/// and the Chrome warning ladder used by the Web Store install dialog.
/// </summary>
public static class PermissionCatalog
{
    private static readonly Dictionary<string, (PermissionRisk Risk, string Description)> Entries =
        new(StringComparer.OrdinalIgnoreCase)
    {
        // High risk — broad access or capability to interfere with other software.
        ["<all_urls>"]        = (PermissionRisk.High, "Read and change all data on every site you visit."),
        ["debugger"]          = (PermissionRisk.High, "Use the Chrome debugger protocol against any tab — equivalent to remote control."),
        ["nativeMessaging"]   = (PermissionRisk.High, "Communicate with cooperating native apps installed on the machine."),
        ["proxy"]             = (PermissionRisk.High, "Override your network proxy settings."),
        ["webRequest"]        = (PermissionRisk.High, "Observe network requests as they happen."),
        ["webRequestBlocking"] = (PermissionRisk.High, "Block or modify network requests in flight (MV2)."),
        ["webNavigation"]     = (PermissionRisk.Medium, "Track navigation events across all tabs."),
        ["cookies"]           = (PermissionRisk.High, "Read and modify cookies for matching origins."),
        ["management"]        = (PermissionRisk.High, "Enumerate, enable, and disable other extensions."),
        ["desktopCapture"]    = (PermissionRisk.High, "Capture the screen, individual windows, or tabs."),
        ["tabCapture"]        = (PermissionRisk.High, "Capture the contents of the active tab."),
        ["pageCapture"]       = (PermissionRisk.High, "Capture page contents as MHTML."),
        ["clipboardRead"]     = (PermissionRisk.High, "Read whatever is on the clipboard."),
        ["downloads"]         = (PermissionRisk.High, "Manage and trigger downloads to disk."),
        ["downloads.open"]    = (PermissionRisk.High, "Open downloaded files programmatically."),
        ["fileSystem"]        = (PermissionRisk.High, "Read and write files on the user's disk."),
        ["history"]           = (PermissionRisk.High, "Read and edit browsing history."),
        ["bookmarks"]         = (PermissionRisk.High, "Read and edit bookmarks."),
        ["topSites"]          = (PermissionRisk.Medium, "Read the list of most-visited sites."),
        ["geolocation"]       = (PermissionRisk.High, "Read your physical location."),
        ["privacy"]           = (PermissionRisk.High, "Change browser privacy settings."),
        ["enterprise.platformKeys"] = (PermissionRisk.High, "Use enterprise-managed device certificates."),

        // Medium risk — narrower but still privileged.
        ["tabs"]              = (PermissionRisk.Medium, "Read tab metadata: URL, title, and favicon for any tab."),
        ["activeTab"]         = (PermissionRisk.Low, "Temporary access to the active tab while the user invokes the extension."),
        ["scripting"]         = (PermissionRisk.Medium, "Inject scripts and stylesheets into matching pages (MV3)."),
        ["declarativeNetRequest"]          = (PermissionRisk.Medium, "Apply declarative network rules (MV3 ad/tracker blockers)."),
        ["declarativeNetRequestWithHostAccess"] = (PermissionRisk.Medium, "Apply DNR rules and observe matched requests."),
        ["declarativeNetRequestFeedback"]  = (PermissionRisk.Low, "Inspect declarativeNetRequest matches in tests."),
        ["clipboardWrite"]    = (PermissionRisk.Medium, "Write to the clipboard without user gesture."),
        ["sessions"]          = (PermissionRisk.Medium, "Read and restore recently closed tabs across devices."),
        ["identity"]          = (PermissionRisk.Medium, "Sign in to Google for the extension."),
        ["identity.email"]    = (PermissionRisk.Medium, "Read the signed-in user's email address."),
        ["unlimitedStorage"]  = (PermissionRisk.Medium, "Use unlimited local storage instead of the default quota."),
        ["notifications"]     = (PermissionRisk.Medium, "Show desktop notifications."),
        ["sidePanel"]         = (PermissionRisk.Low, "Show a side panel UI."),
        ["offscreen"]         = (PermissionRisk.Low, "Run hidden offscreen documents (MV3)."),
        ["userScripts"]       = (PermissionRisk.High, "Run user-supplied scripts via the userScripts API."),
        ["search"]            = (PermissionRisk.Low, "Trigger the user's default search engine."),
        ["audio"]             = (PermissionRisk.Medium, "Control audio devices."),
        ["printing"]          = (PermissionRisk.Medium, "Send print jobs."),
        ["printerProvider"]   = (PermissionRisk.Medium, "Implement a printer driver in extension code."),
        ["system.cpu"]        = (PermissionRisk.Low, "Read CPU information."),
        ["system.memory"]     = (PermissionRisk.Low, "Read memory information."),
        ["system.storage"]    = (PermissionRisk.Low, "Read storage device information."),
        ["system.display"]    = (PermissionRisk.Low, "Read display device information."),

        // Low / informational — common UI capabilities.
        ["storage"]           = (PermissionRisk.Low, "Use chrome.storage to persist extension data locally and via Sync."),
        ["alarms"]            = (PermissionRisk.Informational, "Schedule periodic background work."),
        ["contextMenus"]      = (PermissionRisk.Informational, "Add items to the right-click menu."),
        ["idle"]              = (PermissionRisk.Informational, "Detect when the user goes idle."),
        ["power"]             = (PermissionRisk.Informational, "Override system idle/sleep behavior."),
        ["windows"]           = (PermissionRisk.Informational, "Create and manage browser windows."),
        ["favicon"]           = (PermissionRisk.Informational, "Read favicons."),
        ["fontSettings"]      = (PermissionRisk.Informational, "Read browser font preferences."),
        ["accessibilityFeatures.read"]   = (PermissionRisk.Informational, "Read accessibility settings."),
        ["accessibilityFeatures.modify"] = (PermissionRisk.Medium, "Change accessibility settings."),
        ["browsingData"]      = (PermissionRisk.Medium, "Clear browsing data, cookies, history, etc."),
    };

    public static PermissionEntry Describe(string permission, bool isOptional = false)
    {
        var key = permission;
        if (Entries.TryGetValue(key, out var entry))
            return new PermissionEntry(key, entry.Risk, entry.Description, IsHostPermission: false, IsOptional: isOptional);

        // Unknown extension permission — surface as informational; reviewers can decide.
        return new PermissionEntry(key, PermissionRisk.Informational,
            "Permission not in the local catalog. Review the extension's manifest and Chrome reference.",
            IsHostPermission: false, IsOptional: isOptional);
    }

    public static PermissionEntry DescribeHost(string host, bool isOptional = false)
    {
        if (string.IsNullOrWhiteSpace(host))
            return new PermissionEntry("<empty>", PermissionRisk.Informational, "Empty host pattern.", IsHostPermission: true, IsOptional: isOptional);

        if (host == "<all_urls>" || host.Contains("://*/*", StringComparison.Ordinal))
            return new PermissionEntry(host, PermissionRisk.High,
                "Read and change data on EVERY site the user visits.",
                IsHostPermission: true, IsOptional: isOptional);

        if (host.Contains("*://*.", StringComparison.Ordinal))
            return new PermissionEntry(host, PermissionRisk.Medium,
                $"Access pages on every subdomain of {host}.",
                IsHostPermission: true, IsOptional: isOptional);

        if (host.Contains('*'))
            return new PermissionEntry(host, PermissionRisk.Medium,
                $"Access pages matching {host}.",
                IsHostPermission: true, IsOptional: isOptional);

        return new PermissionEntry(host, PermissionRisk.Low,
            $"Access exactly {host}.",
            IsHostPermission: true, IsOptional: isOptional);
    }

    public static PermissionRisk Aggregate(IEnumerable<PermissionEntry> entries)
    {
        var max = PermissionRisk.Informational;
        foreach (var e in entries) if (e.Risk > max) max = e.Risk;
        return max;
    }
}
