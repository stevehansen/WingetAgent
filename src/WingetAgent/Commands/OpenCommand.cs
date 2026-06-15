using System.ComponentModel;
using System.Diagnostics;
using Spectre.Console;
using Spectre.Console.Cli;

namespace WingetAgent.Commands;

/// <summary>
/// open: open a run's Report in the default browser (or the run folder in Explorer with
/// --folder). Defaults to the most recent run, since runs now live under the user profile
/// rather than the repo.
/// </summary>
public sealed class OpenCommand : Command<OpenCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-i|--input <DIR>")]
        [Description("Run directory to open. Default: the most recent run.")]
        public string? Input { get; set; }

        [CommandOption("--folder")]
        [Description("Open the run folder in the file explorer instead of the report.")]
        public bool Folder { get; set; }
    }

    public override int Execute(CommandContext context, Settings s)
    {
        var dir = s.Input ?? RunPaths.Latest();
        if (string.IsNullOrEmpty(dir))
        {
            AnsiConsole.MarkupLine("[yellow]No runs found.[/] Run [grey]wingetagent scan[/] first.");
            return 1;
        }
        if (!Directory.Exists(dir))
        {
            AnsiConsole.MarkupLine($"[red]Run directory not found:[/] {Markup.Escape(dir)}");
            return 1;
        }

        var target = s.Folder ? dir : Path.Combine(dir, "report.html");
        if (!s.Folder && !File.Exists(target))
        {
            AnsiConsole.MarkupLine($"[red]report.html not found in {Markup.Escape(dir)}[/]");
            return 1;
        }

        Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        AnsiConsole.MarkupLine($"Opened [blue]{Markup.Escape(target)}[/]");
        return 0;
    }
}
