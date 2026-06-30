using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalChromeStore.Models;

namespace LocalChromeStore.Services;

public sealed class SettingsService
{
    public string SettingsDir { get; }
    public string SettingsPath { get; }
    public string ExtensionsRoot { get; }
    public string CacheDir { get; }
    public string LogsDir { get; }
    public string ManifestPath { get; }
    public string LoadSetsPath { get; }
    public string IconCacheDir { get; }
    public string PolicyPackagesRoot { get; }
    public string PolicyKeysDir { get; }

    /// <summary>
    /// True when the most recently loaded settings file contained a plaintext
    /// GitHub token that the loader migrated to an in-memory plaintext value
    /// pending a re-save under DPAPI. Used to log the migration once.
    /// </summary>
    public bool TokenWasMigratedFromPlaintext { get; private set; }

    private const string DpapiPrefix = "dpapi:";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public SettingsService(string? appDataRoot = null, string? localAppDataRoot = null)
    {
        var appData = appDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = localAppDataRoot ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        SettingsDir = Path.Combine(appData, "LocalChromeStore");
        SettingsPath = Path.Combine(SettingsDir, "settings.json");
        ExtensionsRoot = Path.Combine(localAppData, "LocalChromeStore", "extensions");
        CacheDir = Path.Combine(localAppData, "LocalChromeStore", "cache");
        LogsDir = Path.Combine(localAppData, "LocalChromeStore", "logs");
        IconCacheDir = Path.Combine(CacheDir, "icons");
        PolicyPackagesRoot = Path.Combine(localAppData, "LocalChromeStore", "policy-packages");
        PolicyKeysDir = Path.Combine(SettingsDir, "policy-keys");
        ManifestPath = Path.Combine(SettingsDir, "installed.json");
        LoadSetsPath = Path.Combine(SettingsDir, "loadsets.json");
        Directory.CreateDirectory(SettingsDir);
        Directory.CreateDirectory(ExtensionsRoot);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(IconCacheDir);
        Directory.CreateDirectory(PolicyPackagesRoot);
        Directory.CreateDirectory(PolicyKeysDir);
    }

    public AppSettings Load()
    {
        var s = ReadJsonWithBackup(SettingsPath, () => new AppSettings());
        if (!string.IsNullOrEmpty(s.GitHubToken))
        {
            if (s.GitHubToken.StartsWith(DpapiPrefix, StringComparison.Ordinal))
            {
                s.GitHubToken = TryUnprotect(s.GitHubToken.Substring(DpapiPrefix.Length));
            }
            else
            {
                // Legacy plaintext token — surface the migration to the caller so it
                // can be re-saved under DPAPI on the next persist.
                TokenWasMigratedFromPlaintext = true;
            }
        }
        if (s.LaunchWithTemporaryProfile && s.LaunchProfileMode == BrowserProfileMode.Default)
            s.LaunchProfileMode = BrowserProfileMode.Temporary;
        return s;
    }

    public void Save(AppSettings settings)
    {
        // Round-trip through a copy so the in-memory AppSettings always holds the
        // plaintext token; only the on-disk JSON is encrypted.
        var copy = new AppSettings
        {
            GitHubUser = settings.GitHubUser,
            GitHubToken = string.IsNullOrEmpty(settings.GitHubToken) ? null : DpapiPrefix + Protect(settings.GitHubToken),
            PreferredBrowserPath = settings.PreferredBrowserPath,
            UseTopicFilter = settings.UseTopicFilter,
            TopicFilter = settings.TopicFilter,
            ExtraOwners = settings.ExtraOwners.ToList(),
            LocalSourceFolders = settings.LocalSourceFolders
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList(),
            HiddenRepos = settings.HiddenRepos.ToList(),
            LaunchBrowserAfterInstall = settings.LaunchBrowserAfterInstall,
            AutoUpdateOnRefresh = settings.AutoUpdateOnRefresh,
            LaunchUrl = string.IsNullOrWhiteSpace(settings.LaunchUrl) ? null : settings.LaunchUrl.Trim(),
            LaunchProfileMode = settings.LaunchProfileMode,
            LaunchWithTemporaryProfile = settings.LaunchProfileMode == BrowserProfileMode.Temporary
        };
        var json = JsonSerializer.Serialize(copy, JsonOpts);
        WriteAtomic(SettingsPath, json);
        TokenWasMigratedFromPlaintext = false;
    }

    public InstalledExtensionsManifest LoadManifest() =>
        ReadJsonWithBackup(ManifestPath, () => new InstalledExtensionsManifest());

    public void SaveManifest(InstalledExtensionsManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, JsonOpts);
        WriteAtomic(ManifestPath, json);
    }

    public List<LoadSet> LoadLoadSets() =>
        ReadJsonWithBackup<List<LoadSet>>(LoadSetsPath, () => []);

    public void SaveLoadSets(IEnumerable<LoadSet> sets)
    {
        var json = JsonSerializer.Serialize(sets.ToList(), JsonOpts);
        WriteAtomic(LoadSetsPath, json);
    }

    /// <summary>
    /// Crash-safe write: serialize to <c>&lt;path&gt;.tmp</c>, flush to disk, then atomically
    /// swap it into place, keeping the previous good copy as <c>&lt;path&gt;.bak</c>. A crash or
    /// power loss mid-write leaves either the prior file intact or the <c>.bak</c> recoverable —
    /// it can never truncate the live file. Used for every JSON state file.
    /// </summary>
    public static void WriteAtomic(string path, string contents)
    {
        var tmp = path + ".tmp";
        var bak = path + ".bak";
        // Write + fsync the temp file before any swap so the bytes are durable.
        using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
        {
            writer.Write(contents);
            writer.Flush();
            fs.Flush(flushToDisk: true);
        }

        if (File.Exists(path))
        {
            // File.Replace is atomic on NTFS and writes the old contents to the backup.
            File.Replace(tmp, path, bak, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(tmp, path);
        }
    }

    /// <summary>
    /// Reads and deserializes <paramref name="path"/>, transparently falling back to the
    /// <c>.bak</c> written by <see cref="WriteAtomic"/> if the primary file is missing or corrupt,
    /// and finally to <paramref name="fallback"/>.
    /// </summary>
    private static T ReadJsonWithBackup<T>(string path, Func<T> fallback)
    {
        foreach (var candidate in new[] { path, path + ".bak" })
        {
            if (!File.Exists(candidate)) continue;
            try
            {
                var json = File.ReadAllText(candidate);
                var value = JsonSerializer.Deserialize<T>(json, JsonOpts);
                if (value is not null) return value;
            }
            catch { /* try the backup, then the fallback */ }
        }
        return fallback();
    }

    private static string Protect(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        var enc = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(enc);
    }

    private static string? TryUnprotect(string base64)
    {
        try
        {
            var enc = Convert.FromBase64String(base64);
            var bytes = ProtectedData.Unprotect(enc, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            // Token cannot be decrypted on this machine/user (e.g. the settings file
            // was copied from another profile). Treat as missing rather than failing.
            return null;
        }
    }
}
