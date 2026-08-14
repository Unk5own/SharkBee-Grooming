using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PetGrooming.Models;

namespace PetGrooming.Controllers;

public class HomeController(DB db) : Controller
{
    // GET: Home/Index
    public IActionResult Index()
    {
        ViewBag.Services = db.Services
                             .Include(s => s.Category)
                             .Where(s => s.Active)
                             .OrderBy(s => s.CategoryId).ThenBy(s => s.Price)
                             .ToList();

        // Average rating per groomer, so member reviews feed back into who a
        // customer chooses when booking.
        ViewBag.Groomers = db.Staffs
                             .Where(s => s.Active)
                             .Select(s => new GroomerRatingVM
                             {
                                 Name = s.Name,
                                 Specialization = s.Specialization,
                                 Average = s.Reviews.Any()
                                         ? Math.Round(s.Reviews.Average(r => (decimal)r.Rating), 1)
                                         : 0m,
                                 Count = s.Reviews.Count(),
                             })
                             .OrderByDescending(g => g.Average)
                             .ToList();

        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
