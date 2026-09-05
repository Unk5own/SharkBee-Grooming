using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PetGrooming.Controllers;

[Authorize(Roles = "Member")]
public class CheckoutController(DB db, Helper hp, StripeService stripe, IConfiguration cf) : Controller
{
    // GET: Checkout/Index
    public IActionResult Index()
    {
        ExpireStalePending();

        var vm = BuildCheckout();

        if (vm.Lines.Count == 0)
        {
            TempData["Info"] = "Your booking cart is empty.";
            return RedirectToAction("Index", "Home");
        }

        ViewBag.Title = "Checkout";
        ViewBag.CardAvailable = stripe.IsConfigured;
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

        if (vm.Method == PaymentMethod.Stripe && !stripe.IsConfigured)
        {
            ModelState.AddModelError("Method", "Card payment is not available right now. " +
                                               "Please choose Pay at Counter.");
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Title = "Checkout";
            ViewBag.CardAvailable = stripe.IsConfigured;
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
                     .Select(l => Extensions.SlotKey(l.Item.StaffEmail, l.Item.SlotStart))
                     .Distinct()
                     .OrderBy(k => k, StringComparer.Ordinal)
                     .ToList();

        foreach (var key in keys)
        {
            if (!db.TryLockSlot(key))
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

        // A card booking is only Pending until Stripe confirms the payment. The
        // slot is still held, because availability ignores Cancelled items only.
        var payingByCard = vm.Method == PaymentMethod.Stripe;
        var status = payingByCard ? AppointmentStatus.Pending : AppointmentStatus.Confirmed;
        var amountDue = vm.Total;

        var appointment = new Appointment
        {
            BookingRef = hp.NextBookingRef(),
            MemberEmail = email,
            CreatedAt = DateTime.Now,
            Status = status,
            Subtotal = vm.Subtotal,
            Discount = vm.Discount,
            Total = vm.Total,
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
                ItemStatus = status,
            });
        }

        appointment.Payments.Add(new Payment
        {
            Method = vm.Method,
            Status = PaymentStatus.Pending,
            Amount = amountDue,
            ProviderRef = "",
        });

        // Counter bookings are confirmed immediately; card bookings only after
        // Stripe reports the payment, so their trail starts in PaymentSuccess.
        if (!payingByCard)
        {
            appointment.StatusHistories.Add(new AppointmentStatusHistory
            {
                FromStatus = AppointmentStatus.Pending,
                ToStatus = AppointmentStatus.Confirmed,
                ChangedAt = DateTime.Now,
                ChangedByEmail = email,
                Remark = "Booking confirmed, payment due at counter",
            });
        }

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

        if (payingByCard)
        {
            var url = stripe.CreateCheckoutSession(
                Load(appointment.Id)!,
                amountDue,
                // The id is a route segment, not a query value, so the session id
                // has to start a fresh query string. Stripe swaps the literal
                // {CHECKOUT_SESSION_ID} in, so it must not be URL-encoded.
                Url.Action("PaymentSuccess", "Checkout",
                           new { id = appointment.Id }, Request.Scheme)!
                    + "?session_id={CHECKOUT_SESSION_ID}",
                Url.Action("PaymentCancelled", "Checkout",
                           new { id = appointment.Id }, Request.Scheme)!);

            if (url == null)
            {
                TempData["Info"] = $"Booking {appointment.BookingRef} was created but the " +
                                   "payment page could not be opened. You can pay at the counter.";
                return RedirectToAction("Complete", new { id = appointment.Id });
            }

            return Redirect(url);
        }

        return CompleteBooking(appointment.Id);
    }

    // GET: Checkout/PaymentSuccess
    // Stripe redirects here after a successful payment. The redirect itself
    // proves nothing -- anyone could type this URL -- so the session is verified
    // server to server with Stripe before any payment is recorded.
    public IActionResult PaymentSuccess(int id, string? session_id)
    {
        var appointment = Load(id);

        if (appointment == null || appointment.MemberEmail != User.Identity!.Name)
        {
            TempData["Info"] = "Booking not found.";
            return RedirectToAction("Index", "Home");
        }

        // Already processed, e.g. the member refreshed the page.
        if (appointment.Status != AppointmentStatus.Pending)
        {
            return RedirectToAction("Complete", new { id });
        }

        var (paid, intentId, amount) = stripe.VerifySession(session_id ?? "");

        if (!paid)
        {
            TempData["Info"] = "We could not confirm your card payment. " +
                               "Your booking is still held -- you may retry or pay at the counter.";
            return RedirectToAction("Complete", new { id });
        }

        var payment = appointment.Payments.FirstOrDefault();

        if (payment != null)
        {
            payment.Status = PaymentStatus.Paid;
            payment.ProviderRef = intentId;
            payment.PaidAt = DateTime.Now;
            if (amount > 0) payment.Amount = amount;
        }

        appointment.Status = AppointmentStatus.Confirmed;

        foreach (var item in appointment.Items)
        {
            item.ItemStatus = AppointmentStatus.Confirmed;
        }

        db.AppointmentStatusHistories.Add(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            FromStatus = AppointmentStatus.Pending,
            ToStatus = AppointmentStatus.Confirmed,
            ChangedAt = DateTime.Now,
            ChangedByEmail = appointment.MemberEmail,
            Remark = "Card payment received via Stripe",
        });

        db.SaveChanges();

        return CompleteBooking(id);
    }

    // GET: Checkout/PaymentCancelled
    // The member backed out on Stripe's page. Release the held slots rather than
    // leaving them locked by an abandoned booking.
    public IActionResult PaymentCancelled(int id)
    {
        var appointment = Load(id);

        if (appointment == null || appointment.MemberEmail != User.Identity!.Name)
        {
            TempData["Info"] = "Booking not found.";
            return RedirectToAction("Index", "Home");
        }

        if (appointment.Status == AppointmentStatus.Pending)
        {
            ReleasePending(appointment, "Payment cancelled by member");
            db.SaveChanges();
        }

        TempData["Info"] = "Payment was cancelled, so the booking was not taken. " +
                           "Your slots have been released.";
        return RedirectToAction("Index", "Home");
    }

    // GET: Checkout/Complete
    public IActionResult Complete(int id)
    {
        var m = Load(id);

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

    // GET: Checkout/Receipt
    // Re-downloadable at any time, which is why the PDF is generated on demand
    // rather than stored on disk.
    public IActionResult Receipt(int id)
    {
        var m = Load(id);

        if (m == null || m.MemberEmail != User.Identity!.Name)
        {
            TempData["Info"] = "Booking not found.";
            return RedirectToAction("Index", "Home");
        }

        return File(hp.GenerateReceipt(m), "application/pdf", hp.ReceiptFileName(m));
    }

    // Loads an appointment with everything the receipt and the confirmation page
    // need, in one query.
    private Appointment? Load(int id)
    {
        return db.Appointments
                 .Include(a => a.Member)
                 .Include(a => a.Items).ThenInclude(i => i.Pet)
                 .Include(a => a.Items).ThenInclude(i => i.Service)
                 .Include(a => a.Items).ThenInclude(i => i.Staff)
                 .Include(a => a.Payments)
                 .FirstOrDefault(a => a.Id == id);
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

        return vm;
    }

    // Emails the receipt and sends the member to the confirmation page. Shared by
    // the counter path and the card path so both behave identically once paid.
    private IActionResult CompleteBooking(int id)
    {
        var saved = Load(id)!;

        // A mail failure must never undo a confirmed booking, so the outcome is
        // reported rather than thrown.
        var problem = hp.EmailReceipt(saved, hp.GenerateReceipt(saved));

        TempData["Info"] = problem == ""
            ? $"Booking {saved.BookingRef} confirmed. The e-receipt has been emailed to you."
            : $"Booking {saved.BookingRef} confirmed. {problem}";

        return RedirectToAction("Complete", new { id });
    }

    // Cancels a booking that never got paid, freeing its slots.
    private void ReleasePending(Appointment appointment, string reason)
    {
        appointment.Status = AppointmentStatus.Cancelled;
        appointment.CancelledAt = DateTime.Now;
        appointment.CancelReason = reason;

        foreach (var item in appointment.Items)
        {
            item.ItemStatus = AppointmentStatus.Cancelled;
        }

        db.AppointmentStatusHistories.Add(new AppointmentStatusHistory
        {
            AppointmentId = appointment.Id,
            FromStatus = AppointmentStatus.Pending,
            ToStatus = AppointmentStatus.Cancelled,
            ChangedAt = DateTime.Now,
            ChangedByEmail = appointment.MemberEmail,
            Remark = reason,
        });
    }

    // A member who closes the Stripe tab never reaches PaymentCancelled, so their
    // unpaid booking would hold its slots indefinitely. Anything left Pending for
    // longer than the payment window is released.
    private void ExpireStalePending()
    {
        var cutoff = DateTime.Now.AddMinutes(-PaymentWindowMinutes);

        var stale = db.Appointments
                      .Include(a => a.Items)
                      .Where(a => a.Status == AppointmentStatus.Pending && a.CreatedAt < cutoff)
                      .ToList();

        if (stale.Count == 0) return;

        foreach (var appointment in stale)
        {
            ReleasePending(appointment, "Payment not completed in time");
        }

        db.SaveChanges();
    }

    private const int PaymentWindowMinutes = 20;

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
