using System.ComponentModel.DataAnnotations;
using AutopilotSeo.Core;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace AutopilotSeo.Web.Pages.Account;

public class RegisterModel(UserManager<AppUser> users, SignInManager<AppUser> signIn) : PageModel
{
    [BindProperty] public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "نام را وارد کنید."), StringLength(100)]
        public string FullName { get; set; } = "";

        [Required(ErrorMessage = "ایمیل را وارد کنید."), EmailAddress(ErrorMessage = "ایمیل معتبر نیست.")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "رمز عبور را وارد کنید."), StringLength(100, MinimumLength = 8, ErrorMessage = "رمز عبور باید دست‌کم ۸ کاراکتر باشد.")]
        public string Password { get; set; } = "";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = new AppUser { UserName = Input.Email, Email = Input.Email, FullName = Input.FullName.Trim() };
        var result = await users.CreateAsync(user, Input.Password);
        if (!result.Succeeded)
        {
            foreach (var e in result.Errors)
                ModelState.AddModelError("", e.Code switch
                {
                    "DuplicateUserName" or "DuplicateEmail" => "با این ایمیل قبلاً ثبت‌نام شده است. وارد شوید.",
                    "PasswordRequiresDigit" => "رمز عبور باید دست‌کم یک رقم داشته باشد.",
                    "PasswordRequiresLower" => "رمز عبور باید دست‌کم یک حرف کوچک انگلیسی داشته باشد.",
                    _ => e.Description,
                });
            return Page();
        }

        await signIn.SignInAsync(user, isPersistent: true);
        return RedirectToPage("/Panel/Sites/New");
    }
}
