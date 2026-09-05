using Stripe;
using Stripe.Checkout;

namespace PetGrooming;

// Stripe integration using hosted Checkout Sessions.
//
// Deliberately hosted rather than card fields on our own page: the member is
// redirected to Stripe, enters their card there, and returns. No card number
// ever reaches this application, which removes the whole PCI surface.
//
// Test mode only. Use card 4242 4242 4242 4242 with any future expiry and any CVC.
public class StripeService(IConfiguration cf, ILogger<StripeService> log)
{
    private string SecretKey => cf["Stripe:SecretKey"] ?? "";

    // The committed appsettings.json ships "sk_test_xxx" placeholders, so a
    // developer without a Stripe account gets a clear message instead of an
    // authentication error from the API.
    public bool IsConfigured =>
        SecretKey.StartsWith("sk_test_") && SecretKey.Length > 20;

    // Guards against a live key being used by accident in a student project.
    public bool IsLiveKey => SecretKey.StartsWith("sk_live_");

    // Creates the hosted payment page and returns its URL, or null on failure.
    public string? CreateCheckoutSession(Appointment appointment, decimal amount,
                                         string successUrl, string cancelUrl)
    {
        if (!IsConfigured) return null;

        try
        {
            StripeConfiguration.ApiKey = SecretKey;

            var lineItems = appointment.Items.Select(item => new SessionLineItemOptions
            {
                Quantity = 1,
                PriceData = new SessionLineItemPriceDataOptions
                {
                    Currency = "myr",
                    // Stripe works in the smallest currency unit, so RM 45.00 is 4500.
                    UnitAmountDecimal = item.UnitPrice * 100m,
                    ProductData = new SessionLineItemPriceDataProductDataOptions
                    {
                        Name = $"{item.Service?.Name} for {item.Pet?.Name}",
                        Description = $"{item.SlotStart:ddd d MMM yyyy, h:mm tt} " +
                                      $"with {item.Staff?.Name}",
                    },
                },
            }).ToList();

            var options = new SessionCreateOptions
            {
                Mode = "payment",
                LineItems = lineItems,
                CustomerEmail = appointment.MemberEmail,
                ClientReferenceId = appointment.Id.ToString(),
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
                Metadata = new Dictionary<string, string>
                {
                    ["bookingRef"] = appointment.BookingRef,
                    ["appointmentId"] = appointment.Id.ToString(),
                },
            };

            return new SessionService().Create(options).Url;
        }
        catch (StripeException ex)
        {
            log.LogError(ex, "Stripe session creation failed for {Ref}", appointment.BookingRef);
            return null;
        }
    }

    // Confirms with Stripe that the session was actually paid.
    //
    // The success redirect must never be trusted on its own: anyone can type the
    // success URL into their browser. Payment is only recorded after this
    // server-to-server check.
    public (bool Paid, string PaymentIntentId, decimal Amount) VerifySession(string sessionId)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(sessionId))
        {
            return (false, "", 0m);
        }

        try
        {
            StripeConfiguration.ApiKey = SecretKey;

            var session = new SessionService().Get(sessionId);
            var paid = session.PaymentStatus == "paid";
            var amount = (session.AmountTotal ?? 0) / 100m;

            return (paid, session.PaymentIntentId ?? "", amount);
        }
        catch (StripeException ex)
        {
            log.LogError(ex, "Stripe session verification failed for {SessionId}", sessionId);
            return (false, "", 0m);
        }
    }

    // Refunds a payment, fully or partially. Returns an empty string on success.
    public string Refund(string paymentIntentId, decimal amount)
    {
        if (!IsConfigured) return "Stripe is not configured.";

        try
        {
            StripeConfiguration.ApiKey = SecretKey;

            new RefundService().Create(new RefundCreateOptions
            {
                PaymentIntent = paymentIntentId,
                Amount = (long)(amount * 100m),
            });

            return "";
        }
        catch (StripeException ex)
        {
            log.LogError(ex, "Stripe refund failed for {Intent}", paymentIntentId);
            return ex.Message;
        }
    }
}
