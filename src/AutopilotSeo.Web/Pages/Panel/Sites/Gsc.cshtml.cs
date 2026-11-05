using System.Security.Claims;
using AutopilotSeo.Core;
using AutopilotSeo.Web.Gsc;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AutopilotSeo.Web.Pages.Panel.Sites;

public class GscModel(AppDbContext db, GoogleApi google, IDataProtectionProvider dp, GscSyncSignal signal) : PageModel
{
    public Site Site { get; private set; } = null!;
    public GscConnection Conn { get; private set; } = null!;
    public List<GoogleApi.Property> Matching { get; private set; } = [];
    public int OtherCount { get; private set; }
    public bool Revoked { get; private set; }

    string Uid => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    async Task<bool> LoadAsync(int id)
    {
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == Uid);
        var conn = site is null ? null : await db.GscConnections.FirstOrDefaultAsync(c => c.SiteId == id);
        if (conn is null) return false;
        (Site, Conn) = (site!, conn);
        ViewData["Site"] = site;

        try
        {
            var token = await google.AccessTokenAsync(GscEndpoints.TokenProtector(dp).Unprotect(conn.ProtectedRefreshToken));
            var all = (await google.ListPropertiesAsync(token)).Where(p => p.PermissionLevel != "siteUnverifiedUser").ToList();
            // Domain properties first: they cover http/https and every subdomain variant of the host.
            Matching = all.Where(p => GscRules.PropertyMatches(site!.Domain, p.SiteUrl))
                .OrderBy(p => p.SiteUrl.StartsWith("sc-domain:") ? 0 : 1).ToList();
            OtherCount = all.Count - Matching.Count;
        }
        catch (Exception ex) when (ex is GoogleAuthRevokedException or System.Security.Cryptography.CryptographicException)
        {
            Revoked = true;
        }
        return true;
    }

    public async Task<IActionResult> OnGetAsync(int id) =>
        await LoadAsync(id) ? Page() : Redirect($"/panel/sites/{id}");

    public async Task<IActionResult> OnPostSelectAsync(int id, string property)
    {
        if (!await LoadAsync(id)) return Redirect($"/panel/sites/{id}");
        // Re-checked against Google's list, so a user can only pick a property this account can read.
        if (!Matching.Any(p => p.SiteUrl == property)) return Page();

        if (Conn.Property != property)
        {
            Conn.Property = property;
            db.Log(id, Uid, "gsc.property_selected", new { property });
            await db.SaveChangesAsync();
        }
        signal.Kick();
        TempData["Notice"] = "دریافت داده‌های سرچ کنسول شروع شد. ۱۶ ماه داده در پس‌زمینه دریافت می‌شود؛ روزهای اخیر زودتر آماده می‌شوند.";
        return Redirect($"/panel/sites/{id}/performance");
    }

    public async Task<IActionResult> OnPostDisconnectAsync(int id)
    {
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == Uid);
        var conn = site is null ? null : await db.GscConnections.FirstOrDefaultAsync(c => c.SiteId == id);
        if (conn is null) return Redirect($"/panel/sites/{id}");

        try { await google.RevokeAsync(GscEndpoints.TokenProtector(dp).Unprotect(conn.ProtectedRefreshToken)); }
        catch (System.Security.Cryptography.CryptographicException) { }
        db.GscConnections.Remove(conn);
        db.Log(id, Uid, "gsc.disconnected");
        await db.SaveChangesAsync();
        TempData["Notice"] = "اتصال سرچ کنسول قطع و دسترسی از گوگل پس گرفته شد. داده‌های دریافت‌شده باقی می‌مانند.";
        return Redirect($"/panel/sites/{id}");
    }
}
