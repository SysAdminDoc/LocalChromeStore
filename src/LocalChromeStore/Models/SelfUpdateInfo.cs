namespace LocalChromeStore.Models;

/// <summary>
/// Result of checking LocalChromeStore's own GitHub releases for a newer build. The app only ever
/// links the user to the release page — it never downloads or installs itself.
/// </summary>
public sealed record SelfUpdateInfo(bool UpdateAvailable, string LatestVersion, string ReleaseUrl)
{
    /// <summary>No newer build / check unavailable. Carries no banner.</summary>
    public static SelfUpdateInfo None { get; } = new(false, string.Empty, string.Empty);
}
