using WingetAgent.Models;

namespace WingetAgent.Scoring;

/// <summary>
/// Deterministic safety score (0-100, higher = safer) from a base of 70, adjusted by
/// independent factors: how long the target version has been out, how big the version
/// jump is, the package category, publisher trust, and whether the installed version is
/// known. Every adjustment is recorded as a ScoreFactor so the report can explain "why".
/// Bands: >=75 Safe, >=50 Review, else Risky.
/// </summary>
public static class SafetyScorer
{
    public static SafetyScore Score(EnrichedUpdate u)
    {
        var s = new SafetyScore { Value = 70 };
        void Add(string name, int delta, string reason)
        {
            s.Factors.Add(new ScoreFactor { Name = name, Delta = delta, Reason = reason });
            s.Value += delta;
        }

        Add("baseline", 0, "Starting score 70");

        // Maturity of the target version.
        if (u.AgeDays is null) Add("age", -10, "Release date unknown");
        else if (u.AgeDays < 3) Add("age", -25, $"Released {u.AgeDays}d ago — very fresh");
        else if (u.AgeDays < 7) Add("age", -15, $"Released {u.AgeDays}d ago — fresh");
        else if (u.AgeDays < 14) Add("age", -8, $"Released {u.AgeDays}d ago");
        else if (u.AgeDays < 30) Add("age", 0, $"Released {u.AgeDays}d ago");
        else if (u.AgeDays < 90) Add("age", 10, $"Matured {u.AgeDays}d in the wild");
        else Add("age", 15, $"Mature release ({u.AgeDays}d)");

        // Size of the change.
        switch (u.Jump)
        {
            case JumpType.Patch: Add("versionJump", 10, "Patch-level update"); break;
            case JumpType.Minor: Add("versionJump", 0, "Minor version update"); break;
            case JumpType.Major: Add("versionJump", -20, "Major version jump — possible breaking changes"); break;
            case JumpType.Same: Add("versionJump", 0, "Same version components"); break;
            default: Add("versionJump", -8, "Version delta could not be determined"); break;
        }

        // What kind of software it is.
        switch (u.Category)
        {
            case Category.Driver: Add("category", -20, "Driver — higher risk to system stability"); break;
            case Category.System: Add("category", -15, "System / runtime component"); break;
            case Category.Developer: Add("category", -5, "Developer tooling"); break;
            case Category.Browser: Add("category", 5, "Browser — security updates matter"); break;
            default: Add("category", 0, "Regular application"); break;
        }

        // Who publishes it.
        Add("publisher",
            u.TrustedPublisher ? 10 : -5,
            u.TrustedPublisher ? $"Trusted publisher ({u.Publisher})" : $"Unrecognized publisher ({u.Publisher})");

        // Uncertainty about the current state.
        if (u.CurrentVersion.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            Add("currentVersion", -10, "Installed version unknown — cannot verify the delta");

        s.Value = Math.Clamp(s.Value, 0, 100);
        s.Band = s.Value >= 75 ? SafetyBand.Safe : s.Value >= 50 ? SafetyBand.Review : SafetyBand.Risky;
        return s;
    }
}
