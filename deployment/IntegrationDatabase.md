# Reusable SQL Integration Database

The integration initializer and V82/V83 SQL tests share `integration-database.config.json`. The approved target is `DESKTOP-DQ0868S / WhatsBizERP_IntegrationTest_V82`; the suffix is historical and is not incremented for later migrations. The legacy `WhatsBizERP_IntegrationTest` database is deliberately not an initializer target.

## Operator workflow

Run these commands from an authenticated Windows PowerShell session where `ConnectionStrings__IntegrationTests` contains the connection-string value for the configured target. The value is never echoed by the scripts.

```powershell
.\deployment\initialize-integration-database.ps1 -Mode Status
.\deployment\initialize-integration-database.ps1 -Mode DryRun
# Review both outputs. Apply is the only mode that can create the database or change schema.
.\deployment\initialize-integration-database.ps1 -Mode Apply -ConfirmTarget 'DESKTOP-DQ0868S/WhatsBizERP_IntegrationTest_V82' -CreateIfAbsent
```

An interrupted V40 publication is never adopted by `Apply`. First run the exhaustive read-only recovery audit:

```powershell
.\deployment\initialize-integration-database.ps1 -Mode AuditBaseline
```

It verifies the exact live server/database, database-level CONTROL, pinned historical commit and DACPAC schema-payload SHA-256, the V40 validator, a full DACPAC schema comparison, absence of later migration artifacts, exact source-derived seed aggregates, absence of business records, and baseline/migration metadata state. Only an `ELIGIBLE` result may be followed by the separately confirmed command below during a future explicitly authorized database-write task:

```powershell
.\deployment\initialize-integration-database.ps1 -Mode RecoverBaseline -ConfirmTarget 'DESKTOP-DQ0868S/WhatsBizERP_IntegrationTest_V82' -ConfirmRecovery 'REGISTER-V40:DESKTOP-DQ0868S/WhatsBizERP_IntegrationTest_V82'
```

`RecoverBaseline` repeats the complete audit while holding a dedicated exclusive application lock, then atomically registers only the canonical V40 baseline ledger, empty migration-history table, and disposable marker. It does not publish V40 or apply V41+. A later normal `Apply` continues with the pending canonical migrations.

`-CreateIfAbsent` is optional for an existing target. It only creates the configured database after a live server identity check, an absence check, explicit Apply mode and exact typed target confirmation. Existing databases are never dropped, reset, or recreated. Apply establishes the pinned V40 baseline only when the target is empty, then discovers, orders, validates and records every canonical migration after V40. Repeated runs skip matching ledger entries and stop on missing predecessors, hash mismatches, failed validators or ambiguous schema.

### Provisioning permissions

Creating a database is a one-time server-level operation. The initializer reports the current principal and whether it has `CREATE ANY DATABASE`; it does not request or grant that permission. If that permission is absent, an authorized SQL administrator may review and execute `provision-integration-database.sql`. The script verifies the exact server, requires `master`, refuses an existing target, and creates only the centrally configured database. It grants no permissions.

Subsequent schema migrations do not need `CREATE ANY DATABASE`. They require `CONTROL` scoped to the approved database, which the initializer checks before Apply. An administrator may either run the guarded initializer under an authorized execution identity or separately delegate database-scoped migration access according to local policy. No server-wide grant is required for migrations against the existing database.

## SQL integration tests

The V82 and V83 tests validate the central target before opening fixtures. Both migrations must already be applied and present in the canonical ledger with matching migration and validator hashes. The tests do not apply migrations. Fixture writes require explicit per-suite authorization and are cleaned up or rolled back by the tests.

```powershell
$env:WHATS_BIZ_V82_SQL_FIXTURE_AUTHORIZATION = 'I_AUTHORIZE_V82_SQL_FIXTURES'
$env:WHATS_BIZ_V83_ROLLBACK_TEST_AUTHORIZATION = 'I_AUTHORIZE_ROLLBACK_ONLY_V83_TESTS'
dotnet test .\backend\tests\WhatsBiz.Tests\WhatsBiz.Tests.csproj --filter "FullyQualifiedName~GuestVisitorAnalyticsSqlIntegrationTests|FullyQualifiedName~SupplierCatalogV83SqlIntegrationTests" --logger "console;verbosity=normal"
```

These SQL suites must not target the regular development database, the legacy integration database, QA, or PROD.
