BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    DROP INDEX [IX_Warehouses_Code] ON [Warehouses];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    DROP INDEX [IX_StockTransfers_TransferNumber] ON [StockTransfers];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    DROP INDEX [IX_StockAdjustments_AdjustmentNumber] ON [StockAdjustments];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    DROP INDEX [IX_SalesReturns_ReturnNumber] ON [SalesReturns];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    DROP INDEX [IX_SalesInvoices_InvoiceNumber] ON [SalesInvoices];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    DROP INDEX [IX_PurchaseReturns_ReturnNumber] ON [PurchaseReturns];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    DROP INDEX [IX_PurchaseInvoices_InvoiceNumber] ON [PurchaseInvoices];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    DROP INDEX [IX_Products_Sku] ON [Products];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    DROP INDEX [IX_PettyCashNames_Name] ON [PettyCashNames];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    DROP INDEX [IX_JournalEntries_EntryNumber] ON [JournalEntries];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    DROP INDEX [IX_Accounts_Code] ON [Accounts];
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [Warehouses] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [UserWarehouses] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [UserPermissions] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [UnitOfMeasures] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [Suppliers] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [StockTransfers] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [StockTransferPurchaseInvoices] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [StockTransferItems] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [StockTransactions] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [StockAdjustments] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [StockAdjustmentItems] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [SalesReturns] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [SalesReturnItems] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [SalesInvoices] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [SalesInvoiceItems] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [PurchaseReturns] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [PurchaseReturnItems] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [PurchaseInvoices] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [PurchaseInvoiceItems] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [ProductWarehouseStocks] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [Products] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [ProductPriceHistories] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [ProductBatches] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [PettyCashNames] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [PettyCashEntries] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [Payments] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [JournalEntryLines] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [JournalEntries] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [Customers] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [CompanySettings] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [Categories] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [TenantId] int NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [Accounts] ADD [TenantId] int NOT NULL DEFAULT 1;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE TABLE [Tenants] (
        [Id] int NOT NULL IDENTITY,
        [Name] nvarchar(200) NOT NULL,
        [Code] nvarchar(50) NOT NULL,
        [ContactName] nvarchar(200) NULL,
        [ContactEmail] nvarchar(256) NULL,
        [ContactPhone] nvarchar(50) NULL,
        [Address] nvarchar(300) NULL,
        [City] nvarchar(100) NULL,
        [Country] nvarchar(100) NULL,
        [TaxId] nvarchar(50) NULL,
        [Status] int NOT NULL,
        [IsAdminHold] bit NOT NULL,
        [AdminHoldReason] nvarchar(500) NULL,
        [SuspendedForNonPayment] bit NOT NULL,
        [OwnerUserId] nvarchar(450) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [UpdatedAt] datetime2 NULL,
        [DeletedAt] datetime2 NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_Tenants] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN

    EXEC(N'INSERT INTO [Tenants] ([Name], [Code], [Status], [IsAdminHold], [SuspendedForNonPayment], [CreatedAt], [Country], [ContactEmail], [Address], [ContactPhone], [CreatedBy])
    SELECT ISNULL((SELECT TOP 1 [CompanyName] FROM [CompanySettings] ORDER BY [Id]), N''Default Business''),
           N''default'',
           1, 0, 0, SYSUTCDATETIME(), N''Bangladesh'',
           (SELECT TOP 1 [Email] FROM [AspNetUsers] WHERE [NormalizedEmail] = N''ADMIN@INVENTORY.LOCAL''),
           (SELECT TOP 1 [Address] FROM [CompanySettings] ORDER BY [Id]),
           (SELECT TOP 1 [Phone] FROM [CompanySettings] ORDER BY [Id]),
           N''migration:AddMultiTenancy'';');

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    EXEC(N'UPDATE [AspNetUsers] SET [TenantId] = (SELECT MIN([Id]) FROM [Tenants]);');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_Warehouses_TenantId] ON [Warehouses] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Warehouses_TenantId_Code] ON [Warehouses] ([TenantId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_UserWarehouses_TenantId] ON [UserWarehouses] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_UserPermissions_TenantId] ON [UserPermissions] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_UnitOfMeasures_TenantId] ON [UnitOfMeasures] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_Suppliers_TenantId] ON [Suppliers] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_StockTransfers_TenantId] ON [StockTransfers] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StockTransfers_TenantId_TransferNumber] ON [StockTransfers] ([TenantId], [TransferNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_StockTransferPurchaseInvoices_TenantId] ON [StockTransferPurchaseInvoices] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_StockTransferItems_TenantId] ON [StockTransferItems] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_StockTransactions_TenantId] ON [StockTransactions] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_StockAdjustments_TenantId] ON [StockAdjustments] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_StockAdjustments_TenantId_AdjustmentNumber] ON [StockAdjustments] ([TenantId], [AdjustmentNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_StockAdjustmentItems_TenantId] ON [StockAdjustmentItems] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_SalesReturns_TenantId] ON [SalesReturns] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SalesReturns_TenantId_ReturnNumber] ON [SalesReturns] ([TenantId], [ReturnNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_SalesReturnItems_TenantId] ON [SalesReturnItems] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_SalesInvoices_TenantId] ON [SalesInvoices] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SalesInvoices_TenantId_InvoiceNumber] ON [SalesInvoices] ([TenantId], [InvoiceNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_SalesInvoiceItems_TenantId] ON [SalesInvoiceItems] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_PurchaseReturns_TenantId] ON [PurchaseReturns] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PurchaseReturns_TenantId_ReturnNumber] ON [PurchaseReturns] ([TenantId], [ReturnNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_PurchaseReturnItems_TenantId] ON [PurchaseReturnItems] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_PurchaseInvoices_TenantId] ON [PurchaseInvoices] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PurchaseInvoices_TenantId_InvoiceNumber] ON [PurchaseInvoices] ([TenantId], [InvoiceNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_PurchaseInvoiceItems_TenantId] ON [PurchaseInvoiceItems] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_ProductWarehouseStocks_TenantId] ON [ProductWarehouseStocks] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_Products_TenantId] ON [Products] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Products_TenantId_Sku] ON [Products] ([TenantId], [Sku]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_ProductPriceHistories_TenantId] ON [ProductPriceHistories] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_ProductBatches_TenantId] ON [ProductBatches] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_PettyCashNames_TenantId] ON [PettyCashNames] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PettyCashNames_TenantId_Name] ON [PettyCashNames] ([TenantId], [Name]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_PettyCashEntries_TenantId] ON [PettyCashEntries] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_Payments_TenantId] ON [Payments] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_JournalEntryLines_TenantId] ON [JournalEntryLines] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_JournalEntries_TenantId] ON [JournalEntries] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_JournalEntries_TenantId_EntryNumber] ON [JournalEntries] ([TenantId], [EntryNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_Customers_TenantId] ON [Customers] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_CompanySettings_TenantId] ON [CompanySettings] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_Categories_TenantId] ON [Categories] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_AspNetUsers_TenantId] ON [AspNetUsers] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_Accounts_TenantId] ON [Accounts] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Accounts_TenantId_Code] ON [Accounts] ([TenantId], [Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Tenants_Code] ON [Tenants] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    CREATE INDEX [IX_Tenants_Status] ON [Tenants] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [Accounts] ADD CONSTRAINT [FK_Accounts_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD CONSTRAINT [FK_AspNetUsers_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [Categories] ADD CONSTRAINT [FK_Categories_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [CompanySettings] ADD CONSTRAINT [FK_CompanySettings_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [Customers] ADD CONSTRAINT [FK_Customers_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [JournalEntries] ADD CONSTRAINT [FK_JournalEntries_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [JournalEntryLines] ADD CONSTRAINT [FK_JournalEntryLines_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [Payments] ADD CONSTRAINT [FK_Payments_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [PettyCashEntries] ADD CONSTRAINT [FK_PettyCashEntries_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [PettyCashNames] ADD CONSTRAINT [FK_PettyCashNames_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [ProductBatches] ADD CONSTRAINT [FK_ProductBatches_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [ProductPriceHistories] ADD CONSTRAINT [FK_ProductPriceHistories_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [Products] ADD CONSTRAINT [FK_Products_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [ProductWarehouseStocks] ADD CONSTRAINT [FK_ProductWarehouseStocks_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [PurchaseInvoiceItems] ADD CONSTRAINT [FK_PurchaseInvoiceItems_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [PurchaseInvoices] ADD CONSTRAINT [FK_PurchaseInvoices_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [PurchaseReturnItems] ADD CONSTRAINT [FK_PurchaseReturnItems_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [PurchaseReturns] ADD CONSTRAINT [FK_PurchaseReturns_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [SalesInvoiceItems] ADD CONSTRAINT [FK_SalesInvoiceItems_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [SalesInvoices] ADD CONSTRAINT [FK_SalesInvoices_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [SalesReturnItems] ADD CONSTRAINT [FK_SalesReturnItems_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [SalesReturns] ADD CONSTRAINT [FK_SalesReturns_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [StockAdjustmentItems] ADD CONSTRAINT [FK_StockAdjustmentItems_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [StockAdjustments] ADD CONSTRAINT [FK_StockAdjustments_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [StockTransactions] ADD CONSTRAINT [FK_StockTransactions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [StockTransferItems] ADD CONSTRAINT [FK_StockTransferItems_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [StockTransferPurchaseInvoices] ADD CONSTRAINT [FK_StockTransferPurchaseInvoices_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [StockTransfers] ADD CONSTRAINT [FK_StockTransfers_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [Suppliers] ADD CONSTRAINT [FK_Suppliers_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [UnitOfMeasures] ADD CONSTRAINT [FK_UnitOfMeasures_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [UserPermissions] ADD CONSTRAINT [FK_UserPermissions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [UserWarehouses] ADD CONSTRAINT [FK_UserWarehouses_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    ALTER TABLE [Warehouses] ADD CONSTRAINT [FK_Warehouses_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN

    DECLARE @sql nvarchar(max) = N'';
    SELECT @sql += N'ALTER TABLE ' + QUOTENAME(OBJECT_SCHEMA_NAME(dc.parent_object_id)) + N'.' + QUOTENAME(OBJECT_NAME(dc.parent_object_id))
                 + N' DROP CONSTRAINT ' + QUOTENAME(dc.name) + N';'
    FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE c.name = N'TenantId';
    EXEC sp_executesql @sql;

END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010081541_AddMultiTenancy'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261010081541_AddMultiTenancy', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    ALTER TABLE [Tenants] ADD [BillingEmail] nvarchar(256) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE TABLE [PlatformSettings] (
        [Key] nvarchar(80) NOT NULL,
        [Value] nvarchar(1000) NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_PlatformSettings] PRIMARY KEY ([Key])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE TABLE [SaasAuditLogs] (
        [Id] bigint NOT NULL IDENTITY,
        [At] datetime2 NOT NULL,
        [ActorUserId] nvarchar(450) NULL,
        [ActorName] nvarchar(256) NULL,
        [Action] nvarchar(80) NOT NULL,
        [EntityType] nvarchar(60) NULL,
        [EntityId] nvarchar(60) NULL,
        [TenantId] int NULL,
        [Result] nvarchar(20) NOT NULL,
        [Details] nvarchar(1000) NULL,
        [IpAddress] nvarchar(60) NULL,
        CONSTRAINT [PK_SaasAuditLogs] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE TABLE [SaasInvoices] (
        [Id] int NOT NULL IDENTITY,
        [InvoiceNumber] nvarchar(40) NOT NULL,
        [TenantId] int NOT NULL,
        [SubscriptionId] int NULL,
        [PlanId] int NULL,
        [PlanName] nvarchar(120) NOT NULL,
        [Purpose] int NOT NULL,
        [Status] int NOT NULL,
        [PeriodStart] datetime2 NOT NULL,
        [PeriodEnd] datetime2 NOT NULL,
        [IssueDate] datetime2 NOT NULL,
        [DueDate] datetime2 NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [SubTotal] decimal(18,2) NOT NULL,
        [DiscountTotal] decimal(18,2) NOT NULL,
        [TaxTotal] decimal(18,2) NOT NULL,
        [Total] decimal(18,2) NOT NULL,
        [AmountPaid] decimal(18,2) NOT NULL,
        [BillToName] nvarchar(200) NULL,
        [BillToEmail] nvarchar(256) NULL,
        [BillToAddress] nvarchar(300) NULL,
        [BillToTaxId] nvarchar(50) NULL,
        [Notes] nvarchar(1000) NULL,
        [AppliedToSubscription] bit NOT NULL,
        [PaidAt] datetime2 NULL,
        [CancelledAt] datetime2 NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [RemindersSent] nvarchar(400) NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_SaasInvoices] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SaasInvoices_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE TABLE [SaasNotifications] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] int NOT NULL,
        [Channel] int NOT NULL,
        [Type] nvarchar(60) NOT NULL,
        [Subject] nvarchar(200) NOT NULL,
        [Body] nvarchar(2000) NOT NULL,
        [ToEmail] nvarchar(256) NULL,
        [Status] int NOT NULL,
        [Attempts] int NOT NULL,
        [LastError] nvarchar(500) NULL,
        [DedupeKey] nvarchar(200) NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [SentAt] datetime2 NULL,
        [ReadAt] datetime2 NULL,
        CONSTRAINT [PK_SaasNotifications] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE TABLE [SubscriptionHistories] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] int NOT NULL,
        [SubscriptionId] int NULL,
        [Event] nvarchar(60) NOT NULL,
        [FromStatus] int NULL,
        [ToStatus] int NULL,
        [PlanId] int NULL,
        [Note] nvarchar(500) NULL,
        [Actor] nvarchar(256) NULL,
        [CreatedAt] datetime2 NOT NULL,
        CONSTRAINT [PK_SubscriptionHistories] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE TABLE [SubscriptionPlans] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(40) NOT NULL,
        [Name] nvarchar(120) NOT NULL,
        [Description] nvarchar(1000) NULL,
        [Price] decimal(18,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [IntervalCount] int NOT NULL,
        [IntervalUnit] int NOT NULL,
        [TrialDays] int NOT NULL,
        [GraceDays] int NOT NULL,
        [TaxPercent] decimal(5,2) NOT NULL,
        [DiscountPercent] decimal(5,2) NOT NULL,
        [MaxUsers] int NULL,
        [MaxProducts] int NULL,
        [MaxWarehouses] int NULL,
        [MaxStorageMb] int NULL,
        [AutoRenew] bit NOT NULL,
        [IsPublic] bit NOT NULL,
        [Status] int NOT NULL,
        [AvailableFrom] datetime2 NULL,
        [AvailableTo] datetime2 NULL,
        [SortOrder] int NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        CONSTRAINT [PK_SubscriptionPlans] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE TABLE [TenantFeatureOverrides] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] int NOT NULL,
        [FeatureKey] nvarchar(60) NOT NULL,
        [IsEnabled] bit NOT NULL,
        CONSTRAINT [PK_TenantFeatureOverrides] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE TABLE [SaasInvoiceItems] (
        [Id] int NOT NULL IDENTITY,
        [InvoiceId] int NOT NULL,
        [ItemType] int NOT NULL,
        [Description] nvarchar(300) NOT NULL,
        [Quantity] decimal(18,2) NOT NULL,
        [UnitPrice] decimal(18,2) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        CONSTRAINT [PK_SaasInvoiceItems] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SaasInvoiceItems_SaasInvoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [SaasInvoices] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE TABLE [SaasPayments] (
        [Id] int NOT NULL IDENTITY,
        [InvoiceId] int NOT NULL,
        [TenantId] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [Status] int NOT NULL,
        [Method] nvarchar(40) NOT NULL,
        [Provider] nvarchar(40) NOT NULL,
        [ProviderTransactionId] nvarchar(100) NULL,
        [IdempotencyKey] nvarchar(100) NULL,
        [Reference] nvarchar(200) NULL,
        [ReceiptNumber] nvarchar(30) NULL,
        [Note] nvarchar(500) NULL,
        [ReceivedAt] datetime2 NOT NULL,
        [CreatedAt] datetime2 NOT NULL,
        [RecordedBy] nvarchar(256) NULL,
        [ConfirmedBy] nvarchar(256) NULL,
        [ConfirmedAt] datetime2 NULL,
        [IsReconciled] bit NOT NULL,
        CONSTRAINT [PK_SaasPayments] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SaasPayments_SaasInvoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [SaasInvoices] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE TABLE [PlanFeatures] (
        [Id] int NOT NULL IDENTITY,
        [PlanId] int NOT NULL,
        [FeatureKey] nvarchar(60) NOT NULL,
        CONSTRAINT [PK_PlanFeatures] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PlanFeatures_SubscriptionPlans_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [SubscriptionPlans] ([Id]) ON DELETE CASCADE
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE TABLE [TenantSubscriptions] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] int NOT NULL,
        [PlanId] int NOT NULL,
        [Status] int NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [IntervalCount] int NOT NULL,
        [IntervalUnit] int NOT NULL,
        [GraceDays] int NOT NULL,
        [TaxPercent] decimal(5,2) NOT NULL,
        [DiscountPercent] decimal(5,2) NOT NULL,
        [AutoRenew] bit NOT NULL,
        [MaxUsersOverride] int NULL,
        [MaxProductsOverride] int NULL,
        [MaxWarehousesOverride] int NULL,
        [MaxStorageMbOverride] int NULL,
        [StartedAt] datetime2 NOT NULL,
        [TrialEndsAt] datetime2 NULL,
        [CurrentPeriodStart] datetime2 NULL,
        [CurrentPeriodEnd] datetime2 NULL,
        [GraceEndsAt] datetime2 NULL,
        [CancelAtPeriodEnd] bit NOT NULL,
        [CancelledAt] datetime2 NULL,
        [SuspendedAt] datetime2 NULL,
        [PendingPlanId] int NULL,
        [CreatedAt] datetime2 NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_TenantSubscriptions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_TenantSubscriptions_SubscriptionPlans_PlanId] FOREIGN KEY ([PlanId]) REFERENCES [SubscriptionPlans] ([Id]) ON DELETE NO ACTION,
        CONSTRAINT [FK_TenantSubscriptions_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PlanFeatures_PlanId_FeatureKey] ON [PlanFeatures] ([PlanId], [FeatureKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE INDEX [IX_SaasAuditLogs_TenantId_At] ON [SaasAuditLogs] ([TenantId], [At]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE INDEX [IX_SaasInvoiceItems_InvoiceId] ON [SaasInvoiceItems] ([InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SaasInvoices_InvoiceNumber] ON [SaasInvoices] ([InvoiceNumber]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_SaasInvoices_SubscriptionId_PeriodStart] ON [SaasInvoices] ([SubscriptionId], [PeriodStart]) WHERE [Status] <> 5 AND [SubscriptionId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE INDEX [IX_SaasInvoices_TenantId_Status] ON [SaasInvoices] ([TenantId], [Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE INDEX [IX_SaasNotifications_Status_Channel] ON [SaasNotifications] ([Status], [Channel]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SaasNotifications_TenantId_DedupeKey_Channel] ON [SaasNotifications] ([TenantId], [DedupeKey], [Channel]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_SaasPayments_IdempotencyKey] ON [SaasPayments] ([IdempotencyKey]) WHERE [IdempotencyKey] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE INDEX [IX_SaasPayments_InvoiceId] ON [SaasPayments] ([InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_SaasPayments_Provider_ProviderTransactionId] ON [SaasPayments] ([Provider], [ProviderTransactionId]) WHERE [ProviderTransactionId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE INDEX [IX_SaasPayments_TenantId] ON [SaasPayments] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE INDEX [IX_SubscriptionHistories_TenantId_CreatedAt] ON [SubscriptionHistories] ([TenantId], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SubscriptionPlans_Code] ON [SubscriptionPlans] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TenantFeatureOverrides_TenantId_FeatureKey] ON [TenantFeatureOverrides] ([TenantId], [FeatureKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE INDEX [IX_TenantSubscriptions_PlanId] ON [TenantSubscriptions] ([PlanId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE INDEX [IX_TenantSubscriptions_Status] ON [TenantSubscriptions] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TenantSubscriptions_TenantId] ON [TenantSubscriptions] ([TenantId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010083109_AddSaasBilling'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261010083109_AddSaasBilling', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010084840_AddPaymentGateway'
)
BEGIN
    ALTER TABLE [SaasPayments] ADD [RefundedAmount] decimal(18,2) NOT NULL DEFAULT 0.0;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010084840_AddPaymentGateway'
)
BEGIN
    CREATE TABLE [PaymentAttempts] (
        [Id] int NOT NULL IDENTITY,
        [Reference] nvarchar(30) NOT NULL,
        [InvoiceId] int NOT NULL,
        [TenantId] int NOT NULL,
        [Provider] nvarchar(40) NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Currency] nvarchar(3) NOT NULL,
        [Status] int NOT NULL,
        [SessionKey] nvarchar(100) NULL,
        [GatewayUrl] nvarchar(600) NULL,
        [ProviderValId] nvarchar(100) NULL,
        [ProviderTransactionId] nvarchar(100) NULL,
        [FailureReason] nvarchar(500) NULL,
        [SavedPaymentId] int NULL,
        [InitiatedBy] nvarchar(256) NULL,
        [IpAddress] nvarchar(60) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [ExpiresAt] datetime2 NOT NULL,
        [CompletedAt] datetime2 NULL,
        [LastCheckedAt] datetime2 NULL,
        [RowVersion] rowversion NULL,
        CONSTRAINT [PK_PaymentAttempts] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PaymentAttempts_SaasInvoices_InvoiceId] FOREIGN KEY ([InvoiceId]) REFERENCES [SaasInvoices] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010084840_AddPaymentGateway'
)
BEGIN
    CREATE TABLE [PaymentProviders] (
        [Id] int NOT NULL IDENTITY,
        [Code] nvarchar(40) NOT NULL,
        [DisplayName] nvarchar(120) NOT NULL,
        [IsEnabled] bit NOT NULL,
        [SupportedMethods] nvarchar(300) NULL,
        [SortOrder] int NOT NULL,
        [UpdatedAt] datetime2 NULL,
        [UpdatedBy] nvarchar(256) NULL,
        CONSTRAINT [PK_PaymentProviders] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010084840_AddPaymentGateway'
)
BEGIN
    CREATE TABLE [PaymentWebhookEvents] (
        [Id] bigint NOT NULL IDENTITY,
        [Provider] nvarchar(40) NOT NULL,
        [Source] nvarchar(20) NOT NULL,
        [EventKey] nvarchar(80) NOT NULL,
        [AttemptReference] nvarchar(30) NULL,
        [ReportedStatus] nvarchar(30) NULL,
        [SignatureValid] bit NULL,
        [Outcome] nvarchar(60) NULL,
        [Payload] nvarchar(4000) NULL,
        [ReceiveCount] int NOT NULL,
        [ReceivedAt] datetime2 NOT NULL,
        [ProcessedAt] datetime2 NULL,
        CONSTRAINT [PK_PaymentWebhookEvents] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010084840_AddPaymentGateway'
)
BEGIN
    CREATE TABLE [SaasRefunds] (
        [Id] int NOT NULL IDENTITY,
        [PaymentId] int NOT NULL,
        [InvoiceId] int NOT NULL,
        [TenantId] int NOT NULL,
        [Amount] decimal(18,2) NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [Status] int NOT NULL,
        [RefundTransId] nvarchar(30) NULL,
        [ProviderRefundRef] nvarchar(80) NULL,
        [FailureReason] nvarchar(500) NULL,
        [CreatedAt] datetime2 NOT NULL,
        [CreatedBy] nvarchar(256) NULL,
        [CompletedAt] datetime2 NULL,
        CONSTRAINT [PK_SaasRefunds] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010084840_AddPaymentGateway'
)
BEGIN
    CREATE INDEX [IX_PaymentAttempts_InvoiceId] ON [PaymentAttempts] ([InvoiceId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010084840_AddPaymentGateway'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PaymentAttempts_Reference] ON [PaymentAttempts] ([Reference]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010084840_AddPaymentGateway'
)
BEGIN
    CREATE INDEX [IX_PaymentAttempts_Status_CreatedAt] ON [PaymentAttempts] ([Status], [CreatedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010084840_AddPaymentGateway'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PaymentProviders_Code] ON [PaymentProviders] ([Code]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010084840_AddPaymentGateway'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PaymentWebhookEvents_Provider_EventKey] ON [PaymentWebhookEvents] ([Provider], [EventKey]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010084840_AddPaymentGateway'
)
BEGIN
    CREATE INDEX [IX_SaasRefunds_PaymentId] ON [SaasRefunds] ([PaymentId]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010084840_AddPaymentGateway'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_SaasRefunds_RefundTransId] ON [SaasRefunds] ([RefundTransId]) WHERE [RefundTransId] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010084840_AddPaymentGateway'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261010084840_AddPaymentGateway', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010090704_AddTenantPurgedAt'
)
BEGIN
    ALTER TABLE [Tenants] ADD [PurgedAt] datetime2 NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010090704_AddTenantPurgedAt'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261010090704_AddTenantPurgedAt', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010093815_AddTenantAccessConfig'
)
BEGIN
    ALTER TABLE [Tenants] ADD [PortalHost] nvarchar(253) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010093815_AddTenantAccessConfig'
)
BEGIN
    ALTER TABLE [Tenants] ADD [PortalUrl] nvarchar(300) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010093815_AddTenantAccessConfig'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [MustChangePassword] bit NOT NULL DEFAULT CAST(0 AS bit);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010093815_AddTenantAccessConfig'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_Tenants_PortalHost] ON [Tenants] ([PortalHost]) WHERE [PortalHost] IS NOT NULL');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010093815_AddTenantAccessConfig'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261010093815_AddTenantAccessConfig', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010124236_AddUserPreferredLanguage'
)
BEGIN
    ALTER TABLE [AspNetUsers] ADD [PreferredLanguage] nvarchar(max) NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010124236_AddUserPreferredLanguage'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261010124236_AddUserPreferredLanguage', N'10.0.11');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010133912_AddDataExportAndAgreement'
)
BEGIN
    CREATE TABLE [DataAgreementAcceptances] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] int NOT NULL,
        [SubscriptionId] int NULL,
        [Version] nvarchar(40) NOT NULL,
        [Context] nvarchar(30) NOT NULL,
        [AcceptedByUserId] nvarchar(450) NULL,
        [AcceptedBy] nvarchar(256) NULL,
        [AcceptedAt] datetime2 NOT NULL,
        [IpAddress] nvarchar(60) NULL,
        CONSTRAINT [PK_DataAgreementAcceptances] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DataAgreementAcceptances_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010133912_AddDataExportAndAgreement'
)
BEGIN
    CREATE TABLE [DataExportRequests] (
        [Id] int NOT NULL IDENTITY,
        [TenantId] int NOT NULL,
        [Format] int NOT NULL,
        [Reason] int NOT NULL,
        [Notes] nvarchar(1000) NULL,
        [Status] int NOT NULL,
        [RequestedByUserId] nvarchar(450) NULL,
        [RequestedBy] nvarchar(256) NULL,
        [RequestedAt] datetime2 NOT NULL,
        [ProcessedBy] nvarchar(256) NULL,
        [ProcessedAt] datetime2 NULL,
        [RejectReason] nvarchar(500) NULL,
        [FileName] nvarchar(200) NULL,
        [FileSize] bigint NOT NULL,
        [TableCount] int NOT NULL,
        [RowCount] bigint NOT NULL,
        [ExpiresAt] datetime2 NULL,
        [DownloadedAt] datetime2 NULL,
        [Error] nvarchar(500) NULL,
        [AgreementVersion] nvarchar(40) NULL,
        CONSTRAINT [PK_DataExportRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_DataExportRequests_Tenants_TenantId] FOREIGN KEY ([TenantId]) REFERENCES [Tenants] ([Id]) ON DELETE NO ACTION
    );
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010133912_AddDataExportAndAgreement'
)
BEGIN
    CREATE INDEX [IX_DataAgreementAcceptances_TenantId_Version] ON [DataAgreementAcceptances] ([TenantId], [Version]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010133912_AddDataExportAndAgreement'
)
BEGIN
    CREATE INDEX [IX_DataExportRequests_Status] ON [DataExportRequests] ([Status]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010133912_AddDataExportAndAgreement'
)
BEGIN
    CREATE INDEX [IX_DataExportRequests_TenantId_RequestedAt] ON [DataExportRequests] ([TenantId], [RequestedAt]);
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261010133912_AddDataExportAndAgreement'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261010133912_AddDataExportAndAgreement', N'10.0.11');
END;

COMMIT;
GO

