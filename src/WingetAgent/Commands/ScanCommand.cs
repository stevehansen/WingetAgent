using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using WingetAgent.Baseline;
using WingetAgent.Enrichment;
using WingetAgent.GitHub;
using WingetAgent.Models;
using WingetAgent.Output;
using WingetAgent.Scoring;
using WingetAgent.Winget;

namespace WingetAgent.Commands;

/// <summary>
/// scan: enumerate winget upgrades, enrich + score them, and write updates.json plus a
/// baseline report.html / apply-updates.cmd into a dated run directory. Standalone-usable
/// (no Claude required); the skill layers annotations on top afterwards.
/// </summary>
public sealed class ScanCommand : AsyncCommand<ScanCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-o|--output <DIR>")]
        [Description("Output run directory. Default: runs/<timestamp>.")]
        public string? Output { get; set; }

        [CommandOption("--no-enrich")]
        [Description("Skip GitHub winget-pkgs enrichment (no release dates / version history).")]
        public bool NoEnrich { get; set; }

        [CommandOption("--max-enrich <N>")]
        [Description("Max packages to enrich. 0 = auto (all with GITHUB_TOKEN, else 25).")]
        public int MaxEnrich { get; set; }

        [CommandOption("--no-include-unknown")]
        [Description("Exclude packages whose installed version winget cannot determine.")]
        public bool NoIncludeUnknown { get; set; }
    }

    public override async Task<int> ExecuteAsync(CommandContext context, Settings s)
    {
        var dir = s.Output ?? RunPaths.NewRunDir();
        Directory.CreateDirectory(dir);

        var run = new RunManifest
        {
            GeneratedOn = DateTime.Now,
            Machine = Environment.MachineName,
            WingetVersion = WingetClient.GetVersion(),
        };

        List<EnrichedUpdate> ups = new();
        AnsiConsole.Status().Start("Querying winget for upgrades...",
            _ => ups = WingetClient.GetUpgrades(!s.NoIncludeUnknown));

        if (ups.Count == 0)
        {
            run.Updates = ups;
            JsonIo.Write(Path.Combine(dir, "updates.json"), run);
            ReportRenderer.Render(dir, run, Empty);
            AnsiConsole.MarkupLine("[green]System is up to date — no upgrades found.[/]");
            AnsiConsole.MarkupLine($"Run directory: [blue]{Path.GetFullPath(dir)}[/]");
            return 0;
        }

        foreach (var u in ups)
        {
            var (cat, pub, trusted) = CategoryClassifier.Classify(u.Id, u.Name);
            u.Category = cat;
            u.Publisher = pub;
            u.TrustedPublisher = trusted;
            u.Jump = VersionAnalyzer.Compare(u.CurrentVersion, u.AvailableVersion);
        }

        if (!s.NoEnrich)
        {
            var (token, tokenSource) = GitHubToken.Resolve();
            var client = new WingetPkgsClient(token);
            int budget = s.MaxEnrich > 0 ? s.MaxEnrich : (client.HasToken ? int.MaxValue : 25);
            run.Enriched = true;
            if (client.HasToken)
                AnsiConsole.MarkupLine($"[grey]GitHub enrichment: authenticated ({tokenSource}).[/]");

            await AnsiConsole.Progress().StartAsync(async ctx =>
            {
                var task = ctx.AddTask("Enriching from winget-pkgs", maxValue: ups.Count);
                int done = 0;
                foreach (var u in ups)
                {
                    if (done < budget && !client.RateLimited)
                        await client.EnrichAsync(u);
                    else
                        u.EnrichmentNote ??= client.RateLimited
                            ? "skipped (GitHub rate limit)"
                            : "skipped (enrichment budget reached)";
                    done++;
                    task.Increment(1);
                }
            });

            if (client.RateLimited)
                AnsiConsole.MarkupLine("[yellow]GitHub rate limit hit. Run [grey]wingetagent token[/] to set up a token for full enrichment.[/]");
            else if (!client.HasToken && ups.Count > 25)
                AnsiConsole.MarkupLine("[yellow]Enriched first 25 packages (unauthenticated). Run [grey]wingetagent token[/] to enrich all.[/]");
        }

        run.Updates = ups;
        BaselineComparer.Apply(run, dir);              // compare vs the previous run before scoring

        foreach (var u in ups) u.Score = SafetyScorer.Score(u);
        run.Updates = ups.OrderBy(u => u.Score.Value).ToList(); // riskiest first

        JsonIo.Write(Path.Combine(dir, "updates.json"), run);
        ReportRenderer.Render(dir, run, Empty);

        PrintSummary(run);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("Wrote [green]updates.json[/], [green]report.html[/], [green]apply-updates.cmd[/].");
        AnsiConsole.MarkupLine($"Run directory: [blue]{Path.GetFullPath(dir)}[/]");
        AnsiConsole.MarkupLine("View it: [grey]wingetagent open[/]  (or [grey]wingetagent open --folder[/])");
        return 0;
    }

    static readonly IReadOnlyDictionary<string, Annotation> Empty = new Dictionary<string, Annotation>();

    static void PrintSummary(RunManifest run)
    {
        var table = new Table().Border(TableBorder.Rounded).Expand();
        table.AddColumns("Score", "Band", "Name", "Id", "Update", "Cat", "Age");
        foreach (var u in run.Updates)
        {
            var color = u.Score.Band switch
            {
                SafetyBand.Safe => "green",
                SafetyBand.Review => "yellow",
                _ => "red",
            };
            table.AddRow(
                $"[{color}]{u.Score.Value}[/]",
                $"[{color}]{u.Score.Band}[/]",
                Markup.Escape(Trunc(u.Name, 26)),
                Markup.Escape(Trunc(u.Id, 30)),
                Markup.Escape(Trunc($"{u.CurrentVersion} -> {u.AvailableVersion}", 28)),
                u.Category.ToString(),
                u.AgeDays?.ToString() ?? "?");
        }
        AnsiConsole.Write(table);

        int safe = run.Updates.Count(u => u.Score.Band == SafetyBand.Safe);
        int review = run.Updates.Count(u => u.Score.Band == SafetyBand.Review);
        int risky = run.Updates.Count(u => u.Score.Band == SafetyBand.Risky);
        AnsiConsole.MarkupLine($"[green]{safe} Safe[/]   [yellow]{review} Review[/]   [red]{risky} Risky[/]   (total {run.Updates.Count})");
    }

    static string Trunc(string s, int n) => s.Length <= n ? s : string.Concat(s.AsSpan(0, n - 1), "…");
}
