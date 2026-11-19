using System.Text.Json;
using WingetAgent.Models;
using WingetAgent.Output;

namespace WingetAgent.Policy;

/// <summary>
/// Attaches the user's Package Policies (~/.winget-agent/policies.json, a JSON array of
/// PackagePolicy) to a Run's Updates. Lives beside the runs, outside the repo, because the
/// rules are this machine's choices. A missing file means no policies; a malformed one
/// fails loudly rather than silently dropping an Exclude.
/// </summary>
public static class PackagePolicies
{
    public static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".winget-agent", "policies.json");

    /// <summary>
    /// Sets each Update's Policy from the current file (clearing stale ones), so build-report
    /// picks up policy edits without a new Scan. Returns how many Updates have a policy.
    /// </summary>
    public static int Apply(RunManifest run)
    {
        var byId = Load();
        foreach (var u in run.Updates)
            u.Policy = byId.GetValueOrDefault(u.Id);
        return run.Updates.Count(u => u.Policy is not null);
    }

    static Dictionary<string, PackagePolicy> Load()
    {
        var byId = new Dictionary<string, PackagePolicy>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(FilePath)) return byId;

        List<PackagePolicy>? policies;
        try { policies = JsonIo.Read<List<PackagePolicy>>(FilePath); }
        catch (JsonException e) { throw new InvalidDataException($"{FilePath}: {e.Message}", e); }

        foreach (var p in policies ?? new())
            if (!string.IsNullOrWhiteSpace(p.Id)) byId[p.Id] = p;
        return byId;
    }
}
