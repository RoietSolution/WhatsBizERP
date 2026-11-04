# WhatsBiz database migrations

The database migration runner is database-only. It does not build/publish a DACPAC, run historical scripts, stop/restart an application service, or deploy an API or frontend.

## Managed migration files

Managed migrations live in **database/migrations/** and use **V<number>-Description.sql**. Numeric versions must be unique and contiguous after the recorded baseline. Optional read-only validators live in **database/migrations/validators/** and use the same file stem plus **.validate.sql**.

Example:

- **database/migrations/V43-NewFeature.sql**
- **database/migrations/validators/V43-NewFeature.validate.sql**

The runner discovers these files at invocation time, orders them numerically, calculates SHA-256 hashes and executes pending files in order. No runner or SQL project change is needed for a new migration.

Applied migration SQL and its validator are immutable. A changed hash, renamed file, missing applied file, duplicate version or gap blocks execution. Correct deployed schema with a new migration.

### V75 City Marketplace media

V75 adds the city-scoped media reference table used by the Application Owner's City Marketplace logo/banner upload. It depends on the V74 `commerce.Cities` table and stores optimized media through the existing storefront media provider. The upload endpoints do not accept media IDs from city-edit payloads; associations are made only by the owner-authorized media endpoints. Run `-Status` and `-DryRun` before any deployment.

### V74 pending-migration correction record

V74's canonical tenant references target `core.Tenants(TenantId uniqueidentifier)`. Retailer route backfill preserves `TenantKey` exactly and permits existing underscores in retailer aliases; city slugs remain letters/digits/interior-hyphens only. The pending V74 validator also checks the actual `CK_Cities_Name` constraint. These V74 source/validator hashes replace the hashes of the unapplied files; no applied ledger entries were rewritten:

- V74 migration SHA-256: `EBF6FD6D809D9D8523DFDFE54B6126E50C29AA424DAA5E1004F666EA0F6D14AB`
- V74 validator SHA-256: `EFF0396991F24010D0DB102B4916B3E4D0F09E3959D8E0B3F849F4923394FA85`
- V75 migration SHA-256 (unchanged): `7E4EE5D2EC4B00EB02E7B4004D7C9952D67FA3B835CE53D4359358EDAC2004CA`
- V75 validator SHA-256 (unchanged): `5F05E571EFF7E948898B1E3A1FE312674E32337C891E5B245B524E40DC5D71B1`

The dedicated disposable database `DESKTOP-DQ0868S/WhatsBizERP_PosGstIntegrationTest` was initialized through V75 for rollback-only verification. The QA/PROD status in this historical note reflects the status at that time and is not a current migration-ledger assertion.

### V77 City Marketplace banner copy

V77 adds nullable, length-bounded `BannerHeadline` and `BannerSubtitle` fields to `commerce.Cities`. It does not change existing city media storage or publication state. Apply only through the canonical migration runner after normal environment preflight; V77 has not been deployed by this UI implementation task.

- V77 migration SHA-256: `FED7A4467D91AFAE69012714A886573665FAD3CDDCF8AF9C317AD8EA99E1CDB7`
- V77 validator SHA-256: `A7181C39BD04820470CBD3AF8D48070ABCB1F90A10546A21BAD929929C069728`

## V40 historical baseline

The repository owns a read-only V40 validator at **database/migrations/validators/Baseline-V40.validate.sql**. Baseline creation is an explicit write operation. It first verifies the actual V40-era schema, requires a verified pre-change backup, then creates deployment-owned history tables and records the baseline validator hash. It does not mark V41+ as applied.

Use only for a database whose actual schema passes the V40 validator:

~~~powershell
.\deployment\deploy-database.ps1 -Environment QA -InitializeBaseline
.\deployment\deploy-database.ps1 -Environment QA -Status
~~~

For PROD, include **-ConfirmProduction** on the write command. The QA/PROD environment label alone never authorizes a write.
### Metadata visibility prerequisite

Before inspecting migration history or running a schema validator, the runner checks that the current database user has database-level `VIEW DEFINITION`. This prevents hidden metadata from being misreported as a missing or invalid schema object. An authorized database administrator can grant the least-privilege prerequisite separately in each target database:

~~~sql
GRANT VIEW DEFINITION TO [deployment_database_user];
~~~

Use the dedicated deployment database user as the principal. The runner never grants permissions automatically. Do not grant `sysadmin`, `db_owner`, or server-level permissions for this check. Migration execution and backup operations may require additional separately reviewed permissions.

## QA adoption of the already-applied V41/V42 state

After explicit V40 baseline initialization, adoption checks actual schema with the read-only validators, confirms the current migration script hashes, makes and verifies a pre-adoption backup, then registers the migration hashes without running migration SQL:

~~~powershell
.\deployment\deploy-database.ps1 -Environment QA -AdoptExisting -ThroughVersion 42
.\deployment\deploy-database.ps1 -Environment QA -Status
~~~

The **ThroughVersion** is an operator-specified upper bound, not runner logic. Adoption requires a validator for every migration being adopted. It is not a general substitute for deploying a migration.

## QA workflow

~~~powershell
.\deployment\deploy-database.ps1 -Environment QA -Status
.\deployment\deploy-database.ps1 -Environment QA -DryRun
.\deployment\deploy-database.ps1 -Environment QA
.\deployment\deploy-database.ps1 -Environment QA -Status
~~~

QA connectivity uses the existing SSH tunnel and a dedicated deployment connection. The configured and live targets must be **WhatsBizERP_QA** on **srv1930195**. It never falls back to `/etc/whatsbiz/qa.env` or `ConnectionStrings__DefaultConnection`.

## PROD workflow

The current production SQL connection must be provided through the established process environment variable **WHATSBIZ_PROD_SQL_CONNECTION**; the runner never reads a tracked file or prints the connection string.

For a repeatable read-only PROD connectivity check, use the tunnel wrapper. It requires explicit production-only SSH settings (`WHATSBIZ_PROD_SSH_HOST`, `WHATSBIZ_PROD_SSH_USER`, and `WHATSBIZ_PROD_SSH_KEY`), verifies the local OpenSSH forward to `srv1930195:1433`, validates encrypted SQL identity, and then invokes the canonical runner with `-Status`. If `WHATSBIZ_PROD_SQL_CONNECTION` is absent, it prompts for the SQL login without saving the password.

~~~powershell
$env:WHATSBIZ_PROD_SSH_HOST = '<verified-production-vps-host>'
$env:WHATSBIZ_PROD_SSH_USER = '<production-ssh-user>'
$env:WHATSBIZ_PROD_SSH_KEY = "$env:USERPROFILE\.ssh\<production-key>"
.\deployment\connect-prod-database.ps1
~~~

The wrapper performs no backup, migration, baseline, schema or data write. Do not replace the production SSH values with QA values.

~~~powershell
.\deployment\deploy-database.ps1 -Environment PROD -Status
.\deployment\deploy-database.ps1 -Environment PROD -DryRun
# Only after reviewing the dry run and approved baseline:
.\deployment\deploy-database.ps1 -Environment PROD -InitializeBaseline -ConfirmProduction
# After the baseline exists:
.\deployment\deploy-database.ps1 -Environment PROD -DryRun
.\deployment\deploy-database.ps1 -Environment PROD -ConfirmProduction
.\deployment\deploy-database.ps1 -Environment PROD -Status
~~~

PROD writes always require **-ConfirmProduction**. The expected live identity is server **srv1930195**, database **WhatsBizERP_PROD**. No automation or CI deployment is configured by this framework.

The last recorded PROD precheck in this repository session found V39/V40 effects missing. Such a database must fail the V40 baseline validator and must not be baselined or receive V41/V42 until its historical schema is safely reconciled and the validator passes.

## Backups and external VERIFYONLY approval

A write operation with pending migrations creates one **COPY_ONLY,CHECKSUM,NOINIT** backup whose filename contains the environment database, pending range and timestamp/random suffix. No backup is created when no migrations are pending. Baseline initialization and history adoption also require a pre-write backup because they write metadata.

The runner executes **RESTORE VERIFYONLY WITH CHECKSUM** before any history/schema change. A verification failure stops execution and writes a local, non-secret pending-verification manifest containing the exact target, plan fingerprint, backup media ID/size/time, backup path and one-time token. The runner also confirms those facts against SQL Server backup history on resume.

An authorized administrator then verifies the exact path printed by the runner:

~~~sql
RESTORE VERIFYONLY
FROM DISK = N'<exact path printed by the runner>'
WITH CHECKSUM;
~~~

After the administrator confirms SQL Server's valid-backup result, resume the same command with the exact emitted path/token and an auditable operator/time reference:

Repeat the operation mode that originally stopped, with the emitted values:

~~~powershell
# Normal migration deployment
.\deployment\deploy-database.ps1 -Environment QA -ResumeAfterBackupVerification -VerifiedBackupPath '<exact path>' -VerificationToken '<one-time token>' -AdminVerificationReference '<operator, UTC time, and verification result>'
# Baseline initialization
.\deployment\deploy-database.ps1 -Environment QA -InitializeBaseline -ResumeAfterBackupVerification -VerifiedBackupPath '<exact path>' -VerificationToken '<one-time token>' -AdminVerificationReference '<operator, UTC time, and verification result>'
# Existing migration adoption (preserve the original upper bound)
.\deployment\deploy-database.ps1 -Environment QA -AdoptExisting -ThroughVersion 42 -ResumeAfterBackupVerification -VerifiedBackupPath '<exact path>' -VerificationToken '<one-time token>' -AdminVerificationReference '<operator, UTC time, and verification result>'
~~~

For PROD use **-Environment PROD -ConfirmProduction** too. Resume rejects a different environment, database, plan, path, token, or changed backup media metadata. It does not create another backup. Never resume until the authorized administrator has actually run **RESTORE VERIFYONLY**.

## Execution and failure behavior

Each migration runs in its own SQL transaction by default, including its optional validator and history insertion. A migration may opt out only by placing **-- WhatsBiz-Migration-Transaction: NONE** in its first six lines for SQL Server operations that cannot run in a transaction; such migrations must be independently restart-safe. **GO** batch separators are supported. SQLCMD directives (**:r**, **:setvar**) are deliberately rejected.

A failed migration or validator is not recorded and later migrations are not attempted. Earlier successful migrations remain recorded; rerun Status/DryRun and deploy to resume at the first pending migration. Validator files are statically screened for common write/administrative SQL verbs and must remain read-only. DryRun and Status do not execute validators that might write; they only inspect identity, files, hashes, baseline/history and plan state.

## PostDeployment / DACPAC boundary

**database/migrations/** is outside **WhatsBiz.Database.sqlproj**, so managed migration files are not DACPAC build inputs. V41/V42 references were removed from **Scripts/PostDeployment.sql**; this prevents DACPAC publishing from applying them a second time. The existing historical PostDeployment chain remains unchanged through V40. Repeatable seed/configuration scripts remain in PostDeployment. The migration runner is the sole owner of V41+ versioned migrations.

The recommended deployment workflow is now **only** `deployment/deploy-database.ps1` for incremental QA and PROD database releases. `deployment/deploy-prod-database.ps1` remains a reviewed legacy DACPAC fallback while the V40 baseline is reconciled; it is not called by the runner and must never be run in the same release window as the runner. The DACPAC fallback must not be used to apply V41 or later migrations.

V48 (`V48-TenantScopedWarehouseUniqueness.sql`) is discovered automatically with its read-only validator. It is applied only after V41 through V47 are recorded, unless an operator adopts already-applied versions after their validators pass. The V48 migration checks for tenant-local conflicts before replacing the legacy warehouse indexes and records its script and validator hashes in migration history.

For an existing DACPAC-managed database, use `-Status` and `-DryRun` first. If the V40 validator passes and the migration tables are absent, run `-InitializeBaseline`; this records only the verified V40 baseline and does not replay V1-V40. If later schema changes are already present, use `-AdoptExisting -ThroughVersion N` only for the explicitly verified range. Adoption records immutable hashes and does not execute migration SQL. Do not edit historical rows to make a plan pass.

## Focused tests

Run the standalone PowerShell tests:

~~~powershell
.\deployment\tests\Test-DatabaseMigration.Core.ps1
~~~

They exercise numeric ordering, integrity/gaps, identity and PROD confirmation, dry-run/status write barriers, backup failure, sequential failure/resume, validation-before-recording, adoption and future-version discovery without database access.


## Deployment principal and automatic backup verification

The migration runner uses a dedicated deployment SQL principal. It must not use the API/runtime SQL login. QA resolves its deployment connection in this order: `WHATSBIZ_QA_SQL_CONNECTION` environment override, encrypted per-user DPAPI configuration at `%LOCALAPPDATA%\WhatsBiz\DatabaseDeployment\qa.connection.dpapi`, then an actionable configuration error. PROD continues to use `WHATSBIZ_PROD_SQL_CONNECTION`. Neither value is tracked or printed. The API service keeps its existing restricted connection separately.

The runner performs a preflight before creating a backup and reports only the principal name and pass/fail capability checks. The deployment principal requires:

- `BACKUP DATABASE` permission on the target database;
- `CREATE ANY DATABASE` at server scope. SQL Server requires the corresponding `CREATE DATABASE` capability for `RESTORE VERIFYONLY`/backup-set inspection, even when the target database already exists;
- `CONTROL` on the target database. The current managed migration set can create/alter schemas, tables, procedures, indexes, validators, and migration history, so `db_owner` is the practical least-privilege fixed role for the dedicated deployment principal unless a separately reviewed custom role is maintained for the complete migration set.

Do not grant these permissions to the API login and do not grant `sysadmin`.

### One-time administrator setup

Run the secure wrapper once for each target database. It prompts for the deployment-login password twice, optionally prompts for SQL administrator credentials, invokes the SQL bootstrap with a short-lived temporary script, rotates the existing login password safely, and saves the deployment connection encrypted with Windows DPAPI for the same operator account. It never prints a password. The underlying SQL script is also idempotent and contains no password. If SQL bootstrap succeeds but local DPAPI persistence fails, the wrapper reports that the SQL login/user may already exist and the same setup command can be retried safely. Setup and the migration runner use the same Base64-wrapped Windows DPAPI CurrentUser format; the scripts load the required ProtectedData assembly at runtime for Windows PowerShell 5.1 and PowerShell 7. For QA:

~~~powershell
.\deployment\setup-database-deployment-principal.ps1 -Environment QA

# Add -AdminUser '<approved SQL administrator>' only when Windows integrated administrator authentication is unavailable.
~~~

The setup must be repeated explicitly against `WhatsBizERP_PROD` by an administrator before production use. This repository task does not execute the setup or modify SQL permissions.

After setup, the normal runner automatically reads the encrypted per-user QA connection. `WHATSBIZ_QA_SQL_CONNECTION` remains an optional environment override and must contain `Initial Catalog=WhatsBizERP_QA` with the `WhatsBizDatabaseDeployment` credential. Do not point the API service at it. Run the wrapper as the same Windows deployment operator who runs the migration command so DPAPI can decrypt the configuration.

### Normal workflow

~~~powershell
.\deployment\deploy-database.ps1 -Environment QA -Status
.\deployment\deploy-database.ps1 -Environment QA -DryRun
.\deployment\deploy-database.ps1 -Environment QA

.\deployment\deploy-database.ps1 -Environment PROD -Status
.\deployment\deploy-database.ps1 -Environment PROD -DryRun
.\deployment\deploy-database.ps1 -Environment PROD -ConfirmProduction
~~~

A normal write automatically performs preflight, `COPY_ONLY,CHECKSUM` backup, `RESTORE VERIFYONLY WITH CHECKSUM`, migration execution, read-only validator, and history recording. A preflight failure occurs before the backup is created. VERIFYONLY remains mandatory; a failed verification never continues to migrations.

V81 adds a retailer-scoped mutable delivery-address draft on intake requests and immutable JSON address snapshots on each quote and Sales Order. It depends on V79 and V80, validates JSON and the required fields/PIN for delivery quotes and Sales Orders at the database boundary, and does not link a customer by phone number. Column creation and constraints are separated by a runner-supported `GO` batch boundary to avoid SQL Server compiling references before the new columns exist. QA `-Status` reports V81 column/constraint state and existing V80 delivery-row counts read-only. Confirm the target ledger and V79/V80 hashes before applying; this source change does not apply V81.

If a previous run left a pending verification manifest, the next normal write first validates the exact recorded backup metadata and automatically runs `RESTORE VERIFYONLY`. It then clears the manifest only after successful verification and continues. This makes the existing pending V47 backup eligible for safe automatic verification after the deployment principal is configured; no new backup is silently substituted.

### Emergency manual resume

The existing controlled fallback remains available when the deployment principal is unavailable or an administrator deliberately performs verification outside the runner. Run `RESTORE VERIFYONLY ... WITH CHECKSUM` against the exact path and then resume with the exact path/token and an audit reference:

~~~powershell
.\deployment\deploy-database.ps1 -Environment QA -ResumeAfterBackupVerification `
  -VerifiedBackupPath '<exact pending path>' `
  -VerificationToken '<exact token>' `
  -AdminVerificationReference '<operator, UTC time, and result reference>'
~~~

The runner still checks the pending manifest, plan fingerprint, SQL backup history, media ID, size, completion time, checksum, and COPY_ONLY flags. It never treats a failed VERIFYONLY as permission to continue.
