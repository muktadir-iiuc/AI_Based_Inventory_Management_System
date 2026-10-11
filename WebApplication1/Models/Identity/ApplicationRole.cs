using Microsoft.AspNetCore.Identity;

namespace WebApplication1.Models.Identity;

public class ApplicationRole : IdentityRole
{
    public string? Description { get; set; }

    public ApplicationRole() { }

    public ApplicationRole(string roleName) : base(roleName) { }
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string PurchaseOfficer = "PurchaseOfficer";
    public const string SalesOfficer = "SalesOfficer";
    public const string Accountant = "Accountant";
    public const string Viewer = "Viewer";

    // Platform (SaaS operator) roles. Held only by users with no tenant; never offered when a tenant
    // admin creates users, and deliberately absent from All and every tenant-level role list above.
    public const string PlatformAdmin = "PlatformAdmin";
    public const string PlatformBilling = "PlatformBilling";
    public const string PlatformStaff = $"{PlatformAdmin},{PlatformBilling}";

    public static readonly string[] All =
    [
        Admin, Manager, PurchaseOfficer, SalesOfficer, Accountant, Viewer
    ];

    public const string AdminManagers = $"{Admin},{Manager}";
    public const string InventoryManagers = $"{Admin},{Manager},{PurchaseOfficer},{SalesOfficer}";
    public const string PurchaseManagers = $"{Admin},{Manager},{PurchaseOfficer}";
    public const string SalesManagers = $"{Admin},{Manager},{SalesOfficer}";
    public const string AccountingManagers = $"{Admin},{Manager},{Accountant}";
    // Who may see purchase/cost prices — everyone except Sales Officers and Viewers.
    public const string PurchasePriceViewers = $"{Admin},{Manager},{PurchaseOfficer},{Accountant}";
    // Customer Ledger (and customer dues/balances): everyone except a Purchase Officer.
    public const string CustomerLedgerViewers = $"{Admin},{Manager},{SalesOfficer},{Accountant},{Viewer}";
    // Supplier Ledger (and supplier dues/balances): same people who may see purchase prices —
    // never a Sales Officer or Viewer.
    public const string SupplierLedgerViewers = PurchasePriceViewers;
    // Everyone except the read-only Viewer — e.g. who may record Petty Cash entries.
    public const string AllExceptViewer = $"{Admin},{Manager},{PurchaseOfficer},{SalesOfficer},{Accountant}";
    public const string AllAuthenticated = $"{Admin},{Manager},{PurchaseOfficer},{SalesOfficer},{Accountant},{Viewer}";
}
