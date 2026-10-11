using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication1.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PermissionKey = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPermissions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissions_UserId_PermissionKey",
                table: "UserPermissions",
                columns: new[] { "UserId", "PermissionKey" },
                unique: true);

            // Keep everyone's current access: Sales/Purchase officers always had Stock Transfers and
            // Stock Adjustments, and any role-level "Manage Stock Transfers" grant becomes a grant
            // for each current member of that role. The role grants are then removed so that
            // unticking a user on their Edit page actually takes the menu away.
            migrationBuilder.Sql(@"
INSERT INTO UserPermissions (UserId, PermissionKey)
SELECT ur.UserId, k.PermissionKey
FROM AspNetUserRoles ur
JOIN AspNetRoles r ON r.Id = ur.RoleId
CROSS JOIN (VALUES ('StockTransfer.Manage'), ('StockAdjustment.Manage')) k(PermissionKey)
WHERE r.Name IN ('SalesOfficer', 'PurchaseOfficer')
UNION
SELECT ur.UserId, rp.PermissionKey
FROM RolePermissions rp
JOIN AspNetRoles r ON r.Name = rp.RoleName
JOIN AspNetUserRoles ur ON ur.RoleId = r.Id
WHERE rp.PermissionKey IN ('StockTransfer.Manage', 'StockAdjustment.Manage');

DELETE FROM RolePermissions WHERE PermissionKey IN ('StockTransfer.Manage', 'StockAdjustment.Manage');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserPermissions");
        }
    }
}
