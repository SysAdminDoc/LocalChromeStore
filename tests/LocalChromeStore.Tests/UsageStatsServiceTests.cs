using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class UsageStatsServiceTests : IDisposable
{
    private readonly string _dir;

    public UsageStatsServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "LocalChromeStore.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, recursive: true);
        }
        catch { }
    }

    [Fact]
    public void RecordRefresh_IncrementsCount()
    {
        var svc = new UsageStatsService(_dir);
        svc.RecordRefresh(10);
        svc.RecordRefresh(12);
        Assert.Equal(2, svc.Current.RefreshCount);
        Assert.Equal(12, svc.Current.LastRefreshExtensionCount);
        Assert.NotNull(svc.Current.LastRefreshAt);
    }

    [Fact]
    public void RecordInstall_TracksPerExtension()
    {
        var svc = new UsageStatsService(_dir);
        svc.RecordInstall("owner/repo");
        svc.RecordInstall("owner/repo");
        svc.RecordInstall("other/ext");
        Assert.Equal(3, svc.Current.InstallCount);
        Assert.Equal(2, svc.Current.PerExtension["owner/repo"].InstallCount);
        Assert.Equal(1, svc.Current.PerExtension["other/ext"].InstallCount);
    }

    [Fact]
    public void RecordLaunch_IncrementsCount()
    {
        var svc = new UsageStatsService(_dir);
        svc.RecordLaunch();
        Assert.Equal(1, svc.Current.LaunchCount);
        Assert.NotNull(svc.Current.LastLaunchAt);
    }

    [Fact]
    public void RecordUpdate_TracksPerExtension()
    {
        var svc = new UsageStatsService(_dir);
        svc.RecordUpdate("owner/repo");
        Assert.Equal(1, svc.Current.UpdateCount);
        Assert.Equal(1, svc.Current.PerExtension["owner/repo"].UpdateCount);
    }

    [Fact]
    public void Stats_PersistAcrossInstances()
    {
        var svc1 = new UsageStatsService(_dir);
        svc1.RecordRefresh(5);
        svc1.RecordInstall("a/b");

        var svc2 = new UsageStatsService(_dir);
        Assert.Equal(1, svc2.Current.RefreshCount);
        Assert.Equal(1, svc2.Current.InstallCount);
        Assert.True(svc2.Current.PerExtension.ContainsKey("a/b"));
    }
}
