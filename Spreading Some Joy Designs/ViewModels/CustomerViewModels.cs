using System.ComponentModel.DataAnnotations;
using SpreadingJoy.ViewModels.Validation;

namespace SpreadingJoy.ViewModels;

public class CustomerRowViewModel
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public int OrderCount { get; set; }
    public bool IsActive { get; set; }
}

public class CustomerListViewModel
{
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
    public IList<CustomerRowViewModel> Customers { get; set; } = [];
}

public class CustomerDetailsViewModel
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public IList<OrderRowViewModel> Orders { get; set; } = [];

    public string? SuccessMessage { get; set; }
}

public class EditCustomerViewModel
{
    public int CustomerId { get; set; }

    [Required(ErrorMessage = "Enter the customer's name.")]
    [StringLength(100)]
    [Display(Name = "Name")]
    public string FullName { get; set; } = string.Empty;

    [EmailAddress(ErrorMessage = "That doesn't look like an email address.")]
    [StringLength(255)]
    [Display(Name = "Email")]
    public string? Email { get; set; }

    [RegularExpression(ValidationPatterns.Phone, ErrorMessage = ValidationPatterns.PhoneMessage)]
    [StringLength(30)]
    [Display(Name = "Phone")]
    public string? Phone { get; set; }

    public string? ErrorMessage { get; set; }
}
