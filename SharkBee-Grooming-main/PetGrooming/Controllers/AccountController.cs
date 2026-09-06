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
    IConfiguration config,
    IWebHostEnvironment en) : Controller
{
    private const string VerificationPrefix = "Student1.EmailVerification:";
    private const string PasswordResetPrefix = "Student1.PasswordReset:";

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
            if (IssueEmailVerification(member.Email, member.Name))
            {
                TempData["Info"] = "Registration successful. Please verify your email before logging in.";
                return RedirectToAction(nameof(VerificationSent));
            }

            TempData["Info"] = "Account created. Email verification is enabled but SMTP is not configured, so the account was created without verification for local development.";
        }

        await hp.SignInAsync(member.Email, member.Role, false);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    public IActionResult VerificationSent() => View();

    // ------------------------------------------------------------------------
    // Resend verification email
    // ------------------------------------------------------------------------
    // Same shape as ForgotPassword below: always show a generic confirmation so
    // this endpoint cannot be used to probe which addresses have an account.

    [HttpGet]
    public IActionResult ResendVerification()
    {
        ViewBag.CaptchaQuestion = captcha.Generate(HttpContext);
        return View(new ResetPasswordVM());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendVerification(ResetPasswordVM vm)
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
        var member = await db.Members.FirstOrDefaultAsync(m => m.Email.ToLower() == email);

        // Same message whether or not the account exists/was already verified,
        // except in Development where SMTP is typically unconfigured -- there
        // we surface the link directly so the flow stays testable locally.
        TempData["Info"] = "If that email belongs to an unverified account, a new verification link has been sent.";

        if (member != null && !member.Blocked && !member.EmailVerified)
        {
            if (en.IsDevelopment() && !hp.IsEmailConfigured())
            {
                string token = Guid.NewGuid().ToString("N");
                cache.Set(VerificationPrefix + token,
                    new EmailVerificationTicket(member.Email, DateTimeOffset.UtcNow.AddHours(24)),
                    TimeSpan.FromHours(24));
                TempData["Info"] = $"SMTP is not configured, so here's the dev link: /Account/VerifyEmail?token={token}";
            }
            else
            {
                IssueEmailVerification(member.Email, member.Name);
            }
        }

        return RedirectToAction(nameof(Login));
    }

    // ------------------------------------------------------------------------
    // Forgot password
    // ------------------------------------------------------------------------

    [HttpGet]
    public IActionResult ForgotPassword()
    {
        ViewBag.CaptchaQuestion = captcha.Generate(HttpContext);
        return View(new ResetPasswordVM());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ResetPasswordVM vm)
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
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);

        // Deliberately generic: does not reveal whether the address is
        // registered, blocked, or the send failed -- except in Development
        // where SMTP is typically unconfigured, so we surface the link
        // directly to keep the flow testable locally.
        TempData["Info"] = "If that email is registered, a password reset link has been sent and will expire in 1 hour.";

        // Admin, Staff and Member can all reset -- whoever forgot the password,
        // not just members.
        if (user != null && !user.Blocked)
        {
            string token = Guid.NewGuid().ToString("N");
            cache.Set(PasswordResetPrefix + token,
                new PasswordResetTicket(user.Email, DateTimeOffset.UtcNow.AddHours(1)),
                TimeSpan.FromHours(1));

            if (en.IsDevelopment() && !hp.IsEmailConfigured())
            {
                TempData["Info"] = $"SMTP is not configured, so here's the dev link: /Account/ResetPassword?token={token}";
            }
            else
            {
                TrySendPasswordResetEmail(user.Email, user.Name, token);
            }
        }

        return RedirectToAction(nameof(Login));
    }

    [HttpGet]
    public IActionResult ResetPassword(string token)
    {
        if (!cache.TryGetValue<PasswordResetTicket>(PasswordResetPrefix + token, out var ticket)
            || ticket == null || ticket.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return Content("This password reset link is invalid or has expired. Please request a new one.");
        }

        return View(new SetNewPasswordVM { Token = token });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(SetNewPasswordVM vm)
    {
        if (!cache.TryGetValue<PasswordResetTicket>(PasswordResetPrefix + vm.Token, out var ticket)
            || ticket == null || ticket.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            cache.Remove(PasswordResetPrefix + vm.Token);
            return Content("This password reset link is invalid or has expired. Please request a new one.");
        }

        if (!ModelState.IsValid)
        {
            return View(vm);
        }

        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == ticket.Email);

        // Token is single-use either way, so the account cannot be reset again
        // through a link that was already spent or has since been blocked.
        cache.Remove(PasswordResetPrefix + vm.Token);

        if (user == null || user.Blocked)
        {
            return Content("This account cannot be reset.");
        }

        user.Hash = hp.HashPassword(vm.Password);
        await db.SaveChangesAsync();

        loginSecurity.Clear(user.Email);

        TempData["Info"] = "Your password has been reset. Please log in with your new password.";
        return RedirectToAction(nameof(Login));
    }

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

    // Creates the verification ticket, caches it and emails the link. Used both
    // right after Register and from ResendVerification. Cleans up the cache
    // entry (rather than leaving an unusable one behind) if the send fails.
    private bool IssueEmailVerification(string email, string name)
    {
        string token = Guid.NewGuid().ToString("N");
        cache.Set(VerificationPrefix + token,
            new EmailVerificationTicket(email, DateTimeOffset.UtcNow.AddHours(24)),
            TimeSpan.FromHours(24));

        if (TrySendVerificationEmail(email, name, token)) return true;

        cache.Remove(VerificationPrefix + token);
        return false;
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

    private bool TrySendPasswordResetEmail(string email, string name, string token)
    {
        try
        {
            string baseUrl = $"{Request.Scheme}://{Request.Host}";
            string link = $"{baseUrl}/Account/ResetPassword?token={Uri.EscapeDataString(token)}";
            using var mail = new MailMessage
            {
                Subject = "Reset your SharkBee Grooming password",
                Body = $"Hello {name},\n\nWe received a request to reset your password. Use this link:\n{link}\n\nThe link expires in 1 hour. If you did not request this, you can ignore this email.",
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
