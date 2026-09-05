namespace PetGrooming;

// Sample data for demonstration. The assignment requires the database to contain
// "sufficient sample data for demonstration purpose", and the reporting charts in
// particular are meaningless without months of history behind them -- so this
// generates six months of completed appointments, not just a handful of rows.
//
// The Random is fixed-seed so every team member gets an identical database.
public static class Seeder
{
    // Every seeded account uses this password.
    public const string DemoPassword = "abc123";

    private const int MonthsOfHistory = 6;

    public static void Seed(DB db, Helper hp)
    {
        if (db.Users.Any()) return;

        var hash = hp.HashPassword(DemoPassword);
        var rnd = new Random(20260812);

        SeedUsers(db, hash);
        SeedSchedules(db);
        SeedServices(db);
        db.SaveChanges();

        SeedPets(db);
        db.SaveChanges();

        SeedHistory(db, rnd);
        db.SaveChanges();
    }



    // ------------------------------------------------------------------------
    // Users
    // ------------------------------------------------------------------------

    private static void SeedUsers(DB db, string hash)
    {
        db.Admins.AddRange(
            new Admin { Email = "admin@pawfect.com", Hash = hash, Name = "System Admin", PhotoURL = "" },
            new Admin { Email = "manager@pawfect.com", Hash = hash, Name = "Salon Manager", PhotoURL = "" }
        );

        db.Staffs.AddRange(
            new Staff { Email = "aisyah@pawfect.com", Hash = hash, Name = "Aisyah Rahman", PhotoURL = "", Specialization = "Small breeds and puppies", HireDate = new(2023, 3, 15) },
            new Staff { Email = "kumar@pawfect.com", Hash = hash, Name = "Kumar Selvam", PhotoURL = "", Specialization = "Large breeds and deshedding", HireDate = new(2022, 8, 1) },
            new Staff { Email = "meiling@pawfect.com", Hash = hash, Name = "Tan Mei Ling", PhotoURL = "", Specialization = "Cats and sensitive skin", HireDate = new(2024, 1, 8) },
            new Staff { Email = "daniel@pawfect.com", Hash = hash, Name = "Daniel Wong", PhotoURL = "", Specialization = "Show cuts and styling", HireDate = new(2024, 6, 20) }
        );

        db.Members.AddRange(
            new Member { Email = "chloe@gmail.com", Hash = hash, Name = "Chloe Lim", PhotoURL = "", Phone = "012-3456789" },
            new Member { Email = "arif@gmail.com", Hash = hash, Name = "Arif Hakim", PhotoURL = "", Phone = "013-2345678" },
            new Member { Email = "priya@gmail.com", Hash = hash, Name = "Priya Nair", PhotoURL = "", Phone = "014-8765432" },
            new Member { Email = "jason@gmail.com", Hash = hash, Name = "Jason Teoh", PhotoURL = "", Phone = "016-5551234" },
            new Member { Email = "sarah@gmail.com", Hash = hash, Name = "Sarah Abdullah", PhotoURL = "", Phone = "017-2223333" },
            new Member { Email = "wei@gmail.com", Hash = hash, Name = "Wei Jun Chan", PhotoURL = "", Phone = "018-9998888" }
        );
    }

    // Weekly working hours. The booking slot generator reads these.
    private static void SeedSchedules(DB db)
    {
        var full = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
                           DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday };

        AddShift(db, "aisyah@pawfect.com", full, new(9, 0), new(17, 0));
        AddShift(db, "kumar@pawfect.com", full, new(10, 0), new(18, 0));

        // Mei Ling does not work Tuesdays.
        AddShift(db, "meiling@pawfect.com",
                 full.Where(d => d != DayOfWeek.Tuesday).ToArray(), new(9, 30), new(17, 30));

        // Daniel works Wednesday to Sunday.
        AddShift(db, "daniel@pawfect.com",
                 [DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday,
                  DayOfWeek.Saturday, DayOfWeek.Sunday], new(11, 0), new(19, 0));

        db.StaffTimeOffs.Add(new StaffTimeOff
        {
            StaffEmail = "kumar@pawfect.com",
            StartDate = DateOnly.FromDateTime(DateTime.Today.AddDays(14)),
            EndDate = DateOnly.FromDateTime(DateTime.Today.AddDays(18)),
            Reason = "Annual leave",
        });
    }

    private static void AddShift(DB db, string email, DayOfWeek[] days, TimeOnly from, TimeOnly to)
    {
        foreach (var d in days)
        {
            db.StaffSchedules.Add(new StaffSchedule
            {
                StaffEmail = email,
                Day = d,
                StartTime = from,
                EndTime = to,
            });
        }
    }



    // ------------------------------------------------------------------------
    // Service Catalog
    // ------------------------------------------------------------------------

    private static void SeedServices(DB db)
    {
        db.ServiceCategories.AddRange(
            new ServiceCategory { Id = "C001", Name = "Bath and Brush" },
            new ServiceCategory { Id = "C002", Name = "Full Grooming" },
            new ServiceCategory { Id = "C003", Name = "Add-On" },
            new ServiceCategory { Id = "C004", Name = "Spa and Treatment" }
        );

        db.Services.AddRange(
            Svc("S001", "C001", "Basic Wash", "Shampoo, rinse and towel dry.", 45m, 30),
            Svc("S002", "C001", "Bath and Blow Dry", "Shampoo, conditioner and full blow dry.", 60m, 45),
            Svc("S003", "C001", "Deshedding Treatment", "Undercoat removal for heavy shedders.", 75m, 60),
            Svc("S004", "C002", "Full Groom (Small)", "Bath, full haircut, nails and ears for small breeds.", 90m, 60),
            Svc("S005", "C002", "Full Groom (Medium)", "Bath, full haircut, nails and ears for medium breeds.", 120m, 90),
            Svc("S006", "C002", "Full Groom (Large)", "Bath, full haircut, nails and ears for large breeds.", 160m, 120),
            Svc("S007", "C002", "Puppy First Groom", "Gentle introductory groom for puppies under 6 months.", 70m, 60),
            Svc("S008", "C003", "Nail Trimming", "Nail clipping and filing.", 20m, 30),
            Svc("S009", "C003", "Ear Cleaning", "Ear inspection and cleaning.", 25m, 30),
            Svc("S010", "C003", "Teeth Brushing", "Dental brushing with pet-safe paste.", 25m, 30),
            Svc("S011", "C004", "Flea and Tick Bath", "Medicated bath with flea and tick treatment.", 95m, 60),
            Svc("S012", "C004", "Aromatherapy Spa", "Relaxing spa bath with aromatherapy and massage.", 130m, 90)
        );
    }

    private static Service Svc(string id, string cat, string name, string desc, decimal price, int mins)
        => new()
        {
            Id = id,
            CategoryId = cat,
            Name = name,
            Description = desc,
            Price = price,
            DurationMinutes = mins,
            PhotoURL = "",
        };



    // ------------------------------------------------------------------------
    // Pets
    // ------------------------------------------------------------------------

    private static void SeedPets(DB db)
    {
        db.Pets.AddRange(
            Pet("chloe@gmail.com", "Mochi", "Dog", "Shih Tzu", new(2022, 5, 14), 6.2m, "None", "Nervous with clippers around the face."),
            Pet("chloe@gmail.com", "Latte", "Cat", "British Shorthair", new(2021, 11, 2), 4.8m, "Chicken protein", "Prefers quiet rooms."),
            Pet("arif@gmail.com", "Bruno", "Dog", "Golden Retriever", new(2020, 2, 20), 31.5m, "None", "Very heavy shedder in humid months."),
            Pet("arif@gmail.com", "Coco", "Dog", "Poodle", new(2023, 7, 9), 5.1m, "Oatmeal shampoo", "Loves the blow dryer."),
            Pet("priya@gmail.com", "Simba", "Cat", "Maine Coon", new(2019, 9, 30), 8.4m, "None", "Requires two groomers for nail trims."),
            Pet("jason@gmail.com", "Rocky", "Dog", "Siberian Husky", new(2021, 1, 17), 24.0m, "None", "Double coat, never shave."),
            Pet("jason@gmail.com", "Bella", "Dog", "Corgi", new(2022, 12, 3), 12.7m, "Fragranced products", "Sensitive skin, hypoallergenic only."),
            Pet("sarah@gmail.com", "Ollie", "Dog", "Beagle", new(2023, 3, 25), 10.9m, "None", "Food motivated, bring treats."),
            Pet("sarah@gmail.com", "Nala", "Cat", "Persian", new(2020, 6, 11), 3.9m, "None", "Matting around the hindquarters."),
            Pet("wei@gmail.com", "Tofu", "Dog", "Pomeranian", new(2024, 1, 28), 3.2m, "None", "Puppy, still getting used to grooming.")
        );
    }

    private static Pet Pet(string owner, string name, string species, string breed,
                           DateOnly born, decimal kg, string allergies, string notes)
        => new()
        {
            MemberEmail = owner,
            Name = name,
            Species = species,
            Breed = breed,
            BirthDate = born,
            WeightKg = kg,
            Allergies = allergies,
            Notes = notes,
        };



    // ------------------------------------------------------------------------
    // Appointment History
    // ------------------------------------------------------------------------
    // Six months of past appointments (so the reporting charts have something
    // real to show) plus a fortnight of upcoming ones (so the staff schedule
    // board and the member's "upcoming" list are populated on first run).

    private static void SeedHistory(DB db, Random rnd)
    {
        var staff = db.Staffs.ToList();
        var schedules = db.StaffSchedules.ToList();
        var timeOffs = db.StaffTimeOffs.ToList();
        var services = db.Services.ToList();
        var pets = db.Pets.ToList();

        // Occupied intervals per groomer. This must track whole intervals, not just
        // start times: a 120-minute Full Groom at 10:00 collides with a 30-minute
        // nail trim at 10:30 even though the start times differ.
        var taken = new Dictionary<string, List<(DateTime Start, DateTime End)>>();

        var from = DateTime.Today.AddMonths(-MonthsOfHistory);
        var to = DateTime.Today.AddDays(14);

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            // Busier on weekends.
            var isWeekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var count = rnd.Next(isWeekend ? 2 : 0, isWeekend ? 6 : 4);

            for (int i = 0; i < count; i++)
            {
                var pet = pets[rnd.Next(pets.Count)];
                var service = services[rnd.Next(services.Count)];
                var groomer = staff[rnd.Next(staff.Count)];

                var slot = FindSlot(day, groomer, service, schedules, timeOffs, taken, rnd);
                if (slot == null) continue;

                if (!taken.TryGetValue(groomer.Email, out var booked))
                {
                    booked = [];
                    taken[groomer.Email] = booked;
                }
                booked.Add((slot.Value, slot.Value.AddMinutes(service.DurationMinutes)));

                var isPast = slot.Value < DateTime.Now;
                var status = isPast ? PastStatus(rnd) : FutureStatus(rnd);

                db.Appointments.Add(BuildAppointment(
                    pet, service, groomer, slot.Value, status, rnd));
            }
        }
    }

    // Weighted so that most history is Completed, with a realistic minority of
    // cancellations and no-shows for the cancellation-rate chart.
    private static AppointmentStatus PastStatus(Random rnd)
    {
        var roll = rnd.Next(100);
        if (roll < 82) return AppointmentStatus.Completed;
        if (roll < 94) return AppointmentStatus.Cancelled;
        return AppointmentStatus.NoShow;
    }

    private static AppointmentStatus FutureStatus(Random rnd)
        => rnd.Next(100) < 75 ? AppointmentStatus.Confirmed : AppointmentStatus.Pending;

    private static DateTime? FindSlot(DateTime day, Staff groomer, Service service,
                                      List<StaffSchedule> schedules, List<StaffTimeOff> timeOffs,
                                      Dictionary<string, List<(DateTime Start, DateTime End)>> taken,
                                      Random rnd)
    {
        var shift = schedules.FirstOrDefault(s => s.StaffEmail == groomer.Email && s.Day == day.DayOfWeek);
        if (shift == null) return null;

        var date = DateOnly.FromDateTime(day);
        if (timeOffs.Any(t => t.StaffEmail == groomer.Email && date >= t.StartDate && date <= t.EndDate))
        {
            return null;
        }

        // Try a few random 30-minute boundaries inside the shift.
        for (int attempt = 0; attempt < 8; attempt++)
        {
            var slots = (int)(shift.EndTime - shift.StartTime).TotalMinutes / 30;
            var start = day.Date + shift.StartTime.ToTimeSpan() + TimeSpan.FromMinutes(30 * rnd.Next(slots));
            var end = start.AddMinutes(service.DurationMinutes);

            if (end.TimeOfDay > shift.EndTime.ToTimeSpan()) continue;

            // Reject any interval that overlaps one this groomer already has.
            if (taken.TryGetValue(groomer.Email, out var booked) &&
                booked.Any(b => start < b.End && b.Start < end))
            {
                continue;
            }

            return start;
        }

        return null;
    }

    private static Appointment BuildAppointment(Pet pet, Service service, Staff groomer,
                                                DateTime slot, AppointmentStatus status, Random rnd)
    {
        var price = service.Price;

        // Booked some time before the slot -- but never in the future, which is
        // what naively subtracting from a future slot would produce.
        var createdAt = slot.AddDays(-rnd.Next(1, 21)).AddHours(-rnd.Next(1, 10));

        if (createdAt > DateTime.Now)
        {
            createdAt = DateTime.Now.AddHours(-rnd.Next(1, 72));
        }

        var appointment = new Appointment
        {
            BookingRef = $"PG-{slot:yyyyMMdd}-{Guid.NewGuid().ToString("N")[..4].ToUpper()}",
            MemberEmail = pet.MemberEmail,
            CreatedAt = createdAt,
            Status = status,
            Subtotal = price,
            Discount = 0m,
            Total = price,
            Notes = "",
            CancelReason = "",
        };

        appointment.Items.Add(new AppointmentItem
        {
            PetId = pet.Id,
            ServiceId = service.Id,
            StaffEmail = groomer.Email,
            SlotStart = slot,
            SlotEnd = slot.AddMinutes(service.DurationMinutes),
            UnitPrice = price,
            ItemStatus = status,
        });

        AddPayment(appointment, status, price, createdAt, rnd);
        AddStatusTrail(appointment, status, createdAt, slot);

        if (status == AppointmentStatus.Completed)
        {
            AddReportAndReview(appointment, groomer, slot, rnd);
        }

        return appointment;
    }

    private static void AddPayment(Appointment a, AppointmentStatus status,
                                   decimal price, DateTime createdAt, Random rnd)
    {
        if (status == AppointmentStatus.Pending) return;

        var method = rnd.Next(100) < 65 ? PaymentMethod.Stripe : PaymentMethod.Counter;
        var amount = price;

        var payment = new Payment
        {
            Method = method,
            Status = PaymentStatus.Paid,
            Amount = amount,
            ProviderRef = method == PaymentMethod.Stripe
                        ? $"pi_seed_{Guid.NewGuid().ToString("N")[..16]}"
                        : "",
            PaidAt = createdAt.AddMinutes(rnd.Next(1, 15)),
        };

        // Cancellations refund according to the same tiers the RefundPolicyService
        // will enforce at runtime.
        if (status == AppointmentStatus.Cancelled)
        {
            var refund = Math.Round(amount * 0.5m, 2);
            payment.Status = PaymentStatus.PartiallyRefunded;
            payment.RefundedAmount = refund;
            payment.RefundedAt = createdAt.AddDays(1);
            a.RefundAmount = refund;
            a.CancelledAt = createdAt.AddDays(1);
            a.CancelReason = "Owner rescheduled";
        }

        a.Payments.Add(payment);
    }

    private static void AddStatusTrail(Appointment a, AppointmentStatus status,
                                       DateTime createdAt, DateTime slot)
    {
        void Step(AppointmentStatus f, AppointmentStatus t, DateTime at, string by, string remark)
            => a.StatusHistories.Add(new AppointmentStatusHistory
            {
                FromStatus = f,
                ToStatus = t,
                ChangedAt = at,
                ChangedByEmail = by,
                Remark = remark,
            });

        var member = a.MemberEmail;
        var groomer = a.Items[0].StaffEmail;

        if (status == AppointmentStatus.Pending) return;

        Step(AppointmentStatus.Pending, AppointmentStatus.Confirmed,
             createdAt.AddMinutes(5), member, "Payment received");

        if (status == AppointmentStatus.Confirmed) return;

        if (status == AppointmentStatus.Cancelled)
        {
            Step(AppointmentStatus.Confirmed, AppointmentStatus.Cancelled,
                 createdAt.AddDays(1), member, "Cancelled by member");
            return;
        }

        if (status == AppointmentStatus.NoShow)
        {
            Step(AppointmentStatus.Confirmed, AppointmentStatus.NoShow,
                 slot.AddMinutes(30), groomer, "Member did not arrive");
            return;
        }

        Step(AppointmentStatus.Confirmed, AppointmentStatus.CheckedIn, slot.AddMinutes(-5), groomer, "Pet checked in");
        Step(AppointmentStatus.CheckedIn, AppointmentStatus.InProgress, slot, groomer, "Grooming started");
        Step(AppointmentStatus.InProgress, AppointmentStatus.Completed,
             a.Items[0].SlotEnd, groomer, "Grooming completed");
    }

    private static void AddReportAndReview(Appointment a, Staff groomer, DateTime slot, Random rnd)
    {
        string[] coats = ["Excellent", "Good", "Slightly matted", "Dry", "Heavy shedding"];
        string[] notes =
        [
            "Coat in good condition, no issues found.",
            "Some matting behind the ears, worked out gently.",
            "Nails were long, trimmed back to a comfortable length.",
            "Mild dry skin noted, recommended a moisturising shampoo.",
            "Very well behaved throughout the session.",
        ];
        string[] comments =
        [
            "Great job, my pet came back so soft!",
            "Friendly groomer and very gentle handling.",
            "Booking was easy and the result was excellent.",
            "Happy with the trim, will book again.",
            "Good service but had to wait a little past my slot.",
        ];

        a.Items[0].Report = new GroomingReport
        {
            GroomerNotes = notes[rnd.Next(notes.Length)],
            CoatCondition = coats[rnd.Next(coats.Length)],
            BehaviourNotes = rnd.Next(100) < 70 ? "Calm and cooperative." : "A little anxious at first, settled quickly.",
            NextVisitRecommendation = $"Recommend next groom in {rnd.Next(4, 13)} weeks.",
            CreatedAt = a.Items[0].SlotEnd,
        };

        // Only some members leave a review.
        if (rnd.Next(100) < 55)
        {
            a.Items[0].Review = new Review
            {
                MemberEmail = a.MemberEmail,
                StaffEmail = groomer.Email,
                Rating = rnd.Next(100) < 80 ? rnd.Next(4, 6) : rnd.Next(2, 4),
                Comment = comments[rnd.Next(comments.Length)],
                CreatedAt = a.Items[0].SlotEnd.AddDays(rnd.Next(1, 5)),
            };
        }
    }
}
