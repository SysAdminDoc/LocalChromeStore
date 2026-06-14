namespace LocalChromeStore.Models;

public enum BrowserKind
{
    Chrome,
    Brave,
    Edge,
    Chromium,
    Vivaldi,
    Opera
}

public sealed class BrowserInfo
{
    public required BrowserKind Kind { get; init; }
    public required string DisplayName { get; init; }
    public required string ExecutablePath { get; init; }

    /// <summary>
    /// Major version parsed from the executable's file version, when detectable.
    /// Drives <see cref="Services.LaunchStrategy"/> selection (e.g. branded Chrome 137 removed
    /// <c>--load-extension</c>; 142 removed the <c>--disable-features</c> workaround too).
    /// Null when the version could not be read.
    /// </summary>
    public int? MajorVersion { get; init; }

    public override string ToString() => DisplayName;
}
