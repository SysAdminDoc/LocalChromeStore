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
    public string IconCacheDir { get; }

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

    public SettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        SettingsDir = Path.Combine(appData, "LocalChromeStore");
        SettingsPath = Path.Combine(SettingsDir, "settings.json");
        ExtensionsRoot = Path.Combine(localAppData, "LocalChromeStore", "extensions");
        CacheDir = Path.Combine(localAppData, "LocalChromeStore", "cache");
        LogsDir = Path.Combine(localAppData, "LocalChromeStore", "logs");
        IconCacheDir = Path.Combine(CacheDir, "icons");
        ManifestPath = Path.Combine(SettingsDir, "installed.json");
        Directory.CreateDirectory(SettingsDir);
        Directory.CreateDirectory(ExtensionsRoot);
        Directory.CreateDirectory(CacheDir);
        Directory.CreateDirectory(LogsDir);
        Directory.CreateDirectory(IconCacheDir);
    }

    public AppSettings Load()
    {
        if (!File.Exists(SettingsPath)) return new AppSettings();
        try
        {
            var json = File.ReadAllText(SettingsPath);
            var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts) ?? new AppSettings();
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
            return s;
        }
        catch { return new AppSettings(); }
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
            HiddenRepos = settings.HiddenRepos.ToList(),
            LaunchBrowserAfterInstall = settings.LaunchBrowserAfterInstall,
            AutoUpdateOnRefresh = settings.AutoUpdateOnRefresh
        };
        var json = JsonSerializer.Serialize(copy, JsonOpts);
        File.WriteAllText(SettingsPath, json);
        TokenWasMigratedFromPlaintext = false;
    }

    public InstalledExtensionsManifest LoadManifest()
    {
        if (!File.Exists(ManifestPath)) return new InstalledExtensionsManifest();
        try
        {
            var json = File.ReadAllText(ManifestPath);
            return JsonSerializer.Deserialize<InstalledExtensionsManifest>(json, JsonOpts)
                ?? new InstalledExtensionsManifest();
        }
        catch { return new InstalledExtensionsManifest(); }
    }

    public void SaveManifest(InstalledExtensionsManifest manifest)
    {
        var json = JsonSerializer.Serialize(manifest, JsonOpts);
        File.WriteAllText(ManifestPath, json);
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
