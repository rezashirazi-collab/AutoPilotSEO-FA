using System.Security.Claims;
using AutopilotSeo.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AutopilotSeo.Web.Gsc;

/// <summary>Google OAuth "connect Search Console" flow. State is bound to the user and site, and expires in 15 minutes.</summary>
public static class GscEndpoints
{
    public const string CallbackPath = "/oauth/google/callback";
    const string StatePurpose = "gsc.oauth-state";

    public static string RedirectUri(HttpRequest r) => $"{r.Scheme}://{r.Host}{CallbackPath}";

    public static void Map(WebApplication app)
    {
        app.MapGet("/oauth/google/start/{siteId:int}", async (int siteId, HttpContext ctx, AppDbContext db,
            GoogleApi google, IOptions<GoogleOptions> opt, IDataProtectionProvider dp) =>
        {
            var uid = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            if (!await db.Sites.AnyAsync(s => s.Id == siteId && s.OwnerId == uid)) return Results.NotFound();
            if (!opt.Value.IsConfigured) return Back(ctx, siteId, "اتصال گوگل هنوز پیکربندی نشده است (Google:ClientId و ClientSecret).");

            var state = dp.CreateProtector(StatePurpose).ToTimeLimitedDataProtector()
                .Protect($"{siteId}|{uid}", TimeSpan.FromMinutes(15));
            return Results.Redirect(google.AuthorizeUrl(RedirectUri(ctx.Request), state));
        }).RequireAuthorization();

        app.MapGet(CallbackPath, async (HttpContext ctx, AppDbContext db, GoogleApi google,
            IDataProtectionProvider dp, GscSyncSignal signal, string? code, string? state, string? error) =>
        {
            var uid = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            int siteId;
            try
            {
                var parts = dp.CreateProtector(StatePurpose).ToTimeLimitedDataProtector().Unprotect(state ?? "").Split('|');
                siteId = int.Parse(parts[0]);
                if (parts[1] != uid) return Results.Redirect("/panel");
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return Results.Redirect("/panel"); // expired or forged state
            }
            if (!await db.Sites.AnyAsync(s => s.Id == siteId && s.OwnerId == uid)) return Results.NotFound();

            if (error is not null || code is null)
                return Back(ctx, siteId, "اتصال سرچ کنسول لغو شد. برای ادامه دوباره روی «اتصال سرچ کنسول» بزنید.");

            var token = await google.ExchangeCodeAsync(code, RedirectUri(ctx.Request), ctx.RequestAborted);
            if (token.RefreshToken is null || token.Scope?.Contains(GoogleApi.SearchConsoleScope) != true)
                return Back(ctx, siteId, "دسترسی خواندن سرچ کنسول داده نشد. دوباره وصل شوید و تیک دسترسی Search Console را بزنید.");

            var conn = await db.GscConnections.FirstOrDefaultAsync(c => c.SiteId == siteId);
            var email = GoogleApi.EmailFromIdToken(token.IdToken);
            if (conn is null)
                db.GscConnections.Add(conn = new GscConnection { SiteId = siteId });
            else if (!string.Equals(conn.GoogleEmail, email, StringComparison.OrdinalIgnoreCase))
                conn.Property = null; // another Google account: its properties may differ, pick again

            conn.GoogleEmail = email;
            conn.ProtectedRefreshToken = TokenProtector(dp).Protect(token.RefreshToken);
            conn.ConnectedAt = DateTime.UtcNow;
            conn.NeedsReconnect = false;
            conn.LastError = null;
            db.Log(siteId, uid, "gsc.connected", new { email });
            await db.SaveChangesAsync();

            if (conn.Property is not null) { signal.Kick(); return Back(ctx, siteId, "سرچ کنسول دوباره وصل شد."); }
            return Results.Redirect($"/panel/sites/{siteId}/gsc");
        }).RequireAuthorization();
    }

    public static IDataProtector TokenProtector(IDataProtectionProvider dp) => dp.CreateProtector("gsc.refresh-token");

    static IResult Back(HttpContext ctx, int siteId, string notice)
    {
        var temp = ctx.RequestServices.GetRequiredService<ITempDataDictionaryFactory>().GetTempData(ctx);
        temp["Notice"] = notice;
        temp.Save();
        return Results.Redirect($"/panel/sites/{siteId}");
    }
}

/// <summary>Wakes the sync worker right away (e.g. after a property is picked) instead of waiting for the next cycle.</summary>
public sealed class GscSyncSignal
{
    readonly SemaphoreSlim _s = new(0, 1);
    public void Kick() { if (_s.CurrentCount == 0) try { _s.Release(); } catch (SemaphoreFullException) { } }
    public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken ct) => _s.WaitAsync(timeout, ct);
}
