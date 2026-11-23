using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WorkFlow.Mvc.Models;
using WorkFlow.Mvc.Services;
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
        var user = new ApplicationUser { UserName = email, Email = email, JoinedAtUtc = DateTimeOffset.UtcNow };
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
        return RedirectToAction(nameof(HomeController.Index), "Home");
    }

    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        return View(PrepareProfile(user, new ProfileViewModel { DisplayName = user.DisplayName, AboutMe = user.AboutMe }));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Profile(ProfileViewModel model)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        PrepareProfile(user, model);
        if (!ModelState.IsValid) return View(model);

        user.DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? null : model.DisplayName.Trim();
        user.AboutMe = string.IsNullOrWhiteSpace(model.AboutMe) ? null : model.AboutMe.Trim();
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, "Profil güncellenemedi. Lütfen bilgilerinizi kontrol edip tekrar deneyin.");
            return View(model);
        }

        await signIn.RefreshSignInAsync(user);
        TempData["Success"] = "Profiliniz güncellendi.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpGet]
    public async Task<IActionResult> Photo(Guid id)
    {
        var user = await users.GetUserAsync(User);
        if (user is null || user.PhotoKey != id || user.PhotoData is not { Length: > 0 })
            return NotFound();
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return File(user.PhotoData, "image/jpeg");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(ProfilePhoto.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = ProfilePhoto.MaxRequestBytes)]
    public async Task<IActionResult> UploadPhoto(IFormFile? photo, CancellationToken cancellationToken)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();

        var normalized = await ProfilePhoto.NormalizeAsync(photo, cancellationToken);
        if (normalized is null)
        {
            TempData["Error"] = "En fazla 2 MB boyutunda, geçerli bir JPEG veya PNG seçin. Genişlik ve yükseklik en fazla 2048 piksel olabilir.";
            return RedirectToAction(nameof(Profile));
        }
        user.PhotoKey = Guid.NewGuid();
        user.PhotoData = normalized;
        return await SavePhotoAsync(user, "Profil fotoğrafınız güncellendi.");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RemovePhoto()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (user.PhotoKey is null) return RedirectToAction(nameof(Profile));
        user.PhotoKey = null;
        user.PhotoData = null;
        return await SavePhotoAsync(user, "Profil fotoğrafınız kaldırıldı.");
    }

    private async Task<IActionResult> SavePhotoAsync(ApplicationUser user, string success)
    {
        if ((await users.UpdateAsync(user)).Succeeded)
        {
            await signIn.RefreshSignInAsync(user);
            TempData["Success"] = success;
        }
        else
        {
            TempData["Error"] = "Profil fotoğrafı kaydedilemedi. Lütfen tekrar deneyin.";
        }
        return RedirectToAction(nameof(Profile));
    }

    private ProfileViewModel PrepareProfile(ApplicationUser user, ProfileViewModel model)
    {
        ViewData["SavedDisplayName"] = user.DisplayName;
        model.Email = user.Email ?? string.Empty;
        model.JoinedAtUtc = user.JoinedAtUtc;
        model.PhotoKey = user.PhotoKey;
        var words = user.DisplayName?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        model.Initials = words is { Length: > 0 }
            ? string.Concat(words.Take(2).Select(word => StringInfo.GetNextTextElement(word))).ToUpperInvariant()
            : (model.Email.Length > 0 ? StringInfo.GetNextTextElement(model.Email).ToUpperInvariant() : "?");
        return model;
    }

    [HttpGet]
    public IActionResult Settings() => View(new ChangePasswordViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword([FromForm] ChangePasswordViewModel model)
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();

        if (!ModelState.IsValid) return View(nameof(Settings), new ChangePasswordViewModel());

        var result = await users.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                var field = error.Code switch
                {
                    nameof(IdentityErrorDescriber.PasswordMismatch) => nameof(model.CurrentPassword),
                    _ when error.Code.StartsWith("Password", StringComparison.Ordinal) => nameof(model.NewPassword),
                    _ => string.Empty
                };
                ModelState.AddModelError(field, error.Description);
            }
            return View(nameof(Settings), new ChangePasswordViewModel());
        }

        await signIn.RefreshSignInAsync(user);
        TempData["Success"] = "Parolanız güncellendi.";
        return RedirectToAction(nameof(Settings));
    }

    private string? SafeReturnUrl(string? url) => !string.IsNullOrEmpty(url) && Url.IsLocalUrl(url) ? url : null;
    private IActionResult RedirectAfterSignIn(string? url) => SafeReturnUrl(url) is { } local
        ? LocalRedirect(local) : RedirectToAction("Index", "Tasks");
}
