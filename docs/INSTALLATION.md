# Installation Guide

## Prerequisites

- Windows 10/11 or Windows Server 2022
- SQL Server 2022 Developer/Standard/Enterprise
- .NET 9 Hosting Bundle and SDK 9.0.307 or compatible
- Node.js 22 and npm 10 for frontend builds
- SQL Server Data Tools when building the `.sqlproj`

## Database

1. Enable Windows authentication for the application identity or create a least-privilege SQL login.
2. Create `WhatsBizERP`, deploy `database/WhatsBiz.Database/WhatsBiz.Database.sqlproj`, then apply the release/post-deployment scripts.
3. Apply `Scripts/V1_ReleaseHardening.sql` once all module objects are deployed.
4. Run `DBCC CHECKCONSTRAINTS` and `DBCC CHECKDB('WhatsBizERP')`.

The development connection is in `appsettings.Development.json`. Production configuration must be supplied externally.

## Backend

```powershell
dotnet restore backend/WhatsBiz.sln
dotnet build backend/WhatsBiz.sln -c Release --no-restore
dotnet test backend/WhatsBiz.sln -c Release --no-build
```

Set `ConnectionStrings__DefaultConnection`, `Jwt__SigningKey`, `Jwt__Issuer`, `Jwt__Audience`, and `Cors__AllowedOrigins__0` before startup. The JWT key must be a random secret of at least 32 characters.

## Frontend

```powershell
cd frontend/WhatsBiz.Web
npm ci
npm run build
```

Deploy `dist/WhatsBiz.Web/browser`. Confirm the runtime API base URL and reverse-proxy routing before login testing.

The initial development administrator is `admin` / `Admin@123456`; change this password immediately outside development.
