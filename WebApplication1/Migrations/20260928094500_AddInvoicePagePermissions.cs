using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using WebApplication1.Data;

#nullable disable

namespace WebApplication1.Migrations
{
    /// <summary>
    /// Data only (no schema change): Purchase Invoices and Sales Invoices became assignable pages.
    /// Everyone keeps the access they had — Purchase Officers and Accountants get Purchase Invoices
    /// (Sales Officers/Viewers never could open them), and Sales Officers, Accountants and Viewers
    /// get Sales Invoices. Admin/Manager need no rows; they always see every page.
    /// </summary>
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260928094500_AddInvoicePagePermissions")]
    public partial class AddInvoicePagePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
INSERT INTO UserPermissions (UserId, PermissionKey)
SELECT DISTINCT ur.UserId, k.PermissionKey
FROM AspNetUserRoles ur
JOIN AspNetRoles r ON r.Id = ur.RoleId
JOIN (VALUES ('PurchaseOfficer', 'PurchaseInvoice.View'),
             ('Accountant',      'PurchaseInvoice.View'),
             ('SalesOfficer',    'SalesInvoice.View'),
             ('Accountant',      'SalesInvoice.View'),
             ('Viewer',          'SalesInvoice.View')) k(RoleName, PermissionKey) ON k.RoleName = r.Name
WHERE NOT EXISTS (SELECT 1 FROM UserPermissions p WHERE p.UserId = ur.UserId AND p.PermissionKey = k.PermissionKey);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM UserPermissions WHERE PermissionKey IN ('PurchaseInvoice.View', 'SalesInvoice.View');");
        }
    }
}
