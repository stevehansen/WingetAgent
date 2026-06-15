using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using WingetAgent.Models;
using WingetAgent.Output;

namespace WingetAgent.Commands;

/// <summary>
/// build-report: re-render report.html + apply-updates.cmd from an existing run's
/// updates.json, merging Claude's annotations.json when present. This is the step the
/// skill runs after writing annotations.
/// </summary>
public sealed class BuildReportCommand : Command<BuildReportCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("-i|--input <DIR>")]
        [Description("Run directory containing updates.json.")]
        public string Input { get; set; } = "";

        [CommandOption("--annotations <FILE>")]
        [Description("Annotations JSON. Default: <input>/annotations.json when present.")]
        public string? Annotations { get; set; }

        public override ValidationResult Validate()
            => string.IsNullOrWhiteSpace(Input)
                ? ValidationResult.Error("--input <DIR> is required")
                : ValidationResult.Success();
    }

    public override int Execute(CommandContext context, Settings s)
    {
        var jsonPath = Path.Combine(s.Input, "updates.json");
        if (!File.Exists(jsonPath))
        {
            AnsiConsole.MarkupLine($"[red]updates.json not found in {Markup.Escape(s.Input)}[/]");
            return 1;
        }

        var run = JsonIo.Read<RunManifest>(jsonPath);
        if (run is null)
        {
            AnsiConsole.MarkupLine("[red]Failed to parse updates.json[/]");
            return 1;
        }

        var annPath = s.Annotations ?? Path.Combine(s.Input, "annotations.json");
        var annotations = new Dictionary<string, Annotation>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(annPath))
        {
            foreach (var a in JsonIo.Read<List<Annotation>>(annPath) ?? new())
                if (!string.IsNullOrWhiteSpace(a.Id)) annotations[a.Id] = a;
            AnsiConsole.MarkupLine($"Merged [green]{annotations.Count}[/] annotation(s).");
        }
        else
        {
            AnsiConsole.MarkupLine("[grey]No annotations.json — rendering baseline report.[/]");
        }

        ReportRenderer.Render(s.Input, run, annotations);
        AnsiConsole.MarkupLine($"Wrote report.html + apply-updates.cmd to [blue]{Path.GetFullPath(s.Input)}[/]");
        AnsiConsole.MarkupLine($"View it: [grey]wingetagent open -i \"{Markup.Escape(s.Input)}\"[/]");
        return 0;
    }
}
