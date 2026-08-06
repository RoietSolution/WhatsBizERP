# Deployment Guide

## IIS deployment

1. Install the .NET 9 Hosting Bundle and IIS URL Rewrite module.
2. Publish with `dotnet publish backend/src/WhatsBiz.Api/WhatsBiz.Api.csproj -c Release`.
3. Create an IIS application pool with “No Managed Code” and a dedicated identity.
4. Grant that identity database access and write access only to the chosen application log directory.
5. Configure secrets as environment variables; do not place them in `appsettings.json`.
6. Bind HTTPS, enable HSTS, proxy `/api` to the API and serve the Angular browser output for other routes.
7. Set allowed hosts and exact CORS origins. Do not use wildcards in production.
8. Verify `/health`, authentication, a protected request, printing and a verified backup.

Swagger is available only in Development. Production requests use forwarded headers, HTTPS redirection, rate limiting, response compression and security headers.

## Release order

1. Take and verify a full backup.
2. Stop API/background jobs.
3. Deploy database package and release hardening script.
4. Deploy API and Angular artifacts.
5. Start API and verify health.
6. Run smoke workflows and monitor logs.

## Rollback

Keep the previous API/web artifacts and verified backup. Stop the API before an actual database restore. Validate with `RESTORE VERIFYONLY`; restore from a `master` connection, then run integrity checks and restart the API.

Docker assets are retained for optional testing. Set `SQL_SA_PASSWORD` and `JWT_SIGNING_KEY`; Docker is not required for supported local SQL Server deployments.
