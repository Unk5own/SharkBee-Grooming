using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace PetGrooming.Controllers;

// Day-to-day salon operations: who is coming in today, checking pets in, moving
// bookings through grooming, and recording no-shows.
[Authorize(Roles = "Staff,Admin")]
public class AppointmentController(DB db) : Controller
{
    // GET: Appointment/Index
    public IActionResult Index(DateOnly? date, AppointmentStatus? status,
                               string? staff, string? name)
    {
        var day = date ?? DateOnly.FromDateTime(DateTime.Today);

        // A groomer sees only their own column of work; an admin sees everyone.
        var lockedToSelf = User.IsInRole("Staff") && !User.IsInRole("Admin");
        if (lockedToSelf) staff = User.Identity!.Name;

        var items = Query(day, status, staff, name);

        ViewBag.Date = day;
        ViewBag.Status = status;
        ViewBag.Staff = staff;
        ViewBag.Name = name;
        ViewBag.LockedToSelf = lockedToSelf;
        ViewBag.StaffList = new SelectList(
            db.Staffs.Where(s => s.Active).OrderBy(s => s.Name).ToList(), "Email", "Name", staff);
        ViewBag.Summary = Summarise(day);

        if (Request.IsAjax()) return PartialView("_Index", items);

        ViewBag.Title = $"Appointments for {day:ddd, d MMM yyyy}";
        return View(items);
    }

    // GET: Appointment/Detail
    public IActionResult Detail(int id)
    {
        var m = Load(id);

        if (m == null)
        {
            TempData["Info"] = "Appointment not found.";
            return RedirectToAction("Index");
        }

        if (!CanTouch(m))
        {
            TempData["Info"] = "That appointment belongs to another groomer.";
            return RedirectToAction("Index");
        }

        ViewBag.Title = $"Appointment {m.BookingRef}";
        return View(m);
    }

    // POST: Appointment/UpdateStatus
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult UpdateStatus(int id, AppointmentStatus to, string? remark)
    {
        var m = Load(id);

        if (m == null)
        {
            TempData["Info"] = "Appointment not found.";
            return RedirectToAction("Index");
        }

        if (!CanTouch(m))
        {
            TempData["Info"] = "That appointment belongs to another groomer.";
            return RedirectToAction("Index");
        }

        // The guard, not the rendered buttons, is what makes this safe. A crafted
        // POST asking for Completed -> Pending is refused here.
        if (!AppointmentWorkflow.CanTransition(m.Status, to))
        {
            TempData["Info"] = $"Cannot move a {m.Status} appointment to {to}.";
            return RedirectToAction("Detail", new { id });
        }

        var from = m.Status;

        m.Status = to;
        foreach (var item in m.Items)
        {
            item.ItemStatus = to;
        }

        if (to == AppointmentStatus.Cancelled)
        {
            m.CancelledAt = DateTime.Now;
            m.CancelReason = string.IsNullOrWhiteSpace(remark)
                           ? AppointmentWorkflow.DefaultRemark(to)
                           : remark;
        }

        db.AppointmentStatusHistories.Add(new AppointmentStatusHistory
        {
            AppointmentId = m.Id,
            FromStatus = from,
            ToStatus = to,
            ChangedAt = DateTime.Now,
            ChangedByEmail = User.Identity!.Name!,
            Remark = string.IsNullOrWhiteSpace(remark)
                   ? AppointmentWorkflow.DefaultRemark(to)
                   : remark,
        });

        db.SaveChanges();

        TempData["Info"] = $"{m.BookingRef} is now {to}.";
        return RedirectToAction("Detail", new { id });
    }

    // POST: Appointment/SettlePayment
    // Records that an outstanding counter payment was taken at the desk.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult SettlePayment(int id)
    {
        var m = Load(id);

        if (m == null || !CanTouch(m))
        {
            TempData["Info"] = "Appointment not found.";
            return RedirectToAction("Index");
        }

        var payment = m.Payments.FirstOrDefault(p => p.Status == PaymentStatus.Pending);

        if (payment == null)
        {
            TempData["Info"] = "There is no outstanding payment on this booking.";
            return RedirectToAction("Detail", new { id });
        }

        payment.Status = PaymentStatus.Paid;
        payment.PaidAt = DateTime.Now;
        db.SaveChanges();

        TempData["Info"] = $"Payment of RM {payment.Amount:N2} recorded for {m.BookingRef}.";
        return RedirectToAction("Detail", new { id });
    }



    // ------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------

    private IQueryable<AppointmentItem> Query(DateOnly day, AppointmentStatus? status,
                                              string? staff, string? name)
    {
        var from = day.ToDateTime(TimeOnly.MinValue);
        var to = from.AddDays(1);

        var q = db.AppointmentItems
                  .Include(i => i.Appointment).ThenInclude(a => a.Member)
                  .Include(i => i.Pet)
                  .Include(i => i.Service)
                  .Include(i => i.Staff)
                  .Where(i => i.SlotStart >= from && i.SlotStart < to);

        if (status != null) q = q.Where(i => i.ItemStatus == status);
        if (!string.IsNullOrWhiteSpace(staff)) q = q.Where(i => i.StaffEmail == staff);

        if (!string.IsNullOrWhiteSpace(name))
        {
            q = q.Where(i => i.Pet.Name.Contains(name)
                          || i.Appointment.Member.Name.Contains(name)
                          || i.Appointment.BookingRef.Contains(name));
        }

        return q.OrderBy(i => i.SlotStart).ThenBy(i => i.StaffEmail);
    }

    // Counts for the small status strip above the list.
    private Dictionary<AppointmentStatus, int> Summarise(DateOnly day)
    {
        var from = day.ToDateTime(TimeOnly.MinValue);
        var to = from.AddDays(1);

        return db.AppointmentItems
                 .Where(i => i.SlotStart >= from && i.SlotStart < to)
                 .GroupBy(i => i.ItemStatus)
                 .Select(g => new { g.Key, Count = g.Count() })
                 .ToDictionary(x => x.Key, x => x.Count);
    }

    // A groomer may only act on bookings assigned to them. Admins are unrestricted.
    private bool CanTouch(Appointment appointment)
    {
        if (User.IsInRole("Admin")) return true;

        return appointment.Items.Any(i => i.StaffEmail == User.Identity!.Name);
    }

    private Appointment? Load(int id)
    {
        return db.Appointments
                 .Include(a => a.Member)
                 .Include(a => a.Items).ThenInclude(i => i.Pet)
                 .Include(a => a.Items).ThenInclude(i => i.Service)
                 .Include(a => a.Items).ThenInclude(i => i.Staff)
                 .Include(a => a.Payments)
                 .Include(a => a.StatusHistories)
                 .FirstOrDefault(a => a.Id == id);
    }
}
