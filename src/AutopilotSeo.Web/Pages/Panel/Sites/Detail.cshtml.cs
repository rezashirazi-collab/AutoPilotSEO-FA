using System.Security.Claims;
using AutopilotSeo.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AutopilotSeo.Web.Pages.Panel.Sites;

public class DetailModel(AppDbContext db) : PageModel
{
    public Site Site { get; private set; } = null!;
    public BusinessProfile Latest { get; private set; } = null!;
    public BusinessProfile? Approved { get; private set; }
    public List<AuditEvent> Events { get; private set; } = [];
    public GscConnection? Gsc { get; private set; }
    public int GscDays { get; private set; }
    public Core.Crawl? LastCrawl { get; private set; }
    public int IssueCount { get; private set; }
    public List<Alert> OpenAlerts { get; private set; } = [];

    string Uid => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var site = await db.Sites.Include(s => s.Profiles).FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == Uid);
        if (site is null) return NotFound();

        Site = site;
        Latest = site.Profiles.MaxBy(p => p.Version)!;
        Approved = site.Profiles.Where(p => p.ApprovedAt != null).MaxBy(p => p.Version);
        Events = await db.AuditEvents.Where(e => e.SiteId == id).OrderByDescending(e => e.Id).Take(12).ToListAsync();
        Gsc = await db.GscConnections.FirstOrDefaultAsync(c => c.SiteId == id);
        GscDays = await db.GscDaily.CountAsync(d => d.SiteId == id);
        LastCrawl = await db.Crawls.Where(c => c.SiteId == id && c.Status == CrawlStatus.Done).OrderByDescending(c => c.Id).FirstOrDefaultAsync();
        if (LastCrawl is not null) IssueCount = await db.Issues.CountAsync(i => i.CrawlId == LastCrawl.Id && i.Severity != Severity.Info);
        OpenAlerts = await db.Alerts.Where(a => a.SiteId == id && a.ResolvedAt == null)
            .OrderByDescending(a => a.Severity).ThenByDescending(a => a.CreatedAt).ToListAsync();
        ViewData["Site"] = site;
        return Page();
    }

    public async Task<IActionResult> OnPostPauseAsync(int id, bool pause)
    {
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == Uid);
        if (site is null) return NotFound();

        if (site.IsPaused != pause)
        {
            site.IsPaused = pause;
            db.Log(id, Uid, pause ? "site.paused" : "site.resumed");
            await db.SaveChangesAsync();
        }
        TempData["Notice"] = pause ? "همهٔ اقدامات روی این سایت متوقف شد." : "اقدامات از سر گرفته شد.";
        return Redirect($"/panel/sites/{id}");
    }
}
