using System.Diagnostics;
using System.Text;
using WingetAgent.Models;

namespace WingetAgent.Winget;

/// <summary>
/// Enumerates available upgrades by shelling out to the winget CLI and parsing its
/// fixed-width table using the header column offsets. Returns EnrichedUpdate objects
/// with only the raw winget fields populated; later stages fill in the rest.
/// (A COM-API backend, Microsoft.Management.Deployment, would be more robust and is
/// the natural hardening path — kept out of v1 to stay small.)
/// </summary>
public static class WingetClient
{
    public static string GetVersion()
    {
        try { return RunWinget("--version").Trim(); }
        catch { return "unknown"; }
    }

    public static List<EnrichedUpdate> GetUpgrades(bool includeUnknown)
    {
        var args = "upgrade --accept-source-agreements" + (includeUnknown ? " --include-unknown" : "");
        return Parse(RunWinget(args));
    }

    internal static List<EnrichedUpdate> Parse(string output)
    {
        var result = new List<EnrichedUpdate>();
        var lines = output.Replace("\r", "").Split('\n');

        int headerIdx = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            var l = lines[i];
            if (l.Contains("Name") && l.Contains("Id") && l.Contains("Version")
                && l.Contains("Available") && l.Contains("Source"))
            {
                headerIdx = i;
                break;
            }
        }
        if (headerIdx < 0) return result;

        var header = lines[headerIdx];
        int idCol = header.IndexOf("Id", StringComparison.Ordinal);
        int verCol = header.IndexOf("Version", StringComparison.Ordinal);
        int availCol = header.IndexOf("Available", StringComparison.Ordinal);
        int srcCol = header.IndexOf("Source", StringComparison.Ordinal);
        if (idCol < 0 || verCol < 0 || availCol < 0 || srcCol < 0) return result;

        for (int i = headerIdx + 1; i < lines.Length; i++)
        {
            var raw = lines[i];
            if (raw.Length == 0) break;                          // blank line ends the table
            if (raw.TrimStart().StartsWith("---")) continue;     // dashed separator
            if (raw.Length < srcCol) break;                      // too short -> summary line

            string Slice(int start, int end)
                => start >= raw.Length ? "" : raw[start..Math.Min(end, raw.Length)].Trim();

            var id = Slice(idCol, verCol);
            if (string.IsNullOrWhiteSpace(id) || id.Contains(' ')) break; // not a real row

            result.Add(new EnrichedUpdate
            {
                Name = Slice(0, idCol),
                Id = id,
                CurrentVersion = Slice(verCol, availCol),
                AvailableVersion = Slice(availCol, srcCol),
                Source = Slice(srcCol, raw.Length),
            });
        }
        return result;
    }

    static string RunWinget(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "winget",
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
        };
        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start winget. Is App Installer present?");
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        return stdout;
    }
}
