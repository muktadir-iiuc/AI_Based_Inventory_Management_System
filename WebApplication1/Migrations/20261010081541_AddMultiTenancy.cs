using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication1.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Warehouses_Code",
                table: "Warehouses");

            migrationBuilder.DropIndex(
                name: "IX_StockTransfers_TransferNumber",
                table: "StockTransfers");

            migrationBuilder.DropIndex(
                name: "IX_StockAdjustments_AdjustmentNumber",
                table: "StockAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_SalesReturns_ReturnNumber",
                table: "SalesReturns");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_InvoiceNumber",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReturns_ReturnNumber",
                table: "PurchaseReturns");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoices_InvoiceNumber",
                table: "PurchaseInvoices");

            migrationBuilder.DropIndex(
                name: "IX_Products_Sku",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_PettyCashNames_Name",
                table: "PettyCashNames");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_EntryNumber",
                table: "JournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_Code",
                table: "Accounts");

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Warehouses",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "UserWarehouses",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "UserPermissions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "UnitOfMeasures",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Suppliers",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "StockTransfers",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "StockTransferPurchaseInvoices",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "StockTransferItems",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "StockTransactions",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "StockAdjustments",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "StockAdjustmentItems",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "SalesReturns",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "SalesReturnItems",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "SalesInvoices",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "SalesInvoiceItems",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "PurchaseReturns",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "PurchaseReturnItems",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "PurchaseInvoices",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "PurchaseInvoiceItems",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ProductWarehouseStocks",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ProductPriceHistories",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "ProductBatches",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "PettyCashNames",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "PettyCashEntries",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Payments",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "JournalEntryLines",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "JournalEntries",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Customers",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "CompanySettings",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Categories",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "Accounts",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ContactName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ContactEmail = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    ContactPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    City = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TaxId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    IsAdminHold = table.Column<bool>(type: "bit", nullable: false),
                    AdminHoldReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SuspendedForNonPayment = table.Column<bool>(type: "bit", nullable: false),
                    OwnerUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            // Raw SQL is wrapped in EXEC so it is compiled when it runs: in a generated (idempotent) script the whole migration is one
            // batch and SQL Server would otherwise reject columns that this same batch is only about to add.
            // The existing single business becomes tenant 1 (named after its Company Settings), so
            // every row added above with DEFAULT 1 and every existing user is already attached to it.
            migrationBuilder.Sql(@"
EXEC(N'INSERT INTO [Tenants] ([Name], [Code], [Status], [IsAdminHold], [SuspendedForNonPayment], [CreatedAt], [Country], [ContactEmail], [Address], [ContactPhone], [CreatedBy])
SELECT ISNULL((SELECT TOP 1 [CompanyName] FROM [CompanySettings] ORDER BY [Id]), N''Default Business''),
       N''default'',
       1, 0, 0, SYSUTCDATETIME(), N''Bangladesh'',
       (SELECT TOP 1 [Email] FROM [AspNetUsers] WHERE [NormalizedEmail] = N''ADMIN@INVENTORY.LOCAL''),
       (SELECT TOP 1 [Address] FROM [CompanySettings] ORDER BY [Id]),
       (SELECT TOP 1 [Phone] FROM [CompanySettings] ORDER BY [Id]),
       N''migration:AddMultiTenancy'';');
");
            migrationBuilder.Sql("EXEC(N'UPDATE [AspNetUsers] SET [TenantId] = (SELECT MIN([Id]) FROM [Tenants]);');");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_TenantId",
                table: "Warehouses",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_TenantId_Code",
                table: "Warehouses",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserWarehouses_TenantId",
                table: "UserWarehouses",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPermissions_TenantId",
                table: "UserPermissions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_UnitOfMeasures_TenantId",
                table: "UnitOfMeasures",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_TenantId",
                table: "Suppliers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_TenantId",
                table: "StockTransfers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_TenantId_TransferNumber",
                table: "StockTransfers",
                columns: new[] { "TenantId", "TransferNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferPurchaseInvoices_TenantId",
                table: "StockTransferPurchaseInvoices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransferItems_TenantId",
                table: "StockTransferItems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockTransactions_TenantId",
                table: "StockTransactions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_TenantId",
                table: "StockAdjustments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_TenantId_AdjustmentNumber",
                table: "StockAdjustments",
                columns: new[] { "TenantId", "AdjustmentNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustmentItems_TenantId",
                table: "StockAdjustmentItems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturns_TenantId",
                table: "SalesReturns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturns_TenantId_ReturnNumber",
                table: "SalesReturns",
                columns: new[] { "TenantId", "ReturnNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnItems_TenantId",
                table: "SalesReturnItems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_TenantId",
                table: "SalesInvoices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_TenantId_InvoiceNumber",
                table: "SalesInvoices",
                columns: new[] { "TenantId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceItems_TenantId",
                table: "SalesInvoiceItems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_TenantId",
                table: "PurchaseReturns",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_TenantId_ReturnNumber",
                table: "PurchaseReturns",
                columns: new[] { "TenantId", "ReturnNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnItems_TenantId",
                table: "PurchaseReturnItems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_TenantId",
                table: "PurchaseInvoices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_TenantId_InvoiceNumber",
                table: "PurchaseInvoices",
                columns: new[] { "TenantId", "InvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoiceItems_TenantId",
                table: "PurchaseInvoiceItems",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductWarehouseStocks_TenantId",
                table: "ProductWarehouseStocks",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId",
                table: "Products",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_TenantId_Sku",
                table: "Products",
                columns: new[] { "TenantId", "Sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductPriceHistories_TenantId",
                table: "ProductPriceHistories",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBatches_TenantId",
                table: "ProductBatches",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PettyCashNames_TenantId",
                table: "PettyCashNames",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_PettyCashNames_TenantId_Name",
                table: "PettyCashNames",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PettyCashEntries_TenantId",
                table: "PettyCashEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_TenantId",
                table: "Payments",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_TenantId",
                table: "JournalEntryLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_TenantId",
                table: "JournalEntries",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_TenantId_EntryNumber",
                table: "JournalEntries",
                columns: new[] { "TenantId", "EntryNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId",
                table: "Customers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanySettings_TenantId",
                table: "CompanySettings",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_TenantId",
                table: "Categories",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_TenantId",
                table: "AspNetUsers",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_TenantId",
                table: "Accounts",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_TenantId_Code",
                table: "Accounts",
                columns: new[] { "TenantId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Code",
                table: "Tenants",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_Status",
                table: "Tenants",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_Accounts_Tenants_TenantId",
                table: "Accounts",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Tenants_TenantId",
                table: "AspNetUsers",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Categories_Tenants_TenantId",
                table: "Categories",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CompanySettings_Tenants_TenantId",
                table: "CompanySettings",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Tenants_TenantId",
                table: "Customers",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_Tenants_TenantId",
                table: "JournalEntries",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_Tenants_TenantId",
                table: "JournalEntryLines",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Payments_Tenants_TenantId",
                table: "Payments",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PettyCashEntries_Tenants_TenantId",
                table: "PettyCashEntries",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PettyCashNames_Tenants_TenantId",
                table: "PettyCashNames",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductBatches_Tenants_TenantId",
                table: "ProductBatches",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductPriceHistories_Tenants_TenantId",
                table: "ProductPriceHistories",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Tenants_TenantId",
                table: "Products",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ProductWarehouseStocks_Tenants_TenantId",
                table: "ProductWarehouseStocks",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoiceItems_Tenants_TenantId",
                table: "PurchaseInvoiceItems",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseInvoices_Tenants_TenantId",
                table: "PurchaseInvoices",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseReturnItems_Tenants_TenantId",
                table: "PurchaseReturnItems",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseReturns_Tenants_TenantId",
                table: "PurchaseReturns",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceItems_Tenants_TenantId",
                table: "SalesInvoiceItems",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoices_Tenants_TenantId",
                table: "SalesInvoices",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesReturnItems_Tenants_TenantId",
                table: "SalesReturnItems",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_SalesReturns_Tenants_TenantId",
                table: "SalesReturns",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockAdjustmentItems_Tenants_TenantId",
                table: "StockAdjustmentItems",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockAdjustments_Tenants_TenantId",
                table: "StockAdjustments",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockTransactions_Tenants_TenantId",
                table: "StockTransactions",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockTransferItems_Tenants_TenantId",
                table: "StockTransferItems",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockTransferPurchaseInvoices_Tenants_TenantId",
                table: "StockTransferPurchaseInvoices",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockTransfers_Tenants_TenantId",
                table: "StockTransfers",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Suppliers_Tenants_TenantId",
                table: "Suppliers",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UnitOfMeasures_Tenants_TenantId",
                table: "UnitOfMeasures",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserPermissions_Tenants_TenantId",
                table: "UserPermissions",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UserWarehouses_Tenants_TenantId",
                table: "UserWarehouses",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Warehouses_Tenants_TenantId",
                table: "Warehouses",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // The DEFAULT 1 above only existed to backfill current rows. Drop it so a row saved
            // without a tenant fails loudly (the context stamps TenantId; see ApplicationDbContext)
            // instead of silently landing in tenant 1.
            migrationBuilder.Sql(@"
DECLARE @sql nvarchar(max) = N'';
SELECT @sql += N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(dc.parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(dc.parent_object_id))
             + N' DROP CONSTRAINT ' + QUOTENAME(dc.name) + N';'
FROM sys.default_constraints dc
JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
WHERE c.name = N'TenantId';
EXEC sp_executesql @sql;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Accounts_Tenants_TenantId",
                table: "Accounts");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Tenants_TenantId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_Categories_Tenants_TenantId",
                table: "Categories");

            migrationBuilder.DropForeignKey(
                name: "FK_CompanySettings_Tenants_TenantId",
                table: "CompanySettings");

            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Tenants_TenantId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntries_Tenants_TenantId",
                table: "JournalEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryLines_Tenants_TenantId",
                table: "JournalEntryLines");

            migrationBuilder.DropForeignKey(
                name: "FK_Payments_Tenants_TenantId",
                table: "Payments");

            migrationBuilder.DropForeignKey(
                name: "FK_PettyCashEntries_Tenants_TenantId",
                table: "PettyCashEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_PettyCashNames_Tenants_TenantId",
                table: "PettyCashNames");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductBatches_Tenants_TenantId",
                table: "ProductBatches");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductPriceHistories_Tenants_TenantId",
                table: "ProductPriceHistories");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Tenants_TenantId",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_ProductWarehouseStocks_Tenants_TenantId",
                table: "ProductWarehouseStocks");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoiceItems_Tenants_TenantId",
                table: "PurchaseInvoiceItems");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseInvoices_Tenants_TenantId",
                table: "PurchaseInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseReturnItems_Tenants_TenantId",
                table: "PurchaseReturnItems");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseReturns_Tenants_TenantId",
                table: "PurchaseReturns");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceItems_Tenants_TenantId",
                table: "SalesInvoiceItems");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoices_Tenants_TenantId",
                table: "SalesInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesReturnItems_Tenants_TenantId",
                table: "SalesReturnItems");

            migrationBuilder.DropForeignKey(
                name: "FK_SalesReturns_Tenants_TenantId",
                table: "SalesReturns");

            migrationBuilder.DropForeignKey(
                name: "FK_StockAdjustmentItems_Tenants_TenantId",
                table: "StockAdjustmentItems");

            migrationBuilder.DropForeignKey(
                name: "FK_StockAdjustments_Tenants_TenantId",
                table: "StockAdjustments");

            migrationBuilder.DropForeignKey(
                name: "FK_StockTransactions_Tenants_TenantId",
                table: "StockTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_StockTransferItems_Tenants_TenantId",
                table: "StockTransferItems");

            migrationBuilder.DropForeignKey(
                name: "FK_StockTransferPurchaseInvoices_Tenants_TenantId",
                table: "StockTransferPurchaseInvoices");

            migrationBuilder.DropForeignKey(
                name: "FK_StockTransfers_Tenants_TenantId",
                table: "StockTransfers");

            migrationBuilder.DropForeignKey(
                name: "FK_Suppliers_Tenants_TenantId",
                table: "Suppliers");

            migrationBuilder.DropForeignKey(
                name: "FK_UnitOfMeasures_Tenants_TenantId",
                table: "UnitOfMeasures");

            migrationBuilder.DropForeignKey(
                name: "FK_UserPermissions_Tenants_TenantId",
                table: "UserPermissions");

            migrationBuilder.DropForeignKey(
                name: "FK_UserWarehouses_Tenants_TenantId",
                table: "UserWarehouses");

            migrationBuilder.DropForeignKey(
                name: "FK_Warehouses_Tenants_TenantId",
                table: "Warehouses");

            migrationBuilder.DropTable(
                name: "Tenants");

            migrationBuilder.DropIndex(
                name: "IX_Warehouses_TenantId",
                table: "Warehouses");

            migrationBuilder.DropIndex(
                name: "IX_Warehouses_TenantId_Code",
                table: "Warehouses");

            migrationBuilder.DropIndex(
                name: "IX_UserWarehouses_TenantId",
                table: "UserWarehouses");

            migrationBuilder.DropIndex(
                name: "IX_UserPermissions_TenantId",
                table: "UserPermissions");

            migrationBuilder.DropIndex(
                name: "IX_UnitOfMeasures_TenantId",
                table: "UnitOfMeasures");

            migrationBuilder.DropIndex(
                name: "IX_Suppliers_TenantId",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_StockTransfers_TenantId",
                table: "StockTransfers");

            migrationBuilder.DropIndex(
                name: "IX_StockTransfers_TenantId_TransferNumber",
                table: "StockTransfers");

            migrationBuilder.DropIndex(
                name: "IX_StockTransferPurchaseInvoices_TenantId",
                table: "StockTransferPurchaseInvoices");

            migrationBuilder.DropIndex(
                name: "IX_StockTransferItems_TenantId",
                table: "StockTransferItems");

            migrationBuilder.DropIndex(
                name: "IX_StockTransactions_TenantId",
                table: "StockTransactions");

            migrationBuilder.DropIndex(
                name: "IX_StockAdjustments_TenantId",
                table: "StockAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_StockAdjustments_TenantId_AdjustmentNumber",
                table: "StockAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_StockAdjustmentItems_TenantId",
                table: "StockAdjustmentItems");

            migrationBuilder.DropIndex(
                name: "IX_SalesReturns_TenantId",
                table: "SalesReturns");

            migrationBuilder.DropIndex(
                name: "IX_SalesReturns_TenantId_ReturnNumber",
                table: "SalesReturns");

            migrationBuilder.DropIndex(
                name: "IX_SalesReturnItems_TenantId",
                table: "SalesReturnItems");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_TenantId",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoices_TenantId_InvoiceNumber",
                table: "SalesInvoices");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceItems_TenantId",
                table: "SalesInvoiceItems");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReturns_TenantId",
                table: "PurchaseReturns");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReturns_TenantId_ReturnNumber",
                table: "PurchaseReturns");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseReturnItems_TenantId",
                table: "PurchaseReturnItems");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoices_TenantId",
                table: "PurchaseInvoices");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoices_TenantId_InvoiceNumber",
                table: "PurchaseInvoices");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseInvoiceItems_TenantId",
                table: "PurchaseInvoiceItems");

            migrationBuilder.DropIndex(
                name: "IX_ProductWarehouseStocks_TenantId",
                table: "ProductWarehouseStocks");

            migrationBuilder.DropIndex(
                name: "IX_Products_TenantId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_TenantId_Sku",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_ProductPriceHistories_TenantId",
                table: "ProductPriceHistories");

            migrationBuilder.DropIndex(
                name: "IX_ProductBatches_TenantId",
                table: "ProductBatches");

            migrationBuilder.DropIndex(
                name: "IX_PettyCashNames_TenantId",
                table: "PettyCashNames");

            migrationBuilder.DropIndex(
                name: "IX_PettyCashNames_TenantId_Name",
                table: "PettyCashNames");

            migrationBuilder.DropIndex(
                name: "IX_PettyCashEntries_TenantId",
                table: "PettyCashEntries");

            migrationBuilder.DropIndex(
                name: "IX_Payments_TenantId",
                table: "Payments");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLines_TenantId",
                table: "JournalEntryLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_TenantId",
                table: "JournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_TenantId_EntryNumber",
                table: "JournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_Customers_TenantId",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_CompanySettings_TenantId",
                table: "CompanySettings");

            migrationBuilder.DropIndex(
                name: "IX_Categories_TenantId",
                table: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_TenantId",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_TenantId",
                table: "Accounts");

            migrationBuilder.DropIndex(
                name: "IX_Accounts_TenantId_Code",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "UserWarehouses");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "UserPermissions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "UnitOfMeasures");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StockTransfers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StockTransferPurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StockTransferItems");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StockTransactions");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StockAdjustments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "StockAdjustmentItems");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SalesReturns");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SalesReturnItems");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SalesInvoices");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "SalesInvoiceItems");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PurchaseReturns");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PurchaseReturnItems");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PurchaseInvoices");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PurchaseInvoiceItems");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ProductWarehouseStocks");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ProductPriceHistories");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "ProductBatches");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PettyCashNames");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "PettyCashEntries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "JournalEntryLines");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "CompanySettings");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "Accounts");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_Code",
                table: "Warehouses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockTransfers_TransferNumber",
                table: "StockTransfers",
                column: "TransferNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_AdjustmentNumber",
                table: "StockAdjustments",
                column: "AdjustmentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturns_ReturnNumber",
                table: "SalesReturns",
                column: "ReturnNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoices_InvoiceNumber",
                table: "SalesInvoices",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_ReturnNumber",
                table: "PurchaseReturns",
                column: "ReturnNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseInvoices_InvoiceNumber",
                table: "PurchaseInvoices",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_Sku",
                table: "Products",
                column: "Sku",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PettyCashNames_Name",
                table: "PettyCashNames",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_EntryNumber",
                table: "JournalEntries",
                column: "EntryNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_Code",
                table: "Accounts",
                column: "Code",
                unique: true);
        }
    }
}
