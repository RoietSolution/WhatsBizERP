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

## Product image storage

Amazon S3 is the default for new product images. Existing image rows retain their recorded provider, so changing `ProductImageStorage__Provider` does not break older images. A developer who needs to run without AWS access can set `ProductImageStorage__Provider=DATABASE` in the ignored local Development configuration or environment.

- `DATABASE` keeps the optimized catalog and thumbnail binaries in SQL Server and requires no additional configuration.
- `LOCAL` stores them below `ProductImageStorage__LocalRootPath`. Use an absolute path on persistent storage and grant the application identity read/write access. Do not place this directory under the public web root.
- `S3` stores private objects in Amazon S3 or an S3-compatible service. It is the production default. Every deployment must set `ProductImageStorage__S3__BucketName` and `ProductImageStorage__S3__Region`; optionally set `ProductImageStorage__S3__KeyPrefix`. Use the runtime IAM role/default AWS credential chain. For MinIO-compatible deployments, set `ServiceUrl` and `ForcePathStyle=true` instead of an AWS region.

If explicit S3 credentials are unavoidable, provide `ProductImageStorage__S3__AccessKey` and `ProductImageStorage__S3__SecretKey` through a secret manager or environment variables. Never commit them. The bucket must remain private; grant the API identity only the object read/write/delete permissions required for the configured prefix.

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
