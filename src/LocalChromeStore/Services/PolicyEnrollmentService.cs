using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace LocalChromeStore.Services;

/// <summary>
/// The management-enrollment state of the current Windows machine. Off-store / self-hosted
/// <c>ExtensionInstallForcelist</c> entries are only honoured by Chrome when the machine is
/// managed — Active Directory domain-joined, Microsoft Entra (Azure AD) joined, or enrolled in
/// Chrome Browser Cloud Management (CBCM). On an un-enrolled consumer machine, force-install
/// silently works only for Chrome Web Store extensions, so the policy backend must refuse the
/// off-store path here and point the user at CBCM enrollment or the dev-load alternative.
/// </summary>
public sealed record EnrollmentState(bool DomainJoined, bool EntraJoined, bool CbcmEnrolled)
{
    /// <summary>True when any management channel that authorizes off-store force-install is present.</summary>
    public bool IsManaged => DomainJoined || EntraJoined || CbcmEnrolled;
}

/// <summary>Result of evaluating whether off-store force-install can work on this machine.</summary>
public sealed record OffStoreForceInstallSupport(bool Supported, string Reason);

public sealed class PolicyEnrollmentService
{
    /// <summary>
    /// Pure decision: can the off-store / self-hosted force-install path work given an enrollment
    /// state? Kept separate from OS probing so it is unit-testable. The reason string is shown to
    /// the user before they commit to policy mode.
    /// </summary>
    public static OffStoreForceInstallSupport EvaluateOffStoreForceInstall(EnrollmentState state)
    {
        if (!state.IsManaged)
        {
            return new OffStoreForceInstallSupport(
                false,
                "This machine is not enrolled in any browser management channel (Active Directory, " +
                "Microsoft Entra, or Chrome Browser Cloud Management). Chrome only force-installs " +
                "off-store / self-hosted extensions on a managed machine — on an un-enrolled machine " +
                "the policy is ignored for your own CRX. Enroll in free Chrome Browser Cloud Management, " +
                "or use the Chrome for Testing / dev-load path instead.");
        }

        var channels = new List<string>();
        if (state.DomainJoined) channels.Add("Active Directory domain join");
        if (state.EntraJoined) channels.Add("Microsoft Entra join");
        if (state.CbcmEnrolled) channels.Add("Chrome Browser Cloud Management");
        return new OffStoreForceInstallSupport(
            true,
            $"Machine is managed ({string.Join(", ", channels)}); off-store force-install is supported.");
    }

    /// <summary>Probes the live machine state. Best-effort — any probe failure is treated as "not enrolled".</summary>
    public EnrollmentState DetectCurrent() => new(
        DomainJoined: IsDomainJoined(),
        EntraJoined: IsEntraJoined(),
        CbcmEnrolled: IsCbcmEnrolled());

    // --- OS probes ---------------------------------------------------------

    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int NetGetJoinInformation(string? server, out IntPtr name, out NetJoinStatus status);

    [DllImport("netapi32.dll")]
    private static extern int NetApiBufferFree(IntPtr buffer);

    private enum NetJoinStatus
    {
        Unknown = 0,
        Unjoined,
        Workgroup,
        Domain
    }

    private static bool IsDomainJoined()
    {
        var ptr = IntPtr.Zero;
        try
        {
            if (NetGetJoinInformation(null, out ptr, out var status) == 0)
                return status == NetJoinStatus.Domain;
            return false;
        }
        catch { return false; }
        finally
        {
            if (ptr != IntPtr.Zero) NetApiBufferFree(ptr);
        }
    }

    private static bool IsEntraJoined()
    {
        // dsregcmd records Entra-joined devices under CloudDomainJoin\JoinInfo\<tenant guid>.
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo");
            return key is not null && key.GetSubKeyNames().Length > 0;
        }
        catch { return false; }
    }

    private static bool IsCbcmEnrolled()
    {
        // CBCM enrollment leaves an enrollment token (machine policy) and, once enrolled, a DM token.
        foreach (var (hive, path, value) in new (RegistryKey, string, string)[]
        {
            (Registry.LocalMachine, @"SOFTWARE\Policies\Google\Chrome", "CloudManagementEnrollmentToken"),
            (Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "CloudManagementEnrollmentToken"),
        })
        {
            try
            {
                using var key = hive.OpenSubKey(path);
                if (key?.GetValue(value) is string s && !string.IsNullOrWhiteSpace(s)) return true;
            }
            catch { /* try next */ }
        }
        return false;
    }
}
