using LocalChromeStore.Services;
using Xunit;

namespace LocalChromeStore.Tests;

public sealed class PolicyEnrollmentServiceTests
{
    [Fact]
    public void UnmanagedMachine_RefusesOffStoreForceInstall()
    {
        var state = new EnrollmentState(DomainJoined: false, EntraJoined: false, CbcmEnrolled: false);
        var result = PolicyEnrollmentService.EvaluateOffStoreForceInstall(state);

        Assert.False(state.IsManaged);
        Assert.False(result.Supported);
        Assert.Contains("not enrolled", result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true, false, false, "Active Directory")]
    [InlineData(false, true, false, "Entra")]
    [InlineData(false, false, true, "Cloud Management")]
    public void AnyManagementChannel_AllowsOffStoreForceInstall(bool domain, bool entra, bool cbcm, string expectedChannelFragment)
    {
        var state = new EnrollmentState(domain, entra, cbcm);
        var result = PolicyEnrollmentService.EvaluateOffStoreForceInstall(state);

        Assert.True(state.IsManaged);
        Assert.True(result.Supported);
        Assert.Contains(expectedChannelFragment, result.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MultipleChannels_AreAllListedInReason()
    {
        var state = new EnrollmentState(DomainJoined: true, EntraJoined: true, CbcmEnrolled: true);
        var result = PolicyEnrollmentService.EvaluateOffStoreForceInstall(state);

        Assert.True(result.Supported);
        Assert.Contains("Active Directory", result.Reason);
        Assert.Contains("Entra", result.Reason);
        Assert.Contains("Cloud Management", result.Reason);
    }

    [Fact]
    public void DetectCurrent_DoesNotThrow()
    {
        // The probe must be safe to call on any machine (managed or not, any privilege level).
        var state = new PolicyEnrollmentService().DetectCurrent();
        Assert.NotNull(state);
    }
}
