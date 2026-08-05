using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpreadingJoy.Domain.EntityModels;
using SpreadingJoy.Domain.IRepositories;
using SpreadingJoy.ViewModels;

namespace SpreadingJoy.Controllers;

// Staff-only: managing customer records isn't something a walk-in visitor
// should be able to do from the public site.
//
// The controller-wide policy is the read floor — every staff role can look at
// customer records. Anything that writes carries ManageCustomers on top, which
// excludes Associates.
[Authorize(Policy = Policies.ViewCustomers)]
public class CustomersController : Controller
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IStudioClock _clock;

    public CustomersController(ICustomerRepository customerRepository, IStudioClock clock)
    {
        _customerRepository = customerRepository;
        _clock = clock;
    }

    // GET /Customers
    public async Task<IActionResult> Index()
    {
        var customers = await _customerRepository.GetAllWithOrdersAsync();

        return View(new CustomerListViewModel
        {
            SuccessMessage = TempData["CustomerSuccess"] as string,
            ErrorMessage = TempData["CustomerError"] as string,
            Customers = customers
                .OrderByDescending(c => c.IsActive)
                .ThenBy(c => c.FullName)
                .Select(c => new CustomerRowViewModel
                {
                    Id = c.CustomerId,
                    FullName = c.FullName,
                    Email = c.Email,
                    Phone = c.Phone,
                    OrderCount = c.Orders.Count,
                    IsActive = c.IsActive
                }).ToList()
        });
    }

    // GET /Customers/Details/{id}
    public async Task<IActionResult> Details(int id)
    {
        var customer = await _customerRepository.GetWithOrdersAsync(id);
        if (customer == null)
            return NotFound();

        var today = _clock.Today;

        return View(new CustomerDetailsViewModel
        {
            Id = customer.CustomerId,
            FullName = customer.FullName,
            Email = customer.Email,
            Phone = customer.Phone,
            IsActive = customer.IsActive,
            CreatedAt = customer.CreatedAt,
            SuccessMessage = TempData["CustomerSuccess"] as string,
            Orders = customer.Orders
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => new OrderRowViewModel
                {
                    Id = o.OrderId,
                    CustomerName = customer.FullName,
                    Status = o.Status,
                    DueOn = o.DueOn,
                    GarmentCount = o.OrderLines.Sum(l => l.Quantity),
                    Total = o.OrderLines.Sum(l => l.UnitPrice * l.Quantity),
                    CreatedAt = o.CreatedAt,
                    IsOverdue = OrderStatus.IsOpen(o.Status) && o.DueOn < today
                }).ToList()
        });
    }

    // GET /Customers/Create
    [Authorize(Policy = Policies.ManageCustomers)]
    public IActionResult Create() => View(new EditCustomerViewModel());

    // POST /Customers/Create
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.ManageCustomers)]
    public async Task<IActionResult> Create(EditCustomerViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var email = Blank(model.Email);

        if (email != null)
        {
            var clash = await _customerRepository.GetAsync(c => c.Email == email);
            if (clash != null)
            {
                model.ErrorMessage = "A customer with that email already exists.";
                return View(model);
            }
        }

        await _customerRepository.AddAsync(new Customer
        {
            FullName = model.FullName.Trim(),
            Email = email,
            Phone = Blank(model.Phone),
            IsActive = true,
            CreatedAt = _clock.UtcNow
        });

        await _customerRepository.SaveChangesAsync();

        TempData["CustomerSuccess"] = $"Added {model.FullName.Trim()}.";
        return RedirectToAction(nameof(Index));
    }

    // GET /Customers/Edit/{id}
    [Authorize(Policy = Policies.ManageCustomers)]
    public async Task<IActionResult> Edit(int id)
    {
        var customer = await _customerRepository.GetAsync(c => c.CustomerId == id);
        if (customer == null)
            return NotFound();

        return View(new EditCustomerViewModel
        {
            CustomerId = customer.CustomerId,
            FullName = customer.FullName,
            Email = customer.Email,
            Phone = customer.Phone
        });
    }

    // POST /Customers/Edit
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.ManageCustomers)]
    public async Task<IActionResult> Edit(EditCustomerViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var customer = await _customerRepository.GetAsync(c => c.CustomerId == model.CustomerId);
        if (customer == null)
            return NotFound();

        var email = Blank(model.Email);

        if (email != null)
        {
            var clash = await _customerRepository.GetAsync(c => c.Email == email && c.CustomerId != model.CustomerId);
            if (clash != null)
            {
                model.ErrorMessage = "Another customer already uses that email.";
                return View(model);
            }
        }

        customer.FullName = model.FullName.Trim();
        customer.Email = email;
        customer.Phone = Blank(model.Phone);

        await _customerRepository.SaveChangesAsync();

        TempData["CustomerSuccess"] = $"Updated {customer.FullName}.";
        return RedirectToAction(nameof(Details), new { id = customer.CustomerId });
    }

    // POST /Customers/SetActive
    //
    // Archive, never delete. A customer is referenced by every order they've
    // placed, and those orders are the studio's own records.
    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize(Policy = Policies.ManageCustomers)]
    public async Task<IActionResult> SetActive(int id, bool isActive)
    {
        var customer = await _customerRepository.GetAsync(c => c.CustomerId == id);
        if (customer == null)
            return NotFound();

        customer.IsActive = isActive;
        await _customerRepository.SaveChangesAsync();

        TempData["CustomerSuccess"] = isActive
            ? $"{customer.FullName} is active again."
            : $"{customer.FullName} archived — their order history is untouched.";

        return RedirectToAction(nameof(Index));
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
