BEGIN TRANSACTION;
DROP TABLE [DataAgreementAcceptances];

DROP TABLE [DataExportRequests];

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261010133912_AddDataExportAndAgreement';

COMMIT;
GO

BEGIN TRANSACTION;
DECLARE @var nvarchar(max);
SELECT @var = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AspNetUsers]') AND [c].[name] = N'PreferredLanguage');
IF @var IS NOT NULL EXEC(N'ALTER TABLE [AspNetUsers] DROP CONSTRAINT ' + @var + ';');
ALTER TABLE [AspNetUsers] DROP COLUMN [PreferredLanguage];

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261010124236_AddUserPreferredLanguage';

COMMIT;
GO

BEGIN TRANSACTION;
DROP INDEX [IX_Tenants_PortalHost] ON [Tenants];

DECLARE @var1 nvarchar(max);
SELECT @var1 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Tenants]') AND [c].[name] = N'PortalHost');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Tenants] DROP CONSTRAINT ' + @var1 + ';');
ALTER TABLE [Tenants] DROP COLUMN [PortalHost];

DECLARE @var2 nvarchar(max);
SELECT @var2 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Tenants]') AND [c].[name] = N'PortalUrl');
IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [Tenants] DROP CONSTRAINT ' + @var2 + ';');
ALTER TABLE [Tenants] DROP COLUMN [PortalUrl];

DECLARE @var3 nvarchar(max);
SELECT @var3 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AspNetUsers]') AND [c].[name] = N'MustChangePassword');
IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [AspNetUsers] DROP CONSTRAINT ' + @var3 + ';');
ALTER TABLE [AspNetUsers] DROP COLUMN [MustChangePassword];

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261010093815_AddTenantAccessConfig';

COMMIT;
GO

BEGIN TRANSACTION;
DECLARE @var4 nvarchar(max);
SELECT @var4 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Tenants]') AND [c].[name] = N'PurgedAt');
IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [Tenants] DROP CONSTRAINT ' + @var4 + ';');
ALTER TABLE [Tenants] DROP COLUMN [PurgedAt];

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261010090704_AddTenantPurgedAt';

COMMIT;
GO

BEGIN TRANSACTION;
DROP TABLE [PaymentAttempts];

DROP TABLE [PaymentProviders];

DROP TABLE [PaymentWebhookEvents];

DROP TABLE [SaasRefunds];

DECLARE @var5 nvarchar(max);
SELECT @var5 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[SaasPayments]') AND [c].[name] = N'RefundedAmount');
IF @var5 IS NOT NULL EXEC(N'ALTER TABLE [SaasPayments] DROP CONSTRAINT ' + @var5 + ';');
ALTER TABLE [SaasPayments] DROP COLUMN [RefundedAmount];

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261010084840_AddPaymentGateway';

COMMIT;
GO

BEGIN TRANSACTION;
DROP TABLE [PlanFeatures];

DROP TABLE [PlatformSettings];

DROP TABLE [SaasAuditLogs];

DROP TABLE [SaasInvoiceItems];

DROP TABLE [SaasNotifications];

DROP TABLE [SaasPayments];

DROP TABLE [SubscriptionHistories];

DROP TABLE [TenantFeatureOverrides];

DROP TABLE [TenantSubscriptions];

DROP TABLE [SaasInvoices];

DROP TABLE [SubscriptionPlans];

DECLARE @var6 nvarchar(max);
SELECT @var6 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Tenants]') AND [c].[name] = N'BillingEmail');
IF @var6 IS NOT NULL EXEC(N'ALTER TABLE [Tenants] DROP CONSTRAINT ' + @var6 + ';');
ALTER TABLE [Tenants] DROP COLUMN [BillingEmail];

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261010083109_AddSaasBilling';

COMMIT;
GO

BEGIN TRANSACTION;
ALTER TABLE [Accounts] DROP CONSTRAINT [FK_Accounts_Tenants_TenantId];

ALTER TABLE [AspNetUsers] DROP CONSTRAINT [FK_AspNetUsers_Tenants_TenantId];

ALTER TABLE [Categories] DROP CONSTRAINT [FK_Categories_Tenants_TenantId];

ALTER TABLE [CompanySettings] DROP CONSTRAINT [FK_CompanySettings_Tenants_TenantId];

ALTER TABLE [Customers] DROP CONSTRAINT [FK_Customers_Tenants_TenantId];

ALTER TABLE [JournalEntries] DROP CONSTRAINT [FK_JournalEntries_Tenants_TenantId];

ALTER TABLE [JournalEntryLines] DROP CONSTRAINT [FK_JournalEntryLines_Tenants_TenantId];

ALTER TABLE [Payments] DROP CONSTRAINT [FK_Payments_Tenants_TenantId];

ALTER TABLE [PettyCashEntries] DROP CONSTRAINT [FK_PettyCashEntries_Tenants_TenantId];

ALTER TABLE [PettyCashNames] DROP CONSTRAINT [FK_PettyCashNames_Tenants_TenantId];

ALTER TABLE [ProductBatches] DROP CONSTRAINT [FK_ProductBatches_Tenants_TenantId];

ALTER TABLE [ProductPriceHistories] DROP CONSTRAINT [FK_ProductPriceHistories_Tenants_TenantId];

ALTER TABLE [Products] DROP CONSTRAINT [FK_Products_Tenants_TenantId];

ALTER TABLE [ProductWarehouseStocks] DROP CONSTRAINT [FK_ProductWarehouseStocks_Tenants_TenantId];

ALTER TABLE [PurchaseInvoiceItems] DROP CONSTRAINT [FK_PurchaseInvoiceItems_Tenants_TenantId];

ALTER TABLE [PurchaseInvoices] DROP CONSTRAINT [FK_PurchaseInvoices_Tenants_TenantId];

ALTER TABLE [PurchaseReturnItems] DROP CONSTRAINT [FK_PurchaseReturnItems_Tenants_TenantId];

ALTER TABLE [PurchaseReturns] DROP CONSTRAINT [FK_PurchaseReturns_Tenants_TenantId];

ALTER TABLE [SalesInvoiceItems] DROP CONSTRAINT [FK_SalesInvoiceItems_Tenants_TenantId];

ALTER TABLE [SalesInvoices] DROP CONSTRAINT [FK_SalesInvoices_Tenants_TenantId];

ALTER TABLE [SalesReturnItems] DROP CONSTRAINT [FK_SalesReturnItems_Tenants_TenantId];

ALTER TABLE [SalesReturns] DROP CONSTRAINT [FK_SalesReturns_Tenants_TenantId];

ALTER TABLE [StockAdjustmentItems] DROP CONSTRAINT [FK_StockAdjustmentItems_Tenants_TenantId];

ALTER TABLE [StockAdjustments] DROP CONSTRAINT [FK_StockAdjustments_Tenants_TenantId];

ALTER TABLE [StockTransactions] DROP CONSTRAINT [FK_StockTransactions_Tenants_TenantId];

ALTER TABLE [StockTransferItems] DROP CONSTRAINT [FK_StockTransferItems_Tenants_TenantId];

ALTER TABLE [StockTransferPurchaseInvoices] DROP CONSTRAINT [FK_StockTransferPurchaseInvoices_Tenants_TenantId];

ALTER TABLE [StockTransfers] DROP CONSTRAINT [FK_StockTransfers_Tenants_TenantId];

ALTER TABLE [Suppliers] DROP CONSTRAINT [FK_Suppliers_Tenants_TenantId];

ALTER TABLE [UnitOfMeasures] DROP CONSTRAINT [FK_UnitOfMeasures_Tenants_TenantId];

ALTER TABLE [UserPermissions] DROP CONSTRAINT [FK_UserPermissions_Tenants_TenantId];

ALTER TABLE [UserWarehouses] DROP CONSTRAINT [FK_UserWarehouses_Tenants_TenantId];

ALTER TABLE [Warehouses] DROP CONSTRAINT [FK_Warehouses_Tenants_TenantId];

DROP TABLE [Tenants];

DROP INDEX [IX_Warehouses_TenantId] ON [Warehouses];

DROP INDEX [IX_Warehouses_TenantId_Code] ON [Warehouses];

DROP INDEX [IX_UserWarehouses_TenantId] ON [UserWarehouses];

DROP INDEX [IX_UserPermissions_TenantId] ON [UserPermissions];

DROP INDEX [IX_UnitOfMeasures_TenantId] ON [UnitOfMeasures];

DROP INDEX [IX_Suppliers_TenantId] ON [Suppliers];

DROP INDEX [IX_StockTransfers_TenantId] ON [StockTransfers];

DROP INDEX [IX_StockTransfers_TenantId_TransferNumber] ON [StockTransfers];

DROP INDEX [IX_StockTransferPurchaseInvoices_TenantId] ON [StockTransferPurchaseInvoices];

DROP INDEX [IX_StockTransferItems_TenantId] ON [StockTransferItems];

DROP INDEX [IX_StockTransactions_TenantId] ON [StockTransactions];

DROP INDEX [IX_StockAdjustments_TenantId] ON [StockAdjustments];

DROP INDEX [IX_StockAdjustments_TenantId_AdjustmentNumber] ON [StockAdjustments];

DROP INDEX [IX_StockAdjustmentItems_TenantId] ON [StockAdjustmentItems];

DROP INDEX [IX_SalesReturns_TenantId] ON [SalesReturns];

DROP INDEX [IX_SalesReturns_TenantId_ReturnNumber] ON [SalesReturns];

DROP INDEX [IX_SalesReturnItems_TenantId] ON [SalesReturnItems];

DROP INDEX [IX_SalesInvoices_TenantId] ON [SalesInvoices];

DROP INDEX [IX_SalesInvoices_TenantId_InvoiceNumber] ON [SalesInvoices];

DROP INDEX [IX_SalesInvoiceItems_TenantId] ON [SalesInvoiceItems];

DROP INDEX [IX_PurchaseReturns_TenantId] ON [PurchaseReturns];

DROP INDEX [IX_PurchaseReturns_TenantId_ReturnNumber] ON [PurchaseReturns];

DROP INDEX [IX_PurchaseReturnItems_TenantId] ON [PurchaseReturnItems];

DROP INDEX [IX_PurchaseInvoices_TenantId] ON [PurchaseInvoices];

DROP INDEX [IX_PurchaseInvoices_TenantId_InvoiceNumber] ON [PurchaseInvoices];

DROP INDEX [IX_PurchaseInvoiceItems_TenantId] ON [PurchaseInvoiceItems];

DROP INDEX [IX_ProductWarehouseStocks_TenantId] ON [ProductWarehouseStocks];

DROP INDEX [IX_Products_TenantId] ON [Products];

DROP INDEX [IX_Products_TenantId_Sku] ON [Products];

DROP INDEX [IX_ProductPriceHistories_TenantId] ON [ProductPriceHistories];

DROP INDEX [IX_ProductBatches_TenantId] ON [ProductBatches];

DROP INDEX [IX_PettyCashNames_TenantId] ON [PettyCashNames];

DROP INDEX [IX_PettyCashNames_TenantId_Name] ON [PettyCashNames];

DROP INDEX [IX_PettyCashEntries_TenantId] ON [PettyCashEntries];

DROP INDEX [IX_Payments_TenantId] ON [Payments];

DROP INDEX [IX_JournalEntryLines_TenantId] ON [JournalEntryLines];

DROP INDEX [IX_JournalEntries_TenantId] ON [JournalEntries];

DROP INDEX [IX_JournalEntries_TenantId_EntryNumber] ON [JournalEntries];

DROP INDEX [IX_Customers_TenantId] ON [Customers];

DROP INDEX [IX_CompanySettings_TenantId] ON [CompanySettings];

DROP INDEX [IX_Categories_TenantId] ON [Categories];

DROP INDEX [IX_AspNetUsers_TenantId] ON [AspNetUsers];

DROP INDEX [IX_Accounts_TenantId] ON [Accounts];

DROP INDEX [IX_Accounts_TenantId_Code] ON [Accounts];

DECLARE @var7 nvarchar(max);
SELECT @var7 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Warehouses]') AND [c].[name] = N'TenantId');
IF @var7 IS NOT NULL EXEC(N'ALTER TABLE [Warehouses] DROP CONSTRAINT ' + @var7 + ';');
ALTER TABLE [Warehouses] DROP COLUMN [TenantId];

DECLARE @var8 nvarchar(max);
SELECT @var8 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[UserWarehouses]') AND [c].[name] = N'TenantId');
IF @var8 IS NOT NULL EXEC(N'ALTER TABLE [UserWarehouses] DROP CONSTRAINT ' + @var8 + ';');
ALTER TABLE [UserWarehouses] DROP COLUMN [TenantId];

DECLARE @var9 nvarchar(max);
SELECT @var9 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[UserPermissions]') AND [c].[name] = N'TenantId');
IF @var9 IS NOT NULL EXEC(N'ALTER TABLE [UserPermissions] DROP CONSTRAINT ' + @var9 + ';');
ALTER TABLE [UserPermissions] DROP COLUMN [TenantId];

DECLARE @var10 nvarchar(max);
SELECT @var10 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[UnitOfMeasures]') AND [c].[name] = N'TenantId');
IF @var10 IS NOT NULL EXEC(N'ALTER TABLE [UnitOfMeasures] DROP CONSTRAINT ' + @var10 + ';');
ALTER TABLE [UnitOfMeasures] DROP COLUMN [TenantId];

DECLARE @var11 nvarchar(max);
SELECT @var11 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Suppliers]') AND [c].[name] = N'TenantId');
IF @var11 IS NOT NULL EXEC(N'ALTER TABLE [Suppliers] DROP CONSTRAINT ' + @var11 + ';');
ALTER TABLE [Suppliers] DROP COLUMN [TenantId];

DECLARE @var12 nvarchar(max);
SELECT @var12 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[StockTransfers]') AND [c].[name] = N'TenantId');
IF @var12 IS NOT NULL EXEC(N'ALTER TABLE [StockTransfers] DROP CONSTRAINT ' + @var12 + ';');
ALTER TABLE [StockTransfers] DROP COLUMN [TenantId];

DECLARE @var13 nvarchar(max);
SELECT @var13 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[StockTransferPurchaseInvoices]') AND [c].[name] = N'TenantId');
IF @var13 IS NOT NULL EXEC(N'ALTER TABLE [StockTransferPurchaseInvoices] DROP CONSTRAINT ' + @var13 + ';');
ALTER TABLE [StockTransferPurchaseInvoices] DROP COLUMN [TenantId];

DECLARE @var14 nvarchar(max);
SELECT @var14 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[StockTransferItems]') AND [c].[name] = N'TenantId');
IF @var14 IS NOT NULL EXEC(N'ALTER TABLE [StockTransferItems] DROP CONSTRAINT ' + @var14 + ';');
ALTER TABLE [StockTransferItems] DROP COLUMN [TenantId];

DECLARE @var15 nvarchar(max);
SELECT @var15 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[StockTransactions]') AND [c].[name] = N'TenantId');
IF @var15 IS NOT NULL EXEC(N'ALTER TABLE [StockTransactions] DROP CONSTRAINT ' + @var15 + ';');
ALTER TABLE [StockTransactions] DROP COLUMN [TenantId];

DECLARE @var16 nvarchar(max);
SELECT @var16 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[StockAdjustments]') AND [c].[name] = N'TenantId');
IF @var16 IS NOT NULL EXEC(N'ALTER TABLE [StockAdjustments] DROP CONSTRAINT ' + @var16 + ';');
ALTER TABLE [StockAdjustments] DROP COLUMN [TenantId];

DECLARE @var17 nvarchar(max);
SELECT @var17 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[StockAdjustmentItems]') AND [c].[name] = N'TenantId');
IF @var17 IS NOT NULL EXEC(N'ALTER TABLE [StockAdjustmentItems] DROP CONSTRAINT ' + @var17 + ';');
ALTER TABLE [StockAdjustmentItems] DROP COLUMN [TenantId];

DECLARE @var18 nvarchar(max);
SELECT @var18 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[SalesReturns]') AND [c].[name] = N'TenantId');
IF @var18 IS NOT NULL EXEC(N'ALTER TABLE [SalesReturns] DROP CONSTRAINT ' + @var18 + ';');
ALTER TABLE [SalesReturns] DROP COLUMN [TenantId];

DECLARE @var19 nvarchar(max);
SELECT @var19 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[SalesReturnItems]') AND [c].[name] = N'TenantId');
IF @var19 IS NOT NULL EXEC(N'ALTER TABLE [SalesReturnItems] DROP CONSTRAINT ' + @var19 + ';');
ALTER TABLE [SalesReturnItems] DROP COLUMN [TenantId];

DECLARE @var20 nvarchar(max);
SELECT @var20 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[SalesInvoices]') AND [c].[name] = N'TenantId');
IF @var20 IS NOT NULL EXEC(N'ALTER TABLE [SalesInvoices] DROP CONSTRAINT ' + @var20 + ';');
ALTER TABLE [SalesInvoices] DROP COLUMN [TenantId];

DECLARE @var21 nvarchar(max);
SELECT @var21 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[SalesInvoiceItems]') AND [c].[name] = N'TenantId');
IF @var21 IS NOT NULL EXEC(N'ALTER TABLE [SalesInvoiceItems] DROP CONSTRAINT ' + @var21 + ';');
ALTER TABLE [SalesInvoiceItems] DROP COLUMN [TenantId];

DECLARE @var22 nvarchar(max);
SELECT @var22 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PurchaseReturns]') AND [c].[name] = N'TenantId');
IF @var22 IS NOT NULL EXEC(N'ALTER TABLE [PurchaseReturns] DROP CONSTRAINT ' + @var22 + ';');
ALTER TABLE [PurchaseReturns] DROP COLUMN [TenantId];

DECLARE @var23 nvarchar(max);
SELECT @var23 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PurchaseReturnItems]') AND [c].[name] = N'TenantId');
IF @var23 IS NOT NULL EXEC(N'ALTER TABLE [PurchaseReturnItems] DROP CONSTRAINT ' + @var23 + ';');
ALTER TABLE [PurchaseReturnItems] DROP COLUMN [TenantId];

DECLARE @var24 nvarchar(max);
SELECT @var24 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PurchaseInvoices]') AND [c].[name] = N'TenantId');
IF @var24 IS NOT NULL EXEC(N'ALTER TABLE [PurchaseInvoices] DROP CONSTRAINT ' + @var24 + ';');
ALTER TABLE [PurchaseInvoices] DROP COLUMN [TenantId];

DECLARE @var25 nvarchar(max);
SELECT @var25 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PurchaseInvoiceItems]') AND [c].[name] = N'TenantId');
IF @var25 IS NOT NULL EXEC(N'ALTER TABLE [PurchaseInvoiceItems] DROP CONSTRAINT ' + @var25 + ';');
ALTER TABLE [PurchaseInvoiceItems] DROP COLUMN [TenantId];

DECLARE @var26 nvarchar(max);
SELECT @var26 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ProductWarehouseStocks]') AND [c].[name] = N'TenantId');
IF @var26 IS NOT NULL EXEC(N'ALTER TABLE [ProductWarehouseStocks] DROP CONSTRAINT ' + @var26 + ';');
ALTER TABLE [ProductWarehouseStocks] DROP COLUMN [TenantId];

DECLARE @var27 nvarchar(max);
SELECT @var27 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Products]') AND [c].[name] = N'TenantId');
IF @var27 IS NOT NULL EXEC(N'ALTER TABLE [Products] DROP CONSTRAINT ' + @var27 + ';');
ALTER TABLE [Products] DROP COLUMN [TenantId];

DECLARE @var28 nvarchar(max);
SELECT @var28 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ProductPriceHistories]') AND [c].[name] = N'TenantId');
IF @var28 IS NOT NULL EXEC(N'ALTER TABLE [ProductPriceHistories] DROP CONSTRAINT ' + @var28 + ';');
ALTER TABLE [ProductPriceHistories] DROP COLUMN [TenantId];

DECLARE @var29 nvarchar(max);
SELECT @var29 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[ProductBatches]') AND [c].[name] = N'TenantId');
IF @var29 IS NOT NULL EXEC(N'ALTER TABLE [ProductBatches] DROP CONSTRAINT ' + @var29 + ';');
ALTER TABLE [ProductBatches] DROP COLUMN [TenantId];

DECLARE @var30 nvarchar(max);
SELECT @var30 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PettyCashNames]') AND [c].[name] = N'TenantId');
IF @var30 IS NOT NULL EXEC(N'ALTER TABLE [PettyCashNames] DROP CONSTRAINT ' + @var30 + ';');
ALTER TABLE [PettyCashNames] DROP COLUMN [TenantId];

DECLARE @var31 nvarchar(max);
SELECT @var31 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[PettyCashEntries]') AND [c].[name] = N'TenantId');
IF @var31 IS NOT NULL EXEC(N'ALTER TABLE [PettyCashEntries] DROP CONSTRAINT ' + @var31 + ';');
ALTER TABLE [PettyCashEntries] DROP COLUMN [TenantId];

DECLARE @var32 nvarchar(max);
SELECT @var32 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Payments]') AND [c].[name] = N'TenantId');
IF @var32 IS NOT NULL EXEC(N'ALTER TABLE [Payments] DROP CONSTRAINT ' + @var32 + ';');
ALTER TABLE [Payments] DROP COLUMN [TenantId];

DECLARE @var33 nvarchar(max);
SELECT @var33 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[JournalEntryLines]') AND [c].[name] = N'TenantId');
IF @var33 IS NOT NULL EXEC(N'ALTER TABLE [JournalEntryLines] DROP CONSTRAINT ' + @var33 + ';');
ALTER TABLE [JournalEntryLines] DROP COLUMN [TenantId];

DECLARE @var34 nvarchar(max);
SELECT @var34 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[JournalEntries]') AND [c].[name] = N'TenantId');
IF @var34 IS NOT NULL EXEC(N'ALTER TABLE [JournalEntries] DROP CONSTRAINT ' + @var34 + ';');
ALTER TABLE [JournalEntries] DROP COLUMN [TenantId];

DECLARE @var35 nvarchar(max);
SELECT @var35 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Customers]') AND [c].[name] = N'TenantId');
IF @var35 IS NOT NULL EXEC(N'ALTER TABLE [Customers] DROP CONSTRAINT ' + @var35 + ';');
ALTER TABLE [Customers] DROP COLUMN [TenantId];

DECLARE @var36 nvarchar(max);
SELECT @var36 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[CompanySettings]') AND [c].[name] = N'TenantId');
IF @var36 IS NOT NULL EXEC(N'ALTER TABLE [CompanySettings] DROP CONSTRAINT ' + @var36 + ';');
ALTER TABLE [CompanySettings] DROP COLUMN [TenantId];

DECLARE @var37 nvarchar(max);
SELECT @var37 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Categories]') AND [c].[name] = N'TenantId');
IF @var37 IS NOT NULL EXEC(N'ALTER TABLE [Categories] DROP CONSTRAINT ' + @var37 + ';');
ALTER TABLE [Categories] DROP COLUMN [TenantId];

DECLARE @var38 nvarchar(max);
SELECT @var38 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[AspNetUsers]') AND [c].[name] = N'TenantId');
IF @var38 IS NOT NULL EXEC(N'ALTER TABLE [AspNetUsers] DROP CONSTRAINT ' + @var38 + ';');
ALTER TABLE [AspNetUsers] DROP COLUMN [TenantId];

DECLARE @var39 nvarchar(max);
SELECT @var39 = QUOTENAME([d].[name])
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Accounts]') AND [c].[name] = N'TenantId');
IF @var39 IS NOT NULL EXEC(N'ALTER TABLE [Accounts] DROP CONSTRAINT ' + @var39 + ';');
ALTER TABLE [Accounts] DROP COLUMN [TenantId];

CREATE UNIQUE INDEX [IX_Warehouses_Code] ON [Warehouses] ([Code]);

CREATE UNIQUE INDEX [IX_StockTransfers_TransferNumber] ON [StockTransfers] ([TransferNumber]);

CREATE UNIQUE INDEX [IX_StockAdjustments_AdjustmentNumber] ON [StockAdjustments] ([AdjustmentNumber]);

CREATE UNIQUE INDEX [IX_SalesReturns_ReturnNumber] ON [SalesReturns] ([ReturnNumber]);

CREATE UNIQUE INDEX [IX_SalesInvoices_InvoiceNumber] ON [SalesInvoices] ([InvoiceNumber]);

CREATE UNIQUE INDEX [IX_PurchaseReturns_ReturnNumber] ON [PurchaseReturns] ([ReturnNumber]);

CREATE UNIQUE INDEX [IX_PurchaseInvoices_InvoiceNumber] ON [PurchaseInvoices] ([InvoiceNumber]);

CREATE UNIQUE INDEX [IX_Products_Sku] ON [Products] ([Sku]);

CREATE UNIQUE INDEX [IX_PettyCashNames_Name] ON [PettyCashNames] ([Name]);

CREATE UNIQUE INDEX [IX_JournalEntries_EntryNumber] ON [JournalEntries] ([EntryNumber]);

CREATE UNIQUE INDEX [IX_Accounts_Code] ON [Accounts] ([Code]);

DELETE FROM [__EFMigrationsHistory]
WHERE [MigrationId] = N'20261010081541_AddMultiTenancy';

COMMIT;
GO

