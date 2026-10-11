# Changes to existing files and database objects

Principle: no existing inventory, purchase, sales, accounting or reporting logic was rewritten. Existing files were touched only for tenant isolation, plan enforcement or SaaS plumbing. Everything else is new code.

## 1. Existing source files that were modified

| File | Change |
|---|---|
| `Program.cs` | Registers tenant context, billing/payment services, background billing job, SSLCOMMERZ HTTP client (request logging off), rate limiter policies (login, payment, sign-up), tenant middleware, secure auth cookie outside Development, optional trusted-proxy forwarded headers, `/health`. |
| `Data/ApplicationDbContext.cs` | Shadow `TenantId` + global query filter + FK on 32 business entities; per-tenant unique indexes (replacing 11 global ones); `SaveChanges` stamps/guards tenant; DbSets and mappings for the SaaS tables. Business entity classes themselves are **unchanged** (the tenant column is a shadow property). |
| `Data/DbInitializer.cs` (untracked in git before) | Runs seeding inside the default tenant; platform roles/admin and example plans seeded; `Database:AutoMigrate` switch; demo logins and sample data only when `Seed:DemoData` is true (default: Development only). Existing chart-of-accounts seeding made reusable (`internal`). |
| `Models/Identity/ApplicationUser.cs` | `int? TenantId` (null = platform staff); `MustChangePassword`. |
| `Models/Identity/ApplicationRole.cs` | Constants `PlatformAdmin`, `PlatformBilling`, `PlatformStaff` (not part of `Roles.All`, so tenants can never assign them). |
| `Controllers/AccountController.cs` | Sign-in adds the `TenantId` claim, loads warehouses inside the tenant, refuses disabled/terminated tenants and accounts with neither tenant nor platform role; login rate limit; tenant-branded sign-in (`/t/{code}` or the tenant's own domain) limited to that tenant's users; forced password change after an admin reset. |
| `Controllers/UsersController.cs`, `PermissionsController.cs` | User lists/lookups limited to the tenant (`TenantUsers`); new users get the tenant id; only tenant roles can be assigned; plan user-limit on create. |
| `Controllers/SalesInvoicesController.cs` | "Served by" lookup limited to the tenant's users. |
| `Controllers/DatabaseBackupController.cs` | Authorization changed from Admin/Manager to **PlatformAdmin** (whole-database backup spans all tenants). Menu entry moved to the platform menu. |
| `Controllers/ProductsController.cs`, `WarehousesController.cs` | `[EnforceLimit]` on the create actions (products incl. quick-add, warehouses). |
| `Controllers/AccountingController`, `ChatbotController`, `CustomersController`, `PettyCashController`, `PurchaseInvoicesController`, `PurchaseReturnsController`, `ReportsController`, `SalesInvoicesController`, `SalesReturnsController`, `StockAdjustmentsController`, `StockTransfersController`, `SuppliersController` | One attribute each: `[RequireFeature(...)]` (plan-based module gate) plus the `using`s. |
| `Extensions/ClaimsPrincipalExtensions.cs` | `GetTenantId()`, `IsPlatformStaff()`. |
| `Hubs/NotificationsHub.cs`, `Services/ActivityNotifier.cs` | SignalR group names prefixed with the tenant id; connections without a tenant are refused. |
| `Services/CompanySettingsService.cs` | Cache key per tenant. |
| `Services/ProductPriceService.cs` | Price-change emails only to managers of the same tenant (optional `ITenantContext` constructor parameter; existing tests unchanged). |
| `Controllers/CompanySettingsController.cs` | `Logo` serves the signed-in tenant's logo, or - anonymously, for a tenant's sign-in page - only the requested tenant's; logo type decided by file content. Still **Admin-only** to edit, as before. |
| `Views/Shared/_LoginLayout.cshtml` | Takes the brand (name/logo) from the page: a tenant's own on its sign-in page, the platform's neutral one elsewhere. |
| `Views/Shared/_Layout.cshtml` | Platform menu for platform staff; billing-only menu for restricted tenants; "Billing & Subscription" item for tenant Admin; billing banner; chatbot hidden when restricted; online-user count limited to the tenant; Database Backup item removed from the tenant menu. |
| `Views/Account/Login.cshtml` | "Start your free trial" link, sign-up success message, tenant-branded heading. |
| `Models/ViewModels/LoginViewModel.cs` | `TenantCode`, `TenantName` for the branded sign-in. |
| `appsettings.json.example` | New sections (`Database`, `Seed`, `Hosting`, `Platform`, `Payments`). |
| `WebApplication1.Tests` | Existing two test classes untouched; new test classes added. |

## 2. Database objects changed (migrations `AddMultiTenancy`, `AddSaasBilling`, `AddPaymentGateway`, `AddTenantPurgedAt`, `AddTenantAccessConfig`)

**Added to existing tables**
* `TenantId int NOT NULL` + index + foreign key to `Tenants` on: Warehouses, UserWarehouses, UserPermissions, UnitOfMeasures, Suppliers, StockTransfers, StockTransferItems, StockTransferPurchaseInvoices, StockTransactions, StockAdjustments, StockAdjustmentItems, SalesReturns, SalesReturnItems, SalesInvoices, SalesInvoiceItems, PurchaseReturns, PurchaseReturnItems, PurchaseInvoices, PurchaseInvoiceItems, Products, ProductWarehouseStocks, ProductPriceHistories, ProductBatches, PettyCashNames, PettyCashEntries, Payments, JournalEntries, JournalEntryLines, Customers, CompanySettings, Categories, Accounts. Existing rows are set to tenant 1; the temporary default is then dropped.
* `AspNetUsers.TenantId int NULL` + FK. Existing users set to tenant 1. `AspNetUsers.MustChangePassword bit NOT NULL DEFAULT 0` (forced password change after an admin reset).

**Indexes replaced** (global unique -> unique per tenant; names change, columns gain `TenantId`)
`IX_Warehouses_Code`, `IX_StockTransfers_TransferNumber`, `IX_StockAdjustments_AdjustmentNumber`, `IX_SalesReturns_ReturnNumber`, `IX_SalesInvoices_InvoiceNumber`, `IX_PurchaseReturns_ReturnNumber`, `IX_PurchaseInvoices_InvoiceNumber`, `IX_Products_Sku`, `IX_PettyCashNames_Name`, `IX_JournalEntries_EntryNumber`, `IX_Accounts_Code` -> `IX_<Table>_TenantId_<Column>`.

**No existing column was renamed, retyped or dropped; no existing data was modified other than the new `TenantId` values. Stored procedures: none exist. Unchanged:** `RolePermissions` (legacy), `BackupSettings`, `DatabaseBackups`, ASP.NET Identity role/claim/token tables.

**New tables (16)**: `Tenants`, `SubscriptionPlans`, `PlanFeatures`, `TenantSubscriptions`, `TenantFeatureOverrides`, `SubscriptionHistories`, `SaasInvoices`, `SaasInvoiceItems`, `SaasPayments`, `SaasAuditLogs`, `SaasNotifications`, `PlatformSettings`, `PaymentProviders`, `PaymentAttempts`, `PaymentWebhookEvents`, `SaasRefunds`. Money columns are `decimal(18,2)`; payments have unique `(Provider, ProviderTransactionId)` and `IdempotencyKey` (filtered unique indexes); invoices have a unique `(SubscriptionId, PeriodStart)` for non-cancelled invoices; invoices, subscriptions and attempts carry `rowversion`.

## 3. Behaviour changes users will notice

1. **Database Backup** is now platform-administrator only.
2. Everyone signs in again once after the upgrade.
3. A tenant on a plan without a module (e.g. Petty Cash) is redirected to an "upgrade" page for it; tenant 1 (the migrated business) has every module.
4. Demo accounts are no longer re-created at start-up outside Development.
5. Suspended / expired tenants can reach only Billing, account pages and sign-out.

## 4. New code (all new files)

`Controllers/PlatformAccountController` (platform sign-in), `Controllers/Platform/PlatformCompanyController`, `Services/Saas/PlatformBrandingService`, `ImageSignature`, `Views/PlatformAccount`, `Views/PlatformCompany`, `Views/Shared/_PlatformLoginLayout`, `Reports/SaasInvoiceReport.rdlc` (subscription invoice PDF; derived from `SalesInvoiceReport.rdlc` - the sales report itself is untouched), `Services/Saas/SaasInvoicePdfService`, `TenantPortalService`, `PasswordGenerator`, `Controllers/TenantPortalController`.

`Models/Tenancy`, `Models/Saas`, `Services/Tenancy`, `Services/Saas` (billing, lifecycle, payments, providers, entitlements, platform metrics, retention), `Controllers/BillingController`, `PaymentsController`, `RegistrationController`, `Controllers/Platform/*`, `Middleware/TenantAccessMiddleware`, `Data/PlatformSeeder`, `Data/SaasSeeder`, `Extensions/TenantUserExtensions`, `Views/Billing`, `Views/Platform*`, `Views/Registration`, shared partials (`_Pager`, `_TenantBadge`, `_InvoiceBadge`, `_SubscriptionBadge`, `_BillingBanner`, `_PlatformMenu`), migrations, tests, `deploy/sql`, `docs`.

## 5. Languages (English / Bangla)

- **Interface language**: every layout (tenant, platform, sign-in pages) has a language switcher (navbar globe menu; English / বাংলা buttons on sign-in pages). Default is English. The choice is stored in the `ui_lang` cookie and in `AspNetUsers.PreferredLanguage` (migration `AddUserPreferredLanguage`, nullable column), so the next visit - and a sign-in on another device - opens in the chosen language.
- **How it works**: pages are still rendered in English by the server; when the language is Bangla the page sets `<html lang="bn">` and loads `wwwroot/i18n/bn.js` (dictionary + regex rules for messages containing numbers/names) and `wwwroot/js/i18n.js`, which translates visible text, placeholders, titles, buttons, `confirm()/alert()`, DataTables wording and later-added content. Data (names, SKUs, emails) is never translated unless it equals a dictionary phrase. Add or fix a phrase by editing `bn.js` (key = English text, value = Bangla). Unknown phrases simply stay English.
- **Sales thermal receipt**: `Print Receipt` offers English or Bangla (`?lang=en|bn`). Bangla receipts use Bangla labels, digits and dates and the `Receipts:BanglaFont` font (default Nirmala UI). The last explicit receipt choice is remembered in `receipt_lang`; with none, the receipt follows the interface language.
- PDF/RDLC reports other than the thermal receipt stay English.

## 6. Data export and data agreement

- **Data agreement** (`Services/Saas/DataAgreement.cs`, public page `/DataAgreement`, version `2026-10`): accepted at self-registration, when choosing a plan (checkbox, once per version) or on Billing > Data & agreement. Each acceptance (tenant, version, user, date, IP, where, subscription) is stored in `DataAgreementAcceptances`. Change the wording and bump `DataAgreement.Version` to ask everyone again; tenants that have not accepted see a banner in Billing.
- **Tenant data requests**: Billing > Data & agreement lets the tenant Admin request a copy of all business data as CSV, JSON (ZIP of one file per table) or Excel (one workbook, one sheet per table), with a reason (ending plan, moving to another system, backup, other). The cancel buttons also offer "also send me a copy of my data". The page stays reachable while a tenant is suspended or expired.
- **Platform menu "Data Exports"** (`/Platform/DataExports`): list/filter requests, record a request taken by phone, **Generate** (choose format), Reject with a reason, Download. Generating/downloading is Platform Administrator only. Files are written to `App_Data/Exports/{tenantId}/`, available to the tenant for 14 days, every step is audited (`DataExport.*`, `DataAgreement.Accepted`).
- **What is exported**: every tenant-owned business table that has rows (tables with no rows are skipped), plus the tenant's users (email, name, roles, status - never password hashes or tokens), a README and manifest. Rows are read with an explicit `TenantId` predicate.
- Migration `AddDataExportAndAgreement` (two new tables). Upgrade/rollback SQL scripts regenerated.
- Not automated: deleting expired export files (remove `App_Data/Exports` files older than 14 days from time to time, or add to the retention job).
