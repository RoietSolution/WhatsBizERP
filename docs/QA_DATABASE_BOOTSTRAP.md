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
IdentityBootstrap__Administrator__IncludeSystemAdministratorRole=true
IdentityBootstrap__Administrator__ResetPasswordOnStart=false
```

Protect the file with `root:root` ownership and mode `600`, restart `whatsbiz-qa`, and confirm login. Then remove the password line and set `Enabled=false`. The administrator receives `Administrator` (all application permissions) and `SystemAdministrator` (feature management), attached to `QA_DEFAULT`.

To reset the password later, place a new strong password in the protected environment file, set `Enabled=true` and `ResetPasswordOnStart=true`, restart once, then immediately remove the password and set both flags to `false`. The reset uses `UserManager.GeneratePasswordResetTokenAsync` and `ResetPasswordAsync`; no plaintext password is stored in SQL.

## Result

The bootstrap creates or repairs one active `QA_DEFAULT` retailer with a `V2_COMMERCE` subscription. This plan provides V1 and WhatsApp Commerce through normal `PlanFeatures`, `TenantFeatures`, parent-feature, and dependency evaluation. It also creates the minimum QA company/branch, INR/current financial year, GST references, finance accounts/payment modes, warehouse, POS counter, customer, supplier, product/UOM/category/brand, inventory balance, commerce collection, and credential-free `MOCK` WhatsApp configuration.

Real `META_TEST` values must be saved later through the application configuration UI/API. That path encrypts AccessToken/AppSecret/VerifyToken with ASP.NET Data Protection and does not return those secrets to Angular.
