using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace PetGrooming.Models;

#nullable disable warnings

public class DB(DbContextOptions options) : DbContext(options)
{
    // DB Sets
    public DbSet<User> Users { get; set; }
    public DbSet<Admin> Admins { get; set; }
    public DbSet<Staff> Staffs { get; set; }
    public DbSet<Member> Members { get; set; }

    public DbSet<StaffSchedule> StaffSchedules { get; set; }
    public DbSet<StaffTimeOff> StaffTimeOffs { get; set; }

    public DbSet<Pet> Pets { get; set; }
    public DbSet<PetPhoto> PetPhotos { get; set; }

    public DbSet<ServiceCategory> ServiceCategories { get; set; }
    public DbSet<Service> Services { get; set; }

    public DbSet<Appointment> Appointments { get; set; }
    public DbSet<AppointmentItem> AppointmentItems { get; set; }
    public DbSet<Payment> Payments { get; set; }
    public DbSet<AppointmentStatusHistory> AppointmentStatusHistories { get; set; }
    public DbSet<GroomingReport> GroomingReports { get; set; }
    public DbSet<GroomingReportPhoto> GroomingReportPhotos { get; set; }
    public DbSet<Review> Reviews { get; set; }
    public DbSet<Waitlist> Waitlists { get; set; }

    // ------------------------------------------------------------------------
    // NOTE ON FLUENT API
    // ------------------------------------------------------------------------
    // The assignment requires data annotations "but NOT Fluent API ... except if
    // really necessary". Delete behaviour is the one thing that has no data
    // annotation equivalent, and without it SQL Server rejects the migration with
    // "may cause cycles or multiple cascade paths" -- because a Member reaches
    // AppointmentItem through two routes (Member -> Appointment -> Item, and
    // Member -> Pet -> Item).
    //
    // Restricting these deletes is also the correct business rule: a pet, groomer
    // or service that already has appointment history must not be deletable.
    // Accounts are deactivated with the Blocked flag instead of being deleted.
    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Appointment>()
          .HasOne(a => a.Member).WithMany(m => m.Appointments)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<AppointmentItem>()
          .HasOne(ai => ai.Pet).WithMany(p => p.AppointmentItems)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<AppointmentItem>()
          .HasOne(ai => ai.Service).WithMany(s => s.AppointmentItems)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<AppointmentItem>()
          .HasOne(ai => ai.Staff).WithMany(s => s.AppointmentItems)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<Review>()
          .HasOne(r => r.Member).WithMany(m => m.Reviews)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<Review>()
          .HasOne(r => r.Staff).WithMany(s => s.Reviews)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<Waitlist>()
          .HasOne(w => w.Pet).WithMany(p => p.Waitlists)
          .OnDelete(DeleteBehavior.Restrict);

        mb.Entity<Waitlist>()
          .HasOne(w => w.Service).WithMany(s => s.Waitlists)
          .OnDelete(DeleteBehavior.Restrict);
    }
}

// Enumerations ---------------------------------------------------------------

public enum AppointmentStatus
{
    Pending,     // created, awaiting payment
    Confirmed,   // paid, or due at the counter; slot secured
    CheckedIn,   // pet has arrived at the salon
    InProgress,  // grooming underway
    Completed,   // grooming finished
    Cancelled,   // cancelled by member or staff
    NoShow,      // member never arrived
}

public enum PaymentMethod
{
    Counter,     // offline / pay at counter
    Stripe,      // 3rd-party payment API
}

public enum PaymentStatus
{
    Pending,
    Paid,
    Failed,
    Refunded,
    PartiallyRefunded,
}

public enum PhotoType
{
    Before,
    After,
}

public enum WaitlistStatus
{
    Waiting,
    Notified,
    Converted,
    Expired,
}

// Entity Classes -------------------------------------------------------------
// PIC: Student 1 (Access and Identity)

public class User
{
    [Key, MaxLength(100)]
    public string Email { get; set; }
    [MaxLength(100)]
    public string Hash { get; set; }
    [MaxLength(100)]
    public string Name { get; set; }
    [MaxLength(100)]
    public string PhotoURL { get; set; }

    // Set by Admin in User Maintenance. Accounts are blocked, never deleted,
    // so that appointment history stays intact.
    public bool Blocked { get; set; }

    // New registrations are unverified until the optional email link is used.
    // Existing/administrator-created accounts are treated as verified.
    public bool EmailVerified { get; set; } = true;

    public string Role => GetType().Name;
}

public class Admin : User
{

}

// A Staff member is a groomer.
public class Staff : User
{
    [MaxLength(100)]
    public string Specialization { get; set; }

    public DateOnly HireDate { get; set; }

    public bool Active { get; set; } = true;

    // Navigation Properties
    public List<StaffSchedule> Schedules { get; set; } = [];
    public List<StaffTimeOff> TimeOffs { get; set; } = [];
    public List<AppointmentItem> AppointmentItems { get; set; } = [];
    public List<Review> Reviews { get; set; } = [];
}

public class Member : User
{
    [MaxLength(20)]
    public string Phone { get; set; }

    // Navigation Properties
    public List<Pet> Pets { get; set; } = [];
    public List<Appointment> Appointments { get; set; } = [];
    public List<Review> Reviews { get; set; } = [];
    public List<Waitlist> Waitlists { get; set; } = [];
}

// The recurring weekly working hours of a groomer. Booking slots are generated
// from these rows, minus any TimeOff, minus already-booked AppointmentItems.
public class StaffSchedule
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public DayOfWeek Day { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    // Foreign Keys
    public string StaffEmail { get; set; }

    // Navigation Properties
    public Staff Staff { get; set; }
}

public class StaffTimeOff
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public DateOnly StartDate { get; set; }

    public DateOnly EndDate { get; set; }

    [MaxLength(200)]
    public string Reason { get; set; }

    // Foreign Keys
    public string StaffEmail { get; set; }

    // Navigation Properties
    public Staff Staff { get; set; }
}

// PIC: Student 2 (Pets, Services and Booking)

public class Pet
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [MaxLength(50)]
    public string Name { get; set; }

    [MaxLength(20)]
    public string Species { get; set; }

    [MaxLength(50)]
    public string Breed { get; set; }

    public DateOnly BirthDate { get; set; }

    [Precision(5, 2)]
    public decimal WeightKg { get; set; }

    [MaxLength(500)]
    public string Allergies { get; set; }

    [MaxLength(500)]
    public string Notes { get; set; }

    public bool Active { get; set; } = true;

    // Foreign Keys
    public string MemberEmail { get; set; }

    // Navigation Properties
    public Member Member { get; set; }
    public List<PetPhoto> Photos { get; set; } = [];
    public List<AppointmentItem> AppointmentItems { get; set; } = [];
    public List<Waitlist> Waitlists { get; set; } = [];
}

// One-to-many: a pet has many photos (multiple photos per record).
public class PetPhoto
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [MaxLength(100)]
    public string PhotoURL { get; set; }

    public bool IsPrimary { get; set; }

    public int SortOrder { get; set; }

    // Foreign Keys
    public int PetId { get; set; }

    // Navigation Properties
    public Pet Pet { get; set; }
}

public class ServiceCategory
{
    [Key, MaxLength(4)]
    public string Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; }

    // Navigation Properties
    public List<Service> Services { get; set; } = [];
}

// A grooming package, e.g. Basic Wash, Full Groom, Nail Trimming.
public class Service
{
    [Key, MaxLength(4)]
    public string Id { get; set; }

    [MaxLength(100)]
    public string Name { get; set; }

    [MaxLength(500)]
    public string Description { get; set; }

    [Precision(6, 2)]
    public decimal Price { get; set; }

    // Drives the length of the booking slot.
    public int DurationMinutes { get; set; }

    [MaxLength(100)]
    public string PhotoURL { get; set; }

    public bool Active { get; set; } = true;

    // Foreign Keys
    public string CategoryId { get; set; }

    // Navigation Properties
    public ServiceCategory Category { get; set; }
    public List<AppointmentItem> AppointmentItems { get; set; } = [];
    public List<Waitlist> Waitlists { get; set; } = [];
}

// PIC: Student 3 (Checkout, Operations and Reporting)

public class Appointment
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    // Human-readable reference shown to the member, e.g. PG-20260812-4F7A
    [MaxLength(20)]
    public string BookingRef { get; set; }

    public DateTime CreatedAt { get; set; }

    public AppointmentStatus Status { get; set; }

    [Precision(8, 2)]
    public decimal Subtotal { get; set; }

    [Precision(8, 2)]
    public decimal Discount { get; set; }

    [Precision(8, 2)]
    public decimal Total { get; set; }

    [Precision(8, 2)]
    public decimal RefundAmount { get; set; }

    [MaxLength(500)]
    public string Notes { get; set; }

    public DateTime? CancelledAt { get; set; }

    [MaxLength(200)]
    public string CancelReason { get; set; }

    // Foreign Keys
    public string MemberEmail { get; set; }

    // Navigation Properties
    public Member Member { get; set; }
    public List<AppointmentItem> Items { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
    public List<AppointmentStatusHistory> StatusHistories { get; set; } = [];
}

// One booked slot: this pet, this service, this groomer, at this time.
[Index(nameof(SlotStart))]
[Index(nameof(StaffEmail), nameof(SlotStart))]
public class AppointmentItem
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public DateTime SlotStart { get; set; }

    public DateTime SlotEnd { get; set; }

    // Snapshotted from Service.Price at booking time. Never read the live price
    // when displaying history -- an admin price change would otherwise silently
    // rewrite every past receipt and distort the revenue charts.
    [Precision(6, 2)]
    public decimal UnitPrice { get; set; }

    public AppointmentStatus ItemStatus { get; set; }

    // Foreign Keys
    public int AppointmentId { get; set; }
    public int PetId { get; set; }
    public string ServiceId { get; set; }
    public string StaffEmail { get; set; }

    // Navigation Properties
    public Appointment Appointment { get; set; }
    public Pet Pet { get; set; }
    public Service Service { get; set; }
    public Staff Staff { get; set; }
    public GroomingReport Report { get; set; }
    public Review Review { get; set; }
}

public class Payment
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public PaymentMethod Method { get; set; }

    public PaymentStatus Status { get; set; }

    [Precision(8, 2)]
    public decimal Amount { get; set; }

    [Precision(8, 2)]
    public decimal RefundedAmount { get; set; }

    // Stripe PaymentIntent id, e.g. pi_3Abc... Empty for counter payments.
    [MaxLength(100)]
    public string ProviderRef { get; set; }

    public DateTime? PaidAt { get; set; }

    public DateTime? RefundedAt { get; set; }

    // Foreign Keys
    public int AppointmentId { get; set; }

    // Navigation Properties
    public Appointment Appointment { get; set; }
}

// Audit trail of every status change. Also the data source for the
// cancellation-rate and turnaround-time charts.
public class AppointmentStatusHistory
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public AppointmentStatus FromStatus { get; set; }

    public AppointmentStatus ToStatus { get; set; }

    [MaxLength(100)]
    public string ChangedByEmail { get; set; }

    public DateTime ChangedAt { get; set; }

    [MaxLength(200)]
    public string Remark { get; set; }

    // Foreign Keys
    public int AppointmentId { get; set; }

    // Navigation Properties
    public Appointment Appointment { get; set; }
}

// Filled in by the groomer when the grooming is completed, then shown to the
// member in their appointment history.
public class GroomingReport
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [MaxLength(1000)]
    public string GroomerNotes { get; set; }

    [MaxLength(100)]
    public string CoatCondition { get; set; }

    [MaxLength(200)]
    public string BehaviourNotes { get; set; }

    [MaxLength(200)]
    public string NextVisitRecommendation { get; set; }

    public DateTime CreatedAt { get; set; }

    // Foreign Keys
    public int AppointmentItemId { get; set; }

    // Navigation Properties
    public AppointmentItem AppointmentItem { get; set; }
    public List<GroomingReportPhoto> Photos { get; set; } = [];
}

public class GroomingReportPhoto
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [MaxLength(100)]
    public string PhotoURL { get; set; }

    public PhotoType PhotoType { get; set; }

    public int SortOrder { get; set; }

    // Foreign Keys
    public int GroomingReportId { get; set; }

    // Navigation Properties
    public GroomingReport GroomingReport { get; set; }
}

// Written by the member after the appointment item is Completed. The average
// rating feeds back into groomer selection during booking.
public class Review
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Range(1, 5)]
    public int Rating { get; set; }

    [MaxLength(500)]
    public string Comment { get; set; }

    public DateTime CreatedAt { get; set; }

    // Foreign Keys
    public int AppointmentItemId { get; set; }
    public string MemberEmail { get; set; }
    public string StaffEmail { get; set; }

    // Navigation Properties
    public AppointmentItem AppointmentItem { get; set; }
    public Member Member { get; set; }
    public Staff Staff { get; set; }
}

// When a member wants a fully-booked day, they join the waitlist. Cancelling an
// appointment notifies the next Waiting entry that overlaps the freed slot.
public class Waitlist
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public DateOnly DesiredFrom { get; set; }

    public DateOnly DesiredTo { get; set; }

    public WaitlistStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? NotifiedAt { get; set; }

    // Foreign Keys
    public string MemberEmail { get; set; }
    public int PetId { get; set; }
    public string ServiceId { get; set; }
    // Optional: the member may not care which groomer.
    public string? PreferredStaffEmail { get; set; }

    // Navigation Properties
    public Member Member { get; set; }
    public Pet Pet { get; set; }
    public Service Service { get; set; }
}
