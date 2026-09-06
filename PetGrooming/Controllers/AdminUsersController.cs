using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetGrooming.Models;

namespace PetGrooming.Controllers;

// PIC: Student 1 (User Maintenance)
[Authorize(Roles = "Admin")]
[Route("Admin/Users")]
public class AdminUsersController(DB db, Helper hp) : Controller
{
    [HttpGet("")]
    [HttpGet("Index")]
    public async Task<IActionResult> Index(string? search, string? role, bool showBlocked = false)
    {
        var query = db.Users.AsQueryable();

        if (!showBlocked)
            query = query.Where(u => !u.Blocked);

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(u => u.Email.Contains(search) || u.Name.Contains(search));
        }

        if (role is "Admin" or "Staff" or "Member")
        {
            query = role switch
            {
                "Admin" => query.OfType<Admin>(),
                "Staff" => query.OfType<Staff>(),
                "Member" => query.OfType<Member>(),
                _ => query
            };
        }

        var users = await query.OrderBy(u => u.Email).ToListAsync();
        ViewBag.Search = search;
        ViewBag.Role = role;
        ViewBag.ShowBlocked = showBlocked;
        return View(users);
    }

    [HttpGet("Create")]
    public IActionResult Create() => View(new UserCreateVM { Role = "Member" });

    [HttpPost("Create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserCreateVM vm)
    {
        if (vm.Role is not ("Admin" or "Staff" or "Member"))
            ModelState.AddModelError(nameof(vm.Role), "Invalid role.");

        if (!ModelState.IsValid) return View(vm);

        if (await db.Users.AnyAsync(u => u.Email == vm.Email))
        {
            ModelState.AddModelError(nameof(vm.Email), "This email is already registered.");
            return View(vm);
        }

        User user = vm.Role switch
        {
            "Admin" => new Admin(),
            "Staff" => new Staff
            {
                Specialization = vm.Specialization ?? "General Grooming",
                HireDate = vm.HireDate ?? DateOnly.FromDateTime(DateTime.Today),
                Active = true
            },
            _ => new Member { Phone = vm.Phone ?? "" }
        };

        user.Email = vm.Email;
        user.Hash = hp.HashPassword(vm.Password);
        user.Name = vm.Name;
        user.PhotoURL = "";
        user.Blocked = false;
        user.EmailVerified = true;

        if (vm.Photo != null && vm.Photo.Length > 0)
        {
            var error = hp.ValidatePhoto(vm.Photo);
            if (!string.IsNullOrEmpty(error))
            {
                ModelState.AddModelError(nameof(vm.Photo), error);
                return View(vm);
            }

            user.PhotoURL = hp.SavePhoto(vm.Photo, "photos/users");
        }

        db.Users.Add(user);
        await db.SaveChangesAsync();

        TempData["Info"] = $"{user.Name} ({user.Role}) has been created successfully.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("Edit/{email}")]
    public async Task<IActionResult> Edit(string email)
    {
        var user = await db.Users.FindAsync(email);
        if (user == null) return NotFound();

        return View(new UserEditVM
        {
            Email = user.Email,
            Name = user.Name,
            Role = user.Role,
            Blocked = user.Blocked,
            Phone = (user as Member)?.Phone,
            Specialization = (user as Staff)?.Specialization,
            HireDate = (user as Staff)?.HireDate,
            ExistingPhotoURL = user.PhotoURL
        });
    }

    [HttpPost("Edit/{email}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string email, UserEditVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var user = await db.Users.FindAsync(email);
        if (user == null) return NotFound();

        user.Name = vm.Name;
        user.Blocked = vm.Blocked;

        if (user is Member member)
        {
            member.Phone = vm.Phone ?? "";
        }
        else if (user is Staff staff)
        {
            staff.Specialization = vm.Specialization ?? "General Grooming";
            staff.HireDate = vm.HireDate ?? staff.HireDate;
            staff.Active = !vm.Blocked;
        }

        if (!string.IsNullOrWhiteSpace(vm.NewPassword))
            user.Hash = hp.HashPassword(vm.NewPassword);

        if (vm.Photo != null && vm.Photo.Length > 0)
        {
            var error = hp.ValidatePhoto(vm.Photo);
            if (!string.IsNullOrEmpty(error))
            {
                ModelState.AddModelError(nameof(vm.Photo), error);
                vm.ExistingPhotoURL = user.PhotoURL;
                return View(vm);
            }

            if (!string.IsNullOrEmpty(user.PhotoURL))
                hp.DeletePhoto(user.PhotoURL, "photos/users");

            user.PhotoURL = hp.SavePhoto(vm.Photo, "photos/users");
        }

        await db.SaveChangesAsync();
        TempData["Info"] = $"{user.Name}'s account has been updated.";
        return RedirectToAction(nameof(Index));
    }

    // The existing DB design intentionally preserves accounts because they are
    // referenced by appointment history. Therefore Delete is a safe soft-delete:
    // the account is blocked rather than physically removed from Users.
    [HttpPost("Delete/{email}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string email)
    {
        var user = await db.Users.FindAsync(email);
        if (user == null) return NotFound();

        if (user.Email == User.Identity?.Name)
        {
            TempData["Info"] = "You cannot block your own account while logged in.";
            return RedirectToAction(nameof(Index));
        }

        user.Blocked = true;
        if (user is Staff staff) staff.Active = false;
        await db.SaveChangesAsync();

        TempData["Info"] = $"{user.Name}'s account has been blocked.";
        return RedirectToAction(nameof(Index));
    }

    // Permanently removes a user record from the database. Unlike Delete (which
    // just blocks the account), this cannot be undone. It is only allowed when
    // the account has no related history (appointments, pets, reviews, etc.),
    // because those foreign keys use DeleteBehavior.Restrict -- SaveChanges
    // would otherwise throw. Accounts with history should be blocked instead.
    [HttpPost("PermanentDelete/{email}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PermanentDelete(string email)
    {
        var user = await db.Users.FindAsync(email);
        if (user == null) return NotFound();

        if (user.Email == User.Identity?.Name)
        {
            TempData["Info"] = "You cannot delete your own account while logged in.";
            return RedirectToAction(nameof(Index));
        }

        var hasHistory = user switch
        {
            Member => await db.Appointments.AnyAsync(a => a.MemberEmail == email)
                   || await db.Pets.AnyAsync(p => p.MemberEmail == email)
                   || await db.Reviews.AnyAsync(r => r.MemberEmail == email)
                   || await db.Waitlists.AnyAsync(w => w.MemberEmail == email),
            Staff => await db.AppointmentItems.AnyAsync(ai => ai.StaffEmail == email)
                   || await db.Reviews.AnyAsync(r => r.StaffEmail == email)
                   || await db.StaffSchedules.AnyAsync(s => s.StaffEmail == email)
                   || await db.StaffTimeOffs.AnyAsync(t => t.StaffEmail == email),
            _ => false
        };

        if (hasHistory)
        {
            TempData["Info"] = $"{user.Name} cannot be permanently deleted because they have related records " +
                                "(appointments, pets, reviews, or schedules). Block the account instead.";
            return RedirectToAction(nameof(Index));
        }

        if (!string.IsNullOrEmpty(user.PhotoURL))
            hp.DeletePhoto(user.PhotoURL, "photos/users");

        db.Users.Remove(user);
        await db.SaveChangesAsync();

        TempData["Info"] = $"{user.Name}'s account has been permanently deleted.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("Unblock/{email}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Unblock(string email)
    {
        var user = await db.Users.FindAsync(email);
        if (user == null) return NotFound();

        user.Blocked = false;
        if (user is Staff staff) staff.Active = true;
        await db.SaveChangesAsync();

        TempData["Info"] = $"{user.Name}'s account has been unblocked.";
        return RedirectToAction(nameof(Index));
    }
}
