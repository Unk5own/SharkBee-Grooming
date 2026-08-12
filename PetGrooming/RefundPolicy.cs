namespace PetGrooming;

// The cancellation refund rules, in one place so they can be pointed at during
// the demo and cannot drift between the member's cancel screen and the code
// that actually moves the money.
//
//   more than 48 hours before the appointment  full refund
//   between 24 and 48 hours before             half refund
//   less than 24 hours before                  the deposit is forfeited
//   already a no-show                          no refund
public static class RefundPolicy
{
    public const int FullRefundHours = 48;
    public const int HalfRefundHours = 24;

    public record Outcome(decimal Amount, string Explanation);

    public static Outcome Calculate(decimal paid, decimal deposit,
                                    DateTime earliestSlot, DateTime now)
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

        // Inside 24 hours the deposit is kept to cover the lost slot.
        var refund = Math.Max(0m, Math.Round(paid - deposit, 2));

        return refund <= 0
            ? new(0m,
                $"Cancelled less than {HalfRefundHours} hours ahead, so the deposit of " +
                $"RM {deposit:N2} is forfeited and no refund is due.")
            : new(refund,
                $"Cancelled less than {HalfRefundHours} hours ahead, so the deposit of " +
                $"RM {deposit:N2} is forfeited and RM {refund:N2} is refunded.");
    }
}
