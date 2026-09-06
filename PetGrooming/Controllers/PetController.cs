using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PetGrooming.Models;

namespace PetGrooming.Controllers;

[Authorize(Roles = "Member")]
public class PetController(DB db, IWebHostEnvironment env) : Controller
{
    private string UserEmail => User.Identity!.Name!;

    public async Task<IActionResult> Index()
    {
        var pets = await db.Pets
            .Include(p => p.Photos)
            .Where(p => p.MemberEmail == UserEmail && p.Active)
            .ToListAsync();

        return View(pets);
    }

    public IActionResult Create() => View(new PetVM());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(PetVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var pet = new Pet
        {
            Name = vm.Name,
            Species = vm.Species,
            Breed = vm.Breed,
            BirthDate = vm.BirthDate,
            WeightKg = vm.WeightKg,
            Allergies = vm.Allergies ?? "None",
            Notes = vm.Notes ?? "", // Fixes NOT NULL constraint error
            MemberEmail = UserEmail,
            Active = true
        };

        if (vm.Photos != null && vm.Photos.Any())
        {
            bool isFirstPhoto = true;
            foreach (var file in vm.Photos)
            {
                if (file.Length > 0)
                {
                    // Fixes the vm.Photo error by referencing the individual 'file' in the loop
                    var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";

                    // Updated path to match your Edit method (Shared/image)
                    var filePath = Path.Combine(env.WebRootPath, "Shared", "image", fileName);

                    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    // Sets only the very first uploaded photo as Primary = true
                    pet.Photos.Add(new PetPhoto { PhotoURL = fileName, IsPrimary = isFirstPhoto, SortOrder = 1 });
                    isFirstPhoto = false;
                }
            }
        }
        try
        {
            db.Pets.Add(pet);
            await db.SaveChangesAsync();

            TempData["Info"] = $"{pet.Name} has been added successfully!";
            return RedirectToAction(nameof(Index));
        }
        catch (DbUpdateException ex)
        {
            // Captures the underlying SQL constraint or foreign key error
            var sqlError = ex.InnerException?.Message ?? ex.Message;
            ModelState.AddModelError("", $"Database Error: {sqlError}");
            return View(vm);
        }
    }

    public async Task<IActionResult> Edit(int id)
    {
        var pet = await db.Pets
            .Include(p => p.Photos)
            .FirstOrDefaultAsync(p => p.Id == id && p.MemberEmail == UserEmail && p.Active);

        if (pet == null) return NotFound();

        var primaryPhoto = pet.Photos.FirstOrDefault(p => p.IsPrimary)?.PhotoURL
                        ?? pet.Photos.FirstOrDefault()?.PhotoURL;

        var vm = new PetVM
        {
            Id = pet.Id,
            Name = pet.Name,
            Species = pet.Species,
            Breed = pet.Breed,
            BirthDate = pet.BirthDate,
            WeightKg = pet.WeightKg,
            Allergies = pet.Allergies,
            Notes = pet.Notes,
            // Provide the full list of photos for the grid
            ExistingPhotos = pet.Photos.ToList(),
            // Set the currently selected primary photo
            PrimaryPhotoURL = pet.Photos.FirstOrDefault(p => p.IsPrimary)?.PhotoURL
        };

        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(PetVM vm)
    {
        if (!ModelState.IsValid) return View(vm);

        var pet = await db.Pets.Include(p => p.Photos)
        .FirstOrDefaultAsync(p => p.Id == vm.Id && p.MemberEmail == UserEmail && p.Active);
        if (pet == null) return NotFound();

       
        pet.Name = vm.Name;
        pet.Species = vm.Species;
        pet.Breed = vm.Breed;
        pet.BirthDate = vm.BirthDate;
        pet.WeightKg = vm.WeightKg;
        pet.Allergies = vm.Allergies ?? "None";
        pet.Notes = vm.Notes ?? "";
        // 1. Process new file uploads
        if (vm.Photos != null && vm.Photos.Any())
        {
            foreach (var file in vm.Photos)
            {
                if (file.Length > 0)
                {
                    var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
                    var filePath = Path.Combine(env.WebRootPath, "Shared", "image", fileName);

                    // ---> ADD IT EXACTLY HERE <---
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await file.CopyToAsync(stream);
                    }

                    // Add new photos as non-primary initially
                    pet.Photos.Add(new PetPhoto { PhotoURL = fileName, IsPrimary = false, SortOrder = 1 });
                }
            }
        }

        // 2. Apply the primary photo selection to the entire collection
        if (!string.IsNullOrEmpty(vm.PrimaryPhotoURL))
        {
            foreach (var photo in pet.Photos)
            {
                photo.IsPrimary = (photo.PhotoURL == vm.PrimaryPhotoURL);
            }
        }

        await db.SaveChangesAsync();
        TempData["Info"] = $"{pet.Name}'s details have been updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var pet = await db.Pets.FirstOrDefaultAsync(p => p.Id == id && p.MemberEmail == UserEmail);
        if (pet == null) return NotFound();

        // Soft delete to protect appointment records
        pet.Active = false;
        await db.SaveChangesAsync();

        TempData["Info"] = $"{pet.Name} has been removed.";
        return RedirectToAction(nameof(Index));
    }
}