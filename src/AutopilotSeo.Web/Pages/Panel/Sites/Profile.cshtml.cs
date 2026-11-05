using System.Security.Claims;
using AutopilotSeo.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AutopilotSeo.Web.Pages.Panel.Sites;

public class ProfileModel(AppDbContext db) : PageModel
{
    const int MaxRows = 50;

    [BindProperty] public ProfileData Data { get; set; } = new();

    public Site Site { get; private set; } = null!;
    public BusinessProfile Latest { get; private set; } = null!;
    public int? ApprovedVersion { get; private set; }
    public Completeness Status { get; private set; } = null!;

    string Uid => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    async Task<bool> LoadAsync(int id)
    {
        var site = await db.Sites.Include(s => s.Profiles).FirstOrDefaultAsync(s => s.Id == id && s.OwnerId == Uid);
        if (site is null) return false;
        Site = site;
        Latest = site.Profiles.MaxBy(p => p.Version)!;
        ApprovedVersion = site.Profiles.Where(p => p.ApprovedAt != null).Max(p => (int?)p.Version);
        ViewData["Site"] = site;
        return true;
    }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id)) return NotFound();
        Data = Latest.Data;
        Status = ProfileRules.Evaluate(Data);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, bool approve)
    {
        if (!await LoadAsync(id)) return NotFound();

        // Model binding turns empty inputs into null; the stored JSON keeps "" so readers never see null.
        Data.Products = Data.Products.Where(p => !string.IsNullOrWhiteSpace(p.Name)).Take(MaxRows)
            .Select(p => { p.Priority = Math.Clamp(p.Priority, 1, 5); p.Url = p.Url?.Trim() ?? ""; return p; }).ToList();
        Data.Competitors = Data.Competitors.Where(c => !string.IsNullOrWhiteSpace(c.Domain)).Take(MaxRows)
            .Select(c => { c.Domain = Domains.Normalize(c.Domain) ?? c.Domain.Trim(); c.Note ??= ""; return c; }).ToList();
        Status = ProfileRules.Evaluate(Data);

        if (approve && !Status.GatePassed)
        {
            ModelState.AddModelError("", "برای تأیید، موارد الزامی را کامل کنید.");
            return Page();
        }

        if (Latest.ApprovedAt is not null && JsonEquals(Latest.Data, Data))
        {
            TempData["Notice"] = "تغییری نسبت به نسخهٔ تأییدشده نبود.";
            return Redirect($"/panel/sites/{id}/profile");
        }

        // Approved versions are frozen: editing one starts the next draft version.
        var target = Latest;
        if (Latest.ApprovedAt is not null)
        {
            target = new BusinessProfile { SiteId = id, Version = Latest.Version + 1 };
            db.BusinessProfiles.Add(target);
        }
        target.Data = Data;
        target.Score = Status.Score;
        target.GatePassed = Status.GatePassed;
        target.UpdatedAt = DateTime.UtcNow;
        db.Log(id, Uid, "profile.saved", new { target.Version, Status.Score });

        if (approve)
        {
            target.ApprovedAt = DateTime.UtcNow;
            target.ApprovedById = Uid;
            db.Log(id, Uid, "profile.approved", new { target.Version });
        }

        await db.SaveChangesAsync();
        TempData["Notice"] = approve
            ? $"نسخهٔ {Fa.Num(target.Version)} پرونده تأیید شد."
            : Status.GatePassed ? "ذخیره شد. پرونده آمادهٔ تأیید است." : "پیش‌نویس ذخیره شد.";
        return Redirect($"/panel/sites/{id}/profile");
    }

    static bool JsonEquals(ProfileData a, ProfileData b) =>
        System.Text.Json.JsonSerializer.Serialize(a) == System.Text.Json.JsonSerializer.Serialize(b);
}
