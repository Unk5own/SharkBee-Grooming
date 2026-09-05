using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace PetGrooming.Models;

// PIC: Student 1 (User Maintenance)
public class UserCreateVM
{
    [Required, EmailAddress, StringLength(100)]
    public string Email { get; set; } = "";

    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    [Required, StringLength(100, MinimumLength = 5), DataType(DataType.Password)]
    public string Password { get; set; } = "";

    [Required]
    public string Role { get; set; } = "Member";

    [StringLength(20), Phone]
    public string? Phone { get; set; }

    [StringLength(100)]
    public string? Specialization { get; set; }

    [DataType(DataType.Date), DisplayName("Hire Date")]
    public DateOnly? HireDate { get; set; }
}

public class UserEditVM
{
    [EmailAddress, StringLength(100)]
    public string Email { get; set; } = "";

    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    public string Role { get; set; } = "";
    public bool Blocked { get; set; }

    [StringLength(20), Phone]
    public string? Phone { get; set; }

    [StringLength(100)]
    public string? Specialization { get; set; }

    [DataType(DataType.Date), DisplayName("Hire Date")]
    public DateOnly? HireDate { get; set; }

    [StringLength(100, MinimumLength = 5), DataType(DataType.Password)]
    [DisplayName("New Password")]
    public string? NewPassword { get; set; }
}
