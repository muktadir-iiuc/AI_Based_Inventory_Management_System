# Security model

## Tenant isolation

* One database; every business table has a `TenantId` (32 tables). EF Core **global query filters** restrict every query to the current tenant; `SaveChanges` stamps new rows and **rejects** writes to another tenant's row or without a tenant. `ExecuteUpdate/ExecuteDelete` honour the filters. Raw SQL exists only in the whole-database backup (platform admin only) and the erase routine (platform admin only).
* The tenant comes **only** from the signed-in user's `TenantId` claim, set at sign-in from the database. No request value (route, form, query, header) is ever used to choose a tenant. Gateway callbacks resolve the tenant from our own `PaymentAttempt` record.
* Users are not query-filtered (sign-in and cookie validation look them up before a tenant is known): every tenant-facing user list/lookup goes through `TenantUsers(...)`, and tenant admins cannot assign platform roles.
* Unique indexes are per tenant, so two businesses can both have SKU `PRD-001` or warehouse `WH-01`.
* Live notifications (SignalR groups) are namespaced per tenant. Company-settings cache is per tenant. Price-change emails go only to the same tenant's managers (roles are global rows, so this needed an explicit filter - there is a regression test).
* Platform staff (no tenant) see zero business rows by construction and cannot reach inventory pages (redirected to `/Platform`). They see counts, invoices, payments and audit entries only. There is no impersonation.

Verified: automated tests (filters, stamping, cross-tenant writes, `FindAsync`, includes, notification groups, the "every business entity is tenant-scoped" guard) and an HTTP probe of 49 tenant-1 record URLs (products, customers, suppliers, sales/purchase invoices, PDFs, warehouses, transfers, categories, units, accounts, users) from a second business: all 49 returned 404/redirect, none 200.

## Authentication and authorization

* Two sign-in surfaces: the platform console (`/Platform/Login`) admits only platform accounts; the business pages never sign a platform account in. Logo uploads (platform and business) are validated by file signature, never by the declared content type; a business logo can only be fetched for that business (by its code/domain on its own sign-in page, or by its signed-in users), with no fallback to another business's logo.

* ASP.NET Identity; password policy unchanged. Login is rate limited (15 / 5 min / address, in addition to account lockout). Cookie: HttpOnly, `Secure` outside development, SameSite=Lax.
* Roles: tenant roles (`Admin`, `Manager`, ...) are unchanged and grant nothing under `/Platform`. `PlatformAdmin` (everything) and `PlatformBilling` (view, record/confirm payments, reports). Money, plans, settings, status changes, erase: `PlatformAdmin` only - enforced by attributes and covered by a reflection test that fails if a state-changing action loses its restriction.
* Subscription gating is server-side: `[RequireFeature]` on 12 controllers, `[EnforceLimit]` on the create actions for users, products, warehouses, and a middleware that evaluates tenant + subscription against the clock on every request. Hiding menu items is cosmetic only.
* Cancel subscription = tenant **owner** only.
* Credential administration (platform admins only, audited, tenant-scoped): password reset shows a generated or chosen temporary password once (never stored/logged), forces a password change, and revokes sessions - Identity's security-stamp check runs every minute instead of the 30-minute default so a reset/deactivation bites quickly. A tenant's sign-in link or own domain only admits that tenant's users, and answers wrong-tenant attempts with the same generic message as a wrong password.

## Payments

* Browser redirects prove nothing: every callback is re-verified with the provider's server API; amount, currency, reference, tenant and invoice are checked against **our** attempt record; the IPN signature (when present) is verified.
* Idempotent: unique provider transaction id and idempotency key, invoice and subscription row versions, retry on races. Replays and simultaneous callbacks produce one payment.
* A failed/pending/cancelled payment never activates anything. A paid payment never lifts an administrative hold or re-enables a disabled tenant.
* No card data, PINs, OTPs or merchant secrets are stored or logged; gateway credentials come from configuration only; HTTP client request logging is off for the gateway.
* Callback endpoints are anonymous by necessity: rate limited, CSRF-exempt (no cookie), input-size capped, never trusted.

## Other controls

* Antiforgery on every state-changing form. CSV exports neutralise spreadsheet-formula injection.
* Audit log for tenant creation/changes, plan changes, invoices, adjustments, payments (including rejected and bad-signature callbacks), refunds, suspensions, settings, staff changes.
* No demo credentials outside Development unless `Seed:DemoData=true`.
* Forwarded headers trusted only from `Hosting:KnownProxies`.

## Known gaps / follow-ups

* No email verification on self-service sign-up (rate limit + honeypot + terms checkbox only). Add verification or CAPTCHA before wide public exposure.
* No tenant self-service data export; no per-tenant restore.
* No two-factor authentication.
* Password-reset by email is not part of the application (unchanged from before).
* `Seed:DemoData` defaults to **on** in Development; do not point a Development-mode instance at production data.
* The SSLCOMMERZ integration has been verified against its documentation and with stubbed responses; it has **not** been exercised against the live sandbox yet (needs your store credentials).
* The sign-up plan `trial` and the example plans are seeded examples - review them before launch.
