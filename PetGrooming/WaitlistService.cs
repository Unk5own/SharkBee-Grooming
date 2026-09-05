using System.Net.Mail;

namespace PetGrooming;

// When a booking is cancelled its slot becomes free again. Rather than leaving
// that to whoever happens to refresh the catalog next, the first member waiting
// for that service on that date is emailed.
//
// One notification per freed slot, oldest entry first, so the queue is fair.
public class WaitlistService(DB db, Helper hp, ILogger<WaitlistService> log)
{
    // Called after a cancellation has been saved. Returns how many members were
    // notified, so the caller can mention it.
    public int NotifyForFreedSlots(Appointment cancelled)
    {
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
