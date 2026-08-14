using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace PetGrooming.Controllers;

// Day-to-day salon operations: who is coming in today, checking pets in, moving
// bookings through grooming, and recording no-shows.
[Authorize(Roles = "Staff,Admin")]
public class AppointmentController(DB db, Helper hp, WaitlistService waitlist,
                                   IConfiguration cf) : Controller
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

    // GET: Appointment/Board
    // The day laid out as groomer columns against time rows, so the whole salon
    // is visible at once and bookings can be dragged to a different groomer or
    // time.
    public IActionResult Board(DateOnly? date)
    {
        var day = date ?? DateOnly.FromDateTime(DateTime.Today);
        var from = day.ToDateTime(TimeOnly.MinValue);
        var to = from.AddDays(1);

        var groomers = db.Staffs.Where(s => s.Active).OrderBy(s => s.Name).ToList();
        var schedules = db.StaffSchedules.Where(s => s.Day == day.DayOfWeek).ToList();
        var timeOffs = db.StaffTimeOffs
                         .Where(t => day >= t.StartDate && day <= t.EndDate)
                         .ToList();

        var items = db.AppointmentItems
                      .Include(i => i.Appointment).ThenInclude(a => a.Member)
                      .Include(i => i.Pet)
                      .Include(i => i.Service)
                      .Where(i => i.SlotStart >= from && i.SlotStart < to
                               && i.ItemStatus != AppointmentStatus.Cancelled)
                      .ToList();

        var vm = new ScheduleBoardVM
        {
            Date = day,
            SlotMinutes = cf.GetValue<int>("Booking:SlotMinutes"),
        };

        foreach (var g in groomers)
        {
            var shift = schedules.FirstOrDefault(s => s.StaffEmail == g.Email);

            vm.Columns.Add(new BoardColumnVM
            {
                Staff = g,
                ShiftStart = shift?.StartTime,
                ShiftEnd = shift?.EndTime,
                OnLeave = timeOffs.Any(t => t.StaffEmail == g.Email),
                Items = items.Where(i => i.StaffEmail == g.Email)
                             .OrderBy(i => i.SlotStart)
                             .ToList(),
            });
        }

        // Rows span the working day, widened if a booking sits outside every shift.
        var open = vm.Columns.Where(c => c.ShiftStart != null).Select(c => c.ShiftStart!.Value).ToList();
        var close = vm.Columns.Where(c => c.ShiftEnd != null).Select(c => c.ShiftEnd!.Value).ToList();

        var first = open.Count > 0 ? open.Min() : new TimeOnly(9, 0);
        var last = close.Count > 0 ? close.Max() : new TimeOnly(18, 0);

        if (items.Count > 0)
        {
            var earliest = TimeOnly.FromDateTime(items.Min(i => i.SlotStart));
            var latest = TimeOnly.FromDateTime(items.Max(i => i.SlotEnd));
            if (earliest < first) first = earliest;
            if (latest > last) last = latest;
        }

        for (var t = first; t < last; t = t.AddMinutes(vm.SlotMinutes))
        {
            vm.Slots.Add(t);
        }

        ViewBag.Title = $"Schedule Board for {day:ddd, d MMM yyyy}";
        ViewBag.CanDrag = User.IsInRole("Admin") || User.IsInRole("Staff");
        return View(vm);
    }

    // POST: Appointment/Reschedule
    // Called by the board when a booking is dropped on a new cell. Returns JSON
    // so the page can revert the block if the move is refused.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Reschedule(int itemId, string staffEmail, DateTime slotStart)
    {
        var item = db.AppointmentItems
                     .Include(i => i.Appointment)
                     .Include(i => i.Service)
                     .FirstOrDefault(i => i.Id == itemId);

        if (item == null)
        {
            return Json(new { ok = false, error = "That booking no longer exists." });
        }

        if (!CanTouch(item.Appointment))
        {
            return Json(new { ok = false, error = "That booking belongs to another groomer." });
        }

        // A groomer may only move work onto themselves; an admin may reassign.
        if (!User.IsInRole("Admin") && staffEmail != User.Identity!.Name)
        {
            return Json(new { ok = false, error = "You can only move bookings onto your own column." });
        }

        if (AppointmentWorkflow.IsTerminal(item.ItemStatus))
        {
            return Json(new { ok = false, error = $"A {item.ItemStatus} booking cannot be moved." });
        }

        var target = db.Staffs.FirstOrDefault(s => s.Email == staffEmail && s.Active);

        if (target == null)
        {
            return Json(new { ok = false, error = "That groomer is not available." });
        }

        var slotEnd = slotStart.AddMinutes(item.Service.DurationMinutes);
        var day = DateOnly.FromDateTime(slotStart);

        var shift = db.StaffSchedules
                      .FirstOrDefault(s => s.StaffEmail == staffEmail && s.Day == day.DayOfWeek);

        if (shift == null)
        {
            return Json(new { ok = false, error = $"{target.Name} does not work on {day.DayOfWeek}." });
        }

        if (TimeOnly.FromDateTime(slotStart) < shift.StartTime ||
            TimeOnly.FromDateTime(slotEnd) > shift.EndTime)
        {
            return Json(new
            {
                ok = false,
                error = $"That runs outside {target.Name}'s shift " +
                        $"({shift.StartTime:h:mm tt} to {shift.EndTime:h:mm tt})."
            });
        }

        if (db.StaffTimeOffs.Any(t => t.StaffEmail == staffEmail
                                   && day >= t.StartDate && day <= t.EndDate))
        {
            return Json(new { ok = false, error = $"{target.Name} is on leave that day." });
        }

        using var tx = db.Database.BeginTransaction();

        if (!db.TryLockSlot(Extensions.SlotKey(staffEmail, slotStart)))
        {
            tx.Rollback();
            return Json(new { ok = false, error = "That slot is busy right now. Try again." });
        }

        var clash = db.AppointmentItems.Any(i =>
            i.Id != itemId &&
            i.StaffEmail == staffEmail &&
            i.ItemStatus != AppointmentStatus.Cancelled &&
            i.SlotStart < slotEnd && slotStart < i.SlotEnd);

        if (clash)
        {
            tx.Rollback();
            return Json(new { ok = false, error = $"{target.Name} already has a booking then." });
        }

        var wasStaff = item.StaffEmail;
        var wasStart = item.SlotStart;

        item.StaffEmail = staffEmail;
        item.SlotStart = slotStart;
        item.SlotEnd = slotEnd;

        // Reuse the status trail as a general activity log for this booking. The
        // status itself does not change, so From and To are the same.
        db.AppointmentStatusHistories.Add(new AppointmentStatusHistory
        {
            AppointmentId = item.AppointmentId,
            FromStatus = item.ItemStatus,
            ToStatus = item.ItemStatus,
            ChangedAt = DateTime.Now,
            ChangedByEmail = User.Identity!.Name!,
            Remark = $"Rescheduled from {wasStart:d MMM h:mm tt} to {slotStart:d MMM h:mm tt}"
                   + (wasStaff == staffEmail ? "" : $", reassigned to {target.Name}"),
        });

        db.SaveChanges();
        tx.Commit();

        return Json(new
        {
            ok = true,
            message = $"Moved to {slotStart:h:mm tt} with {target.Name}.",
            slotEnd = slotEnd.ToString("o"),
        });
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

        // A staff cancellation frees the slot just as a member cancellation does.
        var offered = to == AppointmentStatus.Cancelled
                    ? waitlist.NotifyForFreedSlots(m)
                    : 0;

        TempData["Info"] = $"{m.BookingRef} is now {to}."
            + (offered > 0 ? $" {offered} member(s) on the waitlist have been notified." : "");

        return RedirectToAction("Detail", new { id });
    }

    // GET: Appointment/ReportCard
    // The groomer's write-up of what they did, with before and after photos that
    // the owner sees afterwards in their history.
    public IActionResult ReportCard(int itemId)
    {
        var item = LoadItem(itemId);

        if (item == null || !CanTouch(item.Appointment))
        {
            TempData["Info"] = "Booking not found.";
            return RedirectToAction("Index");
        }

        if (item.ItemStatus is not (AppointmentStatus.InProgress or AppointmentStatus.Completed))
        {
            TempData["Info"] = "A report card can only be written once grooming has started.";
            return RedirectToAction("Detail", new { id = item.AppointmentId });
        }

        var report = item.Report;

        ViewBag.Title = $"Report Card for {item.Pet.Name}";
        ViewBag.Item = item;
        ViewBag.Photos = report?.Photos.OrderBy(p => p.PhotoType).ThenBy(p => p.SortOrder).ToList()
                      ?? [];

        return View(new ReportCardVM
        {
            AppointmentItemId = item.Id,
            PetName = item.Pet.Name,
            ServiceName = item.Service.Name,
            GroomerNotes = report?.GroomerNotes,
            CoatCondition = report?.CoatCondition,
            BehaviourNotes = report?.BehaviourNotes,
            NextVisitRecommendation = report?.NextVisitRecommendation,
        });
    }

    // POST: Appointment/ReportCard
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult ReportCard(ReportCardVM vm)
    {
        var item = LoadItem(vm.AppointmentItemId);

        if (item == null || !CanTouch(item.Appointment))
        {
            TempData["Info"] = "Booking not found.";
            return RedirectToAction("Index");
        }

        if (item.ItemStatus is not (AppointmentStatus.InProgress or AppointmentStatus.Completed))
        {
            TempData["Info"] = "A report card can only be written once grooming has started.";
            return RedirectToAction("Detail", new { id = item.AppointmentId });
        }

        // Reject bad uploads before writing anything, so a rejected photo does not
        // leave a half-saved report behind.
        var incoming = (vm.BeforePhotos ?? []).Select(f => (PhotoType.Before, f))
              .Concat((vm.AfterPhotos ?? []).Select(f => (PhotoType.After, f)))
              .Where(x => x.f != null && x.f.Length > 0)
              .ToList();

        foreach (var (_, file) in incoming)
        {
            var error = hp.ValidatePhoto(file);
            if (error != "") ModelState.AddModelError("", $"{file.FileName}: {error}");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Title = $"Report Card for {item.Pet.Name}";
            ViewBag.Item = item;
            ViewBag.Photos = item.Report?.Photos.OrderBy(p => p.PhotoType).ToList() ?? [];
            return View(vm);
        }

        var report = item.Report;

        if (report == null)
        {
            report = new GroomingReport
            {
                AppointmentItemId = item.Id,
                CreatedAt = DateTime.Now,
            };
            db.GroomingReports.Add(report);
        }

        report.GroomerNotes = vm.GroomerNotes ?? "";
        report.CoatCondition = vm.CoatCondition ?? "";
        report.BehaviourNotes = vm.BehaviourNotes ?? "";
        report.NextVisitRecommendation = vm.NextVisitRecommendation ?? "";

        // Save the report first so a new one has an Id for its photos to hang off.
        db.SaveChanges();

        foreach (var (type, file) in incoming)
        {
            db.GroomingReportPhotos.Add(new GroomingReportPhoto
            {
                GroomingReportId = report.Id,
                PhotoURL = hp.SavePhoto(file, "photos/reports", 800, 600),
                PhotoType = type,
                SortOrder = 0,
            });
        }

        db.SaveChanges();

        TempData["Info"] = incoming.Count > 0
            ? $"Report card saved with {incoming.Count} photo(s)."
            : "Report card saved.";

        return RedirectToAction("ReportCard", new { itemId = item.Id });
    }

    // POST: Appointment/DeleteReportPhoto
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteReportPhoto(int photoId)
    {
        var photo = db.GroomingReportPhotos
                      .Include(p => p.GroomingReport)
                          .ThenInclude(r => r.AppointmentItem)
                              .ThenInclude(i => i.Appointment)
                                  .ThenInclude(a => a.Items)
                      .FirstOrDefault(p => p.Id == photoId);

        if (photo == null || !CanTouch(photo.GroomingReport.AppointmentItem.Appointment))
        {
            TempData["Info"] = "Photo not found.";
            return RedirectToAction("Index");
        }

        var itemId = photo.GroomingReport.AppointmentItemId;

        hp.DeletePhoto(photo.PhotoURL, "photos/reports");
        db.GroomingReportPhotos.Remove(photo);
        db.SaveChanges();

        TempData["Info"] = "Photo removed.";
        return RedirectToAction("ReportCard", new { itemId });
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

    private AppointmentItem? LoadItem(int itemId)
    {
        return db.AppointmentItems
                 .Include(i => i.Pet)
                 .Include(i => i.Service)
                 .Include(i => i.Staff)
                 .Include(i => i.Appointment).ThenInclude(a => a.Items)
                 .Include(i => i.Report).ThenInclude(r => r.Photos)
                 .FirstOrDefault(i => i.Id == itemId);
    }

    private Appointment? Load(int id)
    {
        return db.Appointments
                 .Include(a => a.Member)
                 .Include(a => a.Items).ThenInclude(i => i.Pet)
                 .Include(a => a.Items).ThenInclude(i => i.Service)
                 .Include(a => a.Items).ThenInclude(i => i.Staff)
                 .Include(a => a.Items).ThenInclude(i => i.Report)
                 .Include(a => a.Items).ThenInclude(i => i.Review)
                 .Include(a => a.Payments)
                 .Include(a => a.StatusHistories)
                 .FirstOrDefault(a => a.Id == id);
    }
}
