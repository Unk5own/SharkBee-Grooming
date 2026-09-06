using System.Net.Mail;

namespace PetGrooming;

// When a booking is cancelled its slot becomes free again. Rather than leaving
// that to whoever happens to refresh the catalog next, the first member waiting
// for that service on that date is emailed.
//
// One notification per freed slot, oldest entry first, so the queue is fair.
public class WaitlistService(DB db, Helper hp, ILogger<WaitlistService> log)
{
    // How long a member has to act on an offer before the slot is treated as
    // unclaimed and their entry rejoins the queue. Nothing holds the slot for
    // them -- the email says so -- this only governs when we stop waiting.
    private const int OfferWindowHours = 24;

    // Brings every entry's status up to date. Without this, Notified is a dead
    // end: a member who never answered their one email stays out of the queue
    // for ever while still appearing to be waitlisted.
    //
    // Runs on the paths that read or act on the waitlist, in the same
    // sweep-on-access style as CheckoutController.ExpireStalePending.
    public int Sweep()
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var changed = 0;

        // Past the last date they asked for nothing can be offered any more, so
        // the entry retires instead of sitting in the queue for ever.
        var lapsed = db.Waitlists
                       .Where(w => (w.Status == WaitlistStatus.Waiting
                                 || w.Status == WaitlistStatus.Notified)
                                && w.DesiredTo < today)
                       .ToList();

        foreach (var entry in lapsed)
        {
            entry.Status = WaitlistStatus.Expired;
            changed++;
        }

        // An offer nobody answered must not strand the member. Past the window
        // they go back into the queue -- unless they actually booked, which is
        // the one thing that means Converted.
        var cutoff = DateTime.Now.AddHours(-OfferWindowHours);

        var unanswered = db.Waitlists
                           .Where(w => w.Status == WaitlistStatus.Notified
                                    && w.NotifiedAt != null
                                    && w.NotifiedAt <= cutoff
                                    && w.DesiredTo >= today)
                           .ToList();

        foreach (var entry in unanswered)
        {
            if (Booked(entry))
            {
                entry.Status = WaitlistStatus.Converted;
            }
            else
            {
                // Cleared so the list shows them plainly waiting again rather
                // than carrying the timestamp of an offer that came to nothing.
                entry.Status = WaitlistStatus.Waiting;
                entry.NotifiedAt = null;
            }

            changed++;
        }

        if (changed > 0) db.SaveChanges();

        return changed;
    }

    // A waitlist entry has no link to an appointment, so a conversion has to be
    // inferred: a live booking for the same pet and service, inside the range
    // they asked for, made after we told them a slot had opened.
    private bool Booked(Waitlist entry)
    {
        var from = entry.DesiredFrom.ToDateTime(TimeOnly.MinValue);
        var to = entry.DesiredTo.ToDateTime(TimeOnly.MaxValue);

        return db.AppointmentItems.Any(i =>
            i.Appointment.MemberEmail == entry.MemberEmail &&
            i.PetId == entry.PetId &&
            i.ServiceId == entry.ServiceId &&
            i.ItemStatus != AppointmentStatus.Cancelled &&
            i.SlotStart >= from && i.SlotStart <= to &&
            i.Appointment.CreatedAt >= entry.NotifiedAt);
    }

    // Called after a cancellation has been saved. Returns how many members were
    // notified, so the caller can mention it.
    public int NotifyForFreedSlots(Appointment cancelled)
    {
        // Re-armed entries must be back in the queue before candidates are
        // picked, or a freed slot would skip the very people still waiting.
        Sweep();

        var notified = 0;

        foreach (var item in cancelled.Items)
        {
            var freedDate = DateOnly.FromDateTime(item.SlotStart);

            var candidate = db.Waitlists
                              .Include(w => w.Member)
                              .Include(w => w.Pet)
                              .Include(w => w.Service)
                              .Where(w => w.Status == WaitlistStatus.Waiting
                                       && w.ServiceId == item.ServiceId
                                       && w.DesiredFrom <= freedDate
                                       && w.DesiredTo >= freedDate
                                       && (w.PreferredStaffEmail == null
                                        || w.PreferredStaffEmail == item.StaffEmail))
                              // Do not offer a member the slot they just gave up.
                              .Where(w => w.MemberEmail != cancelled.MemberEmail)
                              .OrderBy(w => w.CreatedAt)
                              .FirstOrDefault();

            if (candidate == null) continue;

            candidate.Status = WaitlistStatus.Notified;
            candidate.NotifiedAt = DateTime.Now;
            notified++;

            Send(candidate, item);
        }

        if (notified > 0) db.SaveChanges();

        return notified;
    }

    // A failed email must not roll back the cancellation that triggered it, so
    // problems are logged rather than thrown.
    private void Send(Waitlist entry, AppointmentItem freed)
    {
        if (!hp.IsEmailConfigured())
        {
            log.LogInformation("Waitlist {Id}: a slot opened on {Date:d MMM} for {Member}, " +
                               "but email is not configured.",
                               entry.Id, freed.SlotStart, entry.MemberEmail);
            return;
        }

        try
        {
            var mail = new MailMessage
            {
                Subject = $"A {entry.Service.Name} slot just opened up",
                IsBodyHtml = true,
                Body = $@"
                    <p>Hi {entry.Member.Name},</p>
                    <p>
                        A slot you were waiting for has become available:
                        <b>{entry.Service.Name}</b> for <b>{entry.Pet.Name}</b> on
                        <b>{freed.SlotStart:dddd, d MMMM yyyy 'at' h:mm tt}</b>.
                    </p>
                    <p>
                        Slots are offered first come, first served, so book soon if
                        you still want it.
                    </p>
                    <p>&mdash; SharkBee Grooming</p>",
            };

            mail.To.Add(new MailAddress(entry.MemberEmail));
            hp.SendEmail(mail);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Waitlist notification failed for {Email}", entry.MemberEmail);
        }
    }
}
