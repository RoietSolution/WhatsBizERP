# WhatsBizERP Retailer Onboarding, Feature Administration and Deboarding Guide

**Document version:** 1.0
**Application:** WhatsBizERP
**Database baseline:** V2 feature entitlements, RCDEV runtime objects, RCDEV009 printing, RCDEV010 customer notifications, V3 product images, V4 dashboard notifications

## 1. Scope and architecture

This guide covers feature access, database setup, configuration, imports, and onboarding/deboarding checklists for retailers.

The current application is multi-tenant. A retailer is represented by a tenant in the shared `WhatsBizERP` database. The supported flow creates a tenant and associates users, subscriptions, feature entitlements, company settings, warehouses, printers, and master data with that tenant. The current code does **not** automatically create one physical SQL database per retailer.

If separate physical database isolation is required, provision an empty database from the SQL project, deploy the complete post-deployment baseline, configure a separate API connection string, and run this checklist against that database. That is an infrastructure decision, not automatic application behavior today.

## 2. Enable or disable features

### 2.1 Tenant feature entitlement

Feature enablement is tenant-level. The effective decision is resolved from the active tenant assignment, plan feature, release state, and date window.

1. Create or identify the retailer tenant.
2. Confirm the feature exists in `core.Features` with `ReleaseState = ACTIVE`.
3. Assign it in `core.TenantFeatures` with `IsEnabled = 1`, or use the administration/API workflow.
4. If plan-controlled, confirm `core.PlanFeatures.IsEnabled = 1` for the subscribed plan.
5. Sign out/in or refresh the session so the current-user feature snapshot is rebuilt.
6. Verify the administration hub, route, and API permission.

To disable a feature, set the tenant assignment `IsEnabled = 0`, set an `EndDate`, or disable the plan feature. Set the related integration/configuration inactive where applicable. Disabling access does not delete historical transactions.

### 2.2 User access

Feature entitlement and user authorization are separate:

- `core.TenantFeatures`: is the feature available to this retailer?
- Identity roles, role claims, and user-role membership: may this user perform the operation?
- API permission attributes and frontend permission guards: enforce access at both layers.

To enable a feature for one user, enable it for the tenant, then assign a role containing the required permissions. To disable it for one user, remove the role or permission claim. Do not rely on frontend-only changes.

**Flow:** `active tenant feature + active release/plan + user permission + route/API authorization = usable feature`.

## 3. Tables, versions, and flow

| Area | Table | Purpose |
|---|---|---|
| Tenant | `core.Tenants` | Retailer identity and lifecycle anchor. |
| Feature catalog | `core.Features` | Feature key, name, module, release state, active flag. |
| Plans | `core.Plans` | Commercial plan definition. |
| Plan mapping | `core.PlanFeatures` | Features included by a plan. |
| Subscription | `core.Subscriptions` | Tenant-to-plan subscription and dates. |
| Tenant override | `core.TenantFeatures` | Tenant enablement, dates, reason, active state. |
| User identity | `core.Users` | Application user and tenant ownership. |
| User roles | Identity role/user-role/role-claim tables | User membership and permission claims. |

Feature schema and seed flow: `database/WhatsBiz.Database/Scripts/V2-FeatureEntitlements.sql`.

| Configuration area | Tables |
|---|---|
| Company/admin | `admin.Companies`, `admin.ApplicationSettings`, `admin.PrinterSettings` |
| Printers | `printing.PrinterConfigurations`, `printing.PaperSizes` |
| WhatsApp | `integration.WhatsAppConfigurations`, `integration.WhatsAppWebhookEvents`, `integration.WhatsAppCommerceOrders` |
| Dashboard alerts | `dashboard.DashboardNotifications`, `inventory.InventoryAlerts`, `inventory.ReorderSuggestions` |
| Product images | `master.ProductImages`, `master.Products` tenant/image columns from V3 |

### Version/deployment flow

1. Publish the SQL project baseline and post-deployment scripts.
2. Apply V2 feature entitlement objects and feature catalog seed.
3. Apply RCDEV runtime, printing, and customer-notification changes.
4. Apply V3 product-image tenant/optimized-image schema.
5. Apply V4 dashboard notification procedures.
6. Deploy the matching API and frontend.
7. Create tenant and administrator.
8. Configure company, financial year, branches, warehouses, printers, integrations, users, roles, and feature assignments.

The deployment source of truth is `database/WhatsBiz.Database/Scripts/PostDeployment.sql` and its referenced scripts. Do not manually create only a subset of production tables.

Two standalone runbooks are included for retailer lifecycle operations:

- `database/WhatsBiz.Database/Scripts/Onboarding_Retailer.sql` creates a tenant, subscription, initial tenant feature rows, and company shell after placeholders are replaced. It does not create a password or silently enable every feature.
- `database/WhatsBiz.Database/Scripts/Deboarding_Retailer.sql` disables the tenant, subscriptions, features, users, and WhatsApp configuration without deleting business history. Export, backup, reconciliation, retention approval, and any purge/anonymization remain mandatory follow-up steps.

These scripts are for the current shared-database model. They must be run by an authorized database operator after approval and after the pre-execution checklist below is complete.

## 4. Blank database and retailer configuration

For a new physical database, create an empty SQL Server database and publish `database/WhatsBiz.Database/WhatsBiz.Database.sqlproj`. Execute post-deployment scripts in order. Do not use `SeedData/DemoData.sql` as a blank production step; it is demo data.

Required deployment configuration:

- SQL Server instance and database name.
- API `ConnectionStrings:DefaultConnection`.
- Encryption, trust-certificate, and CORS policy.
- JWT issuer, audience, signing key, access expiry, and refresh expiry.
- Data-protection application name and persistent key storage.
- Logging destination and retention.
- Backup/file locations and permissions.
- Email provider settings for production password-reset delivery.

Retailer administration records:

- Legal/trade name, address, GST/tax identifiers, currency, timezone, and contacts.
- Financial year and numbering series for sales, purchases, returns, payments, and receipts.
- Branches, warehouses, types, zones, bins, and default warehouse.
- Roles, permissions, first administrator, and tenant association.
- Payment methods, settlement accounts, tax/GST settings, and rates.
- Printer configurations for thermal 58MM/80MM, A4, barcode, and document mappings.
- Notification settings, dashboard refresh policy, WhatsApp provider mode, and backups.

### Printer configuration

Register each printer in `printing.PrinterConfigurations` with name, type, document type, paper size, connection details, and active/default status. Configure default mappings in `admin.PrinterSettings`. Test a sale invoice, purchase document, payment receipt, barcode label, and A4 report.

## 5. Modules with import functionality

Always download and use the module's own template. Review skipped rows after each import.

| Module | Records |
|---|---|
| Products | Product master; referenced categories, brands, and units must exist. |
| Product Categories | Category hierarchy. |
| Brands | Brand master. |
| Units of Measure | Units used by products. |
| Customers | Customer master, payment terms, opening balances. |
| Suppliers | Supplier master, payment terms, opening balances. |
| Warehouses | Warehouse master; warehouse type must exist. |
| Purchases | Purchase invoices/items, supplier, warehouse, product, tax, batch, expiry. |

Recommended order: units, categories, brands, warehouses, products, customers, suppliers, opening purchases, then opening financial balances. Sales/POS invoices should normally use the operational workflow unless an approved migration utility is used.

## 6. Retailer onboarding checklist

### Before running `Onboarding_Retailer.sql`

- [ ] SQL baseline and all required migrations are applied.
- [ ] Tenant key, retailer name, plan key, company code, company name, and operator identity are filled in.
- [ ] Tenant key and company code uniqueness has been checked.
- [ ] Commercial approval identifies which plan features may be enabled.
- [ ] Backup/restore point is recorded.

### After running `Onboarding_Retailer.sql`

### Commercial and security

- [ ] Scope, plan, enabled modules, support contacts, and data ownership confirmed.
- [ ] Tenant key and retailer identity created.
- [ ] Administrator created with unique email and strong password.
- [ ] Least-privilege roles and permissions reviewed.
- [ ] Password-reset email configuration confirmed where applicable.

### Database and application

- [ ] Backup/restore point recorded.
- [ ] SQL project and post-deployment scripts applied.
- [ ] API connection string and secrets configured.
- [ ] Health endpoint and authenticated login verified.
- [ ] Tenant isolation tested with two users/tenants.

### Master data and validation

- [ ] Company, tax, currency, timezone, financial year, and numbering configured.
- [ ] Branches, warehouses, zones, bins, payment methods, and rates configured.
- [ ] Printers, paper sizes, default mappings, and test prints completed.
- [ ] Import templates downloaded and master data imported.
- [ ] Import error reports reviewed and corrected.
- [ ] POS sale, payment, invoice print, return, and invoice search tested.
- [ ] Purchase, payment, receipt, outstanding reports, dashboard values, and notifications tested.
- [ ] Feature routes and denied routes tested for each role.
- [ ] Backup, restore, audit, and support escalation tested.

## 7. Retailer deboarding checklist

### Before running `Deboarding_Retailer.sql`

- [ ] Termination approval, effective date, retention period, export scope, and legal hold are recorded.
- [ ] Tenant key is verified against the correct retailer; do not use a display name or guess an ID.
- [ ] Final backup is generated and a restore test is completed.
- [ ] Customers, suppliers, products, stock, invoices, purchases, payments, reports, audit logs, and configuration are exported.
- [ ] Receivables, payables, stock, cash, bank, and pending orders are reconciled.
- [ ] Provider credentials, webhook ownership, and active sessions are identified.

### After running `Deboarding_Retailer.sql`

- [ ] Termination date, retention period, export scope, and legal hold confirmed.
- [ ] Login disabled and active sessions/refresh tokens revoked.
- [ ] Tenant feature assignments and integrations, including WhatsApp/webhooks, disabled.
- [ ] Users disabled and external credentials/secrets revoked.
- [ ] Operational posting stopped at the agreed cut-off.
- [ ] Customers, suppliers, products, stock, invoices, purchases, payments, reports, audit logs, and configuration exported.
- [ ] Final backup generated, checksum recorded, and restore test completed.
- [ ] Receivables, payables, stock, cash, bank, and pending orders reconciled.
- [ ] Data removed/anonymized only after retention/legal approval.
- [ ] Printers, API keys, webhooks, and support access deactivated.
- [ ] Approver, operator, timestamps, exported artifacts, and final status recorded.

## 8. Ownership

The implementation team owns deployment and initial configuration. The retailer administrator owns master data, users, roles, and daily configuration. The platform/database administrator owns backups, secrets, SQL deployment, monitoring, and deboarding evidence. Production changes must use versioned scripts or approved administration APIs and be recorded in the change log.
