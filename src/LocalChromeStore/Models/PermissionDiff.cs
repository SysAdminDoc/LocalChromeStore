namespace LocalChromeStore.Models;

public enum PermissionDiffKind
{
    RequiredPermission,
    OptionalPermission,
    HostPermission,
    OptionalHostPermission
}

public sealed record PermissionDiffItem(
    PermissionDiffKind Kind,
    string Value,
    PermissionRisk Risk,
    string Description)
{
    public bool IsHostPermission => Kind is PermissionDiffKind.HostPermission or PermissionDiffKind.OptionalHostPermission;
    public bool IsOptional => Kind is PermissionDiffKind.OptionalPermission or PermissionDiffKind.OptionalHostPermission;

    public string CategoryLabel => Kind switch
    {
        PermissionDiffKind.RequiredPermission => "Required permission",
        PermissionDiffKind.OptionalPermission => "Optional permission",
        PermissionDiffKind.HostPermission => "Host access",
        PermissionDiffKind.OptionalHostPermission => "Optional host access",
        _ => "Permission"
    };

    public string RiskLabel => Risk switch
    {
        PermissionRisk.High => "High",
        PermissionRisk.Medium => "Medium",
        PermissionRisk.Low => "Low",
        _ => "Info"
    };
}

public sealed class PermissionDiff
{
    public static PermissionDiff Empty { get; } = new([], []);

    private PermissionDiff(IEnumerable<PermissionDiffItem> added, IEnumerable<PermissionDiffItem> removed)
    {
        Added = added
            .OrderByDescending(i => i.Risk)
            .ThenBy(i => i.Kind)
            .ThenBy(i => i.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
        Removed = removed
            .OrderByDescending(i => i.Risk)
            .ThenBy(i => i.Kind)
            .ThenBy(i => i.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<PermissionDiffItem> Added { get; }
    public IReadOnlyList<PermissionDiffItem> Removed { get; }
    public bool HasAdditions => Added.Count > 0;
    public bool HasRemovals => Removed.Count > 0;
    public bool HasHighRiskAdditions => Added.Any(i => i.Risk == PermissionRisk.High);

    public string AddedSummary
    {
        get
        {
            if (!HasAdditions) return "no new permissions";
            var shown = Added.Take(3)
                .Select(i => $"{i.CategoryLabel}: {i.Value} ({i.RiskLabel})")
                .ToList();
            if (Added.Count > shown.Count)
                shown.Add($"+{Added.Count - shown.Count} more");
            return string.Join("; ", shown);
        }
    }

    public string FormatAddedForPrompt(int maxItems = 8)
    {
        if (!HasAdditions) return "No new permissions.";

        var lines = Added.Take(maxItems)
            .Select(i => $"- {i.CategoryLabel}: {i.Value} ({i.RiskLabel})");
        if (Added.Count > maxItems)
            lines = lines.Concat([$"- +{Added.Count - maxItems} more"]);
        return string.Join(Environment.NewLine, lines);
    }

    public static PermissionDiff Compare(InstalledExtension installed, ExtensionInfo incoming)
    {
        return CompareSets(
            installed.Permissions,
            installed.OptionalPermissions,
            installed.HostPermissions,
            installed.OptionalHostPermissions,
            incoming);
    }

    public static PermissionDiff Compare(EnvironmentExtensionSnapshot snapshot, ExtensionInfo incoming)
    {
        return CompareSets(
            snapshot.Permissions,
            snapshot.OptionalPermissions,
            snapshot.HostPermissions,
            snapshot.OptionalHostPermissions,
            incoming);
    }

    private static PermissionDiff CompareSets(
        IEnumerable<string>? currentPermissions,
        IEnumerable<string>? currentOptionalPermissions,
        IEnumerable<string>? currentHostPermissions,
        IEnumerable<string>? currentOptionalHostPermissions,
        ExtensionInfo incoming)
    {
        var installedRequired = Normalize(currentPermissions);
        var installedOptional = Normalize(currentOptionalPermissions);
        var installedHost = Normalize(currentHostPermissions);
        var installedOptionalHost = Normalize(currentOptionalHostPermissions);
        var incomingRequired = Normalize(incoming.Permissions);
        var incomingOptional = Normalize(incoming.OptionalPermissions);
        var incomingHost = Normalize(incoming.HostPermissions);
        var incomingOptionalHost = Normalize(incoming.OptionalHostPermissions);

        var added = new List<PermissionDiffItem>();
        var removed = new List<PermissionDiffItem>();

        AddEntries(
            added,
            incomingRequired.Where(p => !installedRequired.Contains(p)),
            PermissionDiffKind.RequiredPermission,
            isHost: false,
            isOptional: false);
        AddEntries(
            added,
            incomingOptional.Where(p => !installedRequired.Contains(p) && !installedOptional.Contains(p)),
            PermissionDiffKind.OptionalPermission,
            isHost: false,
            isOptional: true);
        AddEntries(
            added,
            incomingHost.Where(h => !installedHost.Contains(h)),
            PermissionDiffKind.HostPermission,
            isHost: true,
            isOptional: false);
        AddEntries(
            added,
            incomingOptionalHost.Where(h => !installedHost.Contains(h) && !installedOptionalHost.Contains(h)),
            PermissionDiffKind.OptionalHostPermission,
            isHost: true,
            isOptional: true);

        AddEntries(
            removed,
            installedRequired.Where(p => !incomingRequired.Contains(p) && !incomingOptional.Contains(p)),
            PermissionDiffKind.RequiredPermission,
            isHost: false,
            isOptional: false);
        AddEntries(
            removed,
            installedOptional.Where(p => !incomingRequired.Contains(p) && !incomingOptional.Contains(p)),
            PermissionDiffKind.OptionalPermission,
            isHost: false,
            isOptional: true);
        AddEntries(
            removed,
            installedHost.Where(h => !incomingHost.Contains(h) && !incomingOptionalHost.Contains(h)),
            PermissionDiffKind.HostPermission,
            isHost: true,
            isOptional: false);
        AddEntries(
            removed,
            installedOptionalHost.Where(h => !incomingHost.Contains(h) && !incomingOptionalHost.Contains(h)),
            PermissionDiffKind.OptionalHostPermission,
            isHost: true,
            isOptional: true);

        return added.Count == 0 && removed.Count == 0
            ? Empty
            : new PermissionDiff(added, removed);
    }

    private static HashSet<string> Normalize(IEnumerable<string>? values)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (values is null) return set;
        foreach (var value in values)
        {
            var trimmed = value?.Trim();
            if (!string.IsNullOrEmpty(trimmed))
                set.Add(trimmed);
        }
        return set;
    }

    private static void AddEntries(
        List<PermissionDiffItem> target,
        IEnumerable<string> values,
        PermissionDiffKind kind,
        bool isHost,
        bool isOptional)
    {
        foreach (var value in values)
        {
            var entry = isHost
                ? PermissionCatalog.DescribeHost(value, isOptional)
                : PermissionCatalog.Describe(value, isOptional);
            target.Add(new PermissionDiffItem(kind, entry.Name, entry.Risk, entry.Description));
        }
    }
}
