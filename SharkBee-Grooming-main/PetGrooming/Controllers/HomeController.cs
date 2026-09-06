using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetGrooming.Models;

namespace PetGrooming.Controllers;

public class HomeController(DB db) : Controller
{
    public async Task<IActionResult> Index(string? categoryId, string? priceSort)
    {
        ViewBag.Categories = await db.ServiceCategories.ToListAsync();
        ViewBag.SelectedCategory = categoryId;
        ViewBag.SelectedPriceSort = priceSort;

        var serviceQuery = db.Services
            .Include(s => s.Category)
            .Where(s => s.Active);

        // 1. Category Filter
        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            serviceQuery = serviceQuery.Where(s => s.CategoryId == categoryId);
        }

        // 2. Price Sorting
        serviceQuery = priceSort switch
        {
            "asc" => serviceQuery.OrderBy(s => s.Price),
            "desc" => serviceQuery.OrderByDescending(s => s.Price),
            _ => serviceQuery.OrderBy(s => s.Name)
        };

        ViewBag.Services = await serviceQuery.ToListAsync();

        // 3. Groomers with Ratings (queried via AppointmentItems)
        ViewBag.Groomers = await db.Staffs
    .Where(s => s.Active)
    .Select(s => new GroomerRatingVM
    {
        Name = s.Name,
        Specialization = s.Specialization,
        Average = db.AppointmentItems
            .Where(ai => ai.StaffEmail == s.Email && ai.Review != null)
            .Select(ai => (decimal?)ai.Review!.Rating)
            .Average() ?? 0m,
        Count = db.AppointmentItems
            .Count(ai => ai.StaffEmail == s.Email && ai.Review != null)
    })
    .ToListAsync();

        return View();
    }
}