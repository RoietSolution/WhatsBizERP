# GuturGo marketplace SQL integration tests

The existing rollback-only suite targets DEV, `DESKTOP-DQ0868S / WhatsBizERP`, using its established Windows Integrated Shared Memory connection. The separate service-owned suite in `MarketplaceV91ServiceSqlIntegrationTests.cs` targets only the operator-provisioned synthetic database `DESKTOP-DQ0868S / WhatsBizERP_GuturGoMarketplaceTest_V91`. It rejects DEV/QA/PROD and does not initialize, restore, migrate, reset, or drop any database. The old shared target `WhatsBizERP_IntegrationTest_V82` remains separate and must not be used for these tests.

Before rollback fixtures are written, each DEV test verifies exact SQL identity, canonical V40 baseline metadata, every V41–V91 migration and validator hash, the V40 validator, V85–V91 validators, and the disabled marketplace release gate. DEV fixtures always receive an explicit caller transaction and roll it back. Category B tests separately verify exact target, initializer disposable marker, canonical V40 seed tenant, absence of non-test tenant data, all V41–V91 hashes, V40 validator, V85–V91 validators, and disabled checkout gate before committed fixtures. Their target can only be provisioned through the operator workflow below. No real external provider is called.

Fixture seeding inserts its newly generated tenant first, then calls `sys.sp_set_session_context` with key `TenantId` and a `Guid`/SQL `uniqueidentifier` value, matching `ApplicationDbContext.SaveChangesAsync`. It verifies the resulting value before inserting warehouses or inventory. Cleanup clears the context when the transaction is still usable. The fixture connection has `Pooling=False`, so if a procedure already rolled back the transaction and context cleanup cannot execute, disposal closes the physical session instead of returning its tenant context to a pool.

## Operator command

Run this in the authenticated Windows PowerShell session that can already access the DEV target. Do not run if the DEV migration ledger does not show validated V91. This command does not apply migrations. The authorization variable is intentionally separate from the connection settings and must be set only for this rollback-only test run.

```powershell
# Optional read-only operator preflight in the established runner.
.\deployment\deploy-dev-database.ps1 -Mode Status

$env:WHATS_BIZ_DEV_MARKETPLACE_SQL_AUTHORIZATION = 'I_AUTHORIZE_DEV_MARKETPLACE_ROLLBACK_FIXTURES'
try {
  dotnet test .\backend\tests\WhatsBiz.Tests\WhatsBiz.Tests.csproj `
    --configuration Release `
    --filter 'FullyQualifiedName~MarketplaceV85V91SqlIntegrationTests'
  if ($LASTEXITCODE -ne 0) { throw "Marketplace SQL test run failed with exit code $LASTEXITCODE" }
}
finally {
  Remove-Item Env:WHATS_BIZ_DEV_MARKETPLACE_SQL_AUTHORIZATION -ErrorAction SilentlyContinue
}
```

If identity, V91 hashes/validators, release state, or fixture authorization do not pass, stop. Do not rerun against another database or bypass a preflight failure. Category A tests use the rollback-only DEV fixture. Category B tests below must never target DEV.

## Isolation and execution boundary

### Transaction boundaries

| Scenario | Class | Boundary and side effects |
|---|---|---|
| Reservation, order completion/release, settlement draft writer, V91 cancellation-review procedure | A | Existing SQL fixture attaches commands to one caller transaction and rolls it back. Marketplace procedures use nested transaction scopes; fixture rollback remains the outer boundary. No provider calls. |
| Payment attempt and webhook | B | `CommercePaymentService` opens and commits its own transaction. Payment intent creation happens after the obligation transaction commits. Webhook processing opens a separate transaction, applies ERP receipts/inventory effects, records provider-event identity and commits. The provider is fake-able, but committed service state cannot be rolled back by the DEV fixture. |
| COD reassignment and collection | B | `SqlIdempotencyExecutor` opens a serializable transaction and commits it. Collection applies a parent invoice payment and finance records; collection is group-assigned but parent-scoped. |
| Delivery-agent reads, assignment, transitions and OTP | B | `DeliveryService` opens/commits independent transactions. OTP request commits the challenge before notification processing. Notifications can be suppressed/faked, but persisted delivery/OTP state still commits. |
| Settlement review/approval and mark-paid recording | B | Settlement service opens serializable transactions and commits ledger/events. No transfer is executed; paid status requires separately confirmed evidence. |
| Marketplace order cancellation and group cancellation/refund-obligation writes | B | Lifecycle/idempotency or delivery service owns its transaction and commits reservation, group, stock disposition, refund and adjustment state. Refund preparation may follow a commit. |
| Actual customer refund, supplier payout, or real notification | C | Prohibited external money/notification traffic. Only fake transports or database obligation state may be tested. |
| Two simultaneous reservations for the last unit | B | The new test uses two independent SQL connections and service procedure calls. Rows commit in a synthetic-only V91 database; operator discards that database after integrity review. |

The central reusable V82 target is not V91 compatible and remains the default integration target. The exact Category B target was checked once from Codex using a read-only query to `master`; SQL Server access failed with client encryption/Windows security context errors before returning a result. No second connection attempt was made. Database existence and V91 readiness remain unverified. The service tests fail closed unless the exact dedicated target and separate authorization are supplied.

The initializer has a guarded V40-to-latest migration path, but its allowlist previously admitted only V82. The allowlist now separately admits this exact Stage 5A.5 database while leaving V82 as the default. Category B database creation and schema migration are distinct operator actions. The V40 initializer uses the pinned historical source commit and DACPAC payload hash, a read-only V40 validator/audit, a disposable marker, and the canonical V41–latest migration and validator ledger. The Category B create helper refuses to modify an existing database. No database creation or migration was executed.

The rollback-only suite verifies V85–V91 readiness and closed checkout release state; a GuturGo-owned and two partner sellers order; a same-product 2+1 warehouse split; `ITEM_GROSS` and `ITEM_TAXABLE` settlement draft calculations; unsupported agreement terms create `REVIEW_REQUIRED` with zero payable; no payable for owned inventory; reservation replay and tenant mismatch rejection; no inventory deduction during reservation; source-warehouse consumption exactly once; insufficient-stock rollback in the same connection/command; and reservation expiry/release without on-hand deduction. It also verifies duplicate V91 settlement posting rejection and idempotent creation of a sale-linked cancellation adjustment. The user-provided baseline reports **5/5 PASS** for this suite.

`MarketplaceStage5A2SourceContractTests` and `MarketplaceStage5A3TransactionBoundaryTests` are source-only checks. They do not connect to SQL and must not be described as SQL-backed coverage. The new payment/webhook test constructs the real `CommercePaymentService`, `SqlIdempotencyExecutor`, and `ErpPaymentPosting`, with a fake Razorpay gateway. The race test invokes `commerce.Marketplace_ReserveAllocation` concurrently on two SQL sessions. Both scenarios assert persisted SQL state, but their committed fixtures remain in the disposable test database for operator discard.

For this turn, the rollback-only SQL suite was attempted once with the documented authorization value. All five test cases failed to open the SQL connection with `Failed to generate SSPI context`; no test passed its database identity check and no fixture writes ran. Per instruction, do not repeat connectivity investigation. The source-only checks and build are reported separately.

Run the rollback-only SQL suite from an authenticated Windows PowerShell session that can reach the specified DEV instance. It verifies identity, V85–V91 migration and validator hashes, validators and the disabled checkout gate before fixture writes:

```powershell
$env:WHATS_BIZ_DEV_MARKETPLACE_SQL_AUTHORIZATION = 'I_AUTHORIZE_DEV_MARKETPLACE_ROLLBACK_FIXTURES'
try {
  dotnet test .\backend\tests\WhatsBiz.Tests\WhatsBiz.Tests.csproj `
    --configuration Release `
    --filter 'FullyQualifiedName~MarketplaceV85V91SqlIntegrationTests'
  if ($LASTEXITCODE -ne 0) { throw "Marketplace SQL test run failed with exit code $LASTEXITCODE" }
}
finally {
  Remove-Item Env:WHATS_BIZ_DEV_MARKETPLACE_SQL_AUTHORIZATION -ErrorAction SilentlyContinue
}
```

## Category B operator provisioning and execution

For this execution, use the operator-controlled V91 DEV-backup restore followed by sanitization. The repository does not contain a DEV-to-test restore script; the operator must use the already approved backup/restore procedure and must not restore over an existing target automatically. Do not create/reset this target or run initializer Apply; no V40–V91 migrations are replayed. Keep application hosts and background workers stopped. Both tests commit synthetic fixture rows; database-level disposal is required after the run and requires its own operator authorization because financial and inventory history is not safely deletable row-by-row.

For a restored copy, do not create or alter the canonical initializer marker and do not replay migrations. The separate readiness script verifies SQL Server restore history from `WhatsBizERP`, exact V40/V41–V91 ledger hashes, read-only validators, disabled checkout, canonical seed/business-data checks and the absence of live provider credentials. It supports read-only `Audit` and separately authorized `Register`; registration writes only `WhatsBiz.GuturGoMarketplaceRestoredV91Readiness`. The service tests accept that record only when it matches the exact target and local migration/validator fingerprint, then repeat the tenant-scope, credential, ledger, validator and feature-gate checks before fixture writes.

Run from the operator's authenticated Windows PowerShell session. First configure the exact Integrated Security connection string:

```powershell
$sqlBuilder = [Microsoft.Data.SqlClient.SqlConnectionStringBuilder]::new()
$sqlBuilder['Data Source'] = 'DESKTOP-DQ0868S'
$sqlBuilder['Initial Catalog'] = 'WhatsBizERP_GuturGoMarketplaceTest_V91'
$sqlBuilder['Integrated Security'] = $true
$sqlBuilder['Encrypt'] = $false
$sqlBuilder['Trust Server Certificate'] = $true
$sqlBuilder['Pooling'] = $false
$testConnection = $sqlBuilder.ConnectionString
$env:ConnectionStrings__GuturGoMarketplaceTests = $testConnection
# Read-only identity/status checks only. Do not create/reset or migrate this target.
.\deployment\provision-marketplace-v91-test-database.ps1 -Mode Status
.\deployment\initialize-integration-database.ps1 -Mode Status
```

For an operator-restored V91 copy, use the dedicated schema-aware sanitizer. The generated `RCDEV008-RuntimeObjects.sql` deployment source intentionally retains enabled-but-untrusted state for selected legacy constraints; the sanitizer reports and preserves those constraints, then checks actual post-cleanup integrity. Run `Diagnose` first: it is read-only, reports every FK state, and runs `DBCC CHECKCONSTRAINTS`. The separately authorized `Sanitize` mode reports disabled FKs and enables them only with `WITH CHECK CHECK`, so SQL Server validates all surviving rows; a violation rolls back cleanup. The sanitizer requires separate authorization and exact target confirmation, verifies restore provenance, the V40 baseline and V41–V91 source/validator hashes, keeps checkout disabled, deletes non-reference data child-first in one serializable transaction, and rolls back on any failed precondition or postcondition. It does not touch `deployment` migration history, apply migrations, or register readiness. Review diagnosis and target-specific consequences before authorizing writes:

```powershell
$env:ConnectionStrings__GuturGoMarketplaceTests = 'Server=DESKTOP-DQ0868S;Initial Catalog=WhatsBizERP_GuturGoMarketplaceTest_V91;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;Pooling=False'
.\deployment\sanitize-marketplace-v91-restored-copy.ps1 -Mode Diagnose
# Review FK states and DBCC violation counts before authorizing committed cleanup.
$env:WHATS_BIZ_GUTURGO_MARKETPLACE_TEST_SANITIZATION_AUTHORIZATION = 'I_AUTHORIZE_SANITIZING_RESTORED_V91_TEST_DATABASE'
try {
  $sanitization = .\deployment\sanitize-marketplace-v91-restored-copy.ps1 -Mode Sanitize `
    -ConfirmTarget 'DESKTOP-DQ0868S/WhatsBizERP_GuturGoMarketplaceTest_V91'
  $sanitization.SanitizationReference
}
finally {
  Remove-Item Env:WHATS_BIZ_GUTURGO_MARKETPLACE_TEST_SANITIZATION_AUTHORIZATION -ErrorAction SilentlyContinue
}
```

Save the sanitizer's emitted reference, then run the read-only audit:

```powershell
.\deployment\register-marketplace-v91-restored-copy-readiness.ps1 -Mode Audit
# Review PASS output and the restore/sanitization evidence before the separate registration action.
$env:WHATS_BIZ_GUTURGO_MARKETPLACE_TEST_READINESS_AUTHORIZATION = 'I_AUTHORIZE_REGISTERING_SANITIZED_RESTORED_V91_TEST_DATABASE'
.\deployment\register-marketplace-v91-restored-copy-readiness.ps1 -Mode Register `
  -ConfirmTarget 'DESKTOP-DQ0868S/WhatsBizERP_GuturGoMarketplaceTest_V91' `
  -RestoreReference '<backup set id or approved restore record>' `
  -SanitizationReference '<approved sanitization record>'
Remove-Item Env:WHATS_BIZ_GUTURGO_MARKETPLACE_TEST_READINESS_AUTHORIZATION -ErrorAction SilentlyContinue
```

`Audit` is read-only. `Register` does not restore, sanitize, mark the database with the canonical initializer property, or apply migrations. It fails closed unless the exact restored target passes all readiness checks.

After the restored-copy readiness record is registered, run the tests with separate committed-fixture authorization:

```powershell
$env:WHATS_BIZ_GUTURGO_MARKETPLACE_TEST_SQL_AUTHORIZATION = 'I_AUTHORIZE_SYNTHETIC_V91_MARKETPLACE_TEST_DATABASE'
try {
  # Build before Category B test execution.
  dotnet build .\backend\tests\WhatsBiz.Tests\WhatsBiz.Tests.csproj -c Release --no-restore
  if ($LASTEXITCODE -ne 0) { throw "Release test build failed with exit code $LASTEXITCODE" }
  dotnet test .\backend\tests\WhatsBiz.Tests\WhatsBiz.Tests.csproj `
    --configuration Release `
    --no-build `
    --filter 'FullyQualifiedName~MarketplaceV91ServiceSqlIntegrationTests'
  if ($LASTEXITCODE -ne 0) { throw "GuturGo committed SQL integration run failed with exit code $LASTEXITCODE" }
}
finally {
  Remove-Item Env:ConnectionStrings__GuturGoMarketplaceTests -ErrorAction SilentlyContinue
  Remove-Item Env:WHATS_BIZ_GUTURGO_MARKETPLACE_TEST_READINESS_AUTHORIZATION -ErrorAction SilentlyContinue
  Remove-Item Env:WHATS_BIZ_GUTURGO_MARKETPLACE_TEST_SQL_AUTHORIZATION -ErrorAction SilentlyContinue
}
```

Never substitute DEV, QA, PROD, or the V82 integration target. `Mode Create` fails if the named database already exists and does not reset it. If Status reports an existing target, review it read-only and do not run Create or Apply until its provenance and state are approved. Current Stage 5A.5 service-level SQL coverage is limited to online payment/webhook and concurrent reservation. COD collection, delivery OTP, settlement approval/evidence, and service-owned cancellation/refund reconciliation still require Category B tests before release; the existing Category A settlement/refund procedure tests do not cover those service boundaries.
