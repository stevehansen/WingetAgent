using WingetAgent.Models;
using WingetAgent.Output;

namespace WingetAgent.Baseline;

/// <summary>
/// Compares the current run against the previous one, so a weekly cadence can answer
/// "what changed since last time?". For each Update it sets a <see cref="BaselineChange"/>
/// (New / Updated / Pending / NoBaseline) and carries a stable <c>FirstSeen</c> forward,
/// from which <c>PendingDays</c> (maturity) is derived. It also records the packages that
/// were pending last run but are gone now (likely applied).
///
/// All of this is informational + a light scoring input; with no prior run everything is
/// NoBaseline and nothing is penalized.
/// </summary>
public static class BaselineComparer
{
    /// <param name="run">The current run (Updates already enriched, not yet scored).</param>
    /// <param name="currentDir">The directory this run is being written to (excluded from the search).</param>
    public static void Apply(RunManifest run, string currentDir)
    {
        var now = run.GeneratedOn;
        foreach (var u in run.Updates)
        {
            u.Baseline = BaselineChange.NoBaseline;
            u.FirstSeen = now;
            u.PendingDays = 0;
        }

        var priorDir = RunPaths.PreviousRun(currentDir);
        if (priorDir is null) return;

        var prior = JsonIo.Read<RunManifest>(Path.Combine(priorDir, "updates.json"));
        if (prior is null) return;

        run.BaselineDate = prior.GeneratedOn;
        var priorById = prior.Updates
            .GroupBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var u in run.Updates)
        {
            if (!priorById.TryGetValue(u.Id, out var p))
            {
                u.Baseline = BaselineChange.New;          // not offered last run
            }
            else if (string.Equals(p.AvailableVersion, u.AvailableVersion, StringComparison.OrdinalIgnoreCase))
            {
                u.Baseline = BaselineChange.Pending;       // same target carried over → maturing
                u.FirstSeen = p.FirstSeen ?? prior.GeneratedOn;
                u.PendingDays = Math.Max(0, (int)(now.Date - u.FirstSeen.Value.Date).TotalDays);
            }
            else
            {
                u.Baseline = BaselineChange.Updated;       // target moved since last run → fresh again
            }
        }

        var currentIds = run.Updates.Select(u => u.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        run.ResolvedSinceBaseline = prior.Updates
            .Where(p => !currentIds.Contains(p.Id))
            .Select(p => new ResolvedItem { Id = p.Id, Name = p.Name, PreviousVersion = p.AvailableVersion })
            .ToList();
    }
}
