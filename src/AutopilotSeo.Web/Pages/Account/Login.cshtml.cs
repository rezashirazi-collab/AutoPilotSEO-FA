using System.ComponentModel.DataAnnotations;
using AutopilotSeo.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AutopilotSeo.Web.Pages.Account;

public class LoginModel(SignInManager<AppUser> signIn) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public class InputModel
    {
        [Required(ErrorMessage = "ایمیل را وارد کنید.")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "رمز عبور را وارد کنید.")]
        public string Password { get; set; } = "";

        public bool RememberMe { get; set; } = true;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var result = await signIn.PasswordSignInAsync(Input.Email, Input.Password, Input.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded)
            return LocalRedirect(Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : "/panel");

        ModelState.AddModelError("", result.IsLockedOut
            ? "به دلیل تلاش‌های ناموفق، حساب چند دقیقه قفل شد. کمی بعد دوباره امتحان کنید."
            : "ایمیل یا رمز عبور درست نیست.");
        return Page();
    }
}
