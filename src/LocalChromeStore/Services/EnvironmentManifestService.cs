using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalChromeStore.Models;

namespace LocalChromeStore.Services;

public static class EnvironmentManifestService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    static EnvironmentManifestService()
    {
        JsonOpts.Converters.Add(new JsonStringEnumConverter());
    }

    public static EnvironmentManifest Create(AppSettings settings, IEnumerable<InstalledExtension> installed)
    {
        return new EnvironmentManifest
        {
            ExportedAt = DateTimeOffset.UtcNow,
            Settings = new EnvironmentSettingsSnapshot
            {
                GitHubUser = settings.GitHubUser,
                UseTopicFilter = settings.UseTopicFilter,
                TopicFilter = settings.TopicFilter,
                ExtraOwners = settings.ExtraOwners
                    .Where(o => !string.IsNullOrWhiteSpace(o))
                    .Select(o => o.Trim())
                    .Where(o => !o.Equals(settings.GitHubUser, StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(o => o, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                LaunchUrl = string.IsNullOrWhiteSpace(settings.LaunchUrl) ? null : settings.LaunchUrl.Trim(),
                LaunchWithTemporaryProfile = settings.LaunchWithTemporaryProfile
            },
            Extensions = installed
                .OrderBy(e => e.RepoOwner, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.RepoName, StringComparer.OrdinalIgnoreCase)
                .Select(ToSnapshot)
                .ToList()
        };
    }

    public static string ToJson(EnvironmentManifest manifest) => JsonSerializer.Serialize(manifest, JsonOpts);

    public static EnvironmentManifest FromJson(string json)
    {
        var manifest = JsonSerializer.Deserialize<EnvironmentManifest>(json, JsonOpts)
            ?? throw new InvalidOperationException("Environment manifest is empty or invalid.");
        Validate(manifest);
        return manifest;
    }

    public static EnvironmentManifest Load(string path) => FromJson(File.ReadAllText(path));

    public static void Save(string path, EnvironmentManifest manifest) => File.WriteAllText(path, ToJson(manifest));

    public static AppSettings ApplySettings(AppSettings current, EnvironmentManifest manifest)
    {
        Validate(manifest);

        var owners = manifest.Extensions
            .Select(e => e.RepoOwner)
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var primary = string.IsNullOrWhiteSpace(manifest.Settings.GitHubUser)
            ? (owners.FirstOrDefault() ?? current.GitHubUser)
            : manifest.Settings.GitHubUser.Trim();

        var extraOwners = manifest.Settings.ExtraOwners
            .Concat(owners)
            .Where(o => !string.IsNullOrWhiteSpace(o))
            .Select(o => o.Trim())
            .Where(o => !o.Equals(primary, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(o => o, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var importedKeys = new HashSet<string>(manifest.Extensions.Select(e => e.Key), StringComparer.OrdinalIgnoreCase);

        return new AppSettings
        {
            GitHubUser = primary,
            GitHubToken = current.GitHubToken,
            PreferredBrowserPath = current.PreferredBrowserPath,
            UseTopicFilter = manifest.Settings.UseTopicFilter,
            TopicFilter = string.IsNullOrWhiteSpace(manifest.Settings.TopicFilter)
                ? current.TopicFilter
                : manifest.Settings.TopicFilter.Trim(),
            ExtraOwners = extraOwners,
            HiddenRepos = current.HiddenRepos
                .Where(repo => !importedKeys.Contains(repo))
                .OrderBy(repo => repo, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            LaunchBrowserAfterInstall = current.LaunchBrowserAfterInstall,
            AutoUpdateOnRefresh = current.AutoUpdateOnRefresh,
            LaunchUrl = string.IsNullOrWhiteSpace(manifest.Settings.LaunchUrl) ? current.LaunchUrl : manifest.Settings.LaunchUrl.Trim(),
            LaunchWithTemporaryProfile = manifest.Settings.LaunchWithTemporaryProfile
        };
    }

    private static EnvironmentExtensionSnapshot ToSnapshot(InstalledExtension e) => new()
    {
        RepoOwner = e.RepoOwner,
        RepoName = e.RepoName,
        Version = e.Version,
        DisplayName = e.DisplayName,
        RepoUrl = e.RepoUrl,
        ManifestVersionNumber = e.ManifestVersionNumber,
        Framework = e.Framework,
        ChecksumVerified = e.ChecksumVerified,
        ChecksumAlgorithm = e.ChecksumAlgorithm,
        ChecksumValue = e.ChecksumValue,
        ChecksumSource = e.ChecksumSource,
        Permissions = e.Permissions.ToList(),
        OptionalPermissions = e.OptionalPermissions.ToList(),
        HostPermissions = e.HostPermissions.ToList(),
        OptionalHostPermissions = e.OptionalHostPermissions.ToList()
    };

    private static void Validate(EnvironmentManifest manifest)
    {
        if (!manifest.App.Equals("LocalChromeStore", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This is not a LocalChromeStore environment manifest.");
        if (manifest.Version != 1)
            throw new InvalidOperationException($"Unsupported environment manifest version: {manifest.Version}.");
        if (manifest.Extensions.Any(e => string.IsNullOrWhiteSpace(e.RepoOwner) || string.IsNullOrWhiteSpace(e.RepoName)))
            throw new InvalidOperationException("Environment manifest contains an extension without a repository owner/name.");
    }
}
