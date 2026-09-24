using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Extensions;
using WebApplication1.Models.Identity;
using WebApplication1.Models.PettyCash;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Controllers;

// Every signed-in user can open Petty Cash (the app-wide fallback policy already requires
// sign-in); only non-Viewer roles can change it. Entries are kept per warehouse and scoped the
// same way as the rest of the app: a warehouse-restricted user only sees and records entries for
// their assigned warehouse(s). The Names list is shared across all warehouses.
public class PettyCashController(ApplicationDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewData["Warehouses"] = await AllowedWarehousesQuery()
            .OrderBy(w => w.Name)
            .Select(w => new PettyCashWarehouseOption(w.Id, w.Name))
            .ToListAsync();
        ViewData["CanEdit"] = CanEdit();
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> Names()
    {
        var names = await db.PettyCashNames
            .OrderBy(n => n.Name)
            .Select(n => new { id = n.Id, name = n.Name })
            .ToListAsync();
        return Json(names);
    }

    [HttpPost]
    [Authorize(Roles = Roles.AllExceptViewer)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddName([FromBody] PettyCashNameRequest? request)
    {
        var name = request?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return Json(new { ok = false, error = "Enter a name." });
        }

        var existing = await db.PettyCashNames.FirstOrDefaultAsync(n => n.Name == name);
        if (existing is not null)
        {
            return Json(new { ok = true, id = existing.Id, name = existing.Name });
        }

        var entity = new PettyCashName { Name = name, CreatedBy = User.Identity?.Name };
        db.PettyCashNames.Add(entity);
        await db.SaveChangesAsync();
        return Json(new { ok = true, id = entity.Id, name = entity.Name });
    }

    // Names are shared, so a name is only deletable once no warehouse uses it — the check counts
    // entries in every warehouse, not just the caller's.
    [HttpPost]
    [Authorize(Roles = Roles.AllExceptViewer)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteName(int id)
    {
        var entity = await db.PettyCashNames.FirstOrDefaultAsync(n => n.Id == id);
        if (entity is null)
        {
            return Json(new { ok = false, error = "Name not found." });
        }
        if (await db.PettyCashEntries.AnyAsync(e => e.PettyCashNameId == id))
        {
            return Json(new { ok = false, error = "Cannot delete a name that already has petty cash entries (in any warehouse)." });
        }

        db.PettyCashNames.Remove(entity);
        await db.SaveChangesAsync();
        return Json(new { ok = true });
    }

    // Balance is a running cash-in-hand figure computed across the FULL chronological history
    // (oldest first) of the selected warehouse — or of all the caller's warehouses combined when
    // none is picked — so it stays meaningful regardless of which rows a filter then hides. The
    // date/name/type filters only decide which already-computed rows are returned; they never
    // recompute balance from a subset. The warehouse choice is different: it picks WHICH cash
    // book is being read, so it scopes the history the balance runs over. Income/Expense/
    // TotalBalance are sums over just the filtered rows, since those headline figures answer
    // "how did cash move in this filter".
    [HttpGet]
    public async Task<IActionResult> Entries(int? warehouseId, DateTime? from, DateTime? to, int? nameId, int? type)
    {
        var query = db.PettyCashEntries.AsQueryable();
        var allowedIds = User.GetWarehouseIds();
        if (allowedIds is not null)
        {
            query = query.Where(e => allowedIds.Contains(e.WarehouseId));
        }
        if (warehouseId is > 0)
        {
            if (!User.IsWarehouseAllowed(warehouseId.Value))
            {
                return Json(new { rows = Array.Empty<object>(), totalIncome = 0m, totalExpense = 0m, totalBalance = 0m });
            }
            query = query.Where(e => e.WarehouseId == warehouseId.Value);
        }

        var all = await query
            .Include(e => e.PettyCashName)
            .Include(e => e.Warehouse)
            .OrderBy(e => e.Date).ThenBy(e => e.Id)
            .ToListAsync();

        var running = 0m;
        var ledger = new List<(PettyCashEntry Entry, decimal Balance)>(all.Count);
        foreach (var entry in all)
        {
            running += entry.Type == PettyCashType.CashIn ? entry.Amount : -entry.Amount;
            ledger.Add((entry, running));
        }

        IEnumerable<(PettyCashEntry Entry, decimal Balance)> filtered = ledger;
        if (from.HasValue) filtered = filtered.Where(l => l.Entry.Date.Date >= from.Value.Date);
        if (to.HasValue) filtered = filtered.Where(l => l.Entry.Date.Date <= to.Value.Date);
        if (nameId is > 0) filtered = filtered.Where(l => l.Entry.PettyCashNameId == nameId.Value);
        if (type is 0 or 1) filtered = filtered.Where(l => (int)l.Entry.Type == type.Value);

        var filteredList = filtered
            .OrderByDescending(l => l.Entry.Date).ThenByDescending(l => l.Entry.Id)
            .ToList();

        var rows = filteredList.Select((l, index) => new
        {
            sl = index + 1,
            id = l.Entry.Id,
            date = l.Entry.Date.ToString("yyyy-MM-dd"),
            warehouseId = l.Entry.WarehouseId,
            warehouse = l.Entry.Warehouse!.Name,
            nameId = l.Entry.PettyCashNameId,
            name = l.Entry.PettyCashName!.Name,
            type = (int)l.Entry.Type,
            income = l.Entry.Type == PettyCashType.CashIn ? l.Entry.Amount : (decimal?)null,
            expense = l.Entry.Type == PettyCashType.CashOut ? l.Entry.Amount : (decimal?)null,
            balance = l.Balance
        });

        var totalIncome = filteredList.Where(l => l.Entry.Type == PettyCashType.CashIn).Sum(l => l.Entry.Amount);
        var totalExpense = filteredList.Where(l => l.Entry.Type == PettyCashType.CashOut).Sum(l => l.Entry.Amount);

        return Json(new
        {
            rows,
            totalIncome,
            totalExpense,
            totalBalance = totalIncome - totalExpense
        });
    }

    [HttpPost]
    [Authorize(Roles = Roles.AllExceptViewer)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AddEntry([FromBody] PettyCashEntryRequest? request)
    {
        var error = await ValidateEntryRequestAsync(request);
        if (error is not null)
        {
            return Json(new { ok = false, error });
        }

        var entity = new PettyCashEntry
        {
            Date = request!.Date.Date,
            WarehouseId = request.WarehouseId,
            PettyCashNameId = request.NameId,
            Type = (PettyCashType)request.Type,
            Amount = request.Amount,
            CreatedBy = User.Identity?.Name
        };
        db.PettyCashEntries.Add(entity);
        await db.SaveChangesAsync();
        return Json(new { ok = true, id = entity.Id });
    }

    [HttpPost]
    [Authorize(Roles = Roles.AllExceptViewer)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateEntry(int id, [FromBody] PettyCashEntryRequest? request)
    {
        var error = await ValidateEntryRequestAsync(request);
        if (error is not null)
        {
            return Json(new { ok = false, error });
        }

        var entity = await FindAllowedEntryAsync(id);
        if (entity is null)
        {
            return Json(new { ok = false, error = "Entry not found." });
        }

        entity.Date = request!.Date.Date;
        entity.WarehouseId = request.WarehouseId;
        entity.PettyCashNameId = request.NameId;
        entity.Type = (PettyCashType)request.Type;
        entity.Amount = request.Amount;
        await db.SaveChangesAsync();
        return Json(new { ok = true });
    }

    [HttpPost]
    [Authorize(Roles = Roles.AllExceptViewer)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteEntry(int id)
    {
        var entity = await FindAllowedEntryAsync(id);
        if (entity is null)
        {
            return Json(new { ok = false, error = "Entry not found." });
        }

        db.PettyCashEntries.Remove(entity);
        await db.SaveChangesAsync();
        return Json(new { ok = true });
    }

    private bool CanEdit() =>
        Roles.AllExceptViewer.Split(',').Any(User.IsInRole);

    private IQueryable<Models.Inventory.Warehouse> AllowedWarehousesQuery()
    {
        var allowedIds = User.GetWarehouseIds();
        var query = db.Warehouses.Where(w => w.IsActive);
        return allowedIds is null ? query : query.Where(w => allowedIds.Contains(w.Id));
    }

    // An entry in a warehouse the caller isn't assigned to is treated as not found, so its
    // existence doesn't leak either.
    private async Task<PettyCashEntry?> FindAllowedEntryAsync(int id)
    {
        var entity = await db.PettyCashEntries.FindAsync(id);
        return entity is not null && User.IsWarehouseAllowed(entity.WarehouseId) ? entity : null;
    }

    private async Task<string?> ValidateEntryRequestAsync(PettyCashEntryRequest? request)
    {
        if (request is null) return "Invalid request.";
        if (request.Date == default) return "Select a date.";
        if (request.WarehouseId <= 0 || !User.IsWarehouseAllowed(request.WarehouseId)
            || !await AllowedWarehousesQuery().AnyAsync(w => w.Id == request.WarehouseId))
        {
            return "Select a warehouse you're assigned to.";
        }
        if (request.Type != 0 && request.Type != 1) return "Select a valid type.";
        if (request.Amount <= 0) return "Enter an amount greater than zero.";
        if (!await db.PettyCashNames.AnyAsync(n => n.Id == request.NameId)) return "Select a valid name.";
        return null;
    }
}
