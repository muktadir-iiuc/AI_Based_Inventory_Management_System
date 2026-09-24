using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication1.Migrations
{
    /// <inheritdoc />
    public partial class AddPettyCashWarehouse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "PettyCashEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // Hand-added: entries recorded before petty cash was per-warehouse have no warehouse,
            // and the 0 default above would break the foreign key below. They were all entered
            // by Admin/Manager (the only roles with access back then), so they're assigned to the
            // first warehouse (lowest Id — "Main Warehouse" in the seeded data). A no-op when
            // there are no entries.
            migrationBuilder.Sql(@"
UPDATE PettyCashEntries
SET WarehouseId = (SELECT MIN(Id) FROM Warehouses)
WHERE WarehouseId = 0;");

            migrationBuilder.CreateIndex(
                name: "IX_PettyCashEntries_WarehouseId",
                table: "PettyCashEntries",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_PettyCashEntries_Warehouses_WarehouseId",
                table: "PettyCashEntries",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PettyCashEntries_Warehouses_WarehouseId",
                table: "PettyCashEntries");

            migrationBuilder.DropIndex(
                name: "IX_PettyCashEntries_WarehouseId",
                table: "PettyCashEntries");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "PettyCashEntries");
        }
    }
}
