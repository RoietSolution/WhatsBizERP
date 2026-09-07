# WhatsBiz QA tenant-hardening deployment runbook

Status: **NOT READY FOR QA DEPLOYMENT**. This is a preparation document only; it does not authorize a QA or Production change.

## Blocking application findings

1. `DeliveryService.RecordCod` calls `sales.POS_AddPayment` without the required `@TenantId`. After V24, a positive COD collection will fail before posting. The caller must pass the authenticated `tenantId`; the existing SQL session context remains defense-in-depth.
2. `WhatsAppCommerceService.GetSetupAsync` returns every active warehouse because its warehouse query has no tenant predicate. `GetReadinessAsync` repeats that unscoped warehouse check, and its stock readiness query constrains the product tenant but not `InventoryBalances.TenantId` or `Warehouses.TenantId`. These are cross-tenant read/isolation defects.

The known 4/220 test failures are separate: two fail during SQL fixture setup because the test login lacks object permissions, and two fail while constructing `WebApplicationFactory`/`IServiceProvider` before an API request. No production-code workaround should be made for those four failures.

## Procedure caller and direct-writer audit

| Runtime path | Result |
|---|---|
| `POSEngine` -> POS post/payment/return | Current authenticated tenant is passed as `@TenantId`. |
| `PurchaseEngine` -> purchase post/payment/return | Current authenticated tenant is passed as `@TenantId`. |
| `InventoryOperationsRepository` -> adjustment/transfer/verification post | Current authenticated tenant is passed as `@TenantId`. |
| `FinanceRepository` -> party posting | Current authenticated tenant is passed and SQL session context is set. |
| `ReceivablesRepository` -> receipt/payment | Current authenticated tenant is passed; the shared executor establishes session context. |
| `DashboardRepository` -> hardened Finance/customer/supplier/notification procedures | Current authenticated tenant is passed and session context is set. |
| `DeliveryService` -> POS payment | **BLOCKER:** obsolete call signature; `@TenantId` is absent. |

No C# direct INSERT/UPDATE writer was found for the seven audited ownership headers. Supplier and warehouse changes use EF and `ApplicationDbContext.SaveChangesAsync` stamps the authenticated tenant; POS, purchase, inventory-operation and journal creation use procedures. V24 triggers remain a defense against legacy direct table writes.

Three still-reachable legacy inventory calls (`Inventory_Adjust`, `Inventory_Transfer`, `Inventory_Reserve`) do not expose `@TenantId`; their caller runs through `SqlIdempotencyExecutor`, which establishes authenticated session context, and V24 stamps/validates the guarded balance/transaction rows. They cannot silently cross tenants, but should be included in the QA smoke because their unscoped internal lookups may reject otherwise valid work when duplicate product/warehouse shapes exist across tenants.

Repository SQL sources and `RCDEV008-RuntimeObjects.sql` still contain pre-hardening procedure definitions. They are not the final QA runtime definitions: every DACPAC publish that executes RCDEV008 must be followed by V18, V24 and V26 in that order. `VerifyPOS.sql` and `VerifyPurchase.sql` use obsolete signatures and must not be used as post-hardening QA smoke scripts.

## V7-V26 classification

There are two files for several version numbers. The feature stream is already referenced by `Scripts/PostDeployment.sql`; the tenant-hardening stream is deliberately applied after DACPAC PostDeployment because `RCDEV008-RuntimeObjects.sql` recreates older procedure definitions.

| Script | Classification | QA handling |
|---|---|---|
| V7-CustomerTenantIsolation | REQUIRED FOR QA | DACPAC PostDeployment feature stream. |
| V7-SafeInventoryTenantColumns | REQUIRED FOR QA | First manual tenant-hardening migration after DACPAC publish. |
| V8-CommerceAnalyticsEvents | REQUIRED FOR QA | DACPAC PostDeployment feature stream. |
| V8-TenantOwnershipPhase1 | REQUIRED FOR QA | Manual tenant ownership migration. |
| V9-CustomerGroups | REQUIRED FOR QA | DACPAC PostDeployment feature stream. |
| V9-TenantOwnershipResolutionReport | READ-ONLY AUDIT | Run after V7/V8; stop on unresolved or conflicting ownership. |
| V10-WhatsAppCommerceCheckout | REQUIRED FOR QA | DACPAC PostDeployment feature stream. |
| V10-ApprovedLegacyTenantBackfill | DEVELOPMENT-ONLY | Do not run in QA. The Development approval for `11111111-1111-1111-1111-111111111111` is not QA approval. |
| V11-WhatsAppCommerceDeliveryTracking | REQUIRED FOR QA | DACPAC PostDeployment feature stream. |
| V11-OperationalTenantWriteGuards | SUPERSEDED/SKIP | V24 explicitly recreates the guards with valid SET options. |
| V12-HierarchicalFeatureManagement | REQUIRED FOR QA | DACPAC PostDeployment feature stream. |
| V12-ExplicitOperationalTenantProcedures | SUPERSEDED/SKIP | Dynamic transformation superseded by V18/V24. |
| V13-LoyaltyCoins | REQUIRED FOR QA | DACPAC PostDeployment feature stream. |
| V13-Fix-Remaining-TenantProcedureContracts | SUPERSEDED/SKIP | Dynamic correction superseded by V18/V24. |
| V14-WhatsAppOption3TenantConnections | REQUIRED FOR QA | DACPAC PostDeployment feature stream. |
| V14-Fix-POS-TenantProcedure | SUPERSEDED/SKIP | Dynamic correction superseded by V18. |
| V15-CustomerReferralRewards | REQUIRED FOR QA | DACPAC PostDeployment feature stream. |
| V15-Ensure-Procedure-Session-Checks | SUPERSEDED/SKIP | Dynamic correction superseded by V18/V24. |
| V16-PurchaseCoinExpiry | REQUIRED FOR QA | DACPAC PostDeployment feature stream. |
| V16-Fix-POS-Parameter | SUPERSEDED/SKIP | Dynamic correction superseded by V18. |
| V17-DeliveryManagement | REQUIRED FOR QA | DACPAC PostDeployment feature stream. |
| V17-Final-POS-Procedure-Contract | SUPERSEDED/SKIP | Dynamic correction superseded by V18. |
| V18-WhatsAppContacts | REQUIRED FOR QA | DACPAC PostDeployment feature stream. |
| V18-POS-PostInvoice-TenantHardening | REQUIRED FOR QA | Deterministic manual definition after PostDeployment. |
| V19-ProductImageStorageProviders | REQUIRED FOR QA | DACPAC PostDeployment feature stream. |
| V20-DemoRequests | REQUIRED FOR QA | DACPAC PostDeployment feature stream. |
| V21-POSMobileBarcodeScanner | REQUIRED FOR QA | DACPAC PostDeployment feature stream. |
| V22-ProductManufacturerCodes | REQUIRED FOR QA | DACPAC PostDeployment feature stream. |
| V23-WhatsAppProductVisibility | REQUIRED FOR QA | DACPAC PostDeployment feature stream. |
| V24-RecreateOperationalTenantGuards-WithRequiredSetOptions | REQUIRED FOR QA | Deterministic manual operational hardening after V18. |
| V25-FinanceTenantOwnershipAudit | READ-ONLY AUDIT | Run before V26; every QA journal must have one deterministic source-derived tenant. |
| V26-FinanceTenantIsolationAndPostingRepair | REQUIRED FOR QA | Apply only after V25 has no ambiguous/unresolved ownership. |

## Exact safe database sequence

Do not use `bootstrap-qa-database.ps1` unchanged for this upgrade: it publishes and bootstraps without first taking/verifying a backup, and it does not run the manual tenant/Finance hardening chain.

1. Put the QA API in maintenance/stopped state and record the currently deployed application/database artifact versions.
2. Validate `/etc/whatsbiz/qa.env` without printing secrets. Parse `ConnectionStrings__DefaultConnection` and output only `DataSource` and `InitialCatalog`; require `InitialCatalog == WhatsBizERP_QA`. Require `ASPNETCORE_ENVIRONMENT == QA`.
3. Run `QA-TenantHardening-Preflight.sql` against the intended connection. Verify its identity result and retain the output.
4. Take a native full `COPY_ONLY` backup to an explicitly resolved QA backup path using `CHECKSUM` and `COMPRESSION`; run `RESTORE VERIFYONLY ... WITH CHECKSUM`. Record the backup file, backup-set position, size, SHA-256, server, database, UTC time, and operator.
5. Build the database project and generate/review the DACPAC deploy report/script with `BlockOnPossibleDataLoss=True`. Abort on unexpected drops, data movement, or a database name other than `WhatsBizERP_QA`.
6. Publish the DACPAC. Its PostDeployment applies the required feature stream through V23.
7. Before **each** following mutating script, run the identity gate and require `DB_NAME() = WhatsBizERP_QA`: V7-SafeInventoryTenantColumns, V8-TenantOwnershipPhase1.
8. Run V9-TenantOwnershipResolutionReport. Stop for any unresolved/ambiguous row; do not run V10 in QA.
9. Re-run the identity gate, then apply V18-POS-PostInvoice-TenantHardening.
10. Re-run the identity gate, then apply V24-RecreateOperationalTenantGuards-WithRequiredSetOptions. Never run the tenant-hardening V11-V17 scripts.
11. Run V25-FinanceTenantOwnershipAudit. Stop unless every journal has exactly one authoritative source-derived tenant and there are no cross-tenant/orphan conflicts.
12. Re-run the identity gate, then apply V26-FinanceTenantIsolationAndPostingRepair.
13. Run `QA-TenantHardening-PostValidation.sql`. Retain all result sets as deployment evidence.

The mutation gate to execute before each individual mutation is:

```sql
SELECT @@SERVERNAME AS ServerName,DB_NAME() AS DatabaseName,SYSTEM_USER AS LoginName;
IF DB_NAME()<>N'WhatsBizERP_QA' THROW 51690,N'Wrong database target.',1;
```

## Backup and rollback

- The pre-change full backup is the rollback boundary. Never overwrite an older backup file.
- Keep the old backend and frontend artifacts in immutable versioned release directories.
- If a migration or post-validation fails, leave the API stopped, collect the error/output, and prefer a reviewed forward correction when data has legitimately changed after backup.
- If restoration is chosen, verify the exact backup header/database, terminate only QA connections, restore only `WhatsBizERP_QA` with the recorded file list/paths, rerun `DBCC CHECKDB`, then redeploy the previous backend/frontend artifacts. Never construct a restore target from an unvalidated environment variable.
- A SQL transaction is not a substitute for the backup because DACPAC and multi-batch DDL are involved.

## Post-migration evidence

The supplied post-validation checks required `@TenantId` signatures, V24 trigger count/settings/content, `JournalEntries.TenantId`, journal one-side and balance invariants, and orphan detail/ledger/book rows. The deployment operator must additionally retain:

- V9 and V25 ownership/source-type outputs, including zero ambiguous/unresolved rows;
- exact operational `TenantId` counts for supplier, warehouse, sales, purchase, balance, and transaction tables;
- finance source-to-journal tenant equality and zero new NULL journal owners;
- Tenant A/B dashboard results showing no shared journal, ledger, cash/bank, outstanding, sales, purchase, or inventory fixture markers;
- constraint trust/enabled status and all V24 trigger metadata;
- zero orphan source documents, payments, returns, journal details, ledger entries, CashBook, BankBook, customer outstanding, and supplier outstanding rows.

## Minimum Tenant A/B smoke and cleanup

Use two new tenants and unique marker prefix `QA-TI-<UTC timestamp>-<random>`. Do not use the historical legacy tenant.

1. Per tenant create one authenticated user/session, customer, supplier, warehouse, product, positive inventory balance, and required payment/account configuration.
2. Positive tests per tenant: POS sale, purchase, stock adjustment/transfer/physical verification, sales and purchase payment, sales and purchase return, Finance receipt/payment, and tenant-scoped dashboard/ledger/outstanding reads.
3. For every committed flow assert exact header/source/journal tenant equality, balanced positive journal totals, exactly one non-zero debit/credit side per detail, correct ledger/books/outstanding values, and no other tenant's marker in reads.
4. Negative tests under Tenant A use Tenant B product, warehouse, customer, supplier, invoice, payment/return parent, and Finance source. Compare before/after counts and amounts across operational, inventory, payment/return, journal/detail/ledger/books/outstanding tables.
5. Cleanup in reverse dependency order inside guarded transactions: notification/follow-up/outstanding/book/ledger/detail/journal rows; return/payment/detail rows; invoice items/invoices; inventory details/transactions/balances; commerce links; product; customer/supplier/warehouse children and parents; test users; tenants. Every delete must include the fixture IDs and/or exact marker plus expected tenant.
6. Verify zero marker residue, `@@TRANCOUNT=0`, and `SESSION_CONTEXT(N'TenantId') IS NULL` before closing each test connection.

## DB to frontend deployment order

1. Approval/change window, API maintenance, identity/config preflight.
2. Verified QA backup.
3. Database build/deploy report review, DACPAC feature stream, manual V7/V8, audits V9/V25, deterministic V18/V24/V26, post-validation.
4. Backend Release build/tests and publish to a new versioned QA directory.
5. Validate the real QA environment file permissions and non-secret identity fields; atomically point the service release link to the new backend.
6. `systemctl daemon-reload` only if the unit changed; restart `whatsbiz-qa`; inspect service status/journal.
7. API checks: loopback health, HTTPS health, authenticated tenant A/B contract and cross-tenant denial.
8. Build the frontend with the QA configuration; deploy it to a new versioned directory and atomically switch the QA web release.
9. Browser/API smoke, then the SQL-backed Tenant A/B smoke and cleanup above.
10. Close only after monitoring shows no SQL 201/signature errors, tenant-guard errors on valid flows, unbalanced postings, or cross-tenant results.

## Minimum permissions

Use a deployment principal scoped to `WhatsBizERP_QA`: `CONNECT`, `VIEW DEFINITION`, and only the DDL/DML/EXECUTE needed by the reviewed DACPAC and V7/V8/V18/V24/V26 scripts. Backup/restore stays with a separate DBA/operator role. Smoke identities need `EXECUTE` on tested procedures and narrowly scoped fixture DML; revoke those grants after cleanup. Do not grant `db_owner` to the runtime login.
