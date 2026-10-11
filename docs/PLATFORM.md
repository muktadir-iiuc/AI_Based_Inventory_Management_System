# Platform administration (Phase 6)

All pages live under `/Platform` and need a platform account (no tenant). A tenant's own Admin/Manager roles grant nothing there.

| Page | Who | What |
|---|---|---|
| Dashboard `/Platform` | admin, billing | Tenant counts (active, trial, suspended, disabled, terminated, expired, overdue), MRR/ARR, paid/unpaid invoices, collected this month, failed and pending payments, renewals and trial expirations in 7 days, recent activity. All computed from the database. |
| Tenants `/Platform/Tenants` | read: admin, billing. change: admin | Search, filter (tenant/subscription status), sort, page. Create tenant, edit profile, plan/terms/trial/limits/module overrides, cancel/resume, owner and administrators, suspend, disable, terminate, erase. |
| Plans `/Platform/Plans` | admin | Create/edit/enable/disable/archive. Price, currency, interval, trial, grace, tax, discount, limits, modules, availability window. Existing subscribers keep their terms. |
| Subscriptions, Invoices, Payments | read + record/confirm: admin, billing. cancel/adjust/refund/release: admin | Manual cash/bank payments, confirmation of tenant-reported payments, held-payment release, refunds, invoice adjustment and cancellation. |
| Reports `/Platform/Reports` | admin, billing | Revenue by month or year (net of refunds), outstanding balances, failed online payments, rejected notifications; CSV exports. |
| Audit logs | read: admin, billing. CSV: admin | Filter by action, actor, tenant, result, dates. Never contains secrets. |
| Providers, Settings, Platform users | admin | Switch providers on/off, manual-payment instructions, billing defaults, retention, sign-up, platform staff accounts. |

## Two different sign-in pages

| | Platform console | Business (tenant) |
|---|---|---|
| Address | `/Platform/Login` | `/Account/Login` (shared) or the business's own link `/t/<code>` / own domain |
| Look | dark indigo console, "Platform administration" badge, the platform's name and logo | the standard business sign-in showing **that business's company name and logo** |
| Who may sign in | platform accounts only (no tenant, role PlatformAdmin / PlatformBilling) | that business's users (on the shared page: any business user) |
| Wrong account type | generic "Invalid email or password" | a platform account is told to use the platform sign-in (after a correct password); it is never signed in here |
| Signed-out visitor opening a protected page | anything under `/Platform` -> `/Platform/Login` | everything else -> `/Account/Login` |
| Sign-out returns to | `/Platform/Login` | `/Account/Login` |

The shared business page `/Account/Login` shows a business's own name and logo whenever the business is obvious: the one this browser last signed in to (a cookie holding only the business code; "Not <business>? Sign in to a different business" clears it), or the only business on the platform. Otherwise - several businesses and an unknown visitor - it shows the platform's neutral product name, and the visitor should use the business's own link `/t/<code>` or domain. Branding on the shared page is display only: any business user can still sign in there, while an explicit tenant link or domain admits only that business's users. A business's own name and logo appear on its own link or domain, and inside the application for its users. Platform screens always show the **platform's** name and logo (Platform Settings; upload a PNG/JPEG logo there), never a business's.

## Signing in to the platform area

The Platform Administrator is a separate account with no tenant. It is created at first start from configuration (`Platform__AdminEmail` / `Platform__AdminPassword`). On a **Development** install (or with `Seed__DemoData=true`) a demo one is created if none is configured: `platform@inventory.local` / `Platform@123` - never use that outside a demo. Sign in at `/Platform/Login`; you land on `/Platform`. Tenant admins cannot see these pages.

**Add a tenant:** Platform -> Tenants -> *New tenant* (business, owner, plan, start mode). The owner gets a tenant Admin account and can sign in immediately.

## Company Settings of tenants (platform menu -> *Company Settings*, Platform Administrator only)

Lists every business with its company name, short name and logo; *Edit* sets the platform-record name, company name, short name, address, phone and logo of that business - the same profile the business's own Admin edits under *Company Settings* (which stays in the business's Administration menu). Logos are accepted by file content (PNG, JPEG, GIF, BMP up to 1 MB), not by the browser's content type. Changes appear on the business's invoices, its screens and its branded sign-in page, and are audited.

## Access panel (Tenant -> *Access & URL*, Platform Administrator only)

* **Sign-in addresses.** Each tenant has a branded link `<public url>/t/<code>` that shows "Sign in to <business>" and lets **only that tenant's users** in (other tenants and platform staff get the usual "Invalid email or password"). The shared page `/Account/Login` keeps working for everyone. Set the platform's *Public URL* in Platform Settings so the displayed links use the real address.
* **Tenant's own web address.** Optionally record `https://shop.example.com` for a tenant. When that domain is pointed at this application (DNS + reverse proxy, done outside the app) its sign-in page is limited to that tenant in the same way. Validated: real https/http domain, root only, no credentials, unique, never the platform's own address.
* **Sign-in accounts.** For every user of the tenant: reset password (your text, or a generated 14-character one), change sign-in email, deactivate/activate, unlock, sign out everywhere. A reset shows the temporary password **once** (not cached, not stored, not logged, not in the audit trail), signs the user out within a minute and forces them to choose their own password before anything else works. All actions are audited and limited to users of that tenant.

## Subscription invoice PDF

*Print / Save PDF* on an invoice (tenant billing page and platform invoice page) opens a PDF built from the same report layout family as the sales invoice: header, title, invoice and bill-to boxes, item table, subtotal / discount / tax / total / paid / balance, amount in words, notes and signature lines. The issuer is the platform (Platform Settings: name, address, phone; optional logo file `wwwroot/img/platform-logo.png`), never the tenant's own company profile.

## Tenant lifecycle: four different things

| Action | Effect | Reversible |
|---|---|---|
| Automatic non-payment suspension | Billing-only access after the grace period; lifted automatically by a verified payment | yes, by payment |
| Administrative suspension | Billing-only access; **payment never lifts it** | yes, by a platform admin |
| Disable | Users cannot sign in; payment never re-enables | yes, by a platform admin |
| Terminate | Soft delete: access ends, subscription cancelled, data kept for `tenant.retention-days` (default 90) | no (cannot be re-enabled) |
| Erase | After retention and typing the tenant code: business data and users are permanently deleted. Invoices, payments, refunds, subscription history and audit log are kept | no |

## Platform staff and tenant data

Platform staff see counts and billing records only; tenant stock, sales and accounting tables are filtered to "no tenant" for them. There is no impersonation. Whole-database backup is platform-admin only.

## Self-service sign-up

`/Register` creates a tenant, its Admin owner, default warehouse, chart of accounts and the plan named by setting `registration.plan-code` (default `trial`). Protected by antiforgery, a hidden honeypot field, a rate limit (5/hour/IP), the terms checkbox and the global unique-email rule. There is no email verification step yet: add one before opening sign-up to the public internet if mail-based abuse is a concern.

## Responsive UI

New pages use Bootstrap's grid, `table-responsive` wrappers and stacked forms so they reflow on phones and tablets. The sidebar is the existing AdminLTE one (overlay on small screens). Verified by rendering every page (HTTP 200) and by markup review; not yet checked on physical devices.
