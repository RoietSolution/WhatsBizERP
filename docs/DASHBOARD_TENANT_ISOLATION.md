# Dashboard tenant-isolation audit

Dashboard cards are loaded by `DashboardApiService` from `/api/dashboard/{summary,sales,purchase,inventory,customers,suppliers,finance,notifications}`. The API dispatches through `DashboardHandlers` to `DashboardRepository`, which calls the corresponding `dashboard.*` procedures.

The authenticated tenant is now required in `DashboardHandlers` and `DashboardRepository`. Requests without a tenant fail before a dashboard query is executed; the repository also sets `SESSION_CONTEXT('TenantId')` on the SQL connection and cache keys include the effective tenant.

| Table/domain | Direct TenantId | Current ownership source | Classification | Required change |
|---|---:|---|---|---|
| master.Products | Yes (NOT NULL FK) | core.Tenants | A | Use tenant predicate/index |
| sales.Customers | Yes (nullable FK) | core.Tenants | A | Validate/backfill nulls before NOT NULL |
| sales.SalesInvoices/Payments | No | Warehouse/customer can be nullable or shared | D | Add ownership only after ambiguity report |
| purchase.PurchaseInvoices/Payments/Returns | No | Supplier and warehouse are not tenant-owned | D | Add ownership chain first |
| purchase.Suppliers | No | No tenant relationship exists | D | Tenant-supplier mapping or direct FK decision |
| inventory.Warehouses/Balances/Transactions | No | Product alone is insufficient; warehouse is shared | D | Tenant warehouse ownership/mapping required |
| finance cash/bank/ledger/outstanding | No | References operational documents/accounts | D | Finance ownership model and posting update required |
| sales.PaymentMethods and payment terms | No | Shared configuration/master data | C | Remain shared |

The canonical type is `UNIQUEIDENTIFIER`, with `core.Tenants(TenantId)` as the existing FK target. The D-class rows are deliberately not assigned a default tenant: existing records cannot be deterministically backfilled because invoices may have no customer and warehouses/suppliers have no tenant owner. The dashboard procedures therefore cannot yet be proven tenant-isolated for all finance and transaction KPIs without a schema migration that first establishes those ownership chains and updates every posting path.

## Development database audit (read-only, 2026-09-05)

Run the audit with: `sqlcmd -S localhost -d WhatsBizERP -E -C -i database/WhatsBiz.Database/Scripts/V6-DashboardTenantOwnershipAudit.sql`.

Database: `WhatsBizERP` on `localhost`. Tenant IDs present: the default tenant plus two SQLIT tenants. No rows were changed.

| Table | Total | Direct TenantId | Deterministically inferred | Ambiguous/conflicting | Orphan/unowned |
|---|---:|---:|---:|---:|---:|
| master.Products | 111 | 111 | 111 | 0 | 0 |
| sales.Customers | 3 | 3 | 3 | 0 | 0 |
| sales.SalesInvoices | 57 | 0 | 55 via `CreatedBy → core.Users` | 0 | 2 (`demo-seed`, `WC-DEMO-003`) |
| sales.SalesPayments | 44 | 0 | 44 via invoice | 0 | 0 |
| purchase.Suppliers | 2 | 0 | 0 | 2 | 2 (no tenant relationship) |
| inventory.Warehouses | 3 | 0 | 0 | 3 | 3 (no tenant relationship) |
| purchase.PurchaseInvoices | 2 | 0 | 1 via `CreatedBy → core.Users` | 0 | 1 (`demo-seed`) |
| purchase.PurchasePayments | 2 | 0 | 2 via invoice | 0 | 0 |
| purchase.PurchaseReturns | 1 | 0 | 1 via purchase invoice | 0 | 0 |
| inventory.InventoryBalances | 61 | 0 | 61 via product | 0 | 0 |
| inventory.InventoryTransactions | 55 | 0 | not safely inferable without tenant warehouse | unresolved | unresolved |
| finance.JournalEntries | 54 | 0 | source-dependent | unresolved | unresolved |
| finance.CashBook | 36 | 0 | 36 via journal entry, source ownership unresolved | unresolved | 0 FK orphans |
| finance.BankBook | 11 | 0 | 11 via journal entry, source ownership unresolved | unresolved | 0 FK orphans |
| finance.LedgerEntries | 239 | 0 | 239 via journal entry, source ownership unresolved | unresolved | 0 FK orphans |
| finance.CustomerOutstanding | 24 | 0 | 24 via customer | 0 | 0 |
| finance.SupplierOutstanding | 2 | 0 | 0 | 2 | 2 (supplier has no tenant) |

The strongest current ownership source is the authenticated user for the 55 `admin` sales invoices and one `admin` purchase invoice. `demo-seed` and `WC-DEMO-003` are not mapped to a tenant and must be resolved manually or by an approved seed-data rule. Supplier and warehouse ownership cannot be ranked above unsafe with the current schema. No conflicting tenant pairs were observed in the available customer/user joins, but this does not prove ownership for records with no usable chain.

## Proposed phased migration

Phase 1 draft: `database/WhatsBiz.Database/Scripts/V8-TenantOwnershipPhase1.sql` adds nullable `TenantId` columns (FK to `core.Tenants`) to Suppliers, Warehouses, SalesInvoices, PurchaseInvoices and JournalEntries, creates tenant-leading indexes, and backfills only invoice rows with an unambiguous `CreatedBy -> core.Users` tenant. It emits unresolved counts and never assigns a default tenant. The script has not been run against Development/QA.

Phase 2 adds the read-only evidence report `V9-TenantOwnershipResolutionReport.sql`. It reports supplier and warehouse ownership candidates, unresolved ownership counts, and JournalEntry source-type determinism. It must be run after V8 and does not mutate data.

The approved legacy assignment is isolated in `V10-ApprovedLegacyTenantBackfill.sql`. It first validates tenant `11111111-1111-1111-1111-111111111111`, repeats deterministic user/product backfills, then assigns only remaining NULL ownership on approved operational tables. It is transactional and rerunnable; JournalEntries remain audit-only until finance source ownership is approved. Run only after capturing V6/V9 audit output.

The inventory phase-1 script is `V7-SafeInventoryTenantColumns.sql`; it backfills InventoryBalances from Products and InventoryTransactions from product detail ownership only, with guards for unresolved rows.

No application writer or finance posting procedure is considered hardened until it explicitly supplies the authenticated tenant. Nullable columns are intentional during rollout, so existing historical unresolved rows remain visible for manual resolution.

1. Introduce tenant mapping/ownership for suppliers and warehouses (or explicitly classify them as global and add tenant relationship tables).
2. Add nullable `TenantId UNIQUEIDENTIFIER` columns to invoice headers, payments, returns, inventory transactions, and finance transaction headers only after ownership model approval.
3. Backfill only product/customer/invoice/payment rows with a single authoritative chain; emit unresolved/conflict reports and stop before constraint enforcement.
4. Update all application writers and posting procedures to populate trusted server-side tenant IDs.
5. Validate QA, then add composite tenant FKs/indexes and make columns non-null only where zero unresolved rows remain.
6. Update every dashboard procedure to accept `@TenantId` and predicate each tenant-owned table; add SQL-backed two-tenant tests before production rollout.
