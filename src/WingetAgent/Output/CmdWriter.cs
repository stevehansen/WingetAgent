using System.Text;
using WingetAgent.Models;

namespace WingetAgent.Output;

/// <summary>
/// Writes the dated, self-elevating batch file that actually applies the updates.
/// Every package is pinned to its exact target version and installed silently. The
/// file IS the approval record: SAFE/REVIEW items are active, while RISKY items (or
/// anything Claude marked "Skip") are emitted as commented-out REM lines so the
/// default is conservative. The operator curates by toggling "REM " on/off, then runs
/// the file — which elevates itself to administrator on launch.
///
/// ASCII-only on purpose: batch consoles default to an OEM code page, so no em dashes
/// or arrows here.
/// </summary>
public static class CmdWriter
{
    public static void Write(string path, RunManifest run, IReadOnlyDictionary<string, Annotation> annotations)
    {
        var sb = new StringBuilder();
        sb.AppendLine("@echo off");
        sb.AppendLine("setlocal EnableExtensions");
        sb.AppendLine($":: WingetAgent apply script - generated {run.GeneratedOn:yyyy-MM-dd HH:mm:ss} on {run.Machine}");
        sb.AppendLine(":: Review report.html first. RISKY items (and anything flagged 'Skip') are commented out.");
        sb.AppendLine(":: To enable a line, delete its leading 'REM '. To skip a line, add 'REM ' in front.");
        sb.AppendLine();
        sb.AppendLine(":: ---- self-elevate to administrator ----");
        sb.AppendLine("net session >nul 2>&1");
        sb.AppendLine("if %errorlevel% neq 0 (");
        sb.AppendLine("  echo Requesting administrator privileges...");
        sb.AppendLine("  powershell -NoProfile -Command \"Start-Process -FilePath '%~f0' -Verb RunAs\"");
        sb.AppendLine("  exit /b");
        sb.AppendLine(")");
        sb.AppendLine();
        sb.AppendLine("echo WingetAgent - applying approved updates");
        sb.AppendLine("echo.");
        sb.AppendLine();

        foreach (var u in run.Updates.OrderByDescending(x => x.Score.Value))
        {
            annotations.TryGetValue(u.Id, out var a);
            var rec = a?.Recommendation ?? "";
            bool claudeSkip = rec.Equals("Skip", StringComparison.OrdinalIgnoreCase);
            bool claudeApprove = rec.Equals("Approve", StringComparison.OrdinalIgnoreCase);
            bool skip = (u.Score.Band == SafetyBand.Risky || claudeSkip) && !claudeApprove;

            int score = a?.AdjustedScore ?? u.Score.Value;
            var age = u.AgeDays?.ToString() ?? "?";
            sb.AppendLine($":: [{u.Score.Band.ToString().ToUpperInvariant()} {score}] {u.Id}  {u.CurrentVersion} -> {u.AvailableVersion}  ({u.Category}, {u.Jump}, age {age}d)");
            if (!string.IsNullOrWhiteSpace(a?.Notes))
                sb.AppendLine($"::   note: {OneLine(a!.Notes)}");

            var cmd = $"winget install --id \"{u.Id}\" --version \"{u.AvailableVersion}\" --exact " +
                      "--silent --accept-package-agreements --accept-source-agreements --disable-interactivity";
            if (u.Source.Equals("msstore", StringComparison.OrdinalIgnoreCase))
                cmd += " --source msstore";

            // Defense against command injection (STRIDE T1): an Id/version from a hostile
            // source could carry batch metacharacters. Both are quoted; anything that could
            // still break out of quotes is blocked rather than executed.
            if (!IsSafeArg(u.Id) || !IsSafeArg(u.AvailableVersion))
            {
                sb.AppendLine($"REM {cmd}   :: BLOCKED (unsafe characters in id/version)");
            }
            else if (skip)
            {
                var why = claudeSkip ? "Claude: skip" : "risky band";
                sb.AppendLine($"REM {cmd}   :: SKIPPED ({why})");
            }
            else
            {
                sb.AppendLine(cmd);
            }
            sb.AppendLine();
        }

        sb.AppendLine("echo.");
        sb.AppendLine("echo Done. Review the output above for any failures.");
        sb.AppendLine("pause");

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    static string OneLine(string s) => s.Replace("\r", " ").Replace("\n", " ").Trim();

    // Even inside double quotes a batch line can be subverted by " (closes the quote),
    // % (variable expansion), or a newline. winget Ids/versions never legitimately
    // contain these, so their presence means tampering — refuse to emit an active line.
    static bool IsSafeArg(string s)
        => !string.IsNullOrEmpty(s) && s.IndexOfAny(new[] { '"', '%', '\r', '\n' }) < 0;
}
