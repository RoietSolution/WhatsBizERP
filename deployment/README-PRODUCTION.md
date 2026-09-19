# KhataDhari production deployment preparation

This document and the companion artifacts prepare (but do not execute) the production rollout. QA files and services are separate and must not be reused. No production SQL hostname or credential is checked into this repository. Supply the database connection through the deployment process environment as `WHATSBIZ_PROD_SQL_CONNECTION`; never place it in a tracked file or command literal.

## Approved service targets

- UI: `https://app.khatadhari.com`
- API: `https://api.khatadhari.com`
- SQL database: `WhatsBizERP_PROD`
- API service: `whatsbiz-prod`
- API directory: `/var/www/whatsbiz-prod`
- API environment: `/etc/whatsbiz/prod.env`
- Data Protection keys: `/var/lib/whatsbiz-prod/dataprotection-keys`
- SPA web root used by the Nginx file: `/var/www/whatsbiz-prod-web/browser`
- API loopback port used by the prepared service/Nginx files: `5002` (separate from QA's `5001`)

The production database wrapper refuses every database except `WhatsBizERP_PROD`, requires encrypted SQL with certificate validation, creates and verifies a backup before modifying an existing DB, blocks possible data loss, reports/requires approval for table rebuilds, and requires a separate approval for existing-data tenant ownership backfills. It deliberately never runs `Bootstrap_QA.sql`, `Validate_QA_Bootstrap.sql`, QA tenant-hardening validation, or QA fixture data. SQL project post-deployment seed scripts run through the canonical DACPAC; production starts without a demo tenant. The canonical post-DACPAC chain is V7, V8, V18, V24, V25, V26, then `Production_PostValidation.sql`.

## Host setup commands (proposed; do not run until production change approval)

Create the dedicated service identity/directories and persistent private key ring:

```bash
sudo useradd --system --user-group --home-dir /var/lib/whatsbiz-prod --create-home --shell /usr/sbin/nologin whatsbiz-prod
sudo install -d -o whatsbiz-prod -g whatsbiz-prod -m 0750 /var/www/whatsbiz-prod /var/www/whatsbiz-prod/Logs
sudo install -d -o whatsbiz-prod -g whatsbiz-prod -m 0700 /var/lib/whatsbiz-prod/dataprotection-keys
sudo install -d -o root -g whatsbiz-prod -m 0750 /etc/whatsbiz
sudo install -o root -g whatsbiz-prod -m 0640 deployment/prod.env.example /etc/whatsbiz/prod.env
sudoedit /etc/whatsbiz/prod.env
sudo install -o root -g root -m 0644 deployment/whatsbiz-prod.service /etc/systemd/system/whatsbiz-prod.service
sudo systemctl daemon-reload
```

Replace every placeholder in `prod.env` through the secure server editor/secret delivery process. Keep the owner password disabled unless doing initial owner creation; after the first successful startup set `IdentityBootstrap__ApplicationOwner__Enabled=false`, remove the password, and leave `ResetPasswordOnStart=false`. The seeded owner is `TenantId=NULL`; do not create Arjun Garments as part of this deployment.

The service owns the persistent Data Protection key ring, uses application name `WhatsBizERP-Production`, and does not store keys under the publish directory. Preserve this directory across API deployments and backups; never copy the QA key ring into production.

## Production web/API and certificate setup

Confirm DNS points both production hostnames at the approved production edge. If the certificates do not exist, configure `deployment/nginx/khatadhari-production-acme.conf` as an HTTP-only temporary site and create the webroot:

```bash
sudo install -d -o root -g www-data -m 0755 /var/www/letsencrypt
sudo install -o root -g root -m 0644 deployment/nginx/khatadhari-production-acme.conf /etc/nginx/sites-available/khatadhari-production-acme
sudo ln -s /etc/nginx/sites-available/khatadhari-production-acme /etc/nginx/sites-enabled/khatadhari-production-acme
sudo nginx -t
sudo systemctl reload nginx
sudo certbot certonly --webroot -w /var/www/letsencrypt -d api.khatadhari.com
sudo certbot certonly --webroot -w /var/www/letsencrypt -d app.khatadhari.com
```

After certificates are issued, replace the temporary site with `deployment/nginx/api.khatadhari.com.conf` and `deployment/nginx/app.khatadhari.com.conf`, validate `nginx -t`, then reload only after explicit deployment approval. The API vhost proxies TLS traffic to `127.0.0.1:5002`. The SPA vhost serves the Angular browser output from `/var/www/whatsbiz-prod-web/browser`, uses Angular history fallback, and serves `/runtime-config.json` with `Cache-Control: no-store`.

For the UI release, run `npm run build -- --configuration production` from `frontend/WhatsBiz.Web`; copy the build's `browser` contents to the approved web root and install `deployment/runtime-config.production.json` there as `runtime-config.json`. This runtime config points only to `https://api.khatadhari.com`.

## Database plan and deployment sequence

The database script reads only the process environment variable `WHATSBIZ_PROD_SQL_CONNECTION`; it never writes the connection string to output or a file. The connection string must have `Initial Catalog=WhatsBizERP_PROD`, `Encrypt=True`, and `TrustServerCertificate=False`. The deployment operator must use a SQL login/identity with the least privileges needed and a SQL Server-accessible backup destination. Do not use a QA or development credential/connection.

First build and review the generated SQL plan without publishing:

```powershell
# WHATSBIZ_PROD_SQL_CONNECTION is injected by the approved secret provider into this process.
.\deployment\deploy-prod-database.ps1 -PlanOnly
```

Review the report and SQL script paths printed by the wrapper. Resolve any object drops, table rebuilds, data-loss warnings, or ownership ambiguity before proceeding. Existing database V7/V8/V26 scripts update tenant ownership only where deterministic; inspect their before/after ownership reports and obtain explicit approval before passing `-ApproveTenantOwnershipBackfill`. Supply a verified server-side backup path when the production DB already exists.

Only after a separate production deployment approval, and after supplying the production SSH endpoint and key that own `whatsbiz-prod`, the database publish command is:

```powershell
.\deployment\deploy-prod-database.ps1 -ApproveProductionDeployment -BackupPath '<SQL-server-accessible-backup-path>' -ApproveTenantOwnershipBackfill -SshHost '<production-api-host>' -SshUser '<approved-deploy-user>' -SshKey '<private-key-path-outside-repository>'
```

The wrapper verifies over SSH that `whatsbiz-prod` loads `/etc/whatsbiz/prod.env`, backs up and verifies an existing production database, stops the production API before schema changes, and restarts it only after post-deployment validation succeeds. If schema deployment/validation fails, it deliberately leaves the API stopped for operator review. These SSH arguments are not needed for `-PlanOnly`.

Add `-ApproveTableRebuild` only if the reviewed DACPAC report contains table rebuilds and each is separately approved. Do not pass it by default. A new DB has no production fixture bootstrap; the canonical PostDeployment seed chain initializes shared schema/lookup data only.

## Application Owner and Meta setup

If a production Application Owner does not already exist, use the production `IdentityBootstrap__ApplicationOwner__*` environment options for a one-time creation with a strong out-of-band password; then disable and remove the password setting. Login is `/application-owner/login`.

After the API is live, configure the shared platform at `/admin/whatsapp-platform`: production Meta App ID, App Secret, webhook verify token, and enabled flag. Set `WhatsApp__Meta__EmbeddedSignupConfigurationId` in `prod.env`. The shared secret values are protected and saved to `integration.WhatsAppPlatformConfiguration`; do not copy the QA row or its protected values. The webhook is `https://api.khatadhari.com/api/whatsapp/webhook`.

Meta dashboard checklist:

- App domain: `app.khatadhari.com`.
- Website URL: `https://app.khatadhari.com/`.
- Allowed JavaScript SDK origin/domain: `https://app.khatadhari.com` (use the dashboard's required origin format).
- Facebook Login for Business: enable the existing KhataDhari Embedded Signup configuration and required reviewed permissions.
- Embedded Signup configuration: use the production configuration ID, not the QA one.
- Webhooks: production callback above; subscribe the WhatsApp Business Account `messages` field.
- This frontend uses the Facebook JavaScript SDK `FB.login` with `config_id`; it does not pass a redirect URI. Do not invent one.

## Rollback outline

Before deployment, preserve the old API/web release directories and verify a restorable SQL backup (including `RESTORE VERIFYONLY`). Roll back application releases by restoring the prior API/web artifacts and restarting `whatsbiz-prod`/reloading Nginx as needed. Database rollback is not an automatic DACPAC reverse: restore the verified pre-deployment backup during an approved maintenance window, then validate the restored database identity and API health before reopening traffic. Keep the Data Protection key ring intact during rollback.

## Meta HTTP request logging

The OAuth code exchange uses the Meta Graph OAuth access-token endpoint with required parameters in its request URI. The named `MetaWhatsApp` `HttpClient` has factory request-URI logging removed so that the App Secret and one-time code are not emitted by default HttpClientFactory logs. Keep outbound HTTP/APM capture configured to redact query strings for `graph.facebook.com`; never enable full URL/header capture for this client. Application-level Meta diagnostics remain sanitized.
