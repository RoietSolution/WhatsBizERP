# WhatsBizERP QA database bootstrap

The database project remains environment-neutral. `PostDeployment.sql` creates/upgrades the schema and seeds shared platform/reference rows. QA retailer data is intentionally applied afterward by `Bootstrap_QA.sql`, whose database-name guard rejects every target except `WhatsBizERP_QA`.

## Prerequisites

- SQL Server is available at `localhost,1433` and the current OS account can connect with integrated authentication.
- `SqlPackage`, `sqlcmd`, and the .NET SDK are installed.
- Run commands from the repository root. Do not put SQL credentials in this repository.

## Publish and bootstrap

The helper builds the database project, publishes the DACPAC, runs onboarding twice as an idempotency check, and runs SQL validation:

```powershell
.\deployment\bootstrap-qa-database.ps1
```

Equivalent commands:

```powershell
dotnet build .\database\WhatsBiz.Database\WhatsBiz.Database.sqlproj --configuration Release
SqlPackage /Action:Publish /SourceFile:.\database\WhatsBiz.Database\bin\Release\WhatsBiz.Database.dacpac /TargetServerName:localhost,1433 /TargetDatabaseName:WhatsBizERP_QA /TargetIntegratedSecurity:True /TargetEncryptConnection:True /TargetTrustServerCertificate:True /p:BlockOnPossibleDataLoss=True
sqlcmd -S localhost,1433 -d WhatsBizERP_QA -E -C -b -i .\database\WhatsBiz.Database\Scripts\Bootstrap_QA.sql
sqlcmd -S localhost,1433 -d WhatsBizERP_QA -E -C -b -i .\database\WhatsBiz.Database\Scripts\Validate_QA_Bootstrap.sql
```

For SQL authentication, supply credentials to the deployment tools through the server's secret-management mechanism. Do not save them in a publish profile, script, shell history, or tracked environment file.

## Create the initial administrator securely

The SQL bootstrap never writes `PasswordHash`. The existing API hosted Identity seeder uses `UserManager`, so the configured password is validated and hashed by ASP.NET Core Identity.

In `/etc/whatsbiz/qa.env`, temporarily set:

```dotenv
IdentityBootstrap__Administrator__Enabled=true
IdentityBootstrap__Administrator__TenantKey=QA_DEFAULT
IdentityBootstrap__Administrator__Username=qa.admin
IdentityBootstrap__Administrator__Email=qa.admin@khatadhari.com
IdentityBootstrap__Administrator__Password=<ONE_TIME_STRONG_ADMIN_PASSWORD>
IdentityBootstrap__Administrator__ResetPasswordOnStart=false
```

Create the separate application-owner login once with:

```text
IdentityBootstrap__ApplicationOwner__Enabled=true
IdentityBootstrap__ApplicationOwner__Username=qa.owner
IdentityBootstrap__ApplicationOwner__Email=qa.owner@khatadhari.com
IdentityBootstrap__ApplicationOwner__Password=<ONE_TIME_STRONG_OWNER_PASSWORD>
IdentityBootstrap__ApplicationOwner__ResetPasswordOnStart=false
```

`ApplicationOwner` is a platform identity. It deliberately has `TenantId = NULL`, has no `TenantKey` setting, and receives no `tenant_id` JWT claim. Never add an owner tenant setting or reuse a retailer administrator account for this role.

After the first successful startup, set both bootstrap `Enabled` values to `false` and remove the passwords. Application owners sign in at `/application-owner/login`; retailer administrators continue to use `/login`.

Protect the file with `root:root` ownership and mode `600`, restart `whatsbiz-qa`, and confirm both logins. Then remove both password lines and set both `Enabled` values to `false`. The retailer account receives `Administrator`; the separate owner account receives `ApplicationOwner` and the platform-management permission.

To reset the password later, place a new strong password in the protected environment file, set `Enabled=true` and `ResetPasswordOnStart=true`, restart once, then immediately remove the password and set both flags to `false`. The reset uses `UserManager.GeneratePasswordResetTokenAsync` and `ResetPasswordAsync`; no plaintext password is stored in SQL.

## Result

The bootstrap creates or repairs one active `QA_DEFAULT` retailer with a `V2_COMMERCE` subscription. This plan provides V1 and WhatsApp Commerce through normal `PlanFeatures`, `TenantFeatures`, parent-feature, and dependency evaluation. It also creates the minimum QA company/branch, INR/current financial year, GST references, finance accounts/payment modes, warehouse, POS counter, customer, supplier, product/UOM/category/brand, inventory balance, commerce collection, and credential-free `MOCK` WhatsApp configuration.

Real `META_TEST` values must be saved later through the application configuration UI/API. That path encrypts AccessToken/AppSecret/VerifyToken with ASP.NET Data Protection and does not return those secrets to Angular.

## QA platform-owner migration and verification

Do not publish until the generated database plan has been reviewed. From the repository root:

```powershell
dotnet build database/WhatsBiz.Database/WhatsBiz.Database.sqlproj --configuration Release
.\deployment\deploy-qa-database.ps1 -PlanOnly
```

Changing `core.Users.TenantId` to nullable may be reported as a reviewed table rebuild. Inspect the generated deployment report and SQL script named by the command. After confirming that only the intended `core.Users` scope change and additive objects are present, apply the database migration with the established backup gate:

```powershell
.\deployment\deploy-qa-database.ps1 -ApproveTableRebuild
```

Deploy the matching API and frontend release using the normal release process, then temporarily enable only the application-owner bootstrap in `/etc/whatsbiz/qa.env`. Restart once and verify the database without selecting password data:

```sql
SELECT u.Id,u.UserName,u.Email,u.TenantId,u.AccountType,r.Name RoleName
FROM core.Users u
JOIN core.UserRoles ur ON ur.UserId=u.Id
JOIN core.Roles r ON r.Id=ur.RoleId
WHERE u.NormalizedUserName=N'QA.OWNER';
```

Expected: one `ApplicationOwner` role, `AccountType=APPLICATION_OWNER`, and `TenantId=NULL`. Then exercise both portals (use a shell prompt or API client secret store; do not place passwords in shell history):

1. `POST /api/auth/application-owner/login` with owner credentials returns 200 and `user.tenantId` is `null`.
2. Decode the owner access token and confirm it contains the `ApplicationOwner` role and `feature.manage`, but no `tenant_id` claim.
3. `POST /api/auth/login` with the same owner credentials returns 401.
4. `POST /api/auth/application-owner/login` with `qa.admin` returns 401.
5. With the owner token, `GET /api/features/administration/tenants` returns 200.
6. Choose the returned QA tenant ID and call `GET /api/features/administration/tenants/{tenantId}`; confirm the selected tenant is returned.
7. With the owner token, call an ordinary retailer endpoint such as `GET /api/products`; confirm 403.
8. With the retailer-admin token, `GET /api/products` returns the tenant's data and `GET /api/features/administration/tenants` returns 403.
9. Attempt another retailer's resource ID through a retailer API and confirm it remains 404/403 with no cross-tenant data.
10. Perform one owner feature update against the explicitly selected tenant, then verify `admin.AuditLogs.UserId`, `TargetTenantId`, `Action`, `RequestPath`, and `OccurredOn` were recorded.

Finally remove `IdentityBootstrap__ApplicationOwner__Password`, set `IdentityBootstrap__ApplicationOwner__Enabled=false`, keep `ResetPasswordOnStart=false`, restart, and confirm the existing owner can still sign in.
