using System.Globalization;
using System.Security.Claims;
using AutopilotSeo.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AutopilotSeo.Web.Pages.Panel.Sites;

public class PerformanceModel(AppDbContext db) : PageModel
{
    public const int Period = 28, ChartDays = 90;

    public record Totals(long Clicks, long Impressions, double Position)
    {
        public double Ctr => Impressions == 0 ? 0 : (double)Clicks / Impressions;
    }
    public record TopRow(string Label, long Clicks, long Impressions, double Position)
    {
        public double Ctr => Impressions == 0 ? 0 : (double)Clicks / Impressions;
    }
    public record Band(double X, double Width, string Title);

    public Site Site { get; private set; } = null!;
    public GscConnection? Conn { get; private set; }
    public int SyncedDays { get; private set; }
    public int WindowDays { get; private set; }
    public DateOnly? Latest { get; private set; }
    public Totals Cur { get; private set; } = new(0, 0, 0);
    public Totals Prev { get; private set; } = new(0, 0, 0);
    public long AnonymousClicks { get; private set; }
    public List<TopRow> Queries { get; private set; } = [];
    public List<TopRow> Pages { get; private set; } = [];

    // Chart geometry (viewBox 720 × 220, plot area 40..710 × 16..190).
    public string ClicksPath { get; private set; } = "";
    public int ChartMax { get; private set; }
    public List<Band> Bands { get; private set; } = [];
    public DateOnly ChartStart { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var site = await db.Sites.FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == uid);
        if (site is null) return NotFound();
        Site = site;
        ViewData["Site"] = site;
        Conn = await db.GscConnections.FirstOrDefaultAsync(c => c.SiteId == id);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var windowStart = GscRules.WindowStart(today);
        WindowDays = today.AddDays(-3).DayNumber - windowStart.DayNumber + 1; // final data lags ~3 days
        SyncedDays = await db.GscDaily.CountAsync(d => d.SiteId == id && d.Date >= windowStart);
        Latest = await db.GscDaily.Where(d => d.SiteId == id && d.Impressions > 0).MaxAsync(d => (DateOnly?)d.Date);
        if (Latest is not { } end) return Page();

        ChartStart = end.AddDays(-(ChartDays - 1));
        var daily = await db.GscDaily.Where(d => d.SiteId == id && d.Date >= ChartStart && d.Date <= end).ToListAsync();
        var curFrom = end.AddDays(-(Period - 1));
        var prevFrom = curFrom.AddDays(-Period);
        Cur = Sum(daily.Where(d => d.Date >= curFrom));
        Prev = Sum(daily.Where(d => d.Date >= prevFrom && d.Date < curFrom));

        var qp = db.GscQueryPageDaily.Where(x => x.SiteId == id && x.Date >= curFrom && x.Date <= end);
        AnonymousClicks = Math.Max(0, Cur.Clicks - await qp.SumAsync(x => (long)x.Clicks));

        // ponytail: query position is impression-weighted across pages/devices, close to but not exactly GSC's query view.
        var topQ = await qp.GroupBy(x => x.QueryId)
            .Select(g => new { g.Key, C = g.Sum(x => (long)x.Clicks), I = g.Sum(x => (long)x.Impressions), P = g.Sum(x => (double)x.Position * x.Impressions) })
            .OrderByDescending(x => x.C).ThenByDescending(x => x.I).Take(15).ToListAsync();
        var qIds = topQ.Select(x => x.Key).ToList();
        var qText = await db.GscQueries.Where(q => qIds.Contains(q.Id)).ToDictionaryAsync(q => q.Id, q => q.Text);
        Queries = topQ.Select(x => new TopRow(qText[x.Key], x.C, x.I, x.I == 0 ? 0 : x.P / x.I)).ToList();

        var topP = await db.GscPageDaily.Where(x => x.SiteId == id && x.Date >= curFrom && x.Date <= end)
            .GroupBy(x => x.PageId)
            .Select(g => new { g.Key, C = g.Sum(x => (long)x.Clicks), I = g.Sum(x => (long)x.Impressions), P = g.Sum(x => (double)x.Position * x.Impressions) })
            .OrderByDescending(x => x.C).ThenByDescending(x => x.I).Take(15).ToListAsync();
        var pIds = topP.Select(x => x.Key).ToList();
        var pUrl = await db.GscPages.Where(p => pIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Url);
        Pages = topP.Select(x => new TopRow(pUrl[x.Key], x.C, x.I, x.I == 0 ? 0 : x.P / x.I)).ToList();

        BuildChart(daily, end);
        var notes = await db.Annotations
            .Where(a => (a.SiteId == null || a.SiteId == id) && a.StartDate <= end && (a.EndDate ?? a.StartDate) >= ChartStart)
            .ToListAsync();
        Bands = notes.Select(a =>
        {
            var from = X(a.StartDate < ChartStart ? ChartStart : a.StartDate);
            var to = X((a.EndDate ?? a.StartDate) > end ? end : a.EndDate ?? a.StartDate);
            return new Band(from, Math.Max(2, to - from), a.Title + (a.Note is null ? "" : " — " + a.Note));
        }).ToList();
        return Page();
    }

    static Totals Sum(IEnumerable<GscDaily> rows)
    {
        var list = rows.ToList();
        long c = list.Sum(d => (long)d.Clicks), i = list.Sum(d => (long)d.Impressions);
        return new Totals(c, i, i == 0 ? 0 : list.Sum(d => (double)d.Position * d.Impressions) / i);
    }

    double X(DateOnly d) => 40 + 670.0 * (d.DayNumber - ChartStart.DayNumber) / (ChartDays - 1);

    void BuildChart(List<GscDaily> daily, DateOnly end)
    {
        var byDate = daily.ToDictionary(d => d.Date, d => d.Clicks);
        ChartMax = Math.Max(1, daily.Count == 0 ? 1 : daily.Max(d => d.Clicks));
        var pts = new List<string>();
        for (var d = ChartStart; d <= end; d = d.AddDays(1))
        {
            var y = 190 - 174.0 * byDate.GetValueOrDefault(d) / ChartMax;
            pts.Add(string.Create(CultureInfo.InvariantCulture, $"{X(d):0.#},{y:0.#}"));
        }
        ClicksPath = string.Join(' ', pts);
    }

    /// <summary>Relative change as a signed Persian percent (rendered inside an LTR isolate so the sign stays in front).</summary>
    public static (string Text, string Css) Delta(double cur, double prev)
    {
        if (prev == 0) return ("", "");
        var change = (cur - prev) / prev;
        var text = (change >= 0 ? "+" : "−") + Fa.Dec(Math.Abs(change * 100)) + "٪";
        return (text, Math.Abs(change) < 0.005 ? "delta" : change > 0 ? "delta delta--up" : "delta delta--down");
    }

    /// <summary>Average position moves in places, not percent; a lower number is better.</summary>
    public static (string Text, string Css) PositionDelta(double cur, double prev)
    {
        if (prev == 0 || cur == 0) return ("", "");
        var diff = prev - cur;
        if (Math.Abs(diff) < 0.05) return ("بدون تغییر", "delta");
        return ($"{Fa.Dec(Math.Abs(diff))} پله {(diff > 0 ? "بهتر" : "بدتر")}", diff > 0 ? "delta delta--up" : "delta delta--down");
    }
}
