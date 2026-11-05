using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using AutopilotSeo.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace AutopilotSeo.Web.Pages.Panel.Sites;

public class NewModel(AppDbContext db) : PageModel
{
    [BindProperty, Required(ErrorMessage = "دامنهٔ سایت را وارد کنید.")]
    public string Domain { get; set; } = "";

    [BindProperty, StringLength(100)]
    public string? DisplayName { get; set; }

    public async Task<IActionResult> OnPostAsync()
    {
        var host = Domains.Normalize(Domain);
        if (host is null) ModelState.AddModelError(nameof(Domain), "این دامنه معتبر نیست. نمونه: example.com");
        if (!ModelState.IsValid) return Page();

        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (await db.Sites.AnyAsync(s => s.OwnerId == uid && s.Domain == host))
        {
            ModelState.AddModelError(nameof(Domain), "این سایت را قبلاً اضافه کرده‌ای.");
            return Page();
        }

        var site = new Site { OwnerId = uid, Domain = host!, DisplayName = DisplayName?.Trim() ?? "" };
        site.Profiles.Add(new BusinessProfile());
        db.Sites.Add(site);
        await db.SaveChangesAsync();

        db.Log(site.Id, uid, "site.created", new { site.Domain });
        await db.SaveChangesAsync();

        return Redirect($"/panel/sites/{site.Id}/profile");
    }
}
