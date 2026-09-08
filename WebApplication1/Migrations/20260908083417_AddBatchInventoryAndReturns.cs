using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication1.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchInventoryAndReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BatchId",
                table: "StockTransactions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitCost",
                table: "StockTransactions",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "UnitSalePrice",
                table: "StockTransactions",
                type: "decimal(18,2)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BatchId",
                table: "SalesInvoiceItems",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SalePrice",
                table: "PurchaseInvoiceItems",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "ProductBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    PurchaseInvoiceItemId = table.Column<int>(type: "int", nullable: true),
                    BatchNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PurchaseDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PurchasePrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    SalePrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OriginalQuantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    RemainingQuantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ExpiryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductBatches_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductBatches_PurchaseInvoiceItems_PurchaseInvoiceItemId",
                        column: x => x.PurchaseInvoiceItemId,
                        principalTable: "PurchaseInvoiceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProductBatches_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReturns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PurchaseInvoiceId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseReturns_PurchaseInvoices_PurchaseInvoiceId",
                        column: x => x.PurchaseInvoiceId,
                        principalTable: "PurchaseInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SalesReturns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReturnNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SalesInvoiceId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesReturns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesReturns_SalesInvoices_SalesInvoiceId",
                        column: x => x.SalesInvoiceId,
                        principalTable: "SalesInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PurchaseReturnItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PurchaseReturnId = table.Column<int>(type: "int", nullable: false),
                    PurchaseInvoiceItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseReturnItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnItems_PurchaseInvoiceItems_PurchaseInvoiceItemId",
                        column: x => x.PurchaseInvoiceItemId,
                        principalTable: "PurchaseInvoiceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PurchaseReturnItems_PurchaseReturns_PurchaseReturnId",
                        column: x => x.PurchaseReturnId,
                        principalTable: "PurchaseReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SalesReturnItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SalesReturnId = table.Column<int>(type: "int", nullable: false),
                    SalesInvoiceItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(18,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesReturnItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesReturnItems_SalesInvoiceItems_SalesInvoiceItemId",
                        column: x => x.SalesInvoiceItemId,
                        principalTable: "SalesInvoiceItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SalesReturnItems_SalesReturns_SalesReturnId",
                        column: x => x.SalesReturnId,
                        principalTable: "SalesReturns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockTransactions_BatchId",
                table: "StockTransactions",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesInvoiceItems_BatchId",
                table: "SalesInvoiceItems",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBatches_ProductId",
                table: "ProductBatches",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBatches_PurchaseInvoiceItemId",
                table: "ProductBatches",
                column: "PurchaseInvoiceItemId",
                unique: true,
                filter: "[PurchaseInvoiceItemId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBatches_RemainingQuantity",
                table: "ProductBatches",
                column: "RemainingQuantity");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBatches_WarehouseId_ProductId_PurchaseDate",
                table: "ProductBatches",
                columns: new[] { "WarehouseId", "ProductId", "PurchaseDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnItems_PurchaseInvoiceItemId",
                table: "PurchaseReturnItems",
                column: "PurchaseInvoiceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturnItems_PurchaseReturnId",
                table: "PurchaseReturnItems",
                column: "PurchaseReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_PurchaseInvoiceId",
                table: "PurchaseReturns",
                column: "PurchaseInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseReturns_ReturnNumber",
                table: "PurchaseReturns",
                column: "ReturnNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnItems_SalesInvoiceItemId",
                table: "SalesReturnItems",
                column: "SalesInvoiceItemId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturnItems_SalesReturnId",
                table: "SalesReturnItems",
                column: "SalesReturnId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturns_ReturnNumber",
                table: "SalesReturns",
                column: "ReturnNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesReturns_SalesInvoiceId",
                table: "SalesReturns",
                column: "SalesInvoiceId");

            // Backfill: synthesize one batch per existing purchase line (best-effort — the true
            // original per-batch sale price predates this migration and is unrecoverable, so we
            // use the product's current SalePrice as a default), then replay existing sales AND
            // stock transfers chronologically against those batches in FIFO order. Transfers
            // must be replayed too (not just sales) because they move units between warehouses
            // without a purchase/sale — skipping them would leave a batch sitting in its
            // original warehouse while ProductWarehouseStock (the trusted aggregate) shows it
            // moved, undercounting the destination and overcounting the source. A transfer
            // FIFO-consumes from the source warehouse's batches exactly like a sale, then
            // creates a same-priced batch in the destination warehouse for whatever it consumed
            // (i.e. it carries its cost/sale-price/purchase-date lineage across the transfer).
            // Only Posted documents are replayed — Cancelled ones already had their effect
            // reversed in the aggregate and must not double-count here. From this migration
            // forward every new purchase/sale/transfer is exact; this is only an approximation
            // for pre-existing rows (and only for the specific historical documents this ran
            // against — it is not meant to be re-run).
            migrationBuilder.Sql(@"
                INSERT INTO ProductBatches (ProductId, WarehouseId, PurchaseInvoiceItemId, BatchNumber, PurchaseDate, PurchasePrice, SalePrice, OriginalQuantity, RemainingQuantity, ExpiryDate, CreatedAt, CreatedBy, IsActive)
                SELECT
                    pii.ProductId,
                    pi.WarehouseId,
                    pii.Id,
                    'BATCH-' + RIGHT('000000' + CAST(ROW_NUMBER() OVER (ORDER BY pi.[Date], pii.Id) AS varchar(6)), 6),
                    pi.[Date],
                    pii.UnitPrice,
                    p.SalePrice,
                    pii.Quantity,
                    pii.Quantity,
                    NULL,
                    GETUTCDATE(),
                    NULL,
                    1
                FROM PurchaseInvoiceItems pii
                JOIN PurchaseInvoices pi ON pi.Id = pii.PurchaseInvoiceId
                JOIN Products p ON p.Id = pii.ProductId
                WHERE pi.Status = 1;

                UPDATE pii
                SET pii.SalePrice = pb.SalePrice
                FROM PurchaseInvoiceItems pii
                JOIN ProductBatches pb ON pb.PurchaseInvoiceItemId = pii.Id;

                DECLARE @nextBatchSeq INT = (SELECT ISNULL(MAX(CAST(SUBSTRING(BatchNumber, 7, 6) AS INT)), 0) + 1 FROM ProductBatches);

                DECLARE @eventType CHAR(1), @refId INT, @productId INT, @fromWh INT, @toWh INT, @needQty DECIMAL(18,2);
                DECLARE @firstBatchId INT, @batchId INT, @batchRemaining DECIMAL(18,2), @take DECIMAL(18,2);
                DECLARE @batchPurchasePrice DECIMAL(18,2), @batchSalePrice DECIMAL(18,2), @batchPurchaseDate DATETIME2, @newBatchId INT;

                DECLARE event_cursor CURSOR LOCAL FAST_FORWARD FOR
                    SELECT 'S', sii.Id, sii.ProductId, si.WarehouseId, CAST(NULL AS INT), sii.Quantity, si.[Date]
                    FROM SalesInvoiceItems sii
                    JOIN SalesInvoices si ON si.Id = sii.SalesInvoiceId
                    WHERE si.Status = 1
                    UNION ALL
                    SELECT 'T', sti.Id, sti.ProductId, st.FromWarehouseId, st.ToWarehouseId, sti.Quantity, st.[Date]
                    FROM StockTransferItems sti
                    JOIN StockTransfers st ON st.Id = sti.StockTransferId
                    WHERE st.Status = 1
                    ORDER BY 7, 1, 2;

                OPEN event_cursor;
                FETCH NEXT FROM event_cursor INTO @eventType, @refId, @productId, @fromWh, @toWh, @needQty;

                WHILE @@FETCH_STATUS = 0
                BEGIN
                    SET @firstBatchId = NULL;

                    DECLARE batch_cursor CURSOR LOCAL FAST_FORWARD FOR
                        SELECT Id, RemainingQuantity, PurchasePrice, SalePrice, PurchaseDate
                        FROM ProductBatches
                        WHERE ProductId = @productId AND WarehouseId = @fromWh AND RemainingQuantity > 0
                        ORDER BY PurchaseDate, Id;

                    OPEN batch_cursor;
                    FETCH NEXT FROM batch_cursor INTO @batchId, @batchRemaining, @batchPurchasePrice, @batchSalePrice, @batchPurchaseDate;

                    WHILE @@FETCH_STATUS = 0 AND @needQty > 0
                    BEGIN
                        SET @take = CASE WHEN @batchRemaining >= @needQty THEN @needQty ELSE @batchRemaining END;

                        UPDATE ProductBatches SET RemainingQuantity = RemainingQuantity - @take WHERE Id = @batchId;

                        IF @firstBatchId IS NULL SET @firstBatchId = @batchId;

                        IF @eventType = 'T'
                        BEGIN
                            INSERT INTO ProductBatches (ProductId, WarehouseId, PurchaseInvoiceItemId, BatchNumber, PurchaseDate, PurchasePrice, SalePrice, OriginalQuantity, RemainingQuantity, ExpiryDate, CreatedAt, CreatedBy, IsActive)
                            VALUES (@productId, @toWh, NULL, 'BATCH-' + RIGHT('000000' + CAST(@nextBatchSeq AS varchar(6)), 6), @batchPurchaseDate, @batchPurchasePrice, @batchSalePrice, @take, @take, NULL, GETUTCDATE(), NULL, 1);
                            SET @nextBatchSeq = @nextBatchSeq + 1;
                        END

                        SET @needQty = @needQty - @take;

                        FETCH NEXT FROM batch_cursor INTO @batchId, @batchRemaining, @batchPurchasePrice, @batchSalePrice, @batchPurchaseDate;
                    END

                    CLOSE batch_cursor;
                    DEALLOCATE batch_cursor;

                    IF @eventType = 'S' AND @firstBatchId IS NOT NULL
                        UPDATE SalesInvoiceItems SET BatchId = @firstBatchId WHERE Id = @refId;

                    FETCH NEXT FROM event_cursor INTO @eventType, @refId, @productId, @fromWh, @toWh, @needQty;
                END

                CLOSE event_cursor;
                DEALLOCATE event_cursor;
            ");

            migrationBuilder.AddForeignKey(
                name: "FK_SalesInvoiceItems_ProductBatches_BatchId",
                table: "SalesInvoiceItems",
                column: "BatchId",
                principalTable: "ProductBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockTransactions_ProductBatches_BatchId",
                table: "StockTransactions",
                column: "BatchId",
                principalTable: "ProductBatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SalesInvoiceItems_ProductBatches_BatchId",
                table: "SalesInvoiceItems");

            migrationBuilder.DropForeignKey(
                name: "FK_StockTransactions_ProductBatches_BatchId",
                table: "StockTransactions");

            migrationBuilder.DropTable(
                name: "ProductBatches");

            migrationBuilder.DropTable(
                name: "PurchaseReturnItems");

            migrationBuilder.DropTable(
                name: "SalesReturnItems");

            migrationBuilder.DropTable(
                name: "PurchaseReturns");

            migrationBuilder.DropTable(
                name: "SalesReturns");

            migrationBuilder.DropIndex(
                name: "IX_StockTransactions_BatchId",
                table: "StockTransactions");

            migrationBuilder.DropIndex(
                name: "IX_SalesInvoiceItems_BatchId",
                table: "SalesInvoiceItems");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "StockTransactions");

            migrationBuilder.DropColumn(
                name: "UnitCost",
                table: "StockTransactions");

            migrationBuilder.DropColumn(
                name: "UnitSalePrice",
                table: "StockTransactions");

            migrationBuilder.DropColumn(
                name: "BatchId",
                table: "SalesInvoiceItems");

            migrationBuilder.DropColumn(
                name: "SalePrice",
                table: "PurchaseInvoiceItems");
        }
    }
}
