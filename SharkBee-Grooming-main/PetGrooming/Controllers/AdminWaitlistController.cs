using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace PetGrooming.Controllers;

// Members join the waitlist themselves in WaitlistController, but that side is
// scoped to the member who is signed in, so nobody could see the queue from the
// office. This is the salon's view of it: who is waiting, for what, and how far
// along each request is.
[Authorize(Roles = "Admin")]
[Route("Admin/Waitlist")]
public class AdminWaitlistController(DB db, WaitlistService waitlist) : Controller
{
    // GET: Admin/Waitlist/Index
    [HttpGet("")]
    [HttpGet("Index")]
    public IActionResult Index(WaitlistStatus? status, string? serviceId, string? search)
    {
        // Bring lapsed and unanswered entries up to date before they are listed,
        // so the office is never looking at a stale queue.
        waitlist.Sweep();

        var entries = Query(status, serviceId, search);

        ViewBag.Status = status;
        ViewBag.ServiceId = serviceId;
        ViewBag.Search = search;
        ViewBag.ServiceList = new SelectList(
            db.Services.OrderBy(s => s.Name).ToList(), "Id", "Name", serviceId);
        ViewBag.Summary = Summarise();

        if (Request.IsAjax()) return PartialView("_Index", entries);

        ViewBag.Title = "Waitlist";
        return View(entries);
    }

    // GET: Admin/Waitlist/Edit/5
    [HttpGet("Edit/{id:int}")]
    public IActionResult Edit(int id)
    {
        var entry = Load(id);
        if (entry == null) return NotFound();

        ViewBag.Title = "Edit Waitlist Entry";
        Populate(entry.PreferredStaffEmail);

        return View(ToVM(entry));
    }

    // POST: Admin/Waitlist/Edit/5
    [HttpPost("Edit/{id:int}")]
    [ValidateAntiForgeryToken]
    public IActionResult Edit(int id, WaitlistEditVM vm)
    {
        var entry = Load(id);
        if (entry == null) return NotFound();

        if (vm.DesiredTo < vm.DesiredFrom)
        {
            ModelState.AddModelError(nameof(vm.DesiredTo),
                                     "The latest date must be on or after the earliest date.");
        }

        if (!string.IsNullOrWhiteSpace(vm.PreferredStaffEmail)
            && !db.Staffs.Any(s => s.Email == vm.PreferredStaffEmail && s.Active))
        {
            ModelState.AddModelError(nameof(vm.PreferredStaffEmail),
                                     "That groomer is not available.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Title = "Edit Waitlist Entry";
            Populate(vm.PreferredStaffEmail);

            // The form never posts the display-only fields back, so refill them
            // or the redisplayed page loses the member, pet and service names.
            var shown = ToVM(entry);
            vm.MemberName = shown.MemberName;
            vm.MemberEmail = shown.MemberEmail;
            vm.PetName = shown.PetName;
            vm.ServiceName = shown.ServiceName;

            return View(vm);
        }

        entry.Status = vm.Status;
        entry.DesiredFrom = vm.DesiredFrom;
        entry.DesiredTo = vm.DesiredTo;
        entry.PreferredStaffEmail = string.IsNullOrWhiteSpace(vm.PreferredStaffEmail)
                                  ? null
                                  : vm.PreferredStaffEmail;

        // NotifiedAt means "when we emailed them a slot had opened", so it is
        // only invented for Notified. Converted and Expired say nothing about
        // whether an offer was ever made, and Waiting means there is no
        // outstanding one.
        entry.NotifiedAt = vm.Status switch
        {
            WaitlistStatus.Waiting => null,
            // The sweep ages an offer from this timestamp, so one set by hand
            // still needs a time to age from.
            WaitlistStatus.Notified => entry.NotifiedAt ?? DateTime.Now,
            _ => entry.NotifiedAt,
        };

        db.SaveChanges();

        TempData["Info"] = $"{entry.Member.Name}'s waitlist entry has been updated.";
        return RedirectToAction(nameof(Index));
    }

    // POST: Admin/Waitlist/Delete/5
    // A waitlist entry is a request, not history: nothing references it once it
    // is gone, so this is a real delete rather than the soft delete used for
    // accounts.
    [HttpPost("Delete/{id:int}")]
    [ValidateAntiForgeryToken]
    public IActionResult Delete(int id)
    {
        var entry = Load(id);
        if (entry == null) return NotFound();

        var who = entry.Member.Name;

        db.Waitlists.Remove(entry);
        db.SaveChanges();

        TempData["Info"] = $"{who}'s waitlist entry has been removed.";
        return RedirectToAction(nameof(Index));
    }



    // ------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------

    private List<Waitlist> Query(WaitlistStatus? status, string? serviceId, string? search)
    {
        var query = db.Waitlists
                      .Include(w => w.Member)
                      .Include(w => w.Pet)
                      .Include(w => w.Service)
                      .AsQueryable();

        if (status != null)
        {
            query = query.Where(w => w.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(serviceId))
        {
            query = query.Where(w => w.ServiceId == serviceId);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            search = search.Trim();
            query = query.Where(w => w.Member.Name.Contains(search)
                                  || w.MemberEmail.Contains(search)
                                  || w.Pet.Name.Contains(search));
        }

        // Oldest first, because that is the order freed slots are offered in.
        return query.OrderBy(w => w.CreatedAt).ToList();
    }

    private Dictionary<WaitlistStatus, int> Summarise()
    {
        return db.Waitlists
                 .GroupBy(w => w.Status)
                 .Select(g => new { g.Key, Count = g.Count() })
                 .ToDictionary(x => x.Key, x => x.Count);
    }

    private Waitlist? Load(int id)
    {
        return db.Waitlists
                 .Include(w => w.Member)
                 .Include(w => w.Pet)
                 .Include(w => w.Service)
                 .FirstOrDefault(w => w.Id == id);
    }

    private void Populate(string? selected)
    {
        ViewBag.StaffList = new SelectList(
            db.Staffs.Where(s => s.Active).OrderBy(s => s.Name).ToList(),
            "Email", "Name", selected);
    }

    private static WaitlistEditVM ToVM(Waitlist w)
    {
        return new WaitlistEditVM
        {
            Id = w.Id,
            Status = w.Status,
            PreferredStaffEmail = w.PreferredStaffEmail,
            DesiredFrom = w.DesiredFrom,
            DesiredTo = w.DesiredTo,
            MemberName = w.Member.Name,
            MemberEmail = w.MemberEmail,
            PetName = w.Pet.Name,
            ServiceName = w.Service.Name,
        };
    }
}
