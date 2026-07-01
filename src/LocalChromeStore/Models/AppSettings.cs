namespace LocalChromeStore.Models;

public enum BrowserProfileMode
{
    Default,
    Persistent,
    Temporary
}

public sealed class AppSettings
{
    public string GitHubUser { get; set; } = "SysAdminDoc";
    public string? GitHubToken { get; set; }
    public string? PreferredBrowserPath { get; set; }
    public bool UseTopicFilter { get; set; } = false;
    public string TopicFilter { get; set; } = "chrome-extension";
    public List<string> ExtraOwners { get; set; } = new();
    public List<string> LocalSourceFolders { get; set; } = new();
    public List<string> HiddenRepos { get; set; } = new();
    public List<string> PinnedRepos { get; set; } = new();
    public bool LaunchBrowserAfterInstall { get; set; } = false;
    public bool AutoUpdateOnRefresh { get; set; } = false;
    public string? LaunchUrl { get; set; }
    public BrowserProfileMode LaunchProfileMode { get; set; } = BrowserProfileMode.Default;
    public bool LaunchWithTemporaryProfile { get; set; } = false;
}
