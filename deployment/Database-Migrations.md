# WhatsBiz database migrations

The database migration runner is database-only. It does not build/publish a DACPAC, run historical scripts, stop/restart an application service, or deploy an API or frontend.

## Managed migration files

Managed migrations live in **database/migrations/** and use **V<number>-Description.sql**. Numeric versions must be unique and contiguous after the recorded baseline. Optional read-only validators live in **database/migrations/validators/** and use the same file stem plus **.validate.sql**.

Example:

- **database/migrations/V43-NewFeature.sql**
- **database/migrations/validators/V43-NewFeature.validate.sql**

The runner discovers these files at invocation time, orders them numerically, calculates SHA-256 hashes and executes pending files in order. No runner or SQL project change is needed for a new migration.

Applied migration SQL and its validator are immutable. A changed hash, renamed file, missing applied file, duplicate version or gap blocks execution. Correct deployed schema with a new migration.

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

QA connectivity reuses the existing SSH tunnel and **/etc/whatsbiz/qa.env** deployment configuration. The configured and live targets must be **WhatsBizERP_QA** on **srv1930195**.

## PROD workflow

The current production SQL connection must be provided through the established process environment variable **WHATSBIZ_PROD_SQL_CONNECTION**; the runner never reads a tracked file or prints the connection string.

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

## Focused tests

Run the standalone PowerShell tests:

~~~powershell
.\deployment\tests\Test-DatabaseMigration.Core.ps1
~~~

They exercise numeric ordering, integrity/gaps, identity and PROD confirmation, dry-run/status write barriers, backup failure, sequential failure/resume, validation-before-recording, adoption and future-version discovery without database access.

