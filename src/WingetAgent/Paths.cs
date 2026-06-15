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
}
