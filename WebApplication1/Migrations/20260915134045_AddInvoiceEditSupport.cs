using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication1.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceEditSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // IsCurrent must backfill existing rows as true (never edited/superseded before this
            // migration) — false would retroactively hide every existing invoice/transfer line
            // and zero out every existing TotalAmount, since those computations filter on it.
            migrationBuilder.AddColumn<bool>(
                name: "IsCurrent",
                table: "StockTransferItems",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReversed",
                table: "StockTransactions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsCurrent",
                table: "SalesInvoiceItems",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsCurrent",
                table: "PurchaseInvoiceItems",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsReversed",
                table: "JournalEntries",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsCurrent",
                table: "StockTransferItems");

            migrationBuilder.DropColumn(
                name: "IsReversed",
                table: "StockTransactions");

            migrationBuilder.DropColumn(
                name: "IsCurrent",
                table: "SalesInvoiceItems");

            migrationBuilder.DropColumn(
                name: "IsCurrent",
                table: "PurchaseInvoiceItems");

            migrationBuilder.DropColumn(
                name: "IsReversed",
                table: "JournalEntries");
        }
    }
}
