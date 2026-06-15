using Spectre.Console.Cli;
using WingetAgent.Commands;

var app = new CommandApp();
app.Configure(config =>
{
    config.SetApplicationName("wingetagent");
    config.AddCommand<ScanCommand>("scan")
        .WithDescription("Scan winget for upgrades, enrich, score, and write updates.json (+ baseline report & cmd).");
    config.AddCommand<BuildReportCommand>("build-report")
        .WithDescription("Render report.html and apply-updates.cmd from updates.json (+ optional annotations.json).");
    config.AddCommand<TokenCommand>("token")
        .WithDescription("Show or set up the GitHub token used to lift the enrichment rate limit.");
    config.AddCommand<OpenCommand>("open")
        .WithDescription("Open a run's report in the browser (or its folder with --folder). Defaults to the latest run.");
});
return await app.RunAsync(args);
