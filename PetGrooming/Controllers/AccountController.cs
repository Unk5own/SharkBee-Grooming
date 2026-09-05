using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Net.Mail;
using PetGrooming.Models;

namespace PetGrooming.Controllers;

// STUDENT 1 — Access & User Management
// Manual cookie authentication only. ASP.NET Core Identity is NOT used.
public class AccountController(
    DB db,
    Helper hp,
    LoginSecurityService loginSecurity,
    CaptchaService captcha,
    IMemoryCache cache,
    IConfiguration config) : Controller
{
    private const string VerificationPrefix = "Student1.EmailVerification:";

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;
        ViewBag.CaptchaQuestion = captcha.Generate(HttpContext);
        return View(new LoginVM());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginVM vm, string? returnUrl = null)
    {
        ViewBag.ReturnUrl = returnUrl;

        if (!ModelState.IsValid)
        {
            ViewBag.CaptchaQuestion = captcha.Generate(HttpContext);
            return View(vm);
        }

        string email = (vm.Email ?? "").Trim().ToLowerInvariant();

        if (loginSecurity.IsTemporarilyBlocked(email, out var blockedUntil))
        {
            ModelState.AddModelError("", $"Too many failed attempts. Try again after {blockedUntil.LocalDateTime:HH:mm}.");
            ViewBag.CaptchaQuestion = captcha.Generate(HttpContext);
            return View(vm);
        }

        if (!captcha.Validate(HttpContext, vm.CaptchaAnswer))
        {
            ModelState.AddModelError(nameof(vm.CaptchaAnswer), "Incorrect security check.");
            ViewBag.CaptchaQuestion = captcha.Generate(HttpContext);
            return View(vm);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);

        if (user == null || !hp.VerifyPassword(user.Hash, vm.Password))
        {
            int failures = loginSecurity.RecordFailure(email, out var temporaryBlock);
            if (temporaryBlock.HasValue)
            {
                ModelState.AddModelError("", "3 failed attempts. Login is temporarily blocked for 10 minutes.");
            }
            else
            {
                ModelState.AddModelError("", $"Invalid email or password. Failed attempts: {failures}/3.");
            }

            ViewBag.CaptchaQuestion = captcha.Generate(HttpContext);
            return View(vm);
        }

        loginSecurity.Clear(email);

        if (user.Blocked)
        {
            ModelState.AddModelError("", "This account is blocked. Please contact an administrator.");
            ViewBag.CaptchaQuestion = captcha.Generate(HttpContext);
            return View(vm);
        }

        if (config.GetValue<bool>("Security:RequireEmailVerification") && !user.EmailVerified)
        {
            ModelState.AddModelError("", "Please verify your email address before logging in.");
            ViewBag.CaptchaQuestion = captcha.Generate(HttpContext);
            return View(vm);
        }

        await hp.SignInAsync(user.Email, user.Role, vm.RememberMe);

        if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult Register()
    {
        ViewBag.CaptchaQuestion = captcha.Generate(HttpContext);
        return View(new RegisterVM());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterVM vm)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.CaptchaQuestion = captcha.Generate(HttpContext);
            return View(vm);
        }

        if (!captcha.Validate(HttpContext, vm.CaptchaAnswer))
        {
            ModelState.AddModelError(nameof(vm.CaptchaAnswer), "Incorrect security check.");
            ViewBag.CaptchaQuestion = captcha.Generate(HttpContext);
            return View(vm);
        }

        string email = (vm.Email ?? "").Trim().ToLowerInvariant();
        if (await db.Users.AnyAsync(u => u.Email.ToLower() == email))
        {
            ModelState.AddModelError(nameof(vm.Email), "Duplicated email.");
            ViewBag.CaptchaQuestion = captcha.Generate(HttpContext);
            return View(vm);
        }

        var member = new Member
        {
            Email = email,
            Hash = hp.HashPassword(vm.Password),
            Name = vm.Name?.Trim() ?? "",
            Phone = vm.Phone?.Trim() ?? "",
            PhotoURL = "",
            Blocked = false,
            EmailVerified = false
        };

        // Normal file upload takes priority; webcam is used when no file was selected.
        if (vm.Photo != null && vm.Photo.Length > 0)
        {
            var error = hp.ValidatePhoto(vm.Photo);
            if (!string.IsNullOrEmpty(error))
            {
                ModelState.AddModelError(nameof(vm.Photo), error);
                ViewBag.CaptchaQuestion = captcha.Generate(HttpContext);
                return View(vm);
            }

            member.PhotoURL = hp.SavePhoto(vm.Photo, "photos/users");
        }
        else if (!string.IsNullOrWhiteSpace(vm.WebcamPhoto))
        {
            try
            {
                var webcam = DecodeWebcamPhoto(vm.WebcamPhoto);
                if (webcam != null)
                    member.PhotoURL = hp.SavePhoto(webcam, "photos/users");
            }
            catch
            {
                ModelState.AddModelError(nameof(vm.WebcamPhoto), "The webcam photo is invalid.");
                ViewBag.CaptchaQuestion = captcha.Generate(HttpContext);
                return View(vm);
            }
        }

        db.Members.Add(member);
        await db.SaveChangesAsync();

        bool requireVerification = config.GetValue<bool>("Security:RequireEmailVerification");
        if (requireVerification)
        {
            string token = Guid.NewGuid().ToString("N");
            cache.Set(VerificationPrefix + token,
                new EmailVerificationTicket(member.Email, DateTimeOffset.UtcNow.AddHours(24)),
                TimeSpan.FromHours(24));

            if (TrySendVerificationEmail(member.Email, member.Name, token))
            {
                TempData["Info"] = "Registration successful. Please verify your email before logging in.";
                return RedirectToAction(nameof(VerificationSent));
            }

            // Do not leave a user locked out when SMTP has not been configured.
            cache.Remove(VerificationPrefix + token);
            TempData["Info"] = "Account created. Email verification is enabled but SMTP is not configured, so the account was created without verification for local development.";
        }

        await hp.SignInAsync(member.Email, member.Role, false);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult VerificationSent() => View();

    [HttpGet]
    public async Task<IActionResult> VerifyEmail(string token)
    {
        if (!cache.TryGetValue<EmailVerificationTicket>(VerificationPrefix + token, out var ticket) || ticket == null)
            return Content("This verification link is invalid or has expired.");

        if (ticket.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            cache.Remove(VerificationPrefix + token);
            return Content("This verification link has expired.");
        }

        var member = await db.Members.FirstOrDefaultAsync(m => m.Email == ticket.Email);
        cache.Remove(VerificationPrefix + token);

        if (member == null || member.Blocked)
            return Content("This account cannot be verified.");

        member.EmailVerified = true;
        await db.SaveChangesAsync();

        await hp.SignInAsync(member.Email, member.Role, false);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public async Task<IActionResult> CheckEmail(string email)
        => Json(!await db.Users.AnyAsync(u => u.Email.ToLower() == (email ?? "").Trim().ToLower()));

    [HttpGet]
    public IActionResult AccessDenied() => View();

    // Existing layout uses a normal link, so this remains GET for compatibility.
    [HttpGet]
    public async Task<IActionResult> Logout()
    {
        await hp.SignOutAsync();
        return RedirectToAction("Index", "Home");
    }

    private bool TrySendVerificationEmail(string email, string name, string token)
    {
        try
        {
            string baseUrl = $"{Request.Scheme}://{Request.Host}";
            string link = $"{baseUrl}/Account/VerifyEmail?token={Uri.EscapeDataString(token)}";
            using var mail = new MailMessage
            {
                Subject = "Verify your SharkBee Grooming account",
                Body = $"Hello {name},\n\nPlease verify your account using this link:\n{link}\n\nThe link expires in 24 hours.",
                IsBodyHtml = false,
            };
            mail.To.Add(email);
            hp.SendEmail(mail);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static FormFile? DecodeWebcamPhoto(string dataUrl)
    {
        int comma = dataUrl.IndexOf(',');
        if (comma < 0) return null;

        // header is the part before the comma, so the prefix compared here must
        // not contain one.
        string header = dataUrl[..comma];
        if (!header.StartsWith("data:image/jpeg;base64", StringComparison.OrdinalIgnoreCase))
            return null;

        byte[] bytes = Convert.FromBase64String(dataUrl[(comma + 1)..]);
        if (bytes.Length == 0 || bytes.Length > 1_500_000) return null;

        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "WebcamPhoto", "webcam.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = "image/jpeg"
        };
    }
}
