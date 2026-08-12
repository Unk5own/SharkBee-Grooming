using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PetGrooming.Controllers;

[Authorize(Roles = "Member")]
public class CheckoutController(DB db, Helper hp, IConfiguration cf) : Controller
{
    // GET: Checkout/Index
    public IActionResult Index()
    {
        var vm = BuildCheckout();

        if (vm.Lines.Count == 0)
        {
            TempData["Info"] = "Your booking cart is empty.";
            return RedirectToAction("Index", "Home");
        }

        ViewBag.Title = "Checkout";
        return View(vm);
    }

    // POST: Checkout/Confirm
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Confirm(CheckoutVM posted)
    {
        // Rebuild from the session cart and the database. Nothing about price or
        // slot is trusted from the form -- the browser only chooses the payment
        // method and the notes.
        var vm = BuildCheckout();
        vm.Notes = posted.Notes;
        vm.Method = posted.Method;
        vm.DepositOnly = posted.DepositOnly;

        if (vm.Lines.Count == 0)
        {
            TempData["Info"] = "Your booking cart is empty.";
            return RedirectToAction("Index", "Home");
        }

        if (vm.Lines.Any(l => l.Unavailable))
        {
            ModelState.AddModelError("", "One or more slots are no longer available. " +
                                         "Please remove or rebook the highlighted lines.");
        }

        // Stripe arrives in a later phase; for now only counter payment completes.
        if (vm.Method == PaymentMethod.Stripe)
        {
            ModelState.AddModelError("Method", "Online payment is not available yet. " +
                                               "Please choose Pay at Counter.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Title = "Checkout";
            return View("Index", vm);
        }

        var email = User.Identity!.Name!;

        using var tx = db.Database.BeginTransaction();

        // Take one exclusive lock per (groomer, slot) before re-checking.
        //
        // A SERIALIZABLE transaction alone is not enough here: both sessions would
        // take shared range locks while reading, then both try to insert, and SQL
        // Server resolves it by killing one with a deadlock error (1205) -- which
        // reaches the member as a 500 page rather than a useful message.
        //
        // An application lock makes the second session wait instead. The keys are
        // sorted so that every session acquires them in the same order, which is
        // what stops two multi-line bookings from deadlocking on each other.
        var keys = vm.Lines
                     .Select(l => $"slot:{l.Item.StaffEmail}:{l.Item.SlotStart:yyyyMMddHHmm}")
                     .Distinct()
                     .OrderBy(k => k, StringComparer.Ordinal)
                     .ToList();

        foreach (var key in keys)
        {
            if (!TryLockSlot(key))
            {
                tx.Rollback();
                ModelState.AddModelError("", "Another booking for one of these slots is being " +
                                             "confirmed right now. Please try again in a moment.");
                ViewBag.Title = "Checkout";
                return View("Index", vm);
            }
        }

        // Now that the slots are locked, the availability answer cannot change
        // underneath us before the insert.
        foreach (var line in vm.Lines)
        {
            if (!IsSlotFree(line.Item.StaffEmail, line.Item.SlotStart, line.SlotEnd))
            {
                tx.Rollback();
                line.Unavailable = true;
                ModelState.AddModelError("", $"The {line.Service.Name} slot with " +
                                             $"{line.Staff.Name} was just taken. Please pick another time.");
                ViewBag.Title = "Checkout";
                return View("Index", vm);
            }
        }

        var appointment = new Appointment
        {
            BookingRef = hp.NextBookingRef(),
            MemberEmail = email,
            CreatedAt = DateTime.Now,
            Status = AppointmentStatus.Confirmed,
            Subtotal = vm.Subtotal,
            Discount = vm.Discount,
            Total = vm.Total,
            DepositAmount = vm.DepositAmount,
            Notes = vm.Notes ?? "",
            CancelReason = "",
        };

        foreach (var line in vm.Lines)
        {
            appointment.Items.Add(new AppointmentItem
            {
                PetId = line.Item.PetId,
                ServiceId = line.Item.ServiceId,
                StaffEmail = line.Item.StaffEmail,
                SlotStart = line.Item.SlotStart,
                SlotEnd = line.SlotEnd,
                UnitPrice = line.Service.Price,
                ItemStatus = AppointmentStatus.Confirmed,
            });
        }

        // Counter payment is settled on the day, so the row is recorded as
        // outstanding rather than paid.
        appointment.Payments.Add(new Payment
        {
            Method = PaymentMethod.Counter,
            Status = PaymentStatus.Pending,
            Amount = vm.DepositOnly ? vm.DepositAmount : vm.Total,
            ProviderRef = "",
        });

        appointment.StatusHistories.Add(new AppointmentStatusHistory
        {
            FromStatus = AppointmentStatus.Pending,
            ToStatus = AppointmentStatus.Confirmed,
            ChangedAt = DateTime.Now,
            ChangedByEmail = email,
            Remark = "Booking confirmed, payment due at counter",
        });

        db.Appointments.Add(appointment);

        try
        {
            db.SaveChanges();
            tx.Commit();
        }
        catch (Exception)
        {
            // Safety net. The application lock above should prevent contention,
            // but a failed booking must never reach the member as an error page.
            tx.Rollback();
            ModelState.AddModelError("", "We could not confirm your booking just now. " +
                                         "Please check your slots and try again.");
            ViewBag.Title = "Checkout";
            return View("Index", BuildCheckout());
        }

        hp.SetCart(null);

        TempData["Info"] = $"Booking {appointment.BookingRef} confirmed.";
        return RedirectToAction("Complete", new { id = appointment.Id });
    }

    // GET: Checkout/Complete
    public IActionResult Complete(int id)
    {
        var m = db.Appointments
                  .Include(a => a.Items).ThenInclude(i => i.Pet)
                  .Include(a => a.Items).ThenInclude(i => i.Service)
                  .Include(a => a.Items).ThenInclude(i => i.Staff)
                  .Include(a => a.Payments)
                  .FirstOrDefault(a => a.Id == id);

        // A member must not be able to read someone else's booking by changing
        // the id in the URL.
        if (m == null || m.MemberEmail != User.Identity!.Name)
        {
            TempData["Info"] = "Booking not found.";
            return RedirectToAction("Index", "Home");
        }

        ViewBag.Title = "Booking Confirmed";
        return View(m);
    }



    // ------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------

    // Joins the session cart to the database and prices it. Lines whose pet no
    // longer belongs to the member, or whose service is gone, are dropped; lines
    // whose slot has been taken are flagged rather than dropped, so the member
    // can see what went wrong.
    private CheckoutVM BuildCheckout()
    {
        var email = User.Identity!.Name!;
        var cart = hp.GetCart();
        var vm = new CheckoutVM();

        foreach (var item in cart)
        {
            var pet = db.Pets.FirstOrDefault(p => p.Id == item.PetId && p.MemberEmail == email);
            var service = db.Services.FirstOrDefault(s => s.Id == item.ServiceId && s.Active);
            var staff = db.Staffs.FirstOrDefault(s => s.Email == item.StaffEmail && s.Active);

            if (pet == null || service == null || staff == null) continue;

            var end = item.SlotStart.AddMinutes(service.DurationMinutes);

            vm.Lines.Add(new CheckoutLineVM
            {
                Item = item,
                Pet = pet,
                Service = service,
                Staff = staff,
                SlotEnd = end,
                Subtotal = service.Price,
                Unavailable = item.SlotStart <= DateTime.Now
                           || !IsSlotFree(item.StaffEmail, item.SlotStart, end),
            });
        }

        vm.Subtotal = vm.Lines.Sum(l => l.Subtotal);
        vm.Discount = 0m;
        vm.Total = vm.Subtotal - vm.Discount;

        var percent = cf.GetValue<int>("Booking:DepositPercent");
        vm.DepositAmount = Math.Round(vm.Total * percent / 100m, 2);

        return vm;
    }

    // Exclusive, transaction-scoped lock on an arbitrary string key. Released
    // automatically when the transaction commits or rolls back. A negative result
    // means the lock could not be taken within the timeout.
    private bool TryLockSlot(string resource)
    {
        const string sql = @"
            DECLARE @result int;
            EXEC @result = sp_getapplock @Resource = {0},
                                         @LockMode = 'Exclusive',
                                         @LockOwner = 'Transaction',
                                         @LockTimeout = 5000;
            SELECT @result AS Value;";

        return db.Database.SqlQueryRaw<int>(sql, resource).AsEnumerable().First() >= 0;
    }

    // Student 2 owns the full availability service. Checkout only needs the
    // narrow question of whether this groomer is free for this interval.
    private bool IsSlotFree(string staffEmail, DateTime start, DateTime end)
    {
        return !db.AppointmentItems.Any(i =>
            i.StaffEmail == staffEmail &&
            i.ItemStatus != AppointmentStatus.Cancelled &&
            i.SlotStart < end && start < i.SlotEnd);
    }
}
