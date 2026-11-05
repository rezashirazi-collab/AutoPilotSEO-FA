using AutopilotSeo.Core;
using Microsoft.EntityFrameworkCore;

namespace AutopilotSeo.Web.Crawl;

/// <summary>Runs queued crawls one at a time and queues a weekly crawl for every site.</summary>
// ponytail: one crawl at a time in-process; move to the separate worker service with GSC sync when sites grow.
public sealed class CrawlWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<CrawlWorker> log) : BackgroundService
{
    static readonly TimeSpan Weekly = TimeSpan.FromDays(7);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), ct);
        await FailAbandonedAsync(ct);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await QueueWeeklyAsync(db, ct);
                var next = await db.Crawls.Where(c => c.Status == CrawlStatus.Queued).OrderBy(c => c.Id).FirstOrDefaultAsync(ct);
                if (next is not null) { await RunAsync(scope.ServiceProvider, db, next, ct); continue; }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogError(ex, "Crawl worker cycle failed"); }
            try { await Task.Delay(TimeSpan.FromSeconds(30), ct); } catch (OperationCanceledException) { return; }
        }
    }

    async Task FailAbandonedAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Crawls.Where(c => c.Status == CrawlStatus.Running)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CrawlStatus.Failed).SetProperty(c => c.Error, "Interrupted by an app restart."), ct);
    }

    static async Task QueueWeeklyAsync(AppDbContext db, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow - Weekly;
        var due = await db.Sites.Where(s => !db.Crawls.Any(c => c.SiteId == s.Id && (c.Status == CrawlStatus.Queued || c.Status == CrawlStatus.Running || c.QueuedAt > cutoff)))
            .Select(s => s.Id).ToListAsync(ct);
        foreach (var id in due) db.Crawls.Add(new Core.Crawl { SiteId = id });
        if (due.Count > 0) await db.SaveChangesAsync(ct);
    }

    async Task RunAsync(IServiceProvider sp, AppDbContext db, Core.Crawl crawl, CancellationToken ct)
    {
        var site = await db.Sites.FirstAsync(s => s.Id == crawl.SiteId, ct);
        crawl.Status = CrawlStatus.Running;
        crawl.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        try
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(TimeSpan.FromHours(3)); // a stuck crawl must not block every other site's crawl
            var result = await sp.GetRequiredService<Crawler>().CrawlAsync(site.Domain, config.GetValue("Crawl:MaxPages", 1000), limit.Token);
            await SaveAsync(db, site, crawl, result, ct);
            crawl.Status = CrawlStatus.Done;
            crawl.PagesCrawled = result.Pages.Count;
            crawl.HitPageLimit = result.HitLimit;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            log.LogError(ex, "Crawl {CrawlId} failed for {Domain}", crawl.Id, site.Domain);
            crawl.Status = CrawlStatus.Failed;
            crawl.Error = ex is HttpRequestException
                ? "سایت در دسترس نبود یا پاسخ نداد."
                : "کراول به دلیل خطای داخلی کامل نشد؛ جزئیات ثبت شد. دوباره امتحان کنید.";
        }
        crawl.FinishedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        log.LogInformation("Crawl {CrawlId} {Domain}: {Status}, {Pages} pages", crawl.Id, site.Domain, crawl.Status, crawl.PagesCrawled);
    }

    static async Task SaveAsync(AppDbContext db, Site site, Core.Crawl crawl, Crawler.Result r, CancellationToken ct)
    {
        var pages = Bulk.Table(("CrawlId", typeof(int)), ("Url", typeof(string)), ("StatusCode", typeof(int)), ("RedirectTo", typeof(string)),
            ("Depth", typeof(int)), ("InSitemap", typeof(bool)), ("BlockedByRobots", typeof(bool)), ("Title", typeof(string)),
            ("MetaDescription", typeof(string)), ("H1", typeof(string)), ("Canonical", typeof(string)), ("Noindex", typeof(bool)),
            ("WordCount", typeof(int)), ("Inlinks", typeof(int)), ("ResponseMs", typeof(int)));
        foreach (var p in r.Pages.Where(p => p.Url.Length <= 2048))
            pages.Rows.Add(crawl.Id, p.Url, p.StatusCode, Db(Cut(p.RedirectTo, 2048)), p.Depth, p.InSitemap, p.BlockedByRobots, Db(p.Title),
                Db(p.MetaDescription), Db(p.H1), Db(Cut(p.Canonical, 2048)), p.Noindex, p.WordCount, p.Inlinks, p.ResponseMs);
        await Bulk.InsertAsync(db, "CrawlPages", pages, null, ct);

        var ids = await db.CrawlPages.Where(p => p.CrawlId == crawl.Id).Select(p => new { p.Id, p.Url }).ToListAsync(ct);
        var idByUrl = ids.GroupBy(x => x.Url, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Id, StringComparer.Ordinal);
        var links = Bulk.Table(("CrawlId", typeof(int)), ("FromId", typeof(long)), ("ToId", typeof(long)), ("Anchor", typeof(string)));
        foreach (var l in r.Links.Where(l => l.From != l.To).DistinctBy(l => (l.From, l.To)))
            if (idByUrl.TryGetValue(l.From, out var f) && idByUrl.TryGetValue(l.To, out var t))
                links.Rows.Add(crawl.Id, f, t, l.Anchor);
        await Bulk.InsertAsync(db, "CrawlLinks", links, null, ct);

        // Inputs for "valuable page" checks: pages with Search impressions in the last 28 days, and money pages.
        var since = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-31);
        var searchPages = (await (from d in db.GscPageDaily
                                  join p in db.GscPages on d.PageId equals p.Id
                                  where d.SiteId == site.Id && d.Date >= since && d.Impressions > 0
                                  select p.Url).Distinct().ToListAsync(ct))
            .Select(HealthRules.NormalizeUrl).ToHashSet();
        var profile = await db.BusinessProfiles.Where(p => p.SiteId == site.Id && p.ApprovedAt != null).OrderByDescending(p => p.Version).FirstOrDefaultAsync(ct);
        var money = (profile?.Data.Products ?? []).Where(p => !string.IsNullOrWhiteSpace(p.Url)).Select(p => HealthRules.NormalizeUrl(p.Url)).ToHashSet();

        var found = HealthRules.Evaluate(r.Pages, searchPages, money, r.HomeUrl);
        var issues = Bulk.Table(("CrawlId", typeof(int)), ("SiteId", typeof(int)), ("Type", typeof(string)), ("Severity", typeof(byte)), ("Url", typeof(string)), ("Detail", typeof(string)));
        foreach (var i in found) issues.Rows.Add(crawl.Id, site.Id, i.Type, (byte)i.Severity, Cut(i.Url, 2048), Db(Cut(i.Detail, 1024)));
        await Bulk.InsertAsync(db, "Issues", issues, null, ct);

        // One alert per critical issue type; resolved automatically when a later crawl no longer finds it.
        var open = new HashSet<string>();
        foreach (var g in found.Where(i => i.Severity == Severity.Critical).GroupBy(i => i.Type))
        {
            open.Add("crawl|" + g.Key);
            await Alerts.RaiseAsync(db, site.Id, "crawl", g.Key, Severity.Critical,
                $"{Fa.IssueType(g.Key)}: {Fa.Num(g.Count())} صفحه", string.Join("\n", g.Take(5).Select(i => i.Url)));
        }
        await Alerts.ResolveOthersAsync(db, site.Id, ["crawl"], open);
    }

    static object Db(string? s) => (object?)s ?? DBNull.Value;
    static string? Cut(string? s, int max) => s is null ? null : s.Length > max ? s[..max] : s;
}
