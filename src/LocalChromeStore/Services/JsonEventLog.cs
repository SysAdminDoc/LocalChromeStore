using System.IO;
using System.Globalization;
using System.Text.Json;

namespace LocalChromeStore.Services;

public enum EventLevel { Info, Warn, Error }

public enum EventCategory
{
    General,
    Discovery,
    Install,
    Uninstall,
    Update,
    Launch,
    Policy,
    Settings,
    Import,
    Export,
    SelfUpdate
}

public sealed class JsonEventLog
{
    private readonly string _logsDir;
    private readonly object _writeLock = new();
    private readonly int _retentionDays;
    private DateOnly? _lastCleanupDate;

    private const int DefaultRetentionDays = 30;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public JsonEventLog(string logsDir, int retentionDays = DefaultRetentionDays)
    {
        if (retentionDays < 1) throw new ArgumentOutOfRangeException(nameof(retentionDays));
        _logsDir = logsDir;
        _retentionDays = retentionDays;
        Directory.CreateDirectory(logsDir);
    }

    public void Write(EventLevel level, EventCategory category, string message, Dictionary<string, object?>? metadata = null)
    {
        var entry = new Dictionary<string, object?>
        {
            ["ts"] = DateTime.UtcNow.ToString("o"),
            ["level"] = level.ToString().ToLowerInvariant(),
            ["category"] = category.ToString().ToLowerInvariant(),
            ["message"] = message
        };

        if (metadata is { Count: > 0 })
            entry["meta"] = metadata;

        var now = DateTime.UtcNow;
        var line = JsonSerializer.Serialize(entry, JsonOpts);
        var path = Path.Combine(_logsDir, $"events-{now:yyyyMMdd}.jsonl");

        lock (_writeLock)
        {
            try
            {
                File.AppendAllText(path, line + "\n");
                CleanupOldEvents(now);
            }
            catch
            {
                // Best-effort logging — never crash the app.
            }
        }
    }

    private void CleanupOldEvents(DateTime utcNow)
    {
        var currentDate = DateOnly.FromDateTime(utcNow);
        if (_lastCleanupDate == currentDate) return;
        _lastCleanupDate = currentDate;

        var cutoff = utcNow.Date.AddDays(-_retentionDays);
        foreach (var path in Directory.EnumerateFiles(_logsDir, "events-*.jsonl", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (!name.StartsWith("events-", StringComparison.Ordinal) ||
                !DateTime.TryParseExact(
                    name["events-".Length..],
                    "yyyyMMdd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var fileDate) ||
                fileDate.Date >= cutoff)
                continue;

            try { File.Delete(path); }
            catch { /* best-effort retention cleanup */ }
        }
    }

    public void Info(EventCategory category, string message, Dictionary<string, object?>? metadata = null) =>
        Write(EventLevel.Info, category, message, metadata);

    public void Warn(EventCategory category, string message, Dictionary<string, object?>? metadata = null) =>
        Write(EventLevel.Warn, category, message, metadata);

    public void Error(EventCategory category, string message, Dictionary<string, object?>? metadata = null) =>
        Write(EventLevel.Error, category, message, metadata);

    public static EventCategory ClassifyLogLine(string line)
    {
        var clean = line.StartsWith("! ", StringComparison.Ordinal) ? line[2..] : line;

        if (clean.StartsWith("Install", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("installed", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("download", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("extracted", StringComparison.OrdinalIgnoreCase))
            return EventCategory.Install;

        if (clean.StartsWith("Uninstall", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("unlinked", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("removed", StringComparison.OrdinalIgnoreCase))
            return EventCategory.Uninstall;

        if (clean.StartsWith("Launch", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("browser", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("CDP", StringComparison.OrdinalIgnoreCase))
            return EventCategory.Launch;

        if (clean.Contains("self-update", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("newer version", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("newer LocalChromeStore", StringComparison.OrdinalIgnoreCase))
            return EventCategory.SelfUpdate;

        if (clean.StartsWith("Update", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("update", StringComparison.OrdinalIgnoreCase))
            return EventCategory.Update;

        if (clean.StartsWith("Policy", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("HKLM", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("force-install", StringComparison.OrdinalIgnoreCase))
            return EventCategory.Policy;

        if (clean.StartsWith("Discover", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("discovered", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("repos", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("Local source", StringComparison.OrdinalIgnoreCase))
            return EventCategory.Discovery;

        if (clean.StartsWith("Settings", StringComparison.OrdinalIgnoreCase) ||
            clean.Contains("settings", StringComparison.OrdinalIgnoreCase))
            return EventCategory.Settings;

        if (clean.Contains("import", StringComparison.OrdinalIgnoreCase))
            return EventCategory.Import;

        if (clean.Contains("export", StringComparison.OrdinalIgnoreCase))
            return EventCategory.Export;

        return EventCategory.General;
    }
}
