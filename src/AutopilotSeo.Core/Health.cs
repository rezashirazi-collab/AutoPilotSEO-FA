namespace AutopilotSeo.Core;

public enum CrawlStatus { Queued, Running, Done, Failed }

public class Crawl
{
    public int Id { get; set; }
    public int SiteId { get; set; }
    public CrawlStatus Status { get; set; }
    public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public int PagesCrawled { get; set; }
    public bool HitPageLimit { get; set; }
    public string? Error { get; set; }
}

public class CrawlPage
{
    public long Id { get; set; }
    public int CrawlId { get; set; }
    public string Url { get; set; } = "";
    /// <summary>0 = not fetched (blocked by robots.txt or network error).</summary>
    public int StatusCode { get; set; }
    public string? RedirectTo { get; set; }
    public int Depth { get; set; }
    public bool InSitemap { get; set; }
    public bool BlockedByRobots { get; set; }
    public string? Title { get; set; }
    public string? MetaDescription { get; set; }
    public string? H1 { get; set; }
    public string? Canonical { get; set; }
    public bool Noindex { get; set; }
    public int WordCount { get; set; }
    public int Inlinks { get; set; }
    public int ResponseMs { get; set; }
}

/// <summary>Internal link edge, kept for orphan/depth checks now and internal-link suggestions later.</summary>
public class CrawlLink
{
    public int CrawlId { get; set; }
    public long FromId { get; set; }
    public long ToId { get; set; }
    public string Anchor { get; set; } = "";
}

public enum Severity : byte { Info, Warning, Critical }

public class Issue
{
    public long Id { get; set; }
    public int CrawlId { get; set; }
    public int SiteId { get; set; }
    public string Type { get; set; } = "";
    public Severity Severity { get; set; }
    public string Url { get; set; } = "";
    public string? Detail { get; set; }
}

/// <summary>Latest URL Inspection result for a page (one row per site + URL, overwritten on re-inspection).</summary>
public class UrlInspection
{
    public long Id { get; set; }
    public int SiteId { get; set; }
    public string Url { get; set; } = "";
    public DateTime InspectedAt { get; set; }
    public string Verdict { get; set; } = "";
    public string? CoverageState { get; set; }
    public string? IndexingState { get; set; }
    public string? RobotsTxtState { get; set; }
    public string? PageFetchState { get; set; }
    public string? GoogleCanonical { get; set; }
    public string? UserCanonical { get; set; }
    public DateTime? LastCrawlTime { get; set; }
}

/// <summary>Monitoring alert. Key dedupes: one open alert per (site, kind, key).</summary>
public class Alert
{
    public long Id { get; set; }
    public int SiteId { get; set; }
    public string Kind { get; set; } = "";
    public string Key { get; set; } = "";
    public Severity Severity { get; set; }
    public string Title { get; set; } = "";
    public string? Detail { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ResolvedAt { get; set; }
}

public sealed record IssueFound(string Type, Severity Severity, string Url, string? Detail = null);

public static class HealthRules
{
    /// <summary>Click-depth targets from the spec: money pages ≤ 3 clicks, everything else ≤ 4 (industry convention).</summary>
    public const int MoneyPageMaxDepth = 3, PageMaxDepth = 4;

    /// <summary>
    /// Comparison key for URLs from different sources: the crawler sees percent-encoded paths, Search Console
    /// and users write Persian slugs decoded. Decode, drop the trailing slash, ignore case.
    /// </summary>
    public static string NormalizeUrl(string url) => Uri.UnescapeDataString(url).TrimEnd('/').ToLowerInvariant();

    /// <param name="searchPages">Pages that got impressions in Search Console recently.</param>
    /// <param name="moneyPages">Money-page URLs from the business profile.</param>
    public static List<IssueFound> Evaluate(IReadOnlyList<CrawlPage> pages, IReadOnlySet<string> searchPages, IReadOnlySet<string> moneyPages, string homeUrl)
    {
        var issues = new List<IssueFound>();
        var byUrl = pages.GroupBy(p => NormalizeUrl(p.Url)).ToDictionary(g => g.Key, g => g.First());
        bool Valuable(CrawlPage p) => p.InSitemap || searchPages.Contains(NormalizeUrl(p.Url)) || moneyPages.Contains(NormalizeUrl(p.Url));
        bool SelfCanonical(CrawlPage p) => p.Canonical is null || NormalizeUrl(p.Canonical) == NormalizeUrl(p.Url);
        bool Indexable(CrawlPage p) => p.StatusCode == 200 && !p.Noindex && SelfCanonical(p);

        foreach (var p in pages)
        {
            var url = NormalizeUrl(p.Url);
            var linkedOrListed = p.Inlinks > 0 || p.InSitemap;

            if (p.BlockedByRobots)
            {
                if (Valuable(p)) issues.Add(new("robots_blocked", Severity.Critical, p.Url));
                continue;
            }
            if (p.StatusCode == 429) continue; // the server throttled our crawler: unknown, not a site error
            if ((p.StatusCode == 0 || p.StatusCode >= 400) && linkedOrListed)
                issues.Add(new("http_error", p.StatusCode >= 500 || p.StatusCode == 0 ? Severity.Critical : Severity.Warning, p.Url, p.StatusCode == 0 ? "no response" : p.StatusCode.ToString()));

            if (p.StatusCode is >= 300 and < 400)
            {
                if (p.RedirectTo is not null && byUrl.TryGetValue(NormalizeUrl(p.RedirectTo), out var next) && next.StatusCode is >= 300 and < 400)
                    issues.Add(new("redirect_chain", Severity.Warning, p.Url, $"{p.RedirectTo} → {next.RedirectTo}"));
                else if (p.Inlinks > 0)
                    issues.Add(new("internal_redirect", Severity.Info, p.Url, p.RedirectTo));
                continue;
            }
            if (p.StatusCode != 200) continue;

            if (p.Noindex && Valuable(p))
                issues.Add(new("noindex_valuable", searchPages.Contains(url) || moneyPages.Contains(url) ? Severity.Critical : Severity.Warning, p.Url));
            if (!SelfCanonical(p))
            {
                var target = byUrl.GetValueOrDefault(NormalizeUrl(p.Canonical!));
                if (target is not null && target.StatusCode != 200)
                    issues.Add(new("canonical_broken", Severity.Warning, p.Url, $"{p.Canonical} ({target.StatusCode})"));
            }
            if (p.InSitemap && !Indexable(p))
                issues.Add(new("sitemap_not_indexable", Severity.Warning, p.Url, p.Noindex ? "noindex" : "canonical → " + p.Canonical));
            if (!Indexable(p)) continue;

            if (string.IsNullOrWhiteSpace(p.Title)) issues.Add(new("missing_title", Severity.Warning, p.Url));
            if (string.IsNullOrWhiteSpace(p.MetaDescription)) issues.Add(new("missing_description", Severity.Info, p.Url));
            if (string.IsNullOrWhiteSpace(p.H1)) issues.Add(new("missing_h1", Severity.Info, p.Url));
            if (p.Inlinks == 0 && url != NormalizeUrl(homeUrl) && Valuable(p)) issues.Add(new("orphan", Severity.Warning, p.Url));

            var maxDepth = moneyPages.Contains(url) ? MoneyPageMaxDepth : PageMaxDepth;
            if (p.Depth > maxDepth && p.Inlinks > 0)
                issues.Add(new("deep_page", moneyPages.Contains(url) ? Severity.Warning : Severity.Info, p.Url, p.Depth.ToString()));
        }

        var indexable = pages.Where(Indexable).ToList();
        foreach (var g in indexable.Where(p => !string.IsNullOrWhiteSpace(p.Title)).GroupBy(p => p.Title!.Trim()).Where(g => g.Count() > 1))
            foreach (var p in g) issues.Add(new("duplicate_title", Severity.Warning, p.Url, g.Key));
        foreach (var g in indexable.Where(p => !string.IsNullOrWhiteSpace(p.MetaDescription)).GroupBy(p => p.MetaDescription!.Trim()).Where(g => g.Count() > 1))
            foreach (var p in g) issues.Add(new("duplicate_description", Severity.Info, p.Url));
        return issues;
    }
}

public static class MonitorRules
{
    /// <summary>z-score of the last day's clicks against the 28 days before it (spec: |z| ≥ 3, Sight AI pattern).</summary>
    public static double? ClicksZScore(IReadOnlyList<int> last29DaysOldestFirst)
    {
        if (last29DaysOldestFirst.Count < 29) return null;
        var baseline = last29DaysOldestFirst.Take(28).Select(c => (double)c).ToList();
        var mean = baseline.Average();
        var sd = Math.Sqrt(baseline.Sum(c => (c - mean) * (c - mean)) / (baseline.Count - 1));
        // ponytail: sd floor of 1 click keeps tiny sites from alerting on 0 → 2 clicks.
        return (last29DaysOldestFirst[^1] - mean) / Math.Max(sd, 1);
    }

    /// <summary>Last 7 days vs the 7 before: a ≥ 20% drop with at least 50 clicks in the earlier week.</summary>
    public static double? WeeklyDrop(IReadOnlyList<int> last14DaysOldestFirst)
    {
        if (last14DaysOldestFirst.Count < 14) return null;
        double prev = last14DaysOldestFirst.Take(7).Sum(), cur = last14DaysOldestFirst.Skip(7).Sum();
        if (prev < 50) return null;
        var drop = (prev - cur) / prev;
        return drop >= 0.2 ? drop : null;
    }
}
