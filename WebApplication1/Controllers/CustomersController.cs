using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Sales;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Controllers;

public class CustomersController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(string? search, int page = 1)
    {
        var query = db.Customers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(c => c.Name.Contains(search));
        }
        ViewData["Search"] = search;
        return View(await PagedList<Customer>.CreateAsync(query.OrderBy(c => c.Name), page));
    }

    public async Task<IActionResult> Details(int id)
    {
        var customer = await db.Customers
            .Include(c => c.SalesInvoices)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (customer is null) return NotFound();
        return View(customer);
    }

    [Authorize(Roles = Roles.SalesManagers)]
    public IActionResult Create() => View(new Customer());

    [HttpPost]
    [Authorize(Roles = Roles.SalesManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Customer model)
    {
        if (!ModelState.IsValid) return View(model);

        model.CreatedBy = User.Identity?.Name;
        db.Customers.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "Customer created.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = Roles.SalesManagers)]
    public async Task<IActionResult> Edit(int id)
    {
        var customer = await db.Customers.FindAsync(id);
        if (customer is null) return NotFound();
        return View(customer);
    }

    [HttpPost]
    [Authorize(Roles = Roles.SalesManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Customer model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);

        var customer = await db.Customers.FindAsync(id);
        if (customer is null) return NotFound();

        customer.Name = model.Name;
        customer.ContactPerson = model.ContactPerson;
        customer.Phone = model.Phone;
        customer.Email = model.Email;
        customer.Address = model.Address;
        customer.IsActive = model.IsActive;
        await db.SaveChangesAsync();
        TempData["Success"] = "Customer updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = Roles.SalesManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var customer = await db.Customers.Include(c => c.SalesInvoices).FirstOrDefaultAsync(c => c.Id == id);
        if (customer is null) return NotFound();

        if (customer.SalesInvoices.Count != 0)
        {
            TempData["Error"] = "Cannot delete a customer that has sales invoices.";
            return RedirectToAction(nameof(Index));
        }

        db.Customers.Remove(customer);
        await db.SaveChangesAsync();
        TempData["Success"] = "Customer deleted.";
        return RedirectToAction(nameof(Index));
    }
}
