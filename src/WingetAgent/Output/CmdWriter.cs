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
        sb.AppendLine("set \"FAIL=0\"");
        sb.AppendLine();

        var planned = run.Updates
            .OrderByDescending(x => x.Score.Value)
            .Select(u =>
            {
                annotations.TryGetValue(u.Id, out var a);
                var rec = a?.Recommendation ?? "";
                bool claudeSkip = rec.Equals("Skip", StringComparison.OrdinalIgnoreCase);
                bool claudeApprove = rec.Equals("Approve", StringComparison.OrdinalIgnoreCase);
                bool blocked = !IsSafeArg(u.Id) || !IsSafeArg(u.AvailableVersion);
                bool skip = (u.Score.Band == SafetyBand.Risky || claudeSkip) && !claudeApprove;
                return (u, a, claudeSkip, blocked, skip);
            })
            .ToList();

        int activeCount = planned.Count(p => !p.blocked && !p.skip);
        if (activeCount == 0)
            sb.AppendLine("echo (No active updates - everything was skipped or blocked. Edit this file to enable lines.)");

        int active = 0;
        foreach (var (u, a, claudeSkip, blocked, skip) in planned)
        {
            // The Band belongs to the engine's Safety Score and is what gates the line, so
            // keep the pair together; Claude's Adjusted Score and Recommendation are shown
            // alongside, never mixed into it.
            var label = $"{u.Score.Band.ToString().ToUpperInvariant()} {u.Score.Value}";
            if (a?.AdjustedScore is int adjusted) label += $", adjusted {adjusted}";
            if (!string.IsNullOrWhiteSpace(a?.Recommendation)) label += $", Claude: {a!.Recommendation}";
            var age = u.AgeDays?.ToString() ?? "?";
            var since = u.Baseline switch
            {
                BaselineChange.New => ", new since last run",
                BaselineChange.Updated => ", target changed since last run",
                BaselineChange.Pending => $", pending {u.PendingDays ?? 0}d",
                _ => "",
            };
            sb.AppendLine($":: [{label}] {u.Id}  {u.CurrentVersion} -> {u.AvailableVersion}  ({u.Category}, {u.Jump}, age {age}d{since})");
            if (!string.IsNullOrWhiteSpace(a?.Notes))
                sb.AppendLine($"::   note: {OneLine(a!.Notes)}");

            var cmd = $"winget install --id \"{u.Id}\" --version \"{u.AvailableVersion}\" --exact " +
                      "--silent --accept-package-agreements --accept-source-agreements --disable-interactivity";
            if (u.Source.Equals("msstore", StringComparison.OrdinalIgnoreCase))
                cmd += " --source msstore";

            // Defense against command injection (STRIDE T1): an Id/version from a hostile
            // source could carry batch metacharacters. Both are quoted; anything that could
            // still break out of quotes is blocked rather than executed.
            if (blocked)
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
                // Announce each enabled install and report its result. winget prints no
                // package banner when an upgrade fails (e.g. "No applicable installer
                // found"), so without this the console is an unlabeled wall and a failure
                // can't be attributed. Failures are tallied for the end-of-run summary.
                active++;
                sb.AppendLine("echo.");
                sb.AppendLine($"echo [{active}/{activeCount}] {EchoSafe(u.Id)}  {EchoSafe(u.CurrentVersion)} -^> {EchoSafe(u.AvailableVersion)}");
                sb.AppendLine(cmd);
                sb.AppendLine($"if errorlevel 1 (set /a FAIL+=1& echo   ** FAILED: {EchoSafe(u.Id)} ^(see output above^)) else (echo   OK: {EchoSafe(u.Id)})");
            }
            sb.AppendLine();
        }

        sb.AppendLine("echo.");
        sb.AppendLine("echo ----------------------------------------");
        sb.AppendLine("if %FAIL% gtr 0 (echo Completed with %FAIL% failure^(s^). Review the output above.) else (echo All enabled updates applied successfully.)");
        sb.AppendLine();

        // Installers that replace a shell extension (TeraCopy, WinSCP, ...) close Explorer
        // through Restart Manager, which then refuses to restart it from this elevated
        // script ("Application SID does not match Conductor SID"). Explorer can be left
        // running without a taskbar (it lingers while a file copy is in progress), or not
        // running at all. Offer the restart rather than force it: the kill would abort such
        // a copy. An Explorer started from an elevated process de-elevates itself, and
        // AutoRestartShell may already have relaunched it after the kill, hence the check.
        // The window name is passed as IntPtr.Zero because PowerShell turns $null into ""
        // for string parameters, which would only match windows with an empty title.
        sb.AppendLine(":: ---- Explorer check: an installer may have closed it without restarting it ----");
        sb.AppendLine("powershell -NoProfile -Command \"$w = Add-Type -Name Win -Namespace WingetAgent -PassThru -MemberDefinition '[DllImport(\\\"user32.dll\\\")] public static extern System.IntPtr FindWindow(string c, System.IntPtr n);'; if ($w::FindWindow('Shell_TrayWnd', [System.IntPtr]::Zero) -eq [System.IntPtr]::Zero) { exit 1 }\"");
        sb.AppendLine("if errorlevel 1 (");
        sb.AppendLine("  echo.");
        sb.AppendLine("  echo The Windows taskbar is missing - an installer closed Explorer and it was not restarted.");
        sb.AppendLine("  echo A file copy still running in Explorer would be aborted - let it finish first.");
        sb.AppendLine("  choice /c YN /m \"Restart Windows Explorer now\"");
        sb.AppendLine("  if not errorlevel 2 (");
        sb.AppendLine("    taskkill /f /im explorer.exe >nul 2>&1");
        sb.AppendLine("    timeout /t 3 /nobreak >nul");
        sb.AppendLine("    tasklist /fi \"imagename eq explorer.exe\" | find /i \"explorer.exe\" >nul || start \"\" explorer.exe");
        sb.AppendLine("  )");
        sb.AppendLine(")");
        sb.AppendLine("pause");

        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    static string OneLine(string s) => s.Replace("\r", " ").Replace("\n", " ").Trim();

    // The progress banners echo a package id/version unquoted, so escape the batch
    // metacharacters that would otherwise alter control flow (& chains, < > | redirect,
    // ^ escapes, ( ) group inside the if-blocks). IsSafeArg already excludes " % CR LF
    // from active lines; this covers the rest so a banner can never run code.
    static string EchoSafe(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (c is '^' or '&' or '<' or '>' or '|' or '(' or ')') sb.Append('^');
            sb.Append(c);
        }
        return sb.ToString();
    }

    // Even inside double quotes a batch line can be subverted by " (closes the quote),
    // % (variable expansion), or a newline. winget Ids/versions never legitimately
    // contain these, so their presence means tampering — refuse to emit an active line.
    static bool IsSafeArg(string s)
        => !string.IsNullOrEmpty(s) && s.IndexOfAny(new[] { '"', '%', '\r', '\n' }) < 0;
}
