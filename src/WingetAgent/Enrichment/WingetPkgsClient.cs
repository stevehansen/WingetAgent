using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using WingetAgent.Models;

namespace WingetAgent.Enrichment;

/// <summary>
/// Enriches an update with data from the microsoft/winget-pkgs repository: the list
/// of published versions (for history) and the target version's ReleaseDate (for age).
///
/// Cost is two HTTP calls per package. Uses GITHUB_TOKEN when present (5000 req/hr);
/// unauthenticated callers get 60/hr, so the caller is expected to cap how many
/// packages it enriches. On a rate-limit 403 the client latches <see cref="RateLimited"/>
/// and turns subsequent calls into no-ops with an explanatory note, never throwing.
/// </summary>
public sealed class WingetPkgsClient
{
    readonly HttpClient _http;
    bool _rateLimited;

    public bool RateLimited => _rateLimited;
    public bool HasToken => _http.DefaultRequestHeaders.Authorization is not null;

    public WingetPkgsClient(string? token)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("WingetAgent", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!string.IsNullOrWhiteSpace(token))
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task EnrichAsync(EnrichedUpdate u)
    {
        if (_rateLimited) { u.EnrichmentNote = "skipped (GitHub rate limit)"; return; }
        if (u.Source.Equals("msstore", StringComparison.OrdinalIgnoreCase))
        {
            u.EnrichmentNote = "msstore package — no winget-pkgs manifest";
            return;
        }

        try
        {
            var segs = u.Id.Split('.');
            var pkgPath = string.Join('/', segs);
            char letter = char.ToLowerInvariant(u.Id[0]);
            u.ManifestUrl = $"https://github.com/microsoft/winget-pkgs/tree/master/manifests/{letter}/{pkgPath}";

            var dirUrl = $"https://api.github.com/repos/microsoft/winget-pkgs/contents/manifests/{letter}/{pkgPath}";
            var dirJson = await GetAsync(dirUrl);
            if (dirJson is null) { u.EnrichmentNote ??= "manifest path not found"; return; }

            using var doc = JsonDocument.Parse(dirJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                u.EnrichmentNote = "manifest path not found";
                return;
            }

            var versions = new List<string>();
            foreach (var el in doc.RootElement.EnumerateArray())
                if (el.TryGetProperty("type", out var t) && t.GetString() == "dir"
                    && el.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } name)
                    versions.Add(name);

            u.RecentVersions = versions.OrderByDescending(v => v, new VersionishComparer()).Take(8).ToList();

            var rawUrl = $"https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests/{letter}/{pkgPath}/{u.AvailableVersion}/{u.Id}.installer.yaml";
            var yaml = await GetRawAsync(rawUrl);
            if (yaml is not null)
            {
                var m = Regex.Match(yaml, @"ReleaseDate:\s*'?(\d{4}-\d{2}-\d{2})");
                if (m.Success && DateTime.TryParse(m.Groups[1].Value, out var rd))
                {
                    u.ReleaseDate = rd;
                    u.AgeDays = Math.Max(0, (int)(DateTime.UtcNow.Date - rd.Date).TotalDays);
                }
                else u.EnrichmentNote = "no ReleaseDate in manifest";
            }
            else u.EnrichmentNote ??= "installer manifest not found for target version";
        }
        catch (Exception ex)
        {
            u.EnrichmentNote = $"enrichment error: {ex.Message}";
        }
    }

    async Task<string?> GetAsync(string url)
    {
        var resp = await _http.GetAsync(url);
        if ((int)resp.StatusCode == 403
            && resp.Headers.TryGetValues("X-RateLimit-Remaining", out var v)
            && v.FirstOrDefault() == "0")
        {
            _rateLimited = true;
            return null;
        }
        return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync() : null;
    }

    async Task<string?> GetRawAsync(string url)
    {
        var resp = await _http.GetAsync(url);
        return resp.IsSuccessStatusCode ? await resp.Content.ReadAsStringAsync() : null;
    }
}
