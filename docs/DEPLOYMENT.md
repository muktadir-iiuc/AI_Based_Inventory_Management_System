# Deployment guide

## 1. What you need

| Item | Notes |
|---|---|
| .NET 10 ASP.NET Core runtime (or the Windows Hosting Bundle for IIS) | The project targets `net10.0`. |
| SQL Server (the same engine as before) | The database login needs DDL rights **only** if the app applies migrations itself (`Database:AutoMigrate=true`, the default). |
| HTTPS with a real certificate | Outside Development the sign-in cookie is marked `Secure`; it will not work over plain HTTP. |
| A public URL reachable by the payment gateway | For the SSLCOMMERZ IPN/redirects. |
| SMTP account (optional but recommended) | Invoice, reminder and receipt emails. With `Smtp:Host` empty, emails are skipped (logged), in-app notices still work. |
| WebSockets enabled on the proxy | SignalR live notifications. |

## 2. Configuration

Nothing secret belongs in `appsettings.json` in source control (`appsettings.json` is already git-ignored; copy `appsettings.json.example`).
Use **environment variables**, `dotnet user-secrets` (development), or your host's secret store. In environment-variable form `:` becomes `__`.

| Key | Required | Meaning |
|---|---|---|
| `ConnectionStrings:DefaultConnection` | yes | SQL Server connection string. |
| `ASPNETCORE_ENVIRONMENT` | yes | `Production` on servers. |
| `Platform:AdminEmail`, `Platform:AdminPassword`, `Platform:AdminName` | first run | Creates the first Platform Administrator if no user with that email exists. **Remove the password from configuration after the first sign-in** and change it in the app. |
| `Seed:DemoData` | no (default `false` outside Development) | `true` creates the well-known demo logins (`admin@inventory.local / Admin@123` ...) and sample stock. **Never enable in production.** |
| `Database:AutoMigrate` | no (default `true`) | `false` = the app never alters the schema; apply `deploy/sql` yourself (see UPGRADE_AND_ROLLBACK.md). |
| `RateLimits:LoginPerFiveMinutes` | no (default `15`) | Sign-in attempts per client address per 5 minutes. |
| `Hosting:KnownProxies` | behind a proxy | Comma-separated IPs of your reverse proxy / load balancer. When set, `X-Forwarded-For`/`-Proto` are trusted **from those IPs only**. Without it the rate limiters see the proxy's address for every visitor. |
| `Payments:SslCommerz:StoreId`, `:StorePassword` | for online payments | From your SSLCOMMERZ merchant panel. |
| `Payments:SslCommerz:Sandbox` | for online payments | `true` (default) uses `sandbox.sslcommerz.com`. Set `false` only for live credentials. |
| `Payments:PublicBaseUrl` | for online payments | The externally reachable root URL, e.g. `https://app.example.com`. Callback URLs are built from it. |
| `Smtp:Host/Port/EnableSsl/User/Password/FromAddress` | optional | Outgoing mail. |

Then, inside the app (Platform → Settings / Providers; set **Public URL** so tenant sign-in links show your real address; optional invoice logo at `wwwroot/img/platform-logo.png`): invoice prefix, due days, grace, reminders, retention, sign-up on/off and plan, bank-transfer instructions, provider switches.

## 3. First deployment checklist

1. Back up nothing yet (new database) - or follow UPGRADE_AND_ROLLBACK.md if this database already holds a pre-SaaS installation.
2. Create the SQL login and empty database (or point at the existing one).
3. Set the configuration above (environment variables).
4. Start the app once. It applies migrations (unless disabled), creates roles, the platform admin, the example plans and payment-provider rows, and the default tenant (id 1).
5. Check `https://your-host/health` returns `ok`.
6. Sign in as the Platform Administrator at `/Platform/Login`, change the password, then remove `Platform__AdminPassword` from the environment and restart.
7. Review Platform → Plans (prices, limits, modules), Settings and Providers.
8. Decide what to do with tenant 1. On a **new** installation it is an empty shell ("Default Business", internal plan, no users): terminate it or rename it and add an owner with *Tenants → Add administrator*. On an **upgraded** installation it is your existing business - leave it alone.
9. Place a test sign-up through `/Register` (or create a tenant manually), pay a test invoice (sandbox), confirm it activates.

## 4. Reverse proxy notes

* Forward `Host`, `X-Forwarded-For`, `X-Forwarded-Proto`; list the proxy in `Hosting:KnownProxies`.
* Allow WebSocket upgrade for `/hubs/notifications`.
* Do not cache `/Payments/*`, `/Billing/*`, `/Platform/*`.
* Terminate TLS at the proxy or the app; the auth cookie requires HTTPS either way.
* The payment gateway must reach `POST /Payments/Callback/sslcommerz/ipn` and the success/fail/cancel URLs from the internet. Do not put these behind your own login or IP allow-list.

## 5. Background billing job

`BillingSchedulerService` runs inside the web app every 5 minutes (first run 30 s after start): trial expiry, renewal invoices, reminders, overdue marking, grace and automatic suspension, reconciliation of unfinished payments, refund follow-up, email dispatch. It is safe to run on several instances (SQL Server application lock) and safe after restarts (all state is in the database). If the site is down, the next pass after start-up catches up; access is also enforced live against the clock on every request, so a lapsed trial or grace period closes access even before the job runs. Look for `Billing pass:` lines and `failed; it will be retried` errors in the log.

## 6. SSLCOMMERZ go-live

1. Test fully in sandbox first (see PAYMENTS.md): success, failure, cancel, duplicate IPN, refund.
2. Put live `StoreId`/`StorePassword` in the environment, set `Payments__SslCommerz__Sandbox=false`.
3. Register this server's public IP with SSLCOMMERZ (their query and refund APIs require it).
4. Set the IPN URL in the merchant panel to `https://your-host/Payments/Callback/sslcommerz/ipn`.
5. Make a small real payment and refund it; check Platform → Payments and the audit log.
6. SSLCOMMERZ documents no recurring/tokenised card billing: renewals are invoiced and paid each term.

## 7. Logs and privacy

* Do not enable `EnableSensitiveDataLogging`. The SSLCOMMERZ HTTP client has request logging disabled on purpose (its query URLs carry the store password).
* The audit log (`SaasAuditLogs`), webhook log and notification tables never store passwords, tokens, OTPs or card data; the webhook log strips `store_passwd`, `card_no` and `verify_sign`.
