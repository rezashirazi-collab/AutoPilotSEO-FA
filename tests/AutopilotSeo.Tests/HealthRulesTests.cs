using AutopilotSeo.Core;

namespace AutopilotSeo.Tests;

public class HealthRulesTests
{
    const string Home = "https://ex.com/";
    static readonly HashSet<string> None = [];

    static CrawlPage P(string path, int status = 200, Action<CrawlPage>? f = null)
    {
        var p = new CrawlPage { Url = "https://ex.com" + path, StatusCode = status, Title = "t" + path, MetaDescription = "d" + path, H1 = "h", Inlinks = 1, Depth = 1 };
        f?.Invoke(p);
        return p;
    }

    static List<string> Types(IEnumerable<IssueFound> i) => i.Select(x => x.Type).OrderBy(x => x).ToList();

    [Fact]
    public void Clean_site_has_no_issues()
        => Assert.Empty(HealthRules.Evaluate([P("/", f: p => p.Inlinks = 0), P("/a"), P("/b"), P("/throttled", 429)], None, None, Home));

    [Fact]
    public void Errors_redirect_chains_and_noindex()
    {
        var pages = new List<CrawlPage>
        {
            P("/gone", 404),
            P("/down", 503),
            P("/r1", 301, p => p.RedirectTo = "https://ex.com/r2"),
            P("/r2", 301, p => { p.RedirectTo = "https://ex.com/a"; p.Inlinks = 0; }),
            P("/a"),
            P("/hidden", f: p => { p.Noindex = true; p.InSitemap = true; }),
        };
        var issues = HealthRules.Evaluate(pages, new HashSet<string> { "https://ex.com/hidden" }, None, Home);
        Assert.Equal(["http_error", "http_error", "noindex_valuable", "redirect_chain", "sitemap_not_indexable"], Types(issues));
        Assert.Equal(Severity.Critical, issues.Single(i => i.Url.EndsWith("/down")).Severity);
        Assert.Equal(Severity.Critical, issues.Single(i => i.Type == "noindex_valuable").Severity);
    }

    [Fact]
    public void Duplicates_orphans_and_deep_money_pages()
    {
        var pages = new List<CrawlPage>
        {
            P("/x", f: p => p.Title = "Same"),
            P("/y", f: p => p.Title = "Same"),
            P("/lonely", f: p => { p.Inlinks = 0; p.InSitemap = true; }),
            P("/buy", f: p => p.Depth = 4),
            P("/blog/deep", f: p => p.Depth = 4),
        };
        var issues = HealthRules.Evaluate(pages, None, new HashSet<string> { "https://ex.com/buy" }, Home);
        Assert.Equal(["deep_page", "duplicate_title", "duplicate_title", "orphan"], Types(issues));
    }

    [Fact]
    public void Encoded_and_decoded_persian_urls_compare_equal()
        => Assert.Equal(HealthRules.NormalizeUrl("https://ex.com/page/106/%D8%AE%D8%AF%D9%85%D8%A7%D8%AA/"),
                        HealthRules.NormalizeUrl("https://ex.com/page/106/خدمات"));

    [Fact]
    public void Canonicalised_pages_skip_on_page_checks()
        => Assert.Empty(HealthRules.Evaluate([P("/a"), P("/a?ref=1", f: p => { p.Canonical = "https://ex.com/a"; p.Title = null; })], None, None, Home));

    [Fact]
    public void Z_score_flags_a_spike_but_not_noise()
    {
        var flat = Enumerable.Repeat(10, 28).ToList();
        Assert.True(MonitorRules.ClicksZScore([.. flat, 40]) >= 3);
        var noisy = Enumerable.Range(0, 28).Select(i => i % 2 == 0 ? 5 : 15).ToList();
        Assert.True(Math.Abs(MonitorRules.ClicksZScore([.. noisy, 14])!.Value) < 3);
    }

    [Fact]
    public void Weekly_drop_needs_volume_and_twenty_percent()
    {
        Assert.Equal(0.5, MonitorRules.WeeklyDrop([.. Enumerable.Repeat(20, 7), .. Enumerable.Repeat(10, 7)]));
        Assert.Null(MonitorRules.WeeklyDrop([.. Enumerable.Repeat(20, 7), .. Enumerable.Repeat(18, 7)]));
        Assert.Null(MonitorRules.WeeklyDrop([.. Enumerable.Repeat(5, 7), .. Enumerable.Repeat(0, 7)]));
    }
}
