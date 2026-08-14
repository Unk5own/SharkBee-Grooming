using Microsoft.AspNetCore.Mvc;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace PetGrooming.Models;

#nullable disable warnings

// View Models ----------------------------------------------------------------
// PIC: Student 1 (Access and Identity)

public class LoginVM
{
    [StringLength(100)]
    [EmailAddress]
    public string Email { get; set; }

    [StringLength(100, MinimumLength = 5)]
    [DataType(DataType.Password)]
    public string Password { get; set; }

    public bool RememberMe { get; set; }
}

public class RegisterVM
{
    [StringLength(100)]
    [EmailAddress]
    [Remote("CheckEmail", "Account", ErrorMessage = "Duplicated {0}.")]
    public string Email { get; set; }

    [StringLength(100, MinimumLength = 5)]
    [DataType(DataType.Password)]
    public string Password { get; set; }

    [StringLength(100, MinimumLength = 5)]
    [Compare("Password")]
    [DataType(DataType.Password)]
    [DisplayName("Confirm Password")]
    public string Confirm { get; set; }

    [StringLength(100)]
    public string Name { get; set; }

    [StringLength(20)]
    [Phone]
    public string Phone { get; set; }

    public IFormFile Photo { get; set; }
}

public class UpdatePasswordVM
{
    [StringLength(100, MinimumLength = 5)]
    [DataType(DataType.Password)]
    [DisplayName("Current Password")]
    public string Current { get; set; }

    [StringLength(100, MinimumLength = 5)]
    [DataType(DataType.Password)]
    [DisplayName("New Password")]
    public string New { get; set; }

    [StringLength(100, MinimumLength = 5)]
    [Compare("New")]
    [DataType(DataType.Password)]
    [DisplayName("Confirm Password")]
    public string Confirm { get; set; }
}

public class UpdateProfileVM
{
    public string? Email { get; set; }

    [StringLength(100)]
    public string Name { get; set; }

    public string? PhotoURL { get; set; }

    public IFormFile? Photo { get; set; }
}

public class ResetPasswordVM
{
    [StringLength(100)]
    [EmailAddress]
    public string Email { get; set; }
}

// PIC: Student 2 (Pets, Services and Booking)

// One line in the session booking cart. Student 2 writes these during booking;
// Student 3's checkout reads them and then clears the cart.
public class BookingCartItem
{
    public int PetId { get; set; }
    public string ServiceId { get; set; }
    public string StaffEmail { get; set; }
    public DateTime SlotStart { get; set; }
}

// PIC: Student 3 (Checkout, Operations and Reporting)

// A cart line joined to its Pet / Service / Staff records, ready for display.
public class CheckoutLineVM
{
    public BookingCartItem Item { get; set; }
    public Pet Pet { get; set; }
    public Service Service { get; set; }
    public Staff Staff { get; set; }
    public DateTime SlotEnd { get; set; }
    public decimal Subtotal { get; set; }

    // Set when the slot was taken by someone else between adding to cart and
    // checking out, so the view can highlight the offending line.
    public bool Unavailable { get; set; }
}

public class CheckoutVM
{
    public List<CheckoutLineVM> Lines { get; set; } = [];

    public decimal Subtotal { get; set; }
    public decimal Discount { get; set; }
    public decimal Total { get; set; }
    public decimal DepositAmount { get; set; }

    // Nullable on purpose: a non-nullable string is inferred as required by MVC,
    // which would stop anyone booking without typing a note.
    [StringLength(500)]
    [DisplayName("Special Requests")]
    public string? Notes { get; set; }

    [DisplayName("Payment Method")]
    public PaymentMethod Method { get; set; }

    [DisplayName("Pay deposit only")]
    public bool DepositOnly { get; set; }
}

public class CancelVM
{
    public int AppointmentId { get; set; }

    // Display-only, and never posted back -- so they must be nullable, otherwise
    // MVC infers them as required and refuses every cancellation. Everything here
    // except AppointmentId and Reason is recalculated from the database on POST.
    public string? BookingRef { get; set; }
    public DateTime EarliestSlot { get; set; }

    public decimal PaidAmount { get; set; }
    public decimal RefundAmount { get; set; }
    public string? PolicyExplanation { get; set; }

    [Required]
    [StringLength(200)]
    [DisplayName("Reason for cancelling")]
    public string Reason { get; set; }
}

public class ReportCardVM
{
    public int AppointmentItemId { get; set; }

    // All optional, so nullable -- see the note on CheckoutVM.Notes.
    [StringLength(1000)]
    [DisplayName("Groomer Notes")]
    public string? GroomerNotes { get; set; }

    [StringLength(100)]
    [DisplayName("Coat Condition")]
    public string? CoatCondition { get; set; }

    [StringLength(200)]
    [DisplayName("Behaviour Notes")]
    public string? BehaviourNotes { get; set; }

    [StringLength(200)]
    [DisplayName("Recommendation for Next Visit")]
    public string? NextVisitRecommendation { get; set; }

    // Nullable for the same reason as the strings above: a non-nullable reference
    // type is inferred as required, which would block saving a report with no
    // new photos attached.
    [DisplayName("Before Photos")]
    public List<IFormFile>? BeforePhotos { get; set; }

    [DisplayName("After Photos")]
    public List<IFormFile>? AfterPhotos { get; set; }

    // Display only.
    public string? PetName { get; set; }
    public string? ServiceName { get; set; }
}

public class ReviewVM
{
    public int AppointmentItemId { get; set; }

    [Range(1, 5)]
    public int Rating { get; set; }

    [StringLength(500)]
    public string? Comment { get; set; }
}

// A groomer's standing, built from member reviews.
public class GroomerRatingVM
{
    public string? Name { get; set; }
    public string? Specialization { get; set; }
    public decimal Average { get; set; }
    public int Count { get; set; }
}

public class WaitlistVM
{
    [DisplayName("Pet")]
    public int PetId { get; set; }

    // Required on purpose -- you cannot wait for nothing in particular.
    [Required]
    [DisplayName("Service")]
    public string ServiceId { get; set; }

    [DisplayName("Preferred groomer")]
    public string? PreferredStaffEmail { get; set; }

    [DataType(DataType.Date)]
    [DisplayName("Earliest date")]
    public DateOnly DesiredFrom { get; set; }

    [DataType(DataType.Date)]
    [DisplayName("Latest date")]
    public DateOnly DesiredTo { get; set; }
}

// One groomer's column on the daily schedule board.
public class BoardColumnVM
{
    public Staff Staff { get; set; }
    public List<AppointmentItem> Items { get; set; } = [];
    public TimeOnly? ShiftStart { get; set; }
    public TimeOnly? ShiftEnd { get; set; }
    public bool OnLeave { get; set; }
}

public class ScheduleBoardVM
{
    public DateOnly Date { get; set; }
    public List<BoardColumnVM> Columns { get; set; } = [];
    public List<TimeOnly> Slots { get; set; } = [];
    public int SlotMinutes { get; set; }
}

// Payload for the dashboard's AJAX chart endpoint.
public class ChartSeriesVM
{
    public List<string> Labels { get; set; } = [];
    public List<decimal> Values { get; set; } = [];
}

public class DashboardVM
{
    public decimal RevenueThisMonth { get; set; }
    public int AppointmentsThisMonth { get; set; }
    public int CompletedThisMonth { get; set; }
    public decimal CancellationRate { get; set; }

    public ChartSeriesVM MonthlyRevenue { get; set; } = new();
    public ChartSeriesVM PopularServices { get; set; } = new();
    public ChartSeriesVM GroomerUtilisation { get; set; } = new();
    public ChartSeriesVM PeakHours { get; set; } = new();
}
