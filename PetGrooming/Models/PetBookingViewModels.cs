using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace PetGrooming.Models;

public class PetVM
{
    public int Id { get; set; }

    [Required, StringLength(50)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(20)]
    public string Species { get; set; } = string.Empty;

    [Required, StringLength(50)]
    public string Breed { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    public DateOnly BirthDate { get; set; }

    [Range(0.1, 100.0)]
    public decimal WeightKg { get; set; }

    [StringLength(500)]
    public string? Allergies { get; set; }

    [StringLength(500)]
    public string? Notes { get; set; }

    public IFormFile? Photo { get; set; }

    public string? ExistingPhotoURL { get; set; }
}

public class SelectSlotVM
{
    [Required]
    public string ServiceId { get; set; }

    [Required]
    public int PetId { get; set; }

    public string? StaffEmail { get; set; } // Null for "Any Groomer"

    [DataType(DataType.Date)]
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Today.AddDays(1));

    public DateTime? SelectedSlotStart { get; set; }
}

public class TimeSlotVM
{
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public string StaffEmail { get; set; }
    public string StaffName { get; set; }
    public bool IsAvailable { get; set; }
}