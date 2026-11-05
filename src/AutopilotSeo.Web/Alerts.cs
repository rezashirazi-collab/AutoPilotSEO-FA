using AutopilotSeo.Core;
using Microsoft.EntityFrameworkCore;

namespace AutopilotSeo.Web;

/// <summary>Open/resolve monitoring alerts. One open alert per (site, kind, key); callers SaveChanges.</summary>
public static class Alerts
{
    public static async Task RaiseAsync(AppDbContext db, int siteId, string kind, string key, Severity severity, string title, string? detail = null)
    {
        var open = await db.Alerts.FirstOrDefaultAsync(a => a.SiteId == siteId && a.Kind == kind && a.Key == key && a.ResolvedAt == null);
        if (open is null)
            db.Alerts.Add(new Alert { SiteId = siteId, Kind = kind, Key = key, Severity = severity, Title = title, Detail = detail });
        else
            (open.Severity, open.Title, open.Detail) = (severity, title, detail);
    }

    /// <summary>Resolves open alerts of these kinds whose key is not in <paramref name="stillOpen"/>.</summary>
    public static async Task ResolveOthersAsync(AppDbContext db, int siteId, string[] kinds, IReadOnlySet<string> stillOpen)
    {
        var open = await db.Alerts.Where(a => a.SiteId == siteId && kinds.Contains(a.Kind) && a.ResolvedAt == null).ToListAsync();
        foreach (var a in open.Where(a => !stillOpen.Contains(a.Kind + "|" + a.Key))) a.ResolvedAt = DateTime.UtcNow;
    }
}
