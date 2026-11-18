using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkFlow.Mvc.Models;
using WorkFlow.Mvc.ViewModels;

namespace WorkFlow.Mvc.Controllers;

[Authorize]
[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public sealed class AccountController(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn) : Controller
{
    private const string LoginFailure = "Giriş yapılamadı. Bilgilerinizi kontrol edin veya bir süre sonra tekrar deneyin.";

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null) => View(new LoginViewModel { ReturnUrl = SafeReturnUrl(returnUrl) });

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        model.ReturnUrl = SafeReturnUrl(model.ReturnUrl);
        if (!ModelState.IsValid) return View(model);
        var result = await signIn.PasswordSignInAsync(model.Email.Trim(), model.Password, model.RememberMe, lockoutOnFailure: true);
        if (result.Succeeded) return RedirectAfterSignIn(model.ReturnUrl);
        ModelState.AddModelError(string.Empty, LoginFailure);
        return View(model);
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Register(string? returnUrl = null) => View(new RegisterViewModel { ReturnUrl = SafeReturnUrl(returnUrl) });

    [AllowAnonymous]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        model.ReturnUrl = SafeReturnUrl(model.ReturnUrl);
        if (!ModelState.IsValid) return View(model);
        var email = model.Email.Trim();
        var user = new ApplicationUser { UserName = email, Email = email };
        IdentityResult result;
        try { result = await users.CreateAsync(user, model.Password); }
        catch (DbUpdateException)
        {
            ModelState.AddModelError(string.Empty, "Kayıt tamamlanamadı. Bilgilerinizi kontrol edin veya giriş yapmayı deneyin.");
            return View(model);
        }
        if (result.Succeeded)
        {
            await signIn.SignInAsync(user, isPersistent: false);
            return RedirectAfterSignIn(model.ReturnUrl);
        }
        foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
        return View(model);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        return View(new ProfileViewModel { Email = user.Email ?? string.Empty, DisplayName = user.DisplayName });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel model)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        model.Email = user.Email ?? string.Empty;
        if (!ModelState.IsValid) return View(model);

        user.DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? null : model.DisplayName.Trim();
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Profil güncellenemedi. Lütfen bilgilerinizi kontrol edip tekrar deneyin.");
            return View(model);
        }

        await signIn.RefreshSignInAsync(user);
        TempData["Success"] = "Görünen adınız güncellendi.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpGet]
    public IActionResult Settings() => View();

    private string? SafeReturnUrl(string? url) => !string.IsNullOrEmpty(url) && Url.IsLocalUrl(url) ? url : null;
    private IActionResult RedirectAfterSignIn(string? url) => SafeReturnUrl(url) is { } local
        ? LocalRedirect(local) : RedirectToAction("Index", "Tasks");
}
