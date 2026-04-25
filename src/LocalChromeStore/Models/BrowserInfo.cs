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
    public override string ToString() => DisplayName;
}
