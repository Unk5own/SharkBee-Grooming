namespace PetGrooming;

// The cancellation refund rules, in one place so they can be pointed at during
// the demo and cannot drift between the member's cancel screen and the code
// that actually moves the money.
//
//   more than 48 hours before the appointment  full refund
//   between 24 and 48 hours before             half refund
//   less than 24 hours before                  no refund
//   already passed or a no-show                no refund
public static class RefundPolicy
{
    public const int FullRefundHours = 48;
    public const int HalfRefundHours = 24;

    public record Outcome(decimal Amount, string Explanation);

    public static Outcome Calculate(decimal paid, DateTime earliestSlot, DateTime now)
    {
        // Nothing has been paid yet, so there is nothing to give back.
        if (paid <= 0)
        {
            return new(0m, "No payment has been taken, so there is nothing to refund.");
        }

        var hours = (earliestSlot - now).TotalHours;

        if (hours < 0)
        {
            return new(0m, "This appointment has already passed, so no refund applies.");
        }

        if (hours >= FullRefundHours)
        {
            return new(paid,
                $"Cancelled more than {FullRefundHours} hours ahead, so the full " +
                $"RM {paid:N2} is refunded.");
        }

        if (hours >= HalfRefundHours)
        {
            var half = Math.Round(paid * 0.5m, 2);
            return new(half,
                $"Cancelled between {HalfRefundHours} and {FullRefundHours} hours ahead, " +
                $"so half of RM {paid:N2} is refunded, which is RM {half:N2}.");
        }

        // Inside 24 hours the slot can no longer be resold, so nothing is returned.
        return new(0m,
            $"Cancelled less than {HalfRefundHours} hours ahead, so no refund is due.");
    }
}
