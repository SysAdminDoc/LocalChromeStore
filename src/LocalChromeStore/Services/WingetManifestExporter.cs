using System.Text;

namespace LocalChromeStore.Services;

public static class WingetManifestExporter
{
    public static string Generate(
        string packageId,
        string version,
        string publisher,
        string packageName,
        string description,
        string license,
        string releaseUrl,
        string assetUrl,
        string? sha256 = null)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# yaml-language-server: $schema=https://aka.ms/winget-manifest.singleton.1.6.0.schema.json");
        sb.AppendLine($"PackageIdentifier: {YamlSafe(packageId)}");
        sb.AppendLine($"PackageVersion: {YamlSafe(version)}");
        sb.AppendLine($"PackageLocale: en-US");
        sb.AppendLine($"Publisher: {YamlSafe(publisher)}");
        sb.AppendLine($"PackageName: {YamlSafe(packageName)}");
        sb.AppendLine($"ShortDescription: {YamlSafe(TruncateTo(description, 256))}");
        sb.AppendLine($"License: {YamlSafe(license)}");
        sb.AppendLine($"PackageUrl: {YamlSafe(releaseUrl)}");
        sb.AppendLine("Installers:");
        sb.AppendLine("  - Architecture: x64");
        sb.AppendLine("    InstallerType: zip");
        sb.AppendLine($"    InstallerUrl: {assetUrl}");
        if (!string.IsNullOrWhiteSpace(sha256))
            sb.AppendLine($"    InstallerSha256: {sha256.ToUpperInvariant()}");
        sb.AppendLine("ManifestType: singleton");
        sb.AppendLine("ManifestVersion: 1.6.0");
        return sb.ToString();
    }

    public static string GenerateForLocalChromeStore(string version, string assetUrl, string? sha256 = null) =>
        Generate(
            packageId: "SysAdminDoc.LocalChromeStore",
            version: version,
            publisher: "SysAdminDoc",
            packageName: "LocalChromeStore",
            description: "A personal store for the Chromium extensions you build yourself.",
            license: "MIT",
            releaseUrl: $"https://github.com/SysAdminDoc/LocalChromeStore/releases/tag/v{version}",
            assetUrl: assetUrl,
            sha256: sha256);

    private static string YamlSafe(string value)
    {
        if (value.IndexOfAny([':', '#', '{', '}', '[', ']', ',', '&', '*', '?', '|', '-', '<', '>', '=', '!', '%', '@', '`', '\n', '\r']) >= 0)
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        return value;
    }

    private static string TruncateTo(string s, int max) =>
        s.Length <= max ? s : s[..(max - 3)] + "...";
}
