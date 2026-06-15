using System.Net;
using System.Text;
using WingetAgent.Models;

namespace WingetAgent.Output;

/// <summary>
/// Renders a single self-contained HTML dashboard for a run: a summary header with
/// band counts and filter buttons, then one expandable row per update showing the
/// score factor breakdown, version history, the exact install command, and any notes
/// Claude added via annotations. No external assets — CSS and JS are inlined.
/// </summary>
public static class HtmlReportWriter
{
    public static void Write(string path, RunManifest run, IReadOnlyDictionary<string, Annotation> annotations)
    {
        var b = new StringBuilder();
        int safe = run.Updates.Count(u => u.Score.Band == SafetyBand.Safe);
        int review = run.Updates.Count(u => u.Score.Band == SafetyBand.Review);
        int risky = run.Updates.Count(u => u.Score.Band == SafetyBand.Risky);

        b.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        b.Append("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        b.Append($"<title>WingetAgent report — {E(run.GeneratedOn.ToString("yyyy-MM-dd HH:mm"))}</title>");
        b.Append("<style>").Append(Css).Append("</style></head><body>");

        // Header
        b.Append("<header><h1>WingetAgent — Update Safety Report</h1>");
        b.Append("<div class=\"meta\">")
         .Append(E(run.Machine)).Append(" &middot; winget ").Append(E(run.WingetVersion))
         .Append(" &middot; ").Append(E(run.GeneratedOn.ToString("yyyy-MM-dd HH:mm:ss")))
         .Append(" &middot; ").Append(run.Updates.Count).Append(" update(s)")
         .Append(run.Enriched ? "" : " &middot; <span class=\"warn\">not enriched</span>")
         .Append("</div>");
        b.Append("<div class=\"bands\">");
        b.Append($"<button class=\"fbtn active\" onclick=\"flt(event,'all')\">All {run.Updates.Count}</button>");
        b.Append($"<button class=\"fbtn pill-safe\" onclick=\"flt(event,'safe')\">Safe {safe}</button>");
        b.Append($"<button class=\"fbtn pill-review\" onclick=\"flt(event,'review')\">Review {review}</button>");
        b.Append($"<button class=\"fbtn pill-risky\" onclick=\"flt(event,'risky')\">Risky {risky}</button>");
        b.Append("</div></header>");

        if (run.Updates.Count == 0)
        {
            b.Append("<p class=\"empty\">System is up to date — no upgrades found.</p>");
            b.Append("</body></html>");
            File.WriteAllText(path, b.ToString(), new UTF8Encoding(false));
            return;
        }

        // Table
        b.Append("<table><thead><tr>");
        foreach (var h in new[] { "", "Name", "Id", "Update", "Category", "Age", "Jump", "Score", "Recommendation" })
            b.Append("<th>").Append(h).Append("</th>");
        b.Append("</tr></thead><tbody>");

        foreach (var u in run.Updates) // already sorted riskiest-first by the scan
        {
            annotations.TryGetValue(u.Id, out var a);
            var band = u.Score.Band.ToString().ToLowerInvariant();
            int score = a?.AdjustedScore ?? u.Score.Value;
            var rec = string.IsNullOrWhiteSpace(a?.Recommendation) ? "—" : a!.Recommendation;
            var age = u.AgeDays is null ? "?" : u.AgeDays + "d";

            b.Append($"<tr class=\"row band-{band}\" onclick=\"tog(this)\">");
            b.Append("<td class=\"exp\">&#9656;</td>");
            b.Append("<td>").Append(E(u.Name)).Append("</td>");
            b.Append("<td class=\"mono\">").Append(E(u.Id)).Append("</td>");
            b.Append("<td class=\"mono\">").Append(E(u.CurrentVersion)).Append(" &rarr; ").Append(E(u.AvailableVersion)).Append("</td>");
            b.Append("<td>").Append(u.Category).Append("</td>");
            b.Append("<td>").Append(age).Append("</td>");
            b.Append("<td>").Append(u.Jump).Append("</td>");
            b.Append($"<td><span class=\"score band-{band}\">{score}</span></td>");
            b.Append("<td>").Append(E(rec)).Append("</td>");
            b.Append("</tr>");

            // Detail
            b.Append($"<tr class=\"detail band-{band}\"><td></td><td colspan=\"8\"><div class=\"detail-box\">");

            b.Append("<div class=\"col\"><h3>Score factors</h3><ul class=\"factors\">");
            foreach (var f in u.Score.Factors)
            {
                var cls = f.Delta > 0 ? "pos" : f.Delta < 0 ? "neg" : "zero";
                var sign = f.Delta > 0 ? "+" : "";
                b.Append($"<li><span class=\"delta {cls}\">{sign}{f.Delta}</span> <b>{E(f.Name)}</b> — {E(f.Reason)}</li>");
            }
            b.Append("</ul></div>");

            b.Append("<div class=\"col\"><h3>Details</h3><ul class=\"kv\">");
            b.Append($"<li><b>Publisher</b>: {E(u.Publisher)}{(u.TrustedPublisher ? " (trusted)" : "")}</li>");
            b.Append($"<li><b>Source</b>: {E(u.Source)}</li>");
            b.Append($"<li><b>Release date</b>: {(u.ReleaseDate is null ? "unknown" : E(u.ReleaseDate.Value.ToString("yyyy-MM-dd")))}</li>");
            if (u.RecentVersions.Count > 0)
                b.Append($"<li><b>Recent versions</b>: <span class=\"mono\">{E(string.Join(", ", u.RecentVersions))}</span></li>");
            if (!string.IsNullOrWhiteSpace(u.EnrichmentNote))
                b.Append($"<li><b>Note</b>: {E(u.EnrichmentNote)}</li>");
            if (u.ManifestUrl is not null)
                b.Append($"<li><a href=\"{E(u.ManifestUrl)}\" target=\"_blank\" rel=\"noopener\">winget-pkgs manifest &#8599;</a></li>");
            b.Append("</ul>");

            if (a is not null && (!string.IsNullOrWhiteSpace(a.Notes) || a.Sources.Count > 0))
            {
                b.Append("<h3>Claude's review</h3>");
                if (!string.IsNullOrWhiteSpace(a.Notes))
                    b.Append("<p class=\"notes\">").Append(E(a.Notes)).Append("</p>");
                if (a.Sources.Count > 0)
                {
                    b.Append("<ul class=\"sources\">");
                    foreach (var src in a.Sources)
                    {
                        // Only hyperlink http(s) sources; render anything else (e.g. a
                        // javascript: URI) as inert text to avoid script injection (STRIDE I2).
                        if (IsHttpUrl(src))
                            b.Append($"<li><a href=\"{E(src)}\" target=\"_blank\" rel=\"noopener noreferrer\">{E(src)}</a></li>");
                        else
                            b.Append($"<li>{E(src)}</li>");
                    }
                    b.Append("</ul>");
                }
            }
            b.Append("</div>");

            var cmd = $"winget install --id \"{u.Id}\" --version \"{u.AvailableVersion}\" --exact --silent --accept-package-agreements --accept-source-agreements";
            b.Append("<div class=\"col full\"><h3>Install command</h3><pre class=\"cmd\">").Append(E(cmd)).Append("</pre></div>");

            b.Append("</div></td></tr>");
        }

        b.Append("</tbody></table>");
        b.Append("<footer>Generated by WingetAgent. Review this report, then run <code>apply-updates.cmd</code> as administrator. Lines for risky items are commented out in that file by default.</footer>");
        b.Append("<script>").Append(Js).Append("</script>");
        b.Append("</body></html>");

        File.WriteAllText(path, b.ToString(), new UTF8Encoding(false));
    }

    static string E(string s) => WebUtility.HtmlEncode(s);

    static bool IsHttpUrl(string s)
        => Uri.TryCreate(s, UriKind.Absolute, out var u)
           && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    const string Css = @"
:root{--safe:#1a7f37;--review:#9a6700;--risky:#cf222e;--bg:#f6f8fa;--line:#d0d7de;}
*{box-sizing:border-box}
body{font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;margin:0;color:#1f2328;background:#fff}
header{padding:20px 28px;border-bottom:1px solid var(--line);background:var(--bg)}
h1{margin:0 0 6px;font-size:20px}
.meta{color:#57606a;font-size:13px}
.warn{color:var(--risky);font-weight:600}
.bands{margin-top:12px;display:flex;gap:8px;flex-wrap:wrap}
.fbtn{cursor:pointer;border:1px solid var(--line);background:#fff;border-radius:20px;padding:5px 14px;font-size:13px;font-weight:600}
.fbtn.active{outline:2px solid #0969da}
.pill-safe{color:var(--safe)}.pill-review{color:var(--review)}.pill-risky{color:var(--risky)}
table{width:100%;border-collapse:collapse;font-size:13px}
thead th{text-align:left;padding:10px 12px;border-bottom:2px solid var(--line);color:#57606a;font-size:12px;text-transform:uppercase;letter-spacing:.03em}
tr.row{cursor:pointer;border-bottom:1px solid #eaeef2}
tr.row:hover{background:var(--bg)}
tr.row td{padding:9px 12px;vertical-align:middle}
td.exp{color:#57606a;transition:transform .1s}
tr.row.open td.exp{transform:rotate(90deg)}
.mono{font-family:ui-monospace,SFMono-Regular,Consolas,monospace;font-size:12px}
.score{display:inline-block;min-width:34px;text-align:center;padding:2px 8px;border-radius:6px;font-weight:700;color:#fff}
.score.band-safe{background:var(--safe)}.score.band-review{background:var(--review)}.score.band-risky{background:var(--risky)}
tr.row.band-risky td:first-child{box-shadow:inset 3px 0 0 var(--risky)}
tr.row.band-review td:first-child{box-shadow:inset 3px 0 0 var(--review)}
tr.row.band-safe td:first-child{box-shadow:inset 3px 0 0 var(--safe)}
tr.detail{display:none}
tr.detail.open{display:table-row}
.detail-box{display:flex;flex-wrap:wrap;gap:24px;padding:8px 4px 18px}
.col{flex:1;min-width:280px}.col.full{flex-basis:100%}
.detail-box h3{font-size:12px;text-transform:uppercase;color:#57606a;margin:8px 0 6px}
ul.factors,ul.kv,ul.sources{list-style:none;padding:0;margin:0;font-size:13px;line-height:1.7}
.delta{display:inline-block;min-width:32px;font-weight:700;font-family:ui-monospace,monospace}
.delta.pos{color:var(--safe)}.delta.neg{color:var(--risky)}.delta.zero{color:#57606a}
.notes{background:#fff8e6;border:1px solid #f0d488;border-radius:6px;padding:8px 12px;font-size:13px;margin:6px 0}
pre.cmd{background:#0d1117;color:#e6edf3;padding:10px 12px;border-radius:6px;overflow-x:auto;font-size:12px}
a{color:#0969da}
footer{padding:18px 28px;color:#57606a;font-size:12px;border-top:1px solid var(--line)}
.empty{padding:40px;text-align:center;color:var(--safe);font-size:16px}
code{background:var(--bg);padding:1px 5px;border-radius:4px}
";

    const string Js = @"
function tog(r){var d=r.nextElementSibling;if(!d||!d.classList.contains('detail'))return;
var open=d.classList.toggle('open');r.classList.toggle('open',open);}
function flt(e,band){document.querySelectorAll('tr.row').forEach(function(r){
var show=band==='all'||r.classList.contains('band-'+band);
r.style.display=show?'':'none';
var d=r.nextElementSibling;if(d&&d.classList.contains('detail')){d.style.display='none';d.classList.remove('open');r.classList.remove('open');}});
document.querySelectorAll('.fbtn').forEach(function(x){x.classList.remove('active')});
e.target.classList.add('active');}
";
}
