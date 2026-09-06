using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetGrooming.Models;

namespace PetGrooming.Controllers;

// "My Account" for whoever is logged in -- Member, Staff or Admin. Unlike
// AdminUsersController (which lets an admin edit anyone), this only ever
// touches the currently signed-in user's own row.
[Authorize]
public class ProfileController(DB db, Helper hp) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var user = await CurrentUser();
        if (user == null) return NotFound();

        return View(ToVM(user));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(UpdateProfileVM vm)
    {
        var user = await CurrentUser();
        if (user == null) return NotFound();

        // Phone only applies to Members -- clear any validation noise from it
        // for Staff/Admin, whose form does not render that field at all.
        if (user is not Member) ModelState.Remove(nameof(vm.Phone));

        if (!ModelState.IsValid)
        {
            vm.Email = user.Email;
            vm.PhotoURL = user.PhotoURL;
            return View(vm);
        }

        user.Name = vm.Name?.Trim() ?? user.Name;

        if (user is Member member)
            member.Phone = vm.Phone?.Trim() ?? "";

        if (vm.Photo != null && vm.Photo.Length > 0)
        {
            var error = hp.ValidatePhoto(vm.Photo);
            if (!string.IsNullOrEmpty(error))
            {
                ModelState.AddModelError(nameof(vm.Photo), error);
                vm.Email = user.Email;
                vm.PhotoURL = user.PhotoURL;
                return View(vm);
            }

            if (!string.IsNullOrEmpty(user.PhotoURL))
                hp.DeletePhoto(user.PhotoURL, "photos/users");

            user.PhotoURL = hp.SavePhoto(vm.Photo, "photos/users");
        }

        await db.SaveChangesAsync();
        TempData["Info"] = "Your profile has been updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public IActionResult ChangePassword() => View(new UpdatePasswordVM());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(UpdatePasswordVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await CurrentUser();
        if (user == null) return NotFound();

        if (!hp.VerifyPassword(user.Hash, vm.Current))
        {
            ModelState.AddModelError(nameof(vm.Current), "Current password is incorrect.");
            return View(vm);
        }

        user.Hash = hp.HashPassword(vm.New);
        await db.SaveChangesAsync();

        TempData["Info"] = "Your password has been changed.";
        return RedirectToAction(nameof(Index));
    }

    private Task<User?> CurrentUser()
    {
        var email = User.Identity?.Name;
        return db.Users.FirstOrDefaultAsync(u => u.Email == email);
    }

    private static UpdateProfileVM ToVM(User user) => new()
    {
        Email = user.Email,
        Name = user.Name,
        Phone = (user as Member)?.Phone,
        PhotoURL = user.PhotoURL,
    };
}
