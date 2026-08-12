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
