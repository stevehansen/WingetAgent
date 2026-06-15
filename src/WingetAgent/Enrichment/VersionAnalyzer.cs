using WingetAgent.Models;

namespace WingetAgent.Enrichment;

/// <summary>
/// Classifies the magnitude of a version change by comparing leading numeric
/// components. A leading "v" tag prefix (GitHub-style: v1.0.62) and non-numeric
/// suffixes (-beta, +build) are ignored; unparseable or "Unknown" current
/// versions yield JumpType.Unknown so scoring can be cautious.
/// </summary>
public static class VersionAnalyzer
{
    public static JumpType Compare(string current, string available)
    {
        if (string.IsNullOrWhiteSpace(current)
            || current.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return JumpType.Unknown;

        var c = ParseParts(current);
        var a = ParseParts(available);
        if (c is null || a is null) return JumpType.Unknown;

        int len = Math.Max(c.Length, a.Length);
        for (int i = 0; i < len; i++)
        {
            int cv = i < c.Length ? c[i] : 0;
            int av = i < a.Length ? a[i] : 0;
            if (cv == av) continue;
            return i switch { 0 => JumpType.Major, 1 => JumpType.Minor, _ => JumpType.Patch };
        }
        return JumpType.Same;
    }

    internal static int[]? ParseParts(string v)
    {
        v = v.Trim();
        if (v.Length >= 2 && (v[0] is 'v' or 'V') && char.IsDigit(v[1]))
            v = v[1..]; // strip GitHub-style "v" tag prefix
        var main = new string(v.TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray());
        if (main.Length == 0) return null;
        var nums = new List<int>();
        foreach (var p in main.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(p, out var n)) nums.Add(n);
            else break;
        }
        return nums.Count == 0 ? null : nums.ToArray();
    }
}

/// <summary>Orders version strings by their numeric components (descending sorts use Reverse).</summary>
public sealed class VersionishComparer : IComparer<string>
{
    public int Compare(string? x, string? y)
    {
        var a = VersionAnalyzer.ParseParts(x ?? "") ?? Array.Empty<int>();
        var b = VersionAnalyzer.ParseParts(y ?? "") ?? Array.Empty<int>();
        int len = Math.Max(a.Length, b.Length);
        for (int i = 0; i < len; i++)
        {
            int av = i < a.Length ? a[i] : 0;
            int bv = i < b.Length ? b[i] : 0;
            if (av != bv) return av.CompareTo(bv);
        }
        return string.CompareOrdinal(x, y);
    }
}
