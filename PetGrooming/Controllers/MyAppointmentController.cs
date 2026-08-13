using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PetGrooming.Controllers;

// What the member sees after booking: what is coming up, what has happened, and
// the ability to cancel under the published refund policy.
[Authorize(Roles = "Member")]
public class MyAppointmentController(DB db, Helper hp, StripeService stripe,
                                     WaitlistService waitlist) : Controller
{
    // GET: MyAppointment/Index
    public IActionResult Index(string? filter)
    {
        var email = User.Identity!.Name!;

        var all = db.Appointments
                    .Include(a => a.Items).ThenInclude(i => i.Pet)
                    .Include(a => a.Items).ThenInclude(i => i.Service)
                    .Include(a => a.Items).ThenInclude(i => i.Staff)
                    .Include(a => a.Payments)
                    .Where(a => a.MemberEmail == email)
                    .ToList();

        var now = DateTime.Now;

        // "Upcoming" means still actionable, not merely future-dated: a cancelled
        // booking next week belongs in history.
        var upcoming = all.Where(a => !AppointmentWorkflow.IsTerminal(a.Status)
                                   && a.Items.Any(i => i.SlotStart >= now))
                          .OrderBy(a => a.Items.Min(i => i.SlotStart))
                          .ToList();

        var past = all.Except(upcoming)
                      .OrderByDescending(a => a.Items.Max(i => i.SlotStart))
                      .ToList();

        if (filter == "past") upcoming = [];
        if (filter == "upcoming") past = [];

        ViewBag.Upcoming = upcoming;
        ViewBag.Past = past;
        ViewBag.Filter = filter;

        if (Request.IsAjax()) return PartialView("_Index");

        ViewBag.Title = "My Appointments";
        return View();
    }

    // GET: MyAppointment/Detail
    public IActionResult Detail(int id)
    {
        var m = Load(id);

        if (m == null)
        {
            TempData["Info"] = "Booking not found.";
            return RedirectToAction("Index");
        }

        ViewBag.Title = $"Booking {m.BookingRef}";
        ViewBag.CanCancel = AppointmentWorkflow.CanTransition(m.Status, AppointmentStatus.Cancelled);
        return View(m);
    }

    // GET: MyAppointment/Cancel
    // Shows exactly what the refund will be before anything is committed.
    public IActionResult Cancel(int id)
    {
        var m = Load(id);

        if (m == null)
        {
            TempData["Info"] = "Booking not found.";
            return RedirectToAction("Index");
        }

        if (!AppointmentWorkflow.CanTransition(m.Status, AppointmentStatus.Cancelled))
        {
            TempData["Info"] = $"A {m.Status} booking can no longer be cancelled.";
            return RedirectToAction("Detail", new { id });
        }

        ViewBag.Title = "Cancel Booking";
        return View(BuildCancelVM(m));
    }

    // POST: MyAppointment/Cancel
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Cancel(CancelVM posted)
    {
        var m = Load(posted.AppointmentId);

        if (m == null)
        {
            TempData["Info"] = "Booking not found.";
            return RedirectToAction("Index");
        }

        if (!AppointmentWorkflow.CanTransition(m.Status, AppointmentStatus.Cancelled))
        {
            TempData["Info"] = $"A {m.Status} booking can no longer be cancelled.";
            return RedirectToAction("Detail", new { id = m.Id });
        }

        if (!ModelState.IsValid)
        {
            ViewBag.Title = "Cancel Booking";
            var invalid = BuildCancelVM(m);
            invalid.Reason = posted.Reason;
            return View(invalid);
        }

        // Recalculated here, never taken from the form -- the refund is money.
        var vm = BuildCancelVM(m);
        var refund = vm.RefundAmount;
        var note = "";

        if (refund > 0)
        {
            var payment = m.Payments.FirstOrDefault(p => p.Status == PaymentStatus.Paid);

            if (payment == null)
            {
                refund = 0m;
            }
            else if (payment.Method == PaymentMethod.Stripe &&
                     !string.IsNullOrWhiteSpace(payment.ProviderRef))
            {
                var error = stripe.Refund(payment.ProviderRef, refund);

                if (error != "")
                {
                    // Do not cancel a booking we could not actually refund --
                    // the member would lose both the slot and the money.
                    TempData["Info"] = $"The refund could not be processed ({error}). " +
                                       "Nothing was cancelled. Please contact the salon.";
                    return RedirectToAction("Detail", new { id = m.Id });
                }

                note = " The refund has been sent back to your card.";
                ApplyRefund(payment, refund);
            }
            else
            {
                note = " The refund will be returned at the counter.";
                ApplyRefund(payment, refund);
            }
        }

        m.Status = AppointmentStatus.Cancelled;
        m.CancelledAt = DateTime.Now;
        m.CancelReason = posted.Reason;
        m.RefundAmount = refund;

        foreach (var item in m.Items)
        {
            item.ItemStatus = AppointmentStatus.Cancelled;
        }

        db.AppointmentStatusHistories.Add(new AppointmentStatusHistory
        {
            AppointmentId = m.Id,
            FromStatus = AppointmentStatus.Confirmed,
            ToStatus = AppointmentStatus.Cancelled,
            ChangedAt = DateTime.Now,
            ChangedByEmail = User.Identity!.Name!,
            Remark = $"Cancelled by member: {posted.Reason}. Refund RM {refund:N2}",
        });

        db.SaveChanges();

        // The slot is free again, so offer it to whoever has been waiting longest.
        var offered = waitlist.NotifyForFreedSlots(m);

        TempData["Info"] = (refund > 0
            ? $"Booking {m.BookingRef} cancelled. RM {refund:N2} will be refunded.{note}"
            : $"Booking {m.BookingRef} cancelled. {vm.PolicyExplanation}")
            + (offered > 0 ? $" {offered} member(s) on the waitlist have been notified." : "");

        return RedirectToAction("Index");
    }

    // GET: MyAppointment/Receipt
    public IActionResult Receipt(int id)
    {
        var m = Load(id);

        if (m == null)
        {
            TempData["Info"] = "Booking not found.";
            return RedirectToAction("Index");
        }

        return File(hp.GenerateReceipt(m), "application/pdf", hp.ReceiptFileName(m));
    }



    // ------------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------------

    private static void ApplyRefund(Payment payment, decimal refund)
    {
        payment.RefundedAmount = refund;
        payment.RefundedAt = DateTime.Now;
        payment.Status = refund >= payment.Amount
                       ? PaymentStatus.Refunded
                       : PaymentStatus.PartiallyRefunded;
    }

    private CancelVM BuildCancelVM(Appointment m)
    {
        var earliest = m.Items.Min(i => i.SlotStart);
        var paid = m.Payments.Where(p => p.Status == PaymentStatus.Paid).Sum(p => p.Amount);
        var outcome = RefundPolicy.Calculate(paid, m.DepositAmount, earliest, DateTime.Now);

        return new CancelVM
        {
            AppointmentId = m.Id,
            BookingRef = m.BookingRef,
            EarliestSlot = earliest,
            PaidAmount = paid,
            RefundAmount = outcome.Amount,
            PolicyExplanation = outcome.Explanation,
        };
    }

    // Scoped to the signed-in member, so changing the id in the URL cannot reach
    // somebody else's booking.
    private Appointment? Load(int id)
    {
        return db.Appointments
                 .Include(a => a.Member)
                 .Include(a => a.Items).ThenInclude(i => i.Pet)
                 .Include(a => a.Items).ThenInclude(i => i.Service)
                 .Include(a => a.Items).ThenInclude(i => i.Staff)
                 .Include(a => a.Items).ThenInclude(i => i.Report).ThenInclude(r => r.Photos)
                 .Include(a => a.Payments)
                 .Include(a => a.StatusHistories)
                 .FirstOrDefault(a => a.Id == id && a.MemberEmail == User.Identity!.Name);
    }
}
