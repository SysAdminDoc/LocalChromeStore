using System.Text;
using LocalChromeStore.Models;

namespace LocalChromeStore.Services;

public sealed record AssetChangeComparison(bool CanCompare, bool Changed, IReadOnlyList<string> Reasons);

public static class ReleaseProvenance
{
    public static AssetChangeComparison CompareAssetSnapshot(ExtensionInfo info, InstalledExtension? installed)
    {
        if (installed is null) return new AssetChangeComparison(false, false, []);

        var compared = false;
        var reasons = new List<string>();

        CompareString("asset name", info.AssetName, installed.AssetName, reasons, ref compared);
        CompareString("digest", NormalizeDigest(info.AssetDigest), NormalizeDigest(installed.AssetDigest) ?? NormalizeDigest(installed.ChecksumValue), reasons, ref compared);
        CompareLong("asset id", info.AssetId, installed.AssetId, reasons, ref compared);
        CompareLong("size", info.AssetSizeBytes > 0 ? info.AssetSizeBytes : null, installed.AssetSizeBytes, reasons, ref compared);
        CompareTimestamp("upload timestamp", info.AssetUpdatedAt ?? info.AssetCreatedAt, installed.AssetUpdatedAt ?? installed.AssetCreatedAt, reasons, ref compared);

        return new AssetChangeComparison(compared, reasons.Count > 0, reasons);
    }

    public static string CardSummary(ExtensionInfo info, InstalledExtension? installed)
    {
        if (string.IsNullOrWhiteSpace(info.AssetUrl))
            return "No release asset provenance.";

        var parts = new List<string>
        {
            DateLabel(info),
            info.AssetSizeBytes > 0 ? FormatSize(info.AssetSizeBytes) : "size unknown",
            ChecksumSourceLabel(info)
        };

        if (installed is not null)
            parts.Add(ChangeStatusLabel(CompareAssetSnapshot(info, installed)));

        return string.Join(" - ", parts);
    }

    public static string DiagnosticsSummary(ExtensionInfo info, InstalledExtension? installed)
    {
        if (string.IsNullOrWhiteSpace(info.AssetUrl))
            return "unavailable";

        var parts = new List<string>();
        if (info.AssetId.HasValue) parts.Add($"asset id {info.AssetId.Value}");
        if (!string.IsNullOrWhiteSpace(info.AssetUploader)) parts.Add($"uploader {info.AssetUploader}");
        parts.Add(DateLabel(info));
        if (info.AssetSizeBytes > 0) parts.Add($"size {FormatSize(info.AssetSizeBytes)}");
        if (!string.IsNullOrWhiteSpace(info.AssetContentType)) parts.Add($"type {info.AssetContentType}");
        if (info.AssetDownloadCount.HasValue) parts.Add($"downloads {info.AssetDownloadCount.Value}");
        parts.Add(ChecksumSourceLabel(info));
        if (installed is not null) parts.Add(ChangeStatusLabel(CompareAssetSnapshot(info, installed)));
        return string.Join("; ", parts);
    }

    public static string InstalledSummary(InstalledExtension installed)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(installed.AssetName)) parts.Add(installed.AssetName);
        if (installed.AssetId.HasValue) parts.Add($"asset id {installed.AssetId.Value}");
        if (!string.IsNullOrWhiteSpace(installed.AssetUploader)) parts.Add($"uploader {installed.AssetUploader}");
        if (installed.AssetUpdatedAt.HasValue) parts.Add($"updated {installed.AssetUpdatedAt.Value.LocalDateTime:yyyy-MM-dd HH:mm}");
        else if (installed.AssetCreatedAt.HasValue) parts.Add($"uploaded {installed.AssetCreatedAt.Value.LocalDateTime:yyyy-MM-dd HH:mm}");
        else if (installed.ReleasePublishedAt.HasValue) parts.Add($"release {installed.ReleasePublishedAt.Value.LocalDateTime:yyyy-MM-dd HH:mm}");
        if (installed.AssetSizeBytes is > 0) parts.Add($"size {FormatSize(installed.AssetSizeBytes.Value)}");
        if (!string.IsNullOrWhiteSpace(installed.AssetDigest)) parts.Add("digest captured");
        return parts.Count == 0 ? "not captured" : string.Join("; ", parts);
    }

    public static string Detail(ExtensionInfo info, InstalledExtension? installed)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Asset: {info.AssetName ?? "(none)"}");
        if (info.AssetId.HasValue) sb.AppendLine($"GitHub asset ID: {info.AssetId.Value}");
        if (!string.IsNullOrWhiteSpace(info.AssetContentType)) sb.AppendLine($"Content type: {info.AssetContentType}");
        if (!string.IsNullOrWhiteSpace(info.AssetUploader)) sb.AppendLine($"Uploader: {info.AssetUploader}");
        if (info.AssetCreatedAt.HasValue) sb.AppendLine($"Uploaded: {info.AssetCreatedAt.Value.LocalDateTime:yyyy-MM-dd HH:mm}");
        if (info.AssetUpdatedAt.HasValue) sb.AppendLine($"Updated: {info.AssetUpdatedAt.Value.LocalDateTime:yyyy-MM-dd HH:mm}");
        if (info.PublishedAt.HasValue) sb.AppendLine($"Release published: {info.PublishedAt.Value.LocalDateTime:yyyy-MM-dd HH:mm}");
        if (info.AssetSizeBytes > 0) sb.AppendLine($"Size: {FormatSize(info.AssetSizeBytes)}");
        if (info.AssetDownloadCount.HasValue) sb.AppendLine($"Downloads: {info.AssetDownloadCount.Value}");
        sb.AppendLine($"Checksum source: {ChecksumSourceLabel(info)}");

        if (installed is null)
        {
            sb.AppendLine("Installed snapshot: not installed.");
        }
        else
        {
            var comparison = CompareAssetSnapshot(info, installed);
            sb.AppendLine($"Installed snapshot: {InstalledSummary(installed)}");
            sb.AppendLine($"Changed since install: {ChangeStatusLabel(comparison)}");
            if (comparison.Reasons.Count > 0)
                sb.AppendLine($"Change reasons: {string.Join(", ", comparison.Reasons)}");
        }

        return sb.ToString().TrimEnd();
    }

    public static string ChangeStatusLabel(AssetChangeComparison comparison)
    {
        if (!comparison.CanCompare) return "install snapshot unavailable";
        return comparison.Changed ? "changed since install" : "unchanged since install";
    }

    public static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "?";
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var index = 0;
        while (value >= 1024 && index < units.Length - 1)
        {
            value /= 1024;
            index++;
        }
        return $"{value:0.##} {units[index]}";
    }

    private static string DateLabel(ExtensionInfo info)
    {
        if (info.AssetUpdatedAt.HasValue) return $"Updated {info.AssetUpdatedAt.Value.LocalDateTime:MMM d, yyyy}";
        if (info.AssetCreatedAt.HasValue) return $"Uploaded {info.AssetCreatedAt.Value.LocalDateTime:MMM d, yyyy}";
        if (info.PublishedAt.HasValue) return $"Released {info.PublishedAt.Value.LocalDateTime:MMM d, yyyy}";
        return "Upload date unknown";
    }

    private static string ChecksumSourceLabel(ExtensionInfo info)
    {
        if (!string.IsNullOrWhiteSpace(info.ChecksumUrl)) return "sidecar checksum";
        return ExtensionService.TryParseSha256Digest(info.AssetDigest, out _)
            ? "GitHub digest"
            : "no checksum";
    }

    private static void CompareString(string label, string? current, string? installed, List<string> reasons, ref bool compared)
    {
        if (string.IsNullOrWhiteSpace(current) || string.IsNullOrWhiteSpace(installed)) return;
        compared = true;
        if (!current.Trim().Equals(installed.Trim(), StringComparison.OrdinalIgnoreCase))
            reasons.Add($"{label} changed");
    }

    private static void CompareLong(string label, long? current, long? installed, List<string> reasons, ref bool compared)
    {
        if (!current.HasValue || !installed.HasValue || current.Value <= 0 || installed.Value <= 0) return;
        compared = true;
        if (current.Value != installed.Value)
            reasons.Add($"{label} changed");
    }

    private static void CompareTimestamp(string label, DateTimeOffset? current, DateTimeOffset? installed, List<string> reasons, ref bool compared)
    {
        if (!current.HasValue || !installed.HasValue) return;
        compared = true;
        if (current.Value.ToUnixTimeSeconds() != installed.Value.ToUnixTimeSeconds())
            reasons.Add($"{label} changed");
    }

    private static string? NormalizeDigest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        var trimmed = digest.Trim();
        if (ExtensionService.TryParseSha256Digest(trimmed, out var prefixed)) return prefixed;
        if (trimmed.Length == 64 && trimmed.All(Uri.IsHexDigit)) return trimmed.ToLowerInvariant();
        return null;
    }
}
