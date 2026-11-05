using System.Security.Claims;
using AutopilotSeo.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AutopilotSeo.Web.Pages.Panel;

public class IndexModel(AppDbContext db) : PageModel
{
    public record Row(Site Site, int Score, bool GatePassed, bool Approved);

    public List<Row> Rows { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Rows = await db.Sites.Where(s => s.OwnerId == uid)
            .OrderBy(s => s.CreatedAt)
            .Select(s => new
            {
                Site = s,
                Latest = s.Profiles.OrderByDescending(p => p.Version).Select(p => new { p.Score, p.GatePassed }).FirstOrDefault(),
                Approved = s.Profiles.Any(p => p.ApprovedAt != null),
            })
            .Select(x => new Row(x.Site, x.Latest == null ? 0 : x.Latest.Score, x.Latest != null && x.Latest.GatePassed, x.Approved))
            .ToListAsync();
    }
}
