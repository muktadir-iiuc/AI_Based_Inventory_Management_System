# Upgrading an existing installation to the SaaS release, and rolling back

The SaaS release adds tenants to every business table. Treat it like any schema change: **take a verified full backup first.**

## 0. Before anything

1. Announce a maintenance window; stop the old application (so nothing writes during the backup).
2. Full database backup, copy-only, and copy the file off the server:
   ```sql
   BACKUP DATABASE [YourDb] TO DISK = N'D:\Backups\YourDb_pre_saas.bak' WITH COPY_ONLY, CHECKSUM, INIT, FORMAT;
   RESTORE VERIFYONLY FROM DISK = N'D:\Backups\YourDb_pre_saas.bak' WITH CHECKSUM;
   ```
3. Back up the old application folder / publish output and its `appsettings.json`.
4. **Rehearse** on a restored copy of the backup (section 5). It takes minutes and is the cheapest insurance you have.

## 1. Upgrade

Choose one:

**A. Let the app migrate (default).** Deploy the new build, set the new configuration (DEPLOYMENT.md), start it. `Database.MigrateAsync()` applies the four new migrations.

**B. Apply the script yourself (recommended for production).**
```
sqlcmd -S <server> -d <YourDb> -b -i deploy/sql/01-upgrade-to-saas.sql
```
The script is idempotent (safe to run twice) and wraps each migration in a transaction. Then deploy the new build with `Database__AutoMigrate=false`.

Migrations applied: `AddMultiTenancy`, `AddSaasBilling`, `AddPaymentGateway`, `AddTenantPurgedAt`, `AddTenantAccessConfig`.

## 2. What the upgrade does to your data

* Creates tenant **1**, named after your Company Settings, and gives **every existing row of every business table, and every existing user, `TenantId = 1`**. Nothing is deleted or rewritten.
* Rebuilds the unique indexes (SKU, warehouse code, invoice/return/transfer/adjustment/journal numbers, account codes, petty-cash names) as per-tenant indexes.
* On first start the app attaches tenant 1 to the *Internal / Legacy* plan: Active, all modules, no limits, no invoices - the business behaves exactly as before.
* Creates the platform roles. A Platform Administrator is created only if `Platform:AdminEmail/AdminPassword` are configured.
* `Database Backup` moves to platform administrators only (a whole-database backup contains every tenant's data). Tenant Admin/Manager lose that menu item.
* Everyone must sign in again once (old cookies carry no tenant claim).

## 3. After the upgrade: verify

Run the checks in section 5 against production data counts (they only read): row counts per table unchanged, all business rows `TenantId = 1`, `DBCC CHECKCONSTRAINTS` clean, `/health` = `ok`, a manager can open Products, a sales invoice and its PDF.

## 4. Rollback

Pick by situation:

| Situation | Action |
|---|---|
| Upgrade failed half way (script/migration error) | The script runs each migration in a transaction. Fix the cause and re-run; the script skips what is already applied. If in doubt, restore the pre-upgrade backup. |
| New release has a bug, **no second tenant exists yet** (only tenant 1) | Run `deploy/sql/99-rollback-to-pre-saas.sql`, then redeploy the old build. This was verified to restore both data and schema byte-for-byte on a copy of a real database. |
| **Other tenants have been onboarded** | Do **not** run the rollback script - merging tenants back into a single-business schema can violate the old global unique indexes (two tenants' `WH-01`) and would destroy tenant boundaries. Restore the pre-upgrade backup (losing everything written since), or keep running the new release. |
| Old build against the upgraded database | Not supported: the old model does not know `TenantId`, so inserts fail. Always roll the database back together with the application. |

Rollback with a restore:
```sql
ALTER DATABASE [YourDb] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
RESTORE DATABASE [YourDb] FROM DISK = N'D:\Backups\YourDb_pre_saas.bak' WITH REPLACE;
ALTER DATABASE [YourDb] SET MULTI_USER;
```
then redeploy the old application folder.

## 5. Rehearsal / verification recipe (what was run for this release)

1. `RESTORE ... AS YourDb_Rehearsal` from the backup (use `WITH MOVE`).
2. Snapshot: row count of every table + a few aggregates (stock total, sales line value, journal debit = credit, payments total).
3. Apply the upgrade script (twice, to prove idempotence). Re-snapshot: **identical**.
4. `SELECT COUNT(*) ... WHERE TenantId <> 1` on every tenant table: 0. `DBCC CHECKCONSTRAINTS WITH ALL_CONSTRAINTS`: no rows.
5. Apply the rollback script. Re-snapshot data and compare the schema (columns, indexes, foreign keys): **identical to the original**.

Result on a copy of `InventoryManagementDb` (89 products, 5 sales invoices, 9 users, 1.63 M debit = credit in the journal): all of the above passed, both through `dotnet ef database update` and through the generated scripts.

## 6. Regenerating the scripts

```
dotnet ef migrations script 20261003124401_AddPaymentDiscount --idempotent -o deploy/sql/01-upgrade-to-saas.sql
dotnet ef migrations script 20261010133912_AddDataExportAndAgreement 20261003124401_AddPaymentDiscount -o deploy/sql/99-rollback-to-pre-saas.sql
```
(Adjust migration ids if later migrations are added.)
