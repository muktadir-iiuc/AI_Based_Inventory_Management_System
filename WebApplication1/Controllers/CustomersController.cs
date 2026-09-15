using System.ComponentModel.DataAnnotations;
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
    public async Task<IActionResult> Index(string? search)
    {
        var query = db.Customers
            .Include(c => c.SalesInvoices).ThenInclude(s => s.Items)
            .Include(c => c.SalesInvoices).ThenInclude(s => s.Payments)
            .AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(c => c.Name.Contains(search));
        }
        ViewData["Search"] = search;
        return View(await query.OrderBy(c => c.Name).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var customer = await db.Customers
            .Include(c => c.SalesInvoices).ThenInclude(s => s.Items)
            .Include(c => c.SalesInvoices).ThenInclude(s => s.Payments)
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

    // Lets the Sales Invoice Create page add a missing customer inline, without losing
    // whatever line items the user has already entered by navigating away to the full
    // Customers/Create page. Same role gate as that page (Create/Edit/Delete above).
    [HttpPost]
    [Authorize(Roles = Roles.SalesManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickCreate([FromBody] CustomerQuickCreateRequest? request)
    {
        if (request is null || !ModelState.IsValid)
        {
            var error = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault();
            return Json(new { ok = false, error = string.IsNullOrWhiteSpace(error) ? "Invalid customer details." : error });
        }

        var name = request.Name.Trim();
        if (await db.Customers.AnyAsync(c => c.Name == name))
        {
            return Json(new { ok = false, error = "A customer with this name already exists." });
        }

        if (!string.IsNullOrWhiteSpace(request.Email) && !new EmailAddressAttribute().IsValid(request.Email))
        {
            return Json(new { ok = false, error = "Enter a valid email address, or leave it blank." });
        }

        var customer = new Customer
        {
            Name = name,
            ContactPerson = string.IsNullOrWhiteSpace(request.ContactPerson) ? null : request.ContactPerson.Trim(),
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            CreatedBy = User.Identity?.Name
        };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        return Json(new { ok = true, id = customer.Id, name = customer.Name });
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
