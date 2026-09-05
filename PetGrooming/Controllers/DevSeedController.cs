using Microsoft.AspNetCore.Mvc;

namespace PetGrooming.Controllers;

// DEVELOPMENT ONLY -- supports team-member testing; not part of end-user navigation.
//
// Checkout (Student 3) consumes the session booking cart that Booking (Student 2)
// produces. This controller fabricates that cart directly, so the checkout flow
// can be built, run and demonstrated before the booking module exists. Delete this
// file once the real booking module is integrated.
public class DevSeedController(DB db, Helper hp, IWebHostEnvironment en) : Controller
{
    // GET: DevSeed/LoginAs
    // Development shortcut for testing different seeded roles.
    public async Task<IActionResult> LoginAs(string email)
    {
        if (!en.IsDevelopment()) return NotFound();

        var user = db.Users.FirstOrDefault(u => u.Email == email);

        if (user == null)
        {
            TempData["Info"] = $"No such user: {email}";
            return RedirectToAction("Index", "Home");
        }

        // Persistent so the session survives a browser restart, which is what
        // makes scripted screenshot runs possible.
        await hp.SignInAsync(user.Email, user.Role, true);

        TempData["Info"] = $"Signed in as {user.Name} ({user.Role}).";
        return RedirectToAction("Index", "Home");
    }

    // GET: DevSeed/Logout
    public async Task<IActionResult> Logout()
    {
        if (!en.IsDevelopment()) return NotFound();

        await hp.SignOutAsync();
        hp.SetCart(null);

        TempData["Info"] = "Signed out.";
        return RedirectToAction("Index", "Home");
    }

    // GET: DevSeed/FakeCart
    public IActionResult FakeCart(string? email, int lines = 2)
    {
        if (!en.IsDevelopment()) return NotFound();

        email ??= User.Identity?.Name ?? "chloe@gmail.com";

        var pets = db.Pets.Where(p => p.MemberEmail == email && p.Active)
                          .OrderBy(p => p.Id)
                          .ToList();

        if (pets.Count == 0)
        {
            TempData["Info"] = $"No pets found for {email}.";
            return RedirectToAction("Index", "Home");
        }

        var services = db.Services.Where(s => s.Active).OrderBy(s => s.Id).ToList();
        var cart = new List<BookingCartItem>();

        for (int i = 0; i < lines; i++)
        {
            var pet = pets[i % pets.Count];
            var service = services[(i * 3) % services.Count];

            var slot = FindFreeSlot(service, cart);
            if (slot == null) continue;

            cart.Add(new BookingCartItem
            {
                PetId = pet.Id,
                ServiceId = service.Id,
                StaffEmail = slot.Value.StaffEmail,
                SlotStart = slot.Value.Start,
            });
        }

        hp.SetCart(cart);

        TempData["Info"] = $"Fake cart created for {email} with {cart.Count} line(s).";
        return RedirectToAction("Index", "Checkout");
    }

    // GET: DevSeed/SetCart
    // Places one exact line in the cart. Used to drive two sessions at the same
    // slot on purpose, so the double-booking guard can be tested.
    public IActionResult SetCart(int petId, string serviceId, string staffEmail, DateTime slotStart)
    {
        if (!en.IsDevelopment()) return NotFound();

        hp.SetCart([new BookingCartItem
        {
            PetId = petId,
            ServiceId = serviceId,
            StaffEmail = staffEmail,
            SlotStart = slotStart,
        }]);

        return Json(new { ok = true, petId, serviceId, staffEmail, slotStart });
    }

    // GET: DevSeed/ShowCart
    // Lets the cart be inspected before the Checkout views exist.
    public IActionResult ShowCart()
    {
        if (!en.IsDevelopment()) return NotFound();

        var cart = hp.GetCart();

        var detail = cart.Select(c => new
        {
            Pet = db.Pets.Where(p => p.Id == c.PetId).Select(p => p.Name).FirstOrDefault(),
            Service = db.Services.Where(s => s.Id == c.ServiceId).Select(s => s.Name).FirstOrDefault(),
            Groomer = db.Staffs.Where(s => s.Email == c.StaffEmail).Select(s => s.Name).FirstOrDefault(),
            c.SlotStart,
        });

        return Json(new { info = TempData["Info"], count = cart.Count, lines = detail });
    }

    // GET: DevSeed/ClearCart
    public IActionResult ClearCart()
    {
        if (!en.IsDevelopment()) return NotFound();

        hp.SetCart(null);
        TempData["Info"] = "Cart cleared.";
        return RedirectToAction("ShowCart");
    }

    // Minimal availability search. Student 2 owns the real implementation
    // (IAvailabilityService); this exists only so the fake cart lands on slots
    // that are genuinely free.
    private (string StaffEmail, DateTime Start)? FindFreeSlot(Service service,
                                                              List<BookingCartItem> pending)
    {
        var schedules = db.StaffSchedules.ToList();
        var timeOffs = db.StaffTimeOffs.ToList();

        for (int dayOffset = 1; dayOffset <= 21; dayOffset++)
        {
            var day = DateTime.Today.AddDays(dayOffset);
            var date = DateOnly.FromDateTime(day);

            foreach (var shift in schedules.Where(s => s.Day == day.DayOfWeek))
            {
                if (timeOffs.Any(t => t.StaffEmail == shift.StaffEmail &&
                                      date >= t.StartDate && date <= t.EndDate))
                {
                    continue;
                }

                for (var t = shift.StartTime; t < shift.EndTime; t = t.AddMinutes(30))
                {
                    var start = day.Date + t.ToTimeSpan();
                    var end = start.AddMinutes(service.DurationMinutes);

                    if (end.TimeOfDay > shift.EndTime.ToTimeSpan()) break;

                    var clash = db.AppointmentItems.Any(i =>
                        i.StaffEmail == shift.StaffEmail &&
                        i.ItemStatus != AppointmentStatus.Cancelled &&
                        i.SlotStart < end && start < i.SlotEnd);

                    if (clash) continue;

                    // Also avoid clashing with lines already in this fake cart.
                    if (pending.Any(p => p.StaffEmail == shift.StaffEmail &&
                                         p.SlotStart < end && start < p.SlotStart.AddMinutes(180)))
                    {
                        continue;
                    }

                    return (shift.StaffEmail, start);
                }
            }
        }

        return null;
    }
}
