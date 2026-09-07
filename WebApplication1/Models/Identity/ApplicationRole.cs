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

    public static readonly string[] All =
    [
        Admin, Manager, PurchaseOfficer, SalesOfficer, Accountant, Viewer
    ];

    public const string InventoryManagers = $"{Admin},{Manager},{PurchaseOfficer},{SalesOfficer}";
    public const string PurchaseManagers = $"{Admin},{Manager},{PurchaseOfficer}";
    public const string SalesManagers = $"{Admin},{Manager},{SalesOfficer}";
    public const string AccountingManagers = $"{Admin},{Manager},{Accountant}";
    public const string AllAuthenticated = $"{Admin},{Manager},{PurchaseOfficer},{SalesOfficer},{Accountant},{Viewer}";
}
