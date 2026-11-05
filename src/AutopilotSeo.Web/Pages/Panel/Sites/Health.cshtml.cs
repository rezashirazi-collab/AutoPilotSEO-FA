using System.Security.Claims;
using AutopilotSeo.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AutopilotSeo.Web.Pages.Panel.Sites;

public class HealthModel(AppDbContext db) : PageModel
{
    public record IssueGroup(string Type, Severity Severity, int Count, List<Issue> Sample);

    public Site Site { get; private set; } = null!;
    public Core.Crawl? Last { get; private set; }
    public Core.Crawl? Pending { get; private set; }
    public Dictionary<string, int> Statuses { get; private set; } = [];
    public List<IssueGroup> Groups { get; private set; } = [];
    public List<UrlInspection> Inspections { get; private set; } = [];

    string Uid => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == Uid);
        if (site is null) return NotFound();
        Site = site;
        ViewData["Site"] = site;

        Pending = await db.Crawls.Where(c => c.SiteId == id && (c.Status == CrawlStatus.Queued || c.Status == CrawlStatus.Running)).FirstOrDefaultAsync();
        Last = await db.Crawls.Where(c => c.SiteId == id && c.Status != CrawlStatus.Queued && c.Status != CrawlStatus.Running)
            .OrderByDescending(c => c.Id).FirstOrDefaultAsync();
        Inspections = await db.UrlInspections.Where(i => i.SiteId == id).OrderBy(i => i.Verdict == "PASS").ThenBy(i => i.Url).ToListAsync();
        if (Last is not { Status: CrawlStatus.Done }) return Page();

        var codes = await db.CrawlPages.Where(p => p.CrawlId == Last.Id)
            .GroupBy(p => p.BlockedByRobots ? -1 : p.StatusCode / 100).Select(g => new { g.Key, N = g.Count() }).ToListAsync();
        Statuses = codes.ToDictionary(c => c.Key switch { -1 => "robots", 0 => "error", 2 => "2xx", 3 => "3xx", 4 => "4xx", _ => "5xx" }, c => c.N);

        var issues = await db.Issues.Where(i => i.CrawlId == Last.Id).ToListAsync();
        Groups = issues.GroupBy(i => i.Type)
            .Select(g => new IssueGroup(g.Key, g.Max(i => i.Severity), g.Count(), g.Take(50).ToList()))
            .OrderByDescending(g => g.Severity).ThenByDescending(g => g.Count).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostCrawlAsync(int id)
    {
        if (!await db.Sites.AnyAsync(s => s.Id == id && s.OwnerId == Uid)) return NotFound();
        if (!await db.Crawls.AnyAsync(c => c.SiteId == id && (c.Status == CrawlStatus.Queued || c.Status == CrawlStatus.Running)))
        {
            db.Crawls.Add(new Core.Crawl { SiteId = id });
            db.Log(id, Uid, "crawl.queued");
            await db.SaveChangesAsync();
        }
        TempData["Notice"] = "کراول در صف قرار گرفت؛ معمولاً چند دقیقه طول می‌کشد. این صفحه را بعداً تازه کنید.";
        return Redirect($"/panel/sites/{id}/health");
    }
}
