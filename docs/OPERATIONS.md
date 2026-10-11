# Operations: backups, restore, monitoring, runbooks

## Backups

There is **one database for all tenants**, so a backup is all-or-nothing.

| Mechanism | Who | Notes |
|---|---|---|
| Built-in *Database Backup* page (manual and scheduled; retention count) | **Platform Administrator only** (moved from tenant Admin/Manager in this release - the file contains every tenant's data) | Written by SQL Server to its backup folder; the page can copy it to a second folder. |
| SQL Server native backup / your hosting provider's snapshots | DBA | **Recommended as the primary.** Full backup daily, differential or log backups as your recovery point demands, copied off the server. |

Minimum policy: daily full backup, 14+ days retention, one copy off the server, a **restore test every month** (restore to a scratch database and open the app against it), and a full backup immediately before every release.

There is no per-tenant export or per-tenant restore. If one tenant asks for its data back, restore a backup to a scratch database and extract that tenant's rows (every business table has a `TenantId` column). Building a self-service tenant export is a follow-up item.

### Restore
```sql
ALTER DATABASE [Db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
RESTORE DATABASE [Db] FROM DISK = N'...\Db.bak' WITH REPLACE;
ALTER DATABASE [Db] SET MULTI_USER;
```
After a restore: payments made *after* the backup exist at the gateway but not in the database. Run the reconciliation (it happens automatically every 5 minutes for attempts still pending) and check *Platform → Payments → Online attempts* and the gateway's transaction report for the period; refund or manually record anything missing.

## Monitoring

* `GET /health` - `ok` (200) when the database answers, 503 otherwise. Point your uptime monitor at it.
* Log lines to alert on: `Subscription lifecycle step '...' failed`, `Payment reconciliation failed`, `Billing scheduler pass failed`, `Verified payment for attempt ... could not be applied`, `Payment callback ... failed`.
* *Platform → Payments → Needs review*: money the gateway took that the system could not apply (e.g. paid twice). Should be empty; handle the same day (refund, or record against the right invoice).
* *Platform → Payments → Awaiting confirmation*: bank/cash payments tenants reported. Confirm or reject promptly - the tenant is waiting.
* Dashboard "Failed payments (30 days)" and "Tenants with overdue invoices".
* Audit log filter `Result = Rejected` for callbacks with bad signatures and refused payments.

## Runbooks

**A tenant paid but is still suspended.** Open the tenant → Billing. If a *payment* exists and the invoice is Paid, check the tenant for an *administrative hold* (payment never lifts it) and for other overdue invoices (the subscription stays behind until all are paid). If no payment exists, look at *Payments → Online attempts* for the reference; a Pending attempt is re-verified automatically. If the gateway shows it paid and the attempt is stuck, open it and use *Release* (risky payments) or record the payment manually from the invoice page.

**Suspend vs disable vs terminate.** See PLATFORM.md. Suspension for fraud/abuse: *Tenant → Lifecycle → Suspend* (payments cannot lift it). Wrong customer, gone for good: *Terminate*; data is kept for the retention period (default 90 days) before *Erase*.

**A tenant is locked out because of the plan.** *Tenant → Subscription*: override the limit, force a module on, extend the trial, or *Change plan now* (no invoice).

**Gateway outage.** Callbacks fail → the gateway retries IPNs; attempts stay Pending and are re-verified every 5 minutes. Tenants can still report bank transfers. Nothing to do unless it lasts days.

**Duplicate or replayed callbacks.** Safe by design (unique provider transaction id + idempotency key + invoice row version). Verified with 12 simultaneous callbacks against SQL Server in the test suite.

**Clock.** All times are stored in UTC. Trial, grace and renewal logic use the server's UTC clock; keep NTP on.

**Scaling out.** Several web instances may run; the billing job is serialised by an application lock. Sessions use cookies (no server affinity needed beyond the data-protection keys: share the ASP.NET Data Protection key ring between instances or users are signed out when the load balancer switches servers).

**Retention / erase.** Only after termination + retention period + typing the tenant code, by a Platform Administrator. Irreversible; take a backup first if in doubt.
