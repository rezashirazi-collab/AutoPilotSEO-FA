using System.Data;
using AutopilotSeo.Core;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AutopilotSeo.Web.Gsc;

/// <summary>
/// Backfills 16 months of Search Console data and then keeps it current, one day per transaction.
/// A GscDaily row marks a day as done, so a crash mid-backfill just resumes.
/// </summary>
// ponytail: one in-process worker, sites synced one after another. Fine for tens of sites; move to a
// separate worker service (and keep IIS from idling the app pool) before onboarding many.
public sealed class GscSyncWorker(IServiceScopeFactory scopes, GscSyncSignal signal, ILogger<GscSyncWorker> log) : BackgroundService
{
    static readonly TimeSpan Cycle = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), ct);
        while (!ct.IsCancellationRequested)
        {
            List<int> siteIds = [];
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                siteIds = await db.GscConnections.Where(c => c.Property != null && !c.NeedsReconnect)
                    .Select(c => c.SiteId).ToListAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            // An unhandled exception here would stop the whole web host; a DB outage must only skip a cycle.
            catch (Exception ex) { log.LogError(ex, "GSC sync could not list connections"); }
            foreach (var siteId in siteIds)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<GscSyncer>().SyncSiteAsync(siteId, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
                catch (Exception ex) { log.LogError(ex, "GSC sync failed for site {SiteId}", siteId); }
            }
            try { await signal.WaitAsync(Cycle, ct); } catch (OperationCanceledException) { return; }
        }
    }
}

public sealed class GscSyncer(AppDbContext db, GoogleApi google, IDataProtectionProvider dp, ILogger<GscSyncer> log)
{
    static readonly string[] PageDims = ["page"];
    static readonly string[] QueryPageDims = ["query", "page", "device"];

    Dictionary<string, int> _queries = null!, _pages = null!;

    public async Task SyncSiteAsync(int siteId, CancellationToken ct)
    {
        var conn = await db.GscConnections.FirstAsync(c => c.SiteId == siteId, ct);
        string token;
        try
        {
            token = await google.AccessTokenAsync(GscEndpoints.TokenProtector(dp).Unprotect(conn.ProtectedRefreshToken), ct);
        }
        catch (Exception ex) when (ex is GoogleAuthRevokedException or System.Security.Cryptography.CryptographicException)
        {
            conn.NeedsReconnect = true;
            conn.LastError = "دسترسی به سرچ کنسول باطل شده است؛ دوباره وصل شوید.";
            db.Log(siteId, null, "gsc.reconnect_needed");
            await db.SaveChangesAsync(ct);
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var start = GscRules.WindowStart(today);
        var property = conn.Property!;

        try
        {
            // Days with data in the final dataset. Days missing in between had zero impressions.
            var totals = (await google.QueryAllAsync(token, property, start, today, ["date"], ct))
                .ToDictionary(r => DateOnly.Parse(r.Keys[0]));
            if (totals.Count == 0) { await FinishAsync(conn, null, ct); return; }

            var lastFinal = totals.Keys.Max();
            var synced = (await db.GscDaily.Where(d => d.SiteId == siteId && d.Date >= start)
                .Select(d => d.Date).ToListAsync(ct)).ToHashSet();
            var missing = GscRules.MissingDates(start, lastFinal, synced);

            var empty = missing.Where(d => !totals.ContainsKey(d)).ToList();
            if (empty.Count > 0)
            {
                db.GscDaily.AddRange(empty.Select(d => new GscDaily { SiteId = siteId, Date = d }));
                await db.SaveChangesAsync(ct);
            }

            var toFetch = missing.Where(totals.ContainsKey).ToList();
            if (toFetch.Count > 0) await LoadLookupsAsync(siteId, ct);
            foreach (var day in toFetch)
            {
                ct.ThrowIfCancellationRequested();
                await SyncDayAsync(siteId, property, token, day, totals[day], ct);
            }
            log.LogInformation("GSC site {SiteId}: {Days} days synced, {Empty} empty", siteId, toFetch.Count, empty.Count);
            await MonitorTrafficAsync(siteId, ct);
            await InspectAsync(siteId, property, token, ct);
            await FinishAsync(conn, null, ct);
        }
        catch (HttpRequestException ex)
        {
            await FinishAsync(conn, ex.StatusCode == System.Net.HttpStatusCode.Forbidden
                ? "این حساب گوگل دیگر به property انتخاب‌شده دسترسی ندارد."
                : "دریافت داده از سرچ کنسول با خطا روبه‌رو شد؛ دور بعد دوباره امتحان می‌شود.", ct);
            throw;
        }
    }

    /// <summary>Spec monitoring: daily z-score drop (warning) and ≥ 20% weekly drop (critical, a crisis candidate).</summary>
    async Task MonitorTrafficAsync(int siteId, CancellationToken ct)
    {
        var days = await db.GscDaily.Where(d => d.SiteId == siteId).OrderByDescending(d => d.Date).Take(29).ToListAsync(ct);
        days.Reverse();
        if (days.Count < 14) return;
        var clicks = days.Select(d => d.Clicks).ToList();
        var last = days[^1].Date;
        var open = new HashSet<string>();

        if (MonitorRules.ClicksZScore(clicks) is <= -3 and var z)
        {
            open.Add("traffic_anomaly|" + last.ToString("yyyy-MM-dd"));
            await Alerts.RaiseAsync(db, siteId, "traffic_anomaly", last.ToString("yyyy-MM-dd"), Severity.Warning,
                $"افت غیرعادی کلیک در {Fa.Date(last)}", $"z = {z:0.0} نسبت به ۲۸ روز قبل");
        }
        if (MonitorRules.WeeklyDrop(clicks.TakeLast(14).ToList()) is { } drop)
        {
            open.Add("traffic_drop|week");
            await Alerts.RaiseAsync(db, siteId, "traffic_drop", "week", Severity.Critical,
                $"افت {Fa.Pct(drop)} کلیک در هفتهٔ منتهی به {Fa.Date(last)}", "پیشنهاد بررسی علت: فنی، آپدیت گوگل یا فصلی.");
        }
        await Alerts.ResolveOthersAsync(db, siteId, ["traffic_drop", "traffic_anomaly"], open);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Index checks for the pages that matter most: money pages first, then top pages by clicks.
    /// Each page is re-checked at most weekly; the daily budget stays far under Google's 2,000/day.
    /// </summary>
    async Task InspectAsync(int siteId, string property, string token, CancellationToken ct)
    {
        const int DailyBudget = 50;
        var today = DateTime.UtcNow.Date;
        var usedToday = await db.UrlInspections.CountAsync(i => i.SiteId == siteId && i.InspectedAt >= today, ct);
        if (usedToday >= DailyBudget) return;

        var profile = await db.BusinessProfiles.Where(p => p.SiteId == siteId && p.ApprovedAt != null).OrderByDescending(p => p.Version).FirstOrDefaultAsync(ct);
        var money = (profile?.Data.Products ?? []).Select(p => p.Url).Where(u => Uri.IsWellFormedUriString(u, UriKind.Absolute)).ToList();
        var since = DateOnly.FromDateTime(today).AddDays(-31);
        var top = await (from d in db.GscPageDaily
                         join p in db.GscPages on d.PageId equals p.Id
                         where d.SiteId == siteId && d.Date >= since
                         group d by p.Url into g
                         orderby g.Sum(x => x.Clicks) descending, g.Sum(x => x.Impressions) descending
                         select g.Key).Take(200).ToListAsync(ct);

        var existing = await db.UrlInspections.Where(i => i.SiteId == siteId).ToDictionaryAsync(i => i.Url, ct);
        var moneySet = money.Select(HealthRules.NormalizeUrl).ToHashSet();
        var due = money.Concat(top).DistinctBy(HealthRules.NormalizeUrl)
            .Where(u => !existing.TryGetValue(u, out var e) || e.InspectedAt < today.AddDays(-7))
            .Take(DailyBudget - usedToday).ToList();

        foreach (var url in due)
        {
            GoogleApi.IndexStatus? s;
            try { s = await google.InspectAsync(token, property, url, ct); }
            catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.BadRequest or System.Net.HttpStatusCode.Forbidden)
            {
                continue; // URL outside the property (e.g. a mistyped money page)
            }
            if (s is null) continue;

            if (!existing.TryGetValue(url, out var row)) db.UrlInspections.Add(row = existing[url] = new UrlInspection { SiteId = siteId, Url = url });
            row.InspectedAt = DateTime.UtcNow;
            row.Verdict = s.Verdict ?? "";
            (row.CoverageState, row.IndexingState, row.RobotsTxtState, row.PageFetchState) = (s.CoverageState, s.IndexingState, s.RobotsTxtState, s.PageFetchState);
            (row.GoogleCanonical, row.UserCanonical, row.LastCrawlTime) = (s.GoogleCanonical, s.UserCanonical, s.LastCrawlTime);
        }

        // Alerts reflect the latest result of every inspected page, including ones not re-checked today.
        var open = new HashSet<string>();
        foreach (var r in existing.Values)
        {
            if (moneySet.Contains(HealthRules.NormalizeUrl(r.Url)) && r.Verdict != "PASS")
            {
                open.Add("not_indexed|" + r.Url);
                await Alerts.RaiseAsync(db, siteId, "not_indexed", r.Url, Severity.Critical, "صفحهٔ پول‌ساز در ایندکس گوگل نیست", $"{Uri.UnescapeDataString(r.Url)}\n{r.CoverageState}");
            }
            if (r.GoogleCanonical is not null && r.UserCanonical is not null && HealthRules.NormalizeUrl(r.GoogleCanonical) != HealthRules.NormalizeUrl(r.UserCanonical))
            {
                open.Add("canonical_mismatch|" + r.Url);
                await Alerts.RaiseAsync(db, siteId, "canonical_mismatch", r.Url, Severity.Warning, "گوگل canonical اعلام‌شدهٔ صفحه را نپذیرفته",
                    $"{Uri.UnescapeDataString(r.Url)}\nاعلام‌شده: {Uri.UnescapeDataString(r.UserCanonical)}\nانتخاب گوگل: {Uri.UnescapeDataString(r.GoogleCanonical)}");
            }
        }
        await Alerts.ResolveOthersAsync(db, siteId, ["not_indexed", "canonical_mismatch"], open);
        await db.SaveChangesAsync(ct);
        if (due.Count > 0) log.LogInformation("GSC site {SiteId}: inspected {Count} URLs", siteId, due.Count);
    }

    async Task FinishAsync(GscConnection conn, string? error, CancellationToken ct)
    {
        conn.LastRunAt = DateTime.UtcNow;
        conn.LastError = error;
        await db.SaveChangesAsync(ct);
    }

    async Task LoadLookupsAsync(int siteId, CancellationToken ct)
    {
        _queries = new(StringComparer.Ordinal);
        foreach (var q in await db.GscQueries.Where(q => q.SiteId == siteId).Select(q => new { q.Id, q.Text }).ToListAsync(ct))
            _queries.TryAdd(q.Text, q.Id);
        _pages = new(StringComparer.Ordinal);
        foreach (var p in await db.GscPages.Where(p => p.SiteId == siteId).Select(p => new { p.Id, p.Url }).ToListAsync(ct))
            _pages.TryAdd(p.Url, p.Id);
    }

    async Task SyncDayAsync(int siteId, string property, string token, DateOnly day, GoogleApi.Row total, CancellationToken ct)
    {
        var pageRows = await google.QueryAllAsync(token, property, day, day, PageDims, ct);
        var qpRows = await google.QueryAllAsync(token, property, day, day, QueryPageDims, ct);

        await EnsureIdsAsync(siteId, "GscPages", "Url", _pages, pageRows.Select(r => r.Keys[0]).Concat(qpRows.Select(r => r.Keys[1])), 2048, ct);
        await EnsureIdsAsync(siteId, "GscQueries", "Text", _queries, qpRows.Select(r => r.Keys[0]), 1024, ct);

        var date = day.ToDateTime(TimeOnly.MinValue);
        var pages = Bulk.Table(("SiteId", typeof(int)), ("Date", typeof(DateTime)), ("PageId", typeof(int)),
            ("Clicks", typeof(int)), ("Impressions", typeof(int)), ("Position", typeof(float)));
        // Grouping guards the primary keys if two over-long strings were cut to the same lookup entry.
        foreach (var g in pageRows.GroupBy(r => _pages[Cut(r.Keys[0], 2048)]))
            pages.Rows.Add(siteId, date, g.Key, Clicks(g), Impressions(g), Position(g));

        var qp = Bulk.Table(("SiteId", typeof(int)), ("Date", typeof(DateTime)), ("QueryId", typeof(int)), ("PageId", typeof(int)),
            ("Device", typeof(byte)), ("Clicks", typeof(int)), ("Impressions", typeof(int)), ("Position", typeof(float)));
        foreach (var g in qpRows.GroupBy(r => (Q: _queries[Cut(r.Keys[0], 1024)], P: _pages[Cut(r.Keys[1], 2048)], D: ParseDevice(r.Keys[2]))))
            qp.Rows.Add(siteId, date, g.Key.Q, g.Key.P, (byte)g.Key.D, Clicks(g), Impressions(g), Position(g));

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var sqlTx = (SqlTransaction)tx.GetDbTransaction();
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE GscPageDaily WHERE SiteId = {siteId} AND Date = {day}; DELETE GscQueryPageDaily WHERE SiteId = {siteId} AND Date = {day}", ct);
        await Bulk.InsertAsync(db, "GscPageDaily", pages, sqlTx, ct);
        await Bulk.InsertAsync(db, "GscQueryPageDaily", qp, sqlTx, ct);
        var daily = db.GscDaily.Add(new GscDaily { SiteId = siteId, Date = day, Clicks = (int)total.Clicks, Impressions = (int)total.Impressions, Position = (float)total.Position });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        daily.State = EntityState.Detached; // keep the change tracker small during a 480-day backfill
    }

    static int Clicks(IEnumerable<GoogleApi.Row> g) => (int)g.Sum(r => r.Clicks);
    static int Impressions(IEnumerable<GoogleApi.Row> g) => (int)g.Sum(r => r.Impressions);
    static float Position(IEnumerable<GoogleApi.Row> g)
    {
        var imp = g.Sum(r => r.Impressions);
        return imp == 0 ? 0 : (float)(g.Sum(r => r.Position * r.Impressions) / imp);
    }

    /// <summary>Adds unseen strings to a lookup table and loads their new ids into the in-memory map.</summary>
    async Task EnsureIdsAsync(int siteId, string table, string column, Dictionary<string, int> map, IEnumerable<string> values, int maxLen, CancellationToken ct)
    {
        var fresh = values.Select(v => Cut(v, maxLen)).Where(v => !map.ContainsKey(v)).Distinct(StringComparer.Ordinal).ToList();
        if (fresh.Count == 0) return;

        var maxId = map.Count == 0 ? 0 : map.Values.Max();
        var t = Bulk.Table(("SiteId", typeof(int)), (column, typeof(string)));
        foreach (var v in fresh) t.Rows.Add(siteId, v);
        await db.Database.OpenConnectionAsync(ct);
        await Bulk.InsertAsync(db, table, t, null, ct);

        var conn = (SqlConnection)db.Database.GetDbConnection();
        await using var cmd = new SqlCommand($"SELECT Id, [{column}] FROM {table} WHERE SiteId = @s AND Id > @m", conn);
        cmd.Parameters.AddWithValue("@s", siteId);
        cmd.Parameters.AddWithValue("@m", maxId);
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct)) map.TryAdd(r.GetString(1), r.GetInt32(0));
    }

    static string Cut(string s, int max) => s.Length > max ? s[..max] : s;

    static Device ParseDevice(string s) => s switch { "MOBILE" => Device.Mobile, "TABLET" => Device.Tablet, _ => Device.Desktop };
}
