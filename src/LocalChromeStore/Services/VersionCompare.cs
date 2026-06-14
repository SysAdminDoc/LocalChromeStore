using System.Globalization;

namespace LocalChromeStore.Services;

/// <summary>
/// Tolerant semver-aware version comparison for update detection. Extension/release tags in the
/// wild are inconsistent (<c>v1.0</c> vs <c>1.0</c>, <c>1.10</c> vs <c>1.2</c>, optional prerelease
/// and build-metadata suffixes), so ordinal string equality produces false "update available"
/// signals. This normalizes a leading <c>v</c>/<c>V</c>, compares the dotted numeric core
/// segment-by-segment (numerically, zero-padding the shorter side), ignores <c>+build</c> metadata,
/// and orders prereleases (<c>1.0.0-beta</c>) below their release (<c>1.0.0</c>) per SemVer 2.0.0.
/// </summary>
public static class VersionCompare
{
    /// <summary>
    /// Returns &lt;0 if <paramref name="a"/> is older than <paramref name="b"/>, 0 if equal,
    /// &gt;0 if newer. Unparseable/empty versions fall back to ordinal comparison.
    /// </summary>
    public static int Compare(string? a, string? b)
    {
        a = Normalize(a);
        b = Normalize(b);
        if (a == b) return 0;
        if (a.Length == 0) return b.Length == 0 ? 0 : -1;
        if (b.Length == 0) return 1;

        var (coreA, preA) = SplitCore(a);
        var (coreB, preB) = SplitCore(b);

        var coreCmp = CompareCore(coreA, coreB);
        if (coreCmp != 0) return coreCmp;

        // Equal cores: a version with no prerelease outranks one that has a prerelease.
        if (preA.Length == 0 && preB.Length == 0) return 0;
        if (preA.Length == 0) return 1;
        if (preB.Length == 0) return -1;
        return ComparePrerelease(preA, preB);
    }

    /// <summary>True when <paramref name="candidate"/> is a strictly newer version than <paramref name="current"/>.</summary>
    public static bool IsNewer(string? candidate, string? current) => Compare(candidate, current) > 0;

    private static string Normalize(string? v)
    {
        if (string.IsNullOrWhiteSpace(v)) return string.Empty;
        v = v.Trim();
        if (v.Length > 0 && (v[0] == 'v' || v[0] == 'V')) v = v[1..];
        var plus = v.IndexOf('+'); // strip SemVer build metadata
        if (plus >= 0) v = v[..plus];
        return v;
    }

    private static (string core, string pre) SplitCore(string v)
    {
        var dash = v.IndexOf('-');
        return dash < 0 ? (v, string.Empty) : (v[..dash], v[(dash + 1)..]);
    }

    private static int CompareCore(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        var max = Math.Max(pa.Length, pb.Length);
        for (var i = 0; i < max; i++)
        {
            var na = i < pa.Length ? ParseSegment(pa[i]) : 0;
            var nb = i < pb.Length ? ParseSegment(pb[i]) : 0;
            var cmp = na.CompareTo(nb);
            if (cmp != 0) return cmp;
        }
        return 0;
    }

    private static long ParseSegment(string s)
    {
        // Take the leading numeric run (handles "1rc", "0beta" defensively); default 0.
        var end = 0;
        while (end < s.Length && char.IsDigit(s[end])) end++;
        return end == 0 ? 0 : long.Parse(s[..end], CultureInfo.InvariantCulture);
    }

    private static int ComparePrerelease(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        var max = Math.Max(pa.Length, pb.Length);
        for (var i = 0; i < max; i++)
        {
            if (i >= pa.Length) return -1; // fewer identifiers = lower precedence
            if (i >= pb.Length) return 1;
            var idA = pa[i];
            var idB = pb[i];
            var numA = long.TryParse(idA, NumberStyles.None, CultureInfo.InvariantCulture, out var va);
            var numB = long.TryParse(idB, NumberStyles.None, CultureInfo.InvariantCulture, out var vb);
            int cmp;
            if (numA && numB) cmp = va.CompareTo(vb);
            else if (numA) cmp = -1;            // numeric identifiers rank below alphanumeric
            else if (numB) cmp = 1;
            else cmp = string.CompareOrdinal(idA, idB);
            if (cmp != 0) return cmp;
        }
        return 0;
    }
}
