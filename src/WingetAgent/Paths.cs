namespace WingetAgent;

/// <summary>
/// Where runs live. Default is per-user under the profile (~/.winget-agent/runs/),
/// NOT the repo — so the shared/MIT repository stays clean while each machine keeps
/// its own dated update history. Callers can still override with `scan -o`.
/// </summary>
public static class RunPaths
{
    public static string Base =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".winget-agent", "runs");

    public static string NewRunDir() =>
        Path.Combine(Base, DateTime.Now.ToString("yyyy-MM-dd_HHmmss"));

    /// <summary>The most recent run directory, or null if none exist. (Timestamp names sort chronologically.)</summary>
    public static string? Latest() =>
        Directory.Exists(Base)
            ? Directory.GetDirectories(Base).OrderByDescending(d => d, StringComparer.Ordinal).FirstOrDefault()
            : null;

    /// <summary>
    /// The most recent prior run that holds an updates.json among the siblings of
    /// <paramref name="currentDir"/> (i.e. in the same parent folder), excluding the current
    /// run itself. This is the baseline a new run compares against — so a `-o` run baselines
    /// against other runs in that same location, and a default run against ~/.winget-agent/runs.
    /// </summary>
    public static string? PreviousRun(string currentDir)
    {
        var full = Path.GetFullPath(currentDir);
        var parent = Path.GetDirectoryName(full);
        if (parent is null || !Directory.Exists(parent)) return null;
        return Directory.GetDirectories(parent)
            .Where(d => !string.Equals(Path.GetFullPath(d), full, StringComparison.OrdinalIgnoreCase))
            .Where(d => File.Exists(Path.Combine(d, "updates.json")))
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
