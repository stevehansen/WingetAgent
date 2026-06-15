namespace WingetAgent.Models;

public enum Category { System, Driver, Developer, Browser, Application, Unknown }

public enum JumpType { Patch, Minor, Major, Same, Unknown }

public enum SafetyBand { Safe, Review, Risky }

/// <summary>One contributing reason to a safety score, with its point delta.</summary>
public sealed class ScoreFactor
{
    public string Name { get; set; } = "";
    public int Delta { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class SafetyScore
{
    public int Value { get; set; }
    public SafetyBand Band { get; set; }
    public List<ScoreFactor> Factors { get; set; } = new();
}

/// <summary>
/// A single upgradable package: the raw winget fields, everything the C# engine
/// derives/enriches about it, and its deterministic safety score.
/// </summary>
public sealed class EnrichedUpdate
{
    // From winget upgrade
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string CurrentVersion { get; set; } = "";
    public string AvailableVersion { get; set; } = "";
    public string Source { get; set; } = "";

    // Derived locally
    public string Publisher { get; set; } = "";
    public Category Category { get; set; } = Category.Unknown;
    public JumpType Jump { get; set; } = JumpType.Unknown;
    public bool TrustedPublisher { get; set; }

    // Enrichment from microsoft/winget-pkgs
    public DateTime? ReleaseDate { get; set; }
    public int? AgeDays { get; set; }
    public List<string> RecentVersions { get; set; } = new();
    public string? ManifestUrl { get; set; }
    public string? EnrichmentNote { get; set; }

    public SafetyScore Score { get; set; } = new();
}

/// <summary>The full result of one scan, persisted as updates.json.</summary>
public sealed class RunManifest
{
    public DateTime GeneratedOn { get; set; }
    public string Machine { get; set; } = "";
    public string WingetVersion { get; set; } = "";
    public bool Enriched { get; set; }
    public List<EnrichedUpdate> Updates { get; set; } = new();
}

/// <summary>
/// Claude's per-package judgment, written as annotations.json by the skill and
/// merged into the report/cmd by build-report. Recommendation: Approve | Review | Skip.
/// </summary>
public sealed class Annotation
{
    public string Id { get; set; } = "";
    public int? AdjustedScore { get; set; }
    public string Recommendation { get; set; } = "";
    public string Notes { get; set; } = "";
    public List<string> Sources { get; set; } = new();
}
