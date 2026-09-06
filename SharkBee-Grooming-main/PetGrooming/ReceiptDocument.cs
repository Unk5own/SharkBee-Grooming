using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace PetGrooming;

// The e-receipt issued once a booking is confirmed. Kept separate from Helper
// because a QuestPDF document is a layout definition, not a utility function.
public class ReceiptDocument(Appointment appointment) : IDocument
{
    private static readonly string Brand = "#b5622f";
    private static readonly string Ink = "#2c2420";
    private static readonly string Muted = "#7a6a60";
    private static readonly string Line = "#e6ddd6";
    private static readonly string Sand = "#f7f1ea";

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Receipt {appointment.BookingRef}",
        Author = "SharkBee Grooming",
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(40);
            page.DefaultTextStyle(t => t.FontSize(10).FontColor(Ink));

            page.Header().Element(ComposeHeader);
            page.Content().PaddingVertical(20).Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("SharkBee Grooming")
                          .FontSize(20).Bold().FontColor(Brand);
                col.Item().Text("Professional grooming for dogs and cats")
                          .FontSize(9).FontColor(Muted);
            });

            row.ConstantItem(190).Column(col =>
            {
                col.Item().AlignRight().Text("E-RECEIPT")
                          .FontSize(14).Bold().FontColor(Muted);
                col.Item().AlignRight().Text(appointment.BookingRef)
                          .FontSize(12).Bold();
                col.Item().AlignRight()
                          .Text(appointment.CreatedAt.ToString("d MMM yyyy, h:mm tt"))
                          .FontSize(9).FontColor(Muted);
            });
        });
    }

    private void ComposeContent(IContainer container)
    {
        container.Column(col =>
        {
            col.Spacing(16);

            col.Item().Element(ComposeCustomer);
            col.Item().Element(ComposeItems);
            col.Item().Element(ComposeTotals);
            col.Item().Element(ComposePayment);

            if (!string.IsNullOrWhiteSpace(appointment.Notes))
            {
                col.Item().Background(Sand).Padding(10).Column(c =>
                {
                    c.Item().Text("Notes from the owner").Bold().FontSize(9);
                    c.Item().Text(appointment.Notes).FontSize(9);
                });
            }
        });
    }

    private void ComposeCustomer(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().Text("BILLED TO").FontSize(8).Bold().FontColor(Muted);
                col.Item().Text(appointment.Member?.Name ?? appointment.MemberEmail).Bold();
                col.Item().Text(appointment.MemberEmail).FontSize(9).FontColor(Muted);

                if (!string.IsNullOrWhiteSpace(appointment.Member?.Phone))
                {
                    col.Item().Text(appointment.Member.Phone).FontSize(9).FontColor(Muted);
                }
            });

            row.ConstantItem(190).Column(col =>
            {
                col.Item().AlignRight().Text("STATUS").FontSize(8).Bold().FontColor(Muted);
                col.Item().AlignRight().Text(appointment.Status.ToString()).Bold();
            });
        });
    }

    private void ComposeItems(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(c =>
            {
                c.RelativeColumn(3);   // pet
                c.RelativeColumn(4);   // service
                c.RelativeColumn(3);   // groomer
                c.RelativeColumn(4);   // when
                c.RelativeColumn(2);   // price
            });

            table.Header(header =>
            {
                void H(string text, bool right = false)
                {
                    var cell = header.Cell().Background(Sand).PaddingVertical(6).PaddingHorizontal(6);
                    var t = right ? cell.AlignRight() : cell;
                    t.Text(text).FontSize(8).Bold().FontColor(Muted);
                }

                H("PET"); H("SERVICE"); H("GROOMER"); H("DATE AND TIME"); H("PRICE", true);
            });

            foreach (var item in appointment.Items.OrderBy(i => i.SlotStart))
            {
                IContainer Cell() => table.Cell()
                                          .BorderBottom(1).BorderColor(Line)
                                          .PaddingVertical(7).PaddingHorizontal(6);

                Cell().Column(c =>
                {
                    c.Item().Text(item.Pet?.Name ?? "-");
                    c.Item().Text(item.Pet?.Breed ?? "").FontSize(8).FontColor(Muted);
                });

                Cell().Column(c =>
                {
                    c.Item().Text(item.Service?.Name ?? "-");
                    c.Item().Text($"{item.Service?.DurationMinutes} minutes")
                            .FontSize(8).FontColor(Muted);
                });

                Cell().Text(item.Staff?.Name ?? "-");

                Cell().Column(c =>
                {
                    c.Item().Text(item.SlotStart.ToString("ddd, d MMM yyyy"));
                    c.Item().Text($"{item.SlotStart:h:mm tt} - {item.SlotEnd:h:mm tt}")
                            .FontSize(8).FontColor(Muted);
                });

                Cell().AlignRight().Text($"RM {item.UnitPrice:N2}");
            }
        });
    }

    private void ComposeTotals(IContainer container)
    {
        container.AlignRight().Width(240).Column(col =>
        {
            void Row(string label, string value)
            {
                col.Item().Row(r =>
                {
                    r.RelativeItem().Text(label);
                    r.ConstantItem(100).AlignRight().Text(value);
                });
            }

            Row("Subtotal", $"RM {appointment.Subtotal:N2}");
            Row("Discount", $"RM {appointment.Discount:N2}");

            col.Item().PaddingTop(6).BorderTop(1).BorderColor(Ink).PaddingTop(6).Row(r =>
            {
                r.RelativeItem().Text("Total").Bold().FontSize(12);
                r.ConstantItem(100).AlignRight().Text($"RM {appointment.Total:N2}").Bold().FontSize(12);
            });

            if (appointment.RefundAmount > 0)
            {
                Row("Refunded", $"- RM {appointment.RefundAmount:N2}");
            }
        });
    }

    private void ComposePayment(IContainer container)
    {
        var payment = appointment.Payments.FirstOrDefault();

        container.Background(Sand).Padding(10).Column(col =>
        {
            col.Item().Text("PAYMENT").FontSize(8).Bold().FontColor(Muted);

            if (payment == null)
            {
                col.Item().Text("No payment recorded.").FontSize(9);
                return;
            }

            col.Item().Text($"{payment.Method} - {payment.Status} - RM {payment.Amount:N2}");

            if (payment.Status == PaymentStatus.Pending)
            {
                col.Item().Text("Please settle this amount at the counter on the day of your appointment.")
                          .FontSize(9).FontColor(Muted);
            }
            else if (payment.PaidAt != null)
            {
                col.Item().Text($"Paid on {payment.PaidAt:d MMM yyyy, h:mm tt}")
                          .FontSize(9).FontColor(Muted);
            }

            if (!string.IsNullOrWhiteSpace(payment.ProviderRef))
            {
                col.Item().Text($"Reference: {payment.ProviderRef}").FontSize(8).FontColor(Muted);
            }
        });
    }

    private void ComposeFooter(IContainer container)
    {
        container.BorderTop(1).BorderColor(Line).PaddingTop(8).Column(col =>
        {
            col.Item().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                t.Span("This is a computer-generated receipt and needs no signature. ");
                t.Span("Cancellations more than 48 hours before the appointment are fully refunded.");
            });

            col.Item().AlignCenter().Text(t =>
            {
                t.DefaultTextStyle(s => s.FontSize(8).FontColor(Muted));
                t.Span("Page ");
                t.CurrentPageNumber();
                t.Span(" of ");
                t.TotalPages();
            });
        });
    }
}
