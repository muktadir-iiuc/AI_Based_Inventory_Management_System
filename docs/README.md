# SaaS edition documentation

| Document | Read it for |
|---|---|
| [SAAS_PLAN.md](SAAS_PLAN.md) | Original analysis of the existing system and the design decisions |
| [DEPLOYMENT.md](DEPLOYMENT.md) | Requirements, configuration keys, first-run checklist, proxy notes, SSLCOMMERZ go-live |
| [UPGRADE_AND_ROLLBACK.md](UPGRADE_AND_ROLLBACK.md) | Moving an existing installation to this release, SQL scripts, backup first, rollback rules |
| [OPERATIONS.md](OPERATIONS.md) | Backups and restore, monitoring, runbooks |
| [PLATFORM.md](PLATFORM.md) | Platform administration: pages, roles, tenant lifecycle, sign-up |
| [PAYMENTS.md](PAYMENTS.md) | Payment flow, SSLCOMMERZ specifics, manual payments, refunds |
| [SECURITY.md](SECURITY.md) | Isolation and security controls, known gaps |
| [CHANGES.md](CHANGES.md) | Every change to existing files and database objects |
| [TESTING.md](TESTING.md) | Automated tests and the regression pass, plus what is not covered |

SQL scripts: `deploy/sql/01-upgrade-to-saas.sql` (idempotent), `deploy/sql/99-rollback-to-pre-saas.sql`.
