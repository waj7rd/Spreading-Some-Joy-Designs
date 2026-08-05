using System.ComponentModel.DataAnnotations;

namespace SpreadingJoy.ViewModels;

public class StaffRowViewModel
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsLockedOut { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class StaffListViewModel
{
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public IList<StaffRowViewModel> Staff { get; set; } = [];
}

public class CreateStaffViewModel
{
    [Required(ErrorMessage = "Enter their name.")]
    [StringLength(100)]
    [Display(Name = "Name")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter their email.")]
    [EmailAddress(ErrorMessage = "That doesn't look like an email address.")]
    [StringLength(255)]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Pick a role.")]
    [Display(Name = "Role")]
    public string Role { get; set; } = Domain.Identity.Roles.Associate;

    [Required(ErrorMessage = "Set a password.")]
    [StringLength(128, MinimumLength = 10, ErrorMessage = "Passwords need to be at least 10 characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }
}

public class EditStaffViewModel
{
    public int UserId { get; set; }

    [Required(ErrorMessage = "Enter their name.")]
    [StringLength(100)]
    [Display(Name = "Name")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Enter their email.")]
    [EmailAddress(ErrorMessage = "That doesn't look like an email address.")]
    [StringLength(255)]
    [Display(Name = "Email")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Pick a role.")]
    [Display(Name = "Role")]
    public string Role { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }
}

public class ResetPasswordViewModel
{
    public int UserId { get; set; }

    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Set a password.")]
    [StringLength(128, MinimumLength = 10, ErrorMessage = "Passwords need to be at least 10 characters.")]
    [DataType(DataType.Password)]
    [Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }
}

public class LoginActivityRowViewModel
{
    public DateTime OccurredAt { get; set; }
    public string Event { get; set; } = string.Empty;
    public string EmailAttempted { get; set; } = string.Empty;
    public string? IpAddress { get; set; }
    public string? UserName { get; set; }
}
