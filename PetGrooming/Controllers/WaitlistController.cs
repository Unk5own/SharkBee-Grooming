using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace PetGrooming.Controllers;

// Members join the waitlist when the day they want is already full. Cancelling
// a booking notifies the first person waiting for that service on that date.
[Authorize(Roles = "Member")]
public class WaitlistController(DB db) : Controller
{
    // GET: Waitlist/Index
    public IActionResult Index()
    {
        ViewBag.Title = "Waitlist";
        Populate();

        return View(new WaitlistVM
        {
            DesiredFrom = DateOnly.FromDateTime(DateTime.Today.AddDays(1)),
            DesiredTo = DateOnly.FromDateTime(DateTime.Today.AddDays(14)),
        });
    }

    // POST: Waitlist/Join
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Join(WaitlistVM vm)
    {
        var email = User.Identity!.Name!;
        var today = DateOnly.FromDateTime(DateTime.Today);

        // The pet must actually belong to the member asking.
        if (!db.Pets.Any(p => p.Id == vm.PetId && p.MemberEmail == email && p.Active))
        {
            ModelState.AddModelError("PetId", "Please choose one of your pets.");
        }

        if (!db.Services.Any(s => s.Id == vm.ServiceId && s.Active))
        {
            ModelState.AddModelError("ServiceId", "That service is not available.");
        }

        if (vm.DesiredFrom < today)
        {
            ModelState.AddModelError("DesiredFrom", "The earliest date cannot be in the past.");
        }

        if (vm.DesiredTo < vm.DesiredFrom)
        {
            ModelState.AddModelError("DesiredTo", "The latest date must be on or after the earliest date.");
        }

        if (vm.DesiredTo.DayNumber - vm.DesiredFrom.DayNumber > 60)
        {
            ModelState.AddModelError("DesiredTo", "Please keep the range within 60 days.");
        }

        // One open entry per pet and service is enough.
        if (db.Waitlists.Any(w => w.MemberEmail == email
                               && w.PetId == vm.PetId
                               && w.ServiceId == vm.ServiceId
                               && w.Status == WaitlistStatus.Waiting))
        {
            ModelState.AddModelError("", "You are already on the waitlist for that pet and service.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Title = "Waitlist";
            Populate();
            return View("Index", vm);
        }

        db.Waitlists.Add(new Waitlist
        {
            MemberEmail = email,
            PetId = vm.PetId,
            ServiceId = vm.ServiceId,
            PreferredStaffEmail = string.IsNullOrWhiteSpace(vm.PreferredStaffEmail)
                                ? null
                                : vm.PreferredStaffEmail,
            DesiredFrom = vm.DesiredFrom,
            DesiredTo = vm.DesiredTo,
            Status = WaitlistStatus.Waiting,
            CreatedAt = DateTime.Now,
        });

        db.SaveChanges();

        TempData["Info"] = "You are on the waitlist. We will email you if a slot opens up.";
        return RedirectToAction("Index");
    }

    // POST: Waitlist/Leave
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Leave(int id)
    {
        var entry = db.Waitlists.FirstOrDefault(w => w.Id == id
                                                  && w.MemberEmail == User.Identity!.Name);

        if (entry == null)
        {
            TempData["Info"] = "Waitlist entry not found.";
            return RedirectToAction("Index");
        }

        db.Waitlists.Remove(entry);
        db.SaveChanges();

        TempData["Info"] = "Removed from the waitlist.";
        return RedirectToAction("Index");
    }

    private void Populate()
    {
        var email = User.Identity!.Name!;

        ViewBag.Entries = db.Waitlists
                            .Include(w => w.Pet)
                            .Include(w => w.Service)
                            .Where(w => w.MemberEmail == email)
                            .OrderByDescending(w => w.CreatedAt)
                            .ToList();

        ViewBag.PetList = new SelectList(
            db.Pets.Where(p => p.MemberEmail == email && p.Active).OrderBy(p => p.Name).ToList(),
            "Id", "Name");

        ViewBag.ServiceList = new SelectList(
            db.Services.Where(s => s.Active).OrderBy(s => s.Name).ToList(),
            "Id", "Name");

        ViewBag.StaffList = new SelectList(
            db.Staffs.Where(s => s.Active).OrderBy(s => s.Name).ToList(),
            "Email", "Name");
    }
}
