using LocalChromeStore.Models;

namespace LocalChromeStore.Services;

/// <summary>
/// Domain logic for named launch profiles ("load sets"): the implicit "All installed" sentinel,
/// resolving which installed extensions a set targets, snapshotting the current install set under a
/// name, name-uniqueness, and persistence through <see cref="SettingsService"/>.
///
/// WPF-free so it can be unit-tested headlessly — the view model keeps the observable collection and
/// the current selection (UI concerns) and delegates these decisions here.
/// </summary>
public sealed class LoadSetManager
{
    /// <summary>Stable id of the implicit "All installed extensions" set. Never persisted.</summary>
    public const string SentinelId = "__all__";

    private readonly SettingsService _settings;

    public LoadSetManager(SettingsService settings) => _settings = settings;

    /// <summary>Creates the single shared "All installed" sentinel instance the view model selects by default.</summary>
    public static LoadSet CreateSentinel() => new() { Id = SentinelId, Name = "All installed" };

    /// <summary>True for the "all installed" sentinel (or a null selection, treated the same).</summary>
    public static bool IsSentinel(LoadSet? set) => set is null || set.Id == SentinelId;

    /// <summary>
    /// The installed extensions a set targets. The sentinel — or a set whose key list is null —
    /// targets every installed extension; otherwise only installs whose key is in the set.
    /// </summary>
    public static List<InstalledExtension> ResolveActiveExtensions(LoadSet? active, IReadOnlyList<InstalledExtension> installed)
    {
        if (IsSentinel(active) || active!.ExtensionKeys is null) return installed.ToList();
        var keys = active.ExtensionKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return installed.Where(e => keys.Contains(e.Key)).ToList();
    }

    /// <summary>Builds (does not persist) a new set capturing the current install set under a name.</summary>
    public static LoadSet Snapshot(string name, IReadOnlyList<InstalledExtension> installed) =>
        new() { Name = name.Trim(), ExtensionKeys = installed.Select(e => e.Key).ToList() };

    /// <summary>True when a set with the given name (case-insensitive, trimmed) already exists.</summary>
    public static bool NameExists(IEnumerable<LoadSet> existing, string name) =>
        existing.Any(ls => ls.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Loads the persisted (non-sentinel) sets.</summary>
    public IReadOnlyList<LoadSet> LoadSaved() => _settings.LoadLoadSets();

    /// <summary>Persists the given sets, excluding the sentinel which must never be written.</summary>
    public void Save(IEnumerable<LoadSet> sets) =>
        _settings.SaveLoadSets(sets.Where(ls => ls.Id != SentinelId));
}
