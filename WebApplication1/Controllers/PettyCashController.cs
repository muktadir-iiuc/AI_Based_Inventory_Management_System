using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WebApplication1.Data;
using WebApplication1.Models.Identity;
using WebApplication1.Models.PettyCash;
using WebApplication1.Models.ViewModels;

namespace WebApplication1.Controllers;

[Authorize(Roles = Roles.AdminManagers)]
public class PettyCashController(ApplicationDbContext db) : Controller
{
    public IActionResult Index()
    {
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

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteName(int id)
    {
        var entity = await db.PettyCashNames.Include(n => n.Entries).FirstOrDefaultAsync(n => n.Id == id);
        if (entity is null)
        {
            return Json(new { ok = false, error = "Name not found." });
        }
        if (entity.Entries.Count != 0)
        {
            return Json(new { ok = false, error = "Cannot delete a name that already has petty cash entries." });
        }

        db.PettyCashNames.Remove(entity);
        await db.SaveChangesAsync();
        return Json(new { ok = true });
    }

    // Balance is a running cash-in-hand figure computed across the FULL chronological history
    // (oldest first) so it stays meaningful regardless of which rows a filter then hides — the
    // filter only decides which already-computed rows are returned, it never recomputes balance
    // from a subset. Income/Expense/TotalBalance are the opposite: sums over just the filtered
    // rows, since those headline figures are meant to answer "how did cash move in this filter".
    [HttpGet]
    public async Task<IActionResult> Entries(DateTime? from, DateTime? to, int? nameId, int? type)
    {
        var all = await db.PettyCashEntries
            .Include(e => e.PettyCashName)
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
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateEntry(int id, [FromBody] PettyCashEntryRequest? request)
    {
        var error = await ValidateEntryRequestAsync(request);
        if (error is not null)
        {
            return Json(new { ok = false, error });
        }

        var entity = await db.PettyCashEntries.FindAsync(id);
        if (entity is null)
        {
            return Json(new { ok = false, error = "Entry not found." });
        }

        entity.Date = request!.Date.Date;
        entity.PettyCashNameId = request.NameId;
        entity.Type = (PettyCashType)request.Type;
        entity.Amount = request.Amount;
        await db.SaveChangesAsync();
        return Json(new { ok = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteEntry(int id)
    {
        var entity = await db.PettyCashEntries.FindAsync(id);
        if (entity is null)
        {
            return Json(new { ok = false, error = "Entry not found." });
        }

        db.PettyCashEntries.Remove(entity);
        await db.SaveChangesAsync();
        return Json(new { ok = true });
    }

    private async Task<string?> ValidateEntryRequestAsync(PettyCashEntryRequest? request)
    {
        if (request is null) return "Invalid request.";
        if (request.Date == default) return "Select a date.";
        if (request.Type != 0 && request.Type != 1) return "Select a valid type.";
        if (request.Amount <= 0) return "Enter an amount greater than zero.";
        if (!await db.PettyCashNames.AnyAsync(n => n.Id == request.NameId)) return "Select a valid name.";
        return null;
    }
}
