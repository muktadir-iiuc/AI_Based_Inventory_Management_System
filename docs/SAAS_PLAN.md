# SaaS Conversion: Analysis and Implementation Plan

## 1. Existing system (Phase 1 findings)

- ASP.NET Core MVC, .NET 10, EF Core 10 + SQL Server, ASP.NET Identity (string keys), SignalR, ML.NET forecasting, RDLC reports (AspNetCore.Reporting), SMTP email.
- One project `WebApplication1` (assembly `WebAppIMSBdTyre`), tests in `WebApplication1.Tests` (10 passing at baseline). `WebPage` is a separate marketing site, untouched.
- Single database `DbSaasIMS`, 27 migrations, `ApplicationDbContext : IdentityDbContext`. Auto-migrate and seed on startup (`DbInitializer`).
- Single-business assumptions found:
  - `CompanySetting` and `BackupSetting`: one row per database.
  - Unique indexes that must become per-tenant: `Product.Sku`, `Warehouse.Code`, `Account.Code`, `StockTransfer.TransferNumber`, `StockAdjustment.AdjustmentNumber`, `PurchaseInvoice/SalesInvoice.InvoiceNumber`, `SalesReturn/PurchaseReturn.ReturnNumber`, `JournalEntry.EntryNumber`, `PettyCashName.Name`, `RolePermission(RoleName,PermissionKey)`.
  - Roles (`Admin`, `Manager`, ...) are global `IdentityRole` rows. Admin/Manager are short-circuited in `PermissionAuthorizationHandler`.
  - Login (`AccountController`) resolves a user by email (globally unique); claims carry `WarehouseId`.
  - `DatabaseBackupService` / `SqlScriptBackupWriter` back up the **whole database** (cross-tenant) via raw SQL.
  - `ChatbotService`, `ForecastService`, `ActivityNotifier`, `NotificationsHub` (SignalR groups keyed by warehouse id), `BackupSchedulerService` (hosted service), `UserActivityMiddleware`.
  - Raw SQL only in the backup services; everything else is EF LINQ, so EF global query filters cover it.
- Working tree has ~76 uncommitted modified files plus new migrations (user's in-progress work). A source backup was taken at `../IMS-backup/pre-saas-*.tar.gz` (excludes bin/obj/.git/publish). Nothing was committed or reverted.

## 2. Tenant isolation design

- Shared app, shared DB, `TenantId` column (int FK to `Tenants`).
- `ITenantEntity { int TenantId }` on every business entity (products, categories, units, warehouses, stock, purchase/sales docs and items, returns, accounts, journals, payments, petty cash, company settings, backup settings, user-warehouse, user-permission, price history, batches, adjustments, transfers).
- `ApplicationUser.TenantId` (nullable: null = platform staff). `ApplicationRole` stays global; tenant "Admin" = tenant administrator. Platform roles: `PlatformAdmin`, `PlatformBilling` (users with `TenantId = null`).
- `ITenantContext` resolves tenant id **only from the authenticated user's `TenantId` claim** (set at sign-in from the DB), never from request input. Background jobs use an explicit `TenantScope`.
- EF Core global query filter `e.TenantId == CurrentTenantId` on every `ITenantEntity` (platform context bypasses via explicit `IgnoreQueryFilters` in platform services only). `SaveChanges` override stamps `TenantId` on added entities and rejects cross-tenant writes.
- Unique indexes rebuilt as `(TenantId, X)`. Identity email stays globally unique (login by email without tenant picker).
- Existing data: migration creates tenant #1 "Default Tenant", backfills all rows with `TenantId = 1`, assigns existing users to it, creates default subscription (Active, Enterprise-like, no limits) so nothing changes for the current business.
- Whole-database backup moves to Platform Admin only; tenants get no raw DB backup (tenant-scoped export is a later item).
- SignalR groups prefixed with tenant id. Chatbot/forecast run on the filtered context.

## 3. New SaaS entities (additive migration)

`Tenants`, `SubscriptionPlans`, `PlanFeatures`, `TenantSubscriptions`, `SubscriptionHistory`, `Invoices` (`SaasInvoices` to avoid clashing with sales/purchase invoices), `SaasInvoiceItems`, `SaasPayments`, `PaymentAttempts`, `PaymentProviders`, `PaymentWebhookEvents`, `BillingProfiles`, `SubscriptionUsage`, `SaasAuditLogs`, `PlatformSettings`, `NotificationQueue`. Decimal(18,2) money, unique `(SubscriptionId, PeriodStart)` for invoice dedupe, unique provider transaction id + idempotency key, `RowVersion` concurrency.

## 4. Subscription lifecycle

Statuses: Trialing, Active, PastDue, GracePeriod, Suspended, Disabled, Cancelled, Expired. Separate **administrative hold** flag (fraud/security) that payment never clears. Enforcement via a `SubscriptionAccessMiddleware` + `[RequireFeature]` filter + limit checks in services (users, products, warehouses). Suspended tenants may reach only `/Billing/*`, logout, account pages.

## 5. Payments

`IPaymentProvider` abstraction (initiate, verify server-side, parse/validate webhook, refund). Providers: **SSLCOMMERZ** (sandbox; official validation API + IPN with hash verification) first; bKash tokenized checkout second; Nagad/Rocket via SSLCOMMERZ or later adapters after merchant eligibility check. Manual cash/bank flow with authorized confirmation. Idempotent processing in one DB transaction keyed by provider transaction id. Secrets from environment / user-secrets only.

## 6. Background jobs

Hosted `BillingSchedulerService` (pattern already used by `BackupSchedulerService`), DB-persisted job state, idempotent steps: trial expiry, renewal invoice generation, reminders, overdue detection, grace expiry and auto-suspend, reconciliation, notification delivery, stale attempt cleanup. Uses a SQL app-lock to avoid double-run on multiple instances.

## 7. UI and authorization

New area `Platform` (PlatformAdmin/PlatformBilling policies) and tenant `Billing` controller (Tenant Owner/Admin only). Sidebar items added without touching existing ones. Existing permission handler unchanged for inventory; tenant filter makes it tenant-safe.

## 8. Phases and verification gates

1. Phase 3 first: tenant model, filters, migration, isolation tests, regression (build + 10 existing tests + manual smoke).
2. Phase 4: plans, subscriptions, invoices, entitlement enforcement, lifecycle tests.
3. Phase 5: provider abstraction, SSLCOMMERZ sandbox, webhook idempotency, manual payments.
4. Phase 6: platform dashboard/tenants/plans UI, audit, notifications, jobs, reports.
5. Phase 7: docs (deploy, config, backup/rollback), full test pass.

## 9. Known risks

- Global filters + `ExecuteUpdate`/`ExecuteDelete` honour filters (good) but raw SQL does not: only backup code, handled separately.
- Several controllers use `FirstOrDefault`-style single-row settings; each converted to per-tenant lookup.
- Existing uncommitted work must not be lost: all SaaS changes go on a new branch `saas`, migrations are additive plus backfill, rollback script provided.
- Real payment verification against provider sandboxes needs merchant sandbox credentials from the owner; until then tests use mocked provider responses.

## 10. Responsive UI requirement (added)

All new SaaS pages (platform admin, tenant billing, registration, login) must work on phone, tablet, laptop and desktop: mobile-first layout, viewport meta, collapsible sidebar, tables wrapped in responsive containers or card layout under 768px, touch-sized controls. Existing `_Layout` is audited for viewport/sidebar behaviour; only minimal fixes applied to existing pages if the layout is not already responsive.
