using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PetGrooming.Models;

namespace PetGrooming.Controllers;

public class BookingController(DB db, Helper hp) : Controller
{
    private string UserEmail => User.Identity?.Name ?? "";

    // 1. Service Catalog Display
    public async Task<IActionResult> Catalog(string? categoryId)
    {
        var categories = await db.ServiceCategories.ToListAsync();
        ViewBag.Categories = categories;
        ViewBag.SelectedCategory = categoryId;

        var query = db.Services.Include(s => s.Category).Where(s => s.Active);
        if (!string.IsNullOrEmpty(categoryId))
        {
            query = query.Where(s => s.CategoryId == categoryId);
        }

        return View(await query.ToListAsync());
    }

    // 2. Select Groomer, Date & Time Slot
    [Authorize(Roles = "Member")]
    public async Task<IActionResult> SelectSlot(string serviceId)
    {
        var service = await db.Services.FirstOrDefaultAsync(s => s.Id == serviceId && s.Active);
        if (service == null) return NotFound();

        var memberPets = await db.Pets
            .Where(p => p.MemberEmail == UserEmail && p.Active)
            .ToListAsync();

        if (memberPets.Count == 0)
        {
            TempData["Info"] = "Please add a pet before booking a service.";
            return RedirectToAction("Create", "Pet");
        }

        ViewBag.Service = service;
        ViewBag.PetList = new SelectList(memberPets, "Id", "Name");
        ViewBag.StaffList = new SelectList(await db.Staffs.Where(s => s.Active).ToListAsync(), "Email", "Name");

        var vm = new SelectSlotVM
        {
            ServiceId = serviceId,
            PetId = memberPets.First().Id,
            Date = DateOnly.FromDateTime(DateTime.Today.AddDays(1))
        };

        return View(vm);
    }

    // Dynamic Slot Generation API
    [HttpGet]
    public async Task<IActionResult> GetAvailableSlots(string serviceId, string? staffEmail, DateOnly date)
    {
        var service = await db.Services.FindAsync(serviceId);
        if (service == null) return BadRequest();

        var dayOfWeek = date.DayOfWeek;
        var slots = new List<TimeSlotVM>();

        // Fetch staff schedules for specified groomer or all active groomers
        var staffQuery = db.Staffs.Include(s => s.Schedules).Include(s => s.TimeOffs).Where(s => s.Active);
        if (!string.IsNullOrEmpty(staffEmail))
        {
            staffQuery = staffQuery.Where(s => s.Email == staffEmail);
        }

        var availableStaff = await staffQuery.ToListAsync();

        foreach (var staff in availableStaff)
        {
            // Check leave status
            bool onLeave = staff.TimeOffs.Any(t => date >= t.StartDate && date <= t.EndDate);
            if (onLeave) continue;

            // Check shift schedule for the day
            var schedule = staff.Schedules.FirstOrDefault(s => s.Day == dayOfWeek);
            if (schedule == null) continue;

            // Existing bookings for staff on that day
            var startOfDay = date.ToDateTime(TimeOnly.MinValue);
            var endOfDay = date.ToDateTime(TimeOnly.MaxValue);

            var existingItems = await db.AppointmentItems
                .Where(ai => ai.StaffEmail == staff.Email &&
                             ai.SlotStart >= startOfDay && ai.SlotStart <= endOfDay &&
                             ai.ItemStatus != AppointmentStatus.Cancelled)
                .ToListAsync();

            // Generate slots at 30-minute intervals
            var currentSlot = date.ToDateTime(schedule.StartTime);
            var shiftEnd = date.ToDateTime(schedule.EndTime);

            while (currentSlot.AddMinutes(service.DurationMinutes) <= shiftEnd)
            {
                var slotEnd = currentSlot.AddMinutes(service.DurationMinutes);

                // Collision detection with existing appointments
                bool isOccupied = existingItems.Any(ai => currentSlot < ai.SlotEnd && slotEnd > ai.SlotStart);

                slots.Add(new TimeSlotVM
                {
                    StartTime = currentSlot,
                    EndTime = slotEnd,
                    StaffEmail = staff.Email,
                    StaffName = staff.Name,
                    IsAvailable = !isOccupied
                });

                currentSlot = currentSlot.AddMinutes(30); // 30-min slot increments
            }
        }

        return Json(slots.OrderBy(s => s.StartTime));
    }

    // 3. Cart Actions
    [HttpPost]
    [Authorize(Roles = "Member")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddToCart(BookingCartItem item)
    {
        // Nothing posted here is trusted. A line whose pet, service or groomer
        // fails these checks would be dropped again by the cart, leaving the
        // member with a success message and an empty cart.
        bool ownsPet = await db.Pets.AnyAsync(p =>
            p.Id == item.PetId && p.MemberEmail == UserEmail && p.Active);
        bool validService = await db.Services.AnyAsync(s => s.Id == item.ServiceId && s.Active);
        bool validStaff = await db.Staffs.AnyAsync(s => s.Email == item.StaffEmail && s.Active);

        if (!ownsPet || !validService || !validStaff || item.SlotStart <= DateTime.Now)
        {
            TempData["Info"] = "That slot could not be added. Please pick a pet, groomer and time again.";
            return RedirectToAction("SelectSlot", new { serviceId = item.ServiceId });
        }

        var cart = hp.GetCart();

        // Avoid duplicate slot selection for the same pet
        cart.RemoveAll(c => c.PetId == item.PetId && c.SlotStart == item.SlotStart);
        cart.Add(item);

        hp.SetCart(cart);
        TempData["Info"] = "Service added to your booking cart!";

        return RedirectToAction("Cart");
    }

    [Authorize(Roles = "Member")]
    public async Task<IActionResult> Cart()
    {
        var cart = hp.GetCart();
        var lines = new List<CheckoutLineVM>();

        foreach (var item in cart)
        {
            // Same rules Checkout applies, so the cart never green-lights a line
            // that checkout would then refuse.
            var pet = await db.Pets.FirstOrDefaultAsync(p =>
                p.Id == item.PetId && p.MemberEmail == UserEmail);
            var service = await db.Services.FirstOrDefaultAsync(s => s.Id == item.ServiceId && s.Active);
            var staff = await db.Staffs.FirstOrDefaultAsync(s => s.Email == item.StaffEmail && s.Active);

            if (pet == null || service == null || staff == null) continue;

            var slotEnd = item.SlotStart.AddMinutes(service.DurationMinutes);

            // Check if slot was booked by someone else in the meantime
            bool taken = await db.AppointmentItems.AnyAsync(ai =>
                ai.StaffEmail == item.StaffEmail &&
                ai.SlotStart < slotEnd &&
                ai.SlotEnd > item.SlotStart &&
                ai.ItemStatus != AppointmentStatus.Cancelled);

            lines.Add(new CheckoutLineVM
            {
                Item = item,
                Pet = pet,
                Service = service,
                Staff = staff,
                SlotEnd = slotEnd,
                Subtotal = service.Price,
                Unavailable = taken || item.SlotStart <= DateTime.Now
            });
        }

        return View(lines);
    }

    [HttpPost]
    [Authorize(Roles = "Member")]
    [ValidateAntiForgeryToken]
    public IActionResult RemoveFromCart(int petId, DateTime slotStart)
    {
        var cart = hp.GetCart();
        cart.RemoveAll(c => c.PetId == petId && c.SlotStart == slotStart);
        hp.SetCart(cart);

        return RedirectToAction("Cart");
    }

}