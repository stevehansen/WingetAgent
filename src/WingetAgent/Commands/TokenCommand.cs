using System.ComponentModel;
using Spectre.Console;
using Spectre.Console.Cli;
using WingetAgent.GitHub;

namespace WingetAgent.Commands;

/// <summary>
/// token: report whether a GitHub enrichment token is configured (and from where), and
/// guide the user through creating a least-privilege one and storing it in user-secrets.
/// `--set` stores a token without needing the dotnet user-secrets CLI.
/// </summary>
public sealed class TokenCommand : Command<TokenCommand.Settings>
{
    public sealed class Settings : CommandSettings
    {
        [CommandOption("--set <TOKEN>")]
        [Description("Store a GitHub token in per-user .NET user-secrets (outside the repo and environment).")]
        public string? Set { get; set; }
    }

    public override int Execute(CommandContext context, Settings s)
    {
        if (!string.IsNullOrWhiteSpace(s.Set))
        {
            var path = GitHubToken.Save(s.Set.Trim());
            AnsiConsole.MarkupLine("[green]Token saved[/] to user-secrets:");
            AnsiConsole.MarkupLine($"  [blue]{Markup.Escape(path)}[/]");
            AnsiConsole.MarkupLine("It will be used automatically on the next [grey]scan[/].");
            return 0;
        }

        var (token, source) = GitHubToken.Resolve();
        if (token is not null)
        {
            AnsiConsole.MarkupLine($"GitHub enrichment auth: [green]configured[/] — source: {source}, {Mask(token)}.");
            AnsiConsole.MarkupLine("Enrichment uses the full ~5000 req/hr limit.");
            return 0;
        }

        AnsiConsole.MarkupLine("GitHub enrichment auth: [yellow]not configured[/] — capped at ~25 packages/run (60 req/hr).");
        AnsiConsole.WriteLine();
        var body = new Markup(
            "[bold]A least-privilege token is all you need[/] — no scopes = authenticated public read only.\n\n" +
            "[bold]1.[/] Create the token (leave every checkbox unchecked, set an expiry, Generate):\n" +
            $"    [blue]{Markup.Escape(GitHubToken.CreationUrl)}[/]\n\n" +
            "[bold]2.[/] Store it securely (per-user, never in the repo or global environment):\n" +
            "    [grey]wingetagent token --set ghp_your_token_here[/]\n\n" +
            "Alternatively set the [grey]GITHUB_TOKEN[/] environment variable (less private — visible to child processes).");
        AnsiConsole.Write(new Panel(body).Header(" Set up GitHub enrichment ").RoundedBorder());
        return 0;
    }

    static string Mask(string t) => t.Length <= 8 ? "****" : $"{t[..4]}…{t[^2..]}";
}
