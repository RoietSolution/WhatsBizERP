# Customer PWA production deployment

The Shop is an independent static deployment component. Its version-controlled
implementation is `deployment/deploy-customer-pwa-prod.ps1`; it does not deploy
or restart the API, ERP Angular application, public website, database, or QA.

## Commands

Validate local component selection without building or connecting to production:

```powershell
.\deployment\deploy-customer-pwa-prod.ps1 -DeployCustomerPwa -ValidateOnly
```

Print the production plan without executing native commands:

```powershell
.\deployment\deploy-customer-pwa-prod.ps1 -DeployCustomerPwa -DryRun
```

Deploy only the Customer PWA:

```powershell
.\deployment\deploy-customer-pwa-prod.ps1 -DeployCustomerPwa -ConfirmProduction
```

Status, releases, and rollback:

```powershell
.\deployment\deploy-customer-pwa-prod.ps1 -Status
.\deployment\deploy-customer-pwa-prod.ps1 -ListReleases
.\deployment\deploy-customer-pwa-prod.ps1 -Rollback 20260924-180000 -ConfirmProductionRollback
```

The build runs `npm ci` followed by `npm run build:production` and refuses to
upload unless `index.html`, `manifest.webmanifest`, `ngsw-worker.js`, and
`ngsw.json` exist. The archive is uploaded to
`/tmp/khatadhari-customer-deploy/<release-id>`, extracted and validated at
`/var/www/khatadhari-customer/releases/<release-id>`, and published through the
`/var/www/khatadhari-customer/browser` symlink. Subsequent link replacements are
atomic, preventing mixed Angular hashes. A failed public URL check automatically
restores the preceding link target.

## Existing external manager integration

`G:\Saas1\Deployment\deploy-khatadhari-prod.ps1` remains the manager for API,
ERP Angular, and website releases. The Shop implementation remains
version-controlled here. Apply only this small delegation to that external file:

1. Add the Shop switch to its `Deploy` parameter set:

   ```powershell
   [Parameter(ParameterSetName="Deploy")]
   [switch] $DeployCustomerPwa,
   ```
2. After `$Stamp` and `$RepoRoot` are initialized, add:

   ```powershell
   $CustomerPwaDeployer = Join-Path $RepoRoot 'deployment\deploy-customer-pwa-prod.ps1'
   ```

3. Immediately after the existing production-confirmation guard (currently the
   block before `Step "0. Preflight checks"`), add this Shop-only short-circuit.
   This placement preserves the manager's `-ConfirmProduction` protection while
   bypassing its API/service, ERP, website, and database-independent preflight:

   ```powershell
   if ($DeployCustomerPwa -and $SkipApi -and $SkipAngular -and $SkipWebsite) {
       $shopArgs = @('-DeployCustomerPwa', '-ReleaseId', $Stamp)
       if ($DryRun) {
           $shopArgs += '-DryRun'
       } elseif ($ConfirmProduction) {
           $shopArgs += '-ConfirmProduction'
       } else {
           throw 'Customer PWA production deployment requires -ConfirmProduction.'
       }
       & $CustomerPwaDeployer @shopArgs
       if (-not $?) { throw 'Customer PWA deployment failed.' }
       return
   }
   ```

4. Near the end of the normal application deployment, after existing components
   pass verification, add the optional combined component:

   ```powershell
   if ($DeployCustomerPwa) {
       $shopArgs = @('-DeployCustomerPwa', '-ReleaseId', $Stamp)
       if ($DryRun) {
           $shopArgs += '-DryRun'
       } elseif ($ConfirmProduction) {
           $shopArgs += '-ConfirmProduction'
       } else {
           throw 'Customer PWA production deployment requires -ConfirmProduction.'
       }
       & $CustomerPwaDeployer @shopArgs
       if (-not $?) { throw 'Customer PWA deployment failed.' }
   }
   ```

Do not attach this delegation to `systemctl`, database, or QA branches. Existing
manager invocations without `-DeployCustomerPwa` retain their current behavior.

After applying the delegation, the external-manager commands are:

```powershell
# Shop only
G:\Saas1\Deployment\deploy-khatadhari-prod.ps1 -DeployCustomerPwa -SkipApi -SkipAngular -SkipWebsite -ConfirmProduction

# Existing full application selection plus the optional Shop component
G:\Saas1\Deployment\deploy-khatadhari-prod.ps1 -DeployCustomerPwa -ConfirmProduction
```
