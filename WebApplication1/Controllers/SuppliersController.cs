using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Identity;
using WebApplication1.Models.Purchase;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Controllers;

public class SuppliersController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index(string? search)
    {
        var query = db.Suppliers.AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(s => s.Name.Contains(search));
        }
        ViewData["Search"] = search;
        return View(await query.OrderBy(s => s.Name).ToListAsync());
    }

    public async Task<IActionResult> Details(int id)
    {
        var supplier = await db.Suppliers
            .Include(s => s.PurchaseInvoices)
            .FirstOrDefaultAsync(s => s.Id == id);
        if (supplier is null) return NotFound();
        return View(supplier);
    }

    [Authorize(Roles = Roles.PurchaseManagers)]
    public IActionResult Create() => View(new Supplier());

    [HttpPost]
    [Authorize(Roles = Roles.PurchaseManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Supplier model)
    {
        if (!ModelState.IsValid) return View(model);

        model.CreatedBy = User.Identity?.Name;
        db.Suppliers.Add(model);
        await db.SaveChangesAsync();
        TempData["Success"] = "Supplier created.";
        return RedirectToAction(nameof(Index));
    }

    // Lets the Purchase Invoice Create page add a missing supplier inline, without losing
    // whatever line items the user has already entered by navigating away to the full
    // Suppliers/Create page. Same role gate as that page (Create/Edit/Delete above).
    [HttpPost]
    [Authorize(Roles = Roles.PurchaseManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> QuickCreate([FromBody] SupplierQuickCreateRequest? request)
    {
        if (request is null || !ModelState.IsValid)
        {
            var error = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault();
            return Json(new { ok = false, error = string.IsNullOrWhiteSpace(error) ? "Invalid supplier details." : error });
        }

        var name = request.Name.Trim();
        if (await db.Suppliers.AnyAsync(s => s.Name == name))
        {
            return Json(new { ok = false, error = "A supplier with this name already exists." });
        }

        if (!string.IsNullOrWhiteSpace(request.Email) && !new EmailAddressAttribute().IsValid(request.Email))
        {
            return Json(new { ok = false, error = "Enter a valid email address, or leave it blank." });
        }

        var supplier = new Supplier
        {
            Name = name,
            ContactPerson = string.IsNullOrWhiteSpace(request.ContactPerson) ? null : request.ContactPerson.Trim(),
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim(),
            Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim(),
            CreatedBy = User.Identity?.Name
        };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();

        return Json(new { ok = true, id = supplier.Id, name = supplier.Name });
    }

    [Authorize(Roles = Roles.PurchaseManagers)]
    public async Task<IActionResult> Edit(int id)
    {
        var supplier = await db.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound();
        return View(supplier);
    }

    [HttpPost]
    [Authorize(Roles = Roles.PurchaseManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, Supplier model)
    {
        if (id != model.Id) return NotFound();
        if (!ModelState.IsValid) return View(model);

        var supplier = await db.Suppliers.FindAsync(id);
        if (supplier is null) return NotFound();

        supplier.Name = model.Name;
        supplier.ContactPerson = model.ContactPerson;
        supplier.Phone = model.Phone;
        supplier.Email = model.Email;
        supplier.Address = model.Address;
        supplier.IsActive = model.IsActive;
        await db.SaveChangesAsync();
        TempData["Success"] = "Supplier updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [Authorize(Roles = Roles.PurchaseManagers)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var supplier = await db.Suppliers.Include(s => s.PurchaseInvoices).FirstOrDefaultAsync(s => s.Id == id);
        if (supplier is null) return NotFound();

        if (supplier.PurchaseInvoices.Count != 0)
        {
            TempData["Error"] = "Cannot delete a supplier that has purchase invoices.";
            return RedirectToAction(nameof(Index));
        }

        db.Suppliers.Remove(supplier);
        await db.SaveChangesAsync();
        TempData["Success"] = "Supplier deleted.";
        return RedirectToAction(nameof(Index));
    }
}
