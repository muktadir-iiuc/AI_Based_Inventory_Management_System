# Testing and the Phase 7 regression pass

## Running the automated tests

```
dotnet test WebApplication1.Tests                                   # 117 pass, 4 skipped (need SQL Server)
IMS_TEST_SQL="Server=localhost;User Id=sa;Password=..." dotnet test WebApplication1.Tests   # all 121 (creates and drops throw-away databases)
```

| Area | Tests |
|---|---|
| Existing inventory (unchanged) | `ProductPriceServiceTests`, `ProductsControllerAuthorizationTests` (10) |
| Tenant isolation | filters, stamping, cross-tenant write/insert blocked, no tenant = no rows, per-tenant unique SKU, `FindAsync`, includes, notification groups, "every business entity is scoped" guard, price-email leak |
| Subscription lifecycle | trial start/expiry (also live against the clock), renewal invoice once, overdue -> grace -> suspend, restore on payment, admin hold survives payment, disabled stays disabled, cancel at term end, plan upgrade/downgrade, entitlements and limits |
| Billing math / invoices | discount-then-tax rounding, yearly/custom intervals, no duplicate invoice per period, issued invoice immune to price change, adjustments, cancellation rules |
| Payments | applied once, replay, second payment on paid invoice, idempotency key, wrong currency/tenant/amount/zero, partial payments, manual confirm/reject |
| Gateway (stubbed HTTP, fake provider) | documented request fields, response mapping, signature check, forged "success", bad signature, mismatches, cancel/fail, gateway down + reconcile, expiry, risky hold + release, duplicate-tab payment flagged, webhook log hygiene, refunds incl. provider rejection/cancellation |
| Platform administration | dashboard figures, revenue/outstanding, per-tenant overrides, trial reopen, termination, provisioning (+ failure), unique tenant codes, authorization map by reflection |
| Platform / tenant sign-in separation | platform logo validation, image signatures, route and anonymity map; live: distinct pages, who-may-sign-in-where, redirects, sign-out targets, branding of console vs tenant, tenant logo isolation, platform Company Settings |
| Tenant access panel | portal URL validation/uniqueness/normalisation, tenant resolution by domain or code, generated-password rules, credential actions admin-only |
| Real SQL Server | 12 simultaneous callbacks -> 1 payment; racing different transactions -> 1 applied; DB rejects duplicate period/transaction; purge erases only the right tenant, only after retention, keeps financial records |

## Regression pass performed for this release

Against a **restored copy of a real pre-SaaS database** (`InventoryManagementDb`: 89 products, 5 sales invoices, 9 users, 42 tables), the original left untouched:

| Check | Result |
|---|---|
| Row counts of all 42 business/identity tables before vs after upgrade | identical |
| Aggregates: stock 891, sales lines 8,125, purchase lines 756,400, journal debit = credit = 1,630,625, payments 806,975 | identical |
| Every business row and user has `TenantId = 1`; no leftover defaults; `DBCC CHECKCONSTRAINTS` | clean |
| Upgrade via `dotnet ef database update` and via `deploy/sql/01-upgrade-to-saas.sql` (run twice) | identical data |
| Rollback via EF and via `99-rollback-to-pre-saas.sql` | data **and** schema (columns, indexes, FKs) identical to the original |
| Fresh install on an empty database (demo data off) | only the platform admin; 8 roles, 7 plans, 13 accounts, 1 default tenant; `/health` ok |
| Crawl of every reachable page as the migrated business's Admin | 146 of 147 OK; the one redirect is the existing "posted invoice cannot be edited" behaviour; **10 invoice PDFs (RDLC) rendered** |
| Same crawl as Manager (141 pages), Sales Officer, Purchase Officer, Accountant | all 200; the only redirects are the existing role restrictions (e.g. sales cannot open purchase invoices) |
| A second business registered itself and probed 49 record URLs of tenant 1 | 49 blocked, 0 leaked; its lists contain none of tenant 1's data; both businesses can own `WH-01` |

## Not covered / honest limits

* **SSLCOMMERZ live sandbox**: requests, response mapping and the IPN hash follow the official documentation and are tested with stubs; no real transaction has been made (needs your sandbox store credentials).
* **Responsive layout on real phones/tablets**: pages use Bootstrap's responsive grid and render; not inspected on devices.
* **Email delivery**: depends on your SMTP settings; the code path logs and skips when SMTP is not configured.
* **Browser/JS behaviour** of the existing screens (DataTables, SignalR toasts) was not re-tested beyond the pages rendering; no existing JS was changed apart from tenant-prefixed notification groups.
* **Load/performance**: not measured. Every business table has a `TenantId` index; revenue/outstanding reports read the SaaS tables directly and are fine for thousands of tenants but have not been load tested.
* No automated browser tests.

## Languages

`LanguageTests` checks language code normalisation, receipt language resolution, Bangla digits, and that `wwwroot/i18n/bn.js` parses with compilable rules. Rendered Bangla receipt PDFs were checked visually on a restored copy of real data.
