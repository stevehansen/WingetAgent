using WingetAgent.Models;

namespace WingetAgent.Output;

/// <summary>Renders the two human/operator-facing artifacts from a run (+ optional annotations).</summary>
public static class ReportRenderer
{
    public static void Render(string dir, RunManifest run, IReadOnlyDictionary<string, Annotation> annotations)
    {
        HtmlReportWriter.Write(Path.Combine(dir, "report.html"), run, annotations);
        CmdWriter.Write(Path.Combine(dir, "apply-updates.cmd"), run, annotations);
    }
}
