<#
KhataDhari Customer PWA / Shop - Production Component Deployment

This repository-owned component deployer intentionally handles only the static
Customer PWA. It does not deploy the API, ERP Angular application, website,
database, QA, Nginx, DNS, or TLS, and it never restarts whatsbiz-prod.

Examples:
  .\deployment\deploy-customer-pwa-prod.ps1 -DeployCustomerPwa -DryRun
  .\deployment\deploy-customer-pwa-prod.ps1 -DeployCustomerPwa -ConfirmProduction
  .\deployment\deploy-customer-pwa-prod.ps1 -Status
  .\deployment\deploy-customer-pwa-prod.ps1 -ListReleases
  .\deployment\deploy-customer-pwa-prod.ps1 -Rollback 20260924-180000 -ConfirmProductionRollback
#>

[CmdletBinding(DefaultParameterSetName = 'Deploy')]
param(
    [Parameter(ParameterSetName = 'Deploy', Mandatory = $true)]
    [switch] $DeployCustomerPwa,

    [Parameter(ParameterSetName = 'Deploy')]
    [switch] $ConfirmProduction,

    [Parameter(ParameterSetName = 'Deploy')]
    [switch] $DryRun,

    [Parameter(ParameterSetName = 'Deploy')]
    [switch] $ValidateOnly,

    [Parameter(ParameterSetName = 'Deploy')]
    [ValidatePattern('^\d{8}-\d{6}$')]
    [string] $ReleaseId,

    [Parameter(ParameterSetName = 'Status', Mandatory = $true)]
    [switch] $Status,

    [Parameter(ParameterSetName = 'List', Mandatory = $true)]
    [switch] $ListReleases,

    [Parameter(ParameterSetName = 'Rollback', Mandatory = $true)]
    [ValidatePattern('^\d{8}-\d{6}$')]
    [string] $Rollback,

    [Parameter(ParameterSetName = 'Rollback', Mandatory = $true)]
    [switch] $ConfirmProductionRollback
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$server = '93.127.198.50'
$sshUser = 'root'
$sshKey = Join-Path $env:USERPROFILE '.ssh\khatadhari_qa_deploy'
$sshOptions = @('-i', $sshKey, '-o', 'IdentitiesOnly=yes', '-o', 'BatchMode=yes')
$sshTarget = "$sshUser@$server"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$customerPwaRoot = Join-Path $repositoryRoot 'frontend\KhataDhari.Customer'
$customerPwaDist = Join-Path $customerPwaRoot 'dist\KhataDhari.Customer\browser'
$packageLock = Join-Path $customerPwaRoot 'package-lock.json'

$remoteRoot = '/var/www/khatadhari-customer'
$remoteLive = "$remoteRoot/browser"
$remoteReleases = "$remoteRoot/releases"
$remoteStageRoot = '/tmp/khatadhari-customer-deploy'
$shopUrl = 'https://shop.khatadhari.com'
$storeUrl = 'https://shop.khatadhari.com/guturgo'
$maxReleases = 10

if ([string]::IsNullOrWhiteSpace($ReleaseId)) {
    $ReleaseId = Get-Date -Format 'yyyyMMdd-HHmmss'
}
$localWork = Join-Path $env:TEMP "khatadhari-customer-pwa-$ReleaseId"
$localArchive = Join-Path $localWork "SHOP-$ReleaseId.tar.gz"
$resolvedTempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$resolvedLocalWork = [IO.Path]::GetFullPath($localWork)
if (-not $resolvedLocalWork.StartsWith($resolvedTempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to use a work directory outside the system temporary directory: $resolvedLocalWork"
}
$remoteStage = "$remoteStageRoot/$ReleaseId"
$remoteArchive = "$remoteStage/SHOP-$ReleaseId.tar.gz"
$remoteIncoming = "$remoteReleases/$ReleaseId.incoming"
$remoteRelease = "$remoteReleases/$ReleaseId"

function Write-Step([string] $Message) {
    Write-Host ''
    Write-Host '============================================================'
    Write-Host $Message
    Write-Host '============================================================'
}

function Require-Path([string] $Path, [string] $Description) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "$Description not found: $Path" }
}

function Invoke-Native([string] $File, [string[]] $Arguments) {
    Write-Host ">> $File $($Arguments -join ' ')"
    $global:LASTEXITCODE = 0
    & $File @Arguments
    $exitCode = $LASTEXITCODE
    if (-not $?) { throw "Command invocation failed: $File" }
    if ($exitCode -ne 0) { throw "Command failed with exit code $exitCode`: $File" }
}

function Invoke-Ssh([string] $Command) {
    Invoke-Native 'ssh' ($sshOptions + @($sshTarget, $Command))
}

function Test-PublicUrl([string] $Url) {
    Write-Host "Checking $Url"
    try {
        $response = Invoke-WebRequest -Uri $Url -Method Head -UseBasicParsing -TimeoutSec 20
    }
    catch {
        $response = Invoke-WebRequest -Uri $Url -Method Get -UseBasicParsing -TimeoutSec 20
    }
    if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 400) {
        throw "Unexpected HTTP $($response.StatusCode) from $Url"
    }
    Write-Host "HTTP $($response.StatusCode) - $Url"
}

function Get-PublishCommand([string] $TargetReleaseId) {
    $targetRelease = "$remoteReleases/$TargetReleaseId"
    return @"
set -eu
test -f '$targetRelease/index.html'
test -f '$targetRelease/manifest.webmanifest'
test -f '$targetRelease/ngsw-worker.js'
test -f '$targetRelease/ngsw.json'
previous=''
if [ -L '$remoteLive' ]; then
  previous=`$(readlink -f '$remoteLive')
elif [ -d '$remoteLive' ]; then
  previous='$remoteReleases/legacy-$TargetReleaseId'
  test ! -e "`$previous"
  mv '$remoteLive' "`$previous"
fi
printf '%s' "`$previous" > '$remoteStage/previous-target'
ln -sfn 'releases/$TargetReleaseId' '$remoteRoot/browser.next'
mv -Tf '$remoteRoot/browser.next' '$remoteLive'
test "`$(readlink -f '$remoteLive')" = '$targetRelease'
"@
}

function Restore-PreviousRelease {
    $command = @"
set -eu
previous=`$(cat '$remoteStage/previous-target' 2>/dev/null || true)
if [ -n "`$previous" ] && [ -d "`$previous" ]; then
  ln -sfn "`$previous" '$remoteRoot/browser.rollback'
  mv -Tf '$remoteRoot/browser.rollback' '$remoteLive'
elif [ -L '$remoteLive' ]; then
  rm -f '$remoteLive'
  echo 'No previous Shop release existed; the failed live link was removed.' >&2
else
  echo 'No previous Shop release was recorded; the existing live directory was left untouched.' >&2
fi
"@
    Invoke-Ssh $command
}

if ($PSCmdlet.ParameterSetName -eq 'Status') {
    Require-Path $sshKey 'SSH private key'
    Invoke-Ssh "set -eu; if [ -L '$remoteLive' ]; then readlink -f '$remoteLive'; elif [ -d '$remoteLive' ]; then echo '$remoteLive (legacy directory)'; else echo 'Shop is not published'; fi; if [ -f '$remoteLive/RELEASE-SHOP.txt' ]; then cat '$remoteLive/RELEASE-SHOP.txt'; fi"
    return
}

if ($PSCmdlet.ParameterSetName -eq 'List') {
    Require-Path $sshKey 'SSH private key'
    Invoke-Ssh "set -eu; mkdir -p '$remoteReleases'; for d in `$(ls -1dt '$remoteReleases'/20* 2>/dev/null || true); do [ -d \"`$d\" ] && basename \"`$d\"; done"
    return
}

if ($PSCmdlet.ParameterSetName -eq 'Rollback') {
    Require-Path $sshKey 'SSH private key'
    Write-Step "Rollback Customer PWA to $Rollback"
    $remoteStage = "$remoteStageRoot/rollback-$ReleaseId"
    Invoke-Ssh "set -eu; mkdir -p '$remoteStage'; test -d '$remoteReleases/$Rollback'"
    try {
        Invoke-Ssh (Get-PublishCommand $Rollback)
        Test-PublicUrl $shopUrl
        Test-PublicUrl $storeUrl
    }
    catch {
        Restore-PreviousRelease
        throw 'Shop rollback failed during publication or URL validation; the previously published Shop release was restored.'
    }
    Invoke-Ssh "rm -rf '$remoteStage'"
    Write-Host "Customer PWA rollback complete: $Rollback"
    return
}

if (-not $DeployCustomerPwa) { throw 'Customer PWA selection is required.' }
if ($DryRun -and $ValidateOnly) { throw 'Choose either -DryRun or -ValidateOnly, not both.' }
if ($ValidateOnly) {
    Require-Path $customerPwaRoot 'Customer PWA project'
    Require-Path $packageLock 'Customer PWA package lock'
    Write-Host 'VALID: Customer PWA is the only selected component.'
    Write-Host 'VALID: API, ERP Angular, website, database, and QA are not part of this script.'
    Write-Host "VALID: Build output is $customerPwaDist"
    Write-Host "VALID: Live destination is $remoteLive"
    return
}
if (-not $DryRun -and -not $ConfirmProduction) {
    throw 'Customer PWA production deployment requires -ConfirmProduction. Use -DryRun first.'
}

Require-Path $customerPwaRoot 'Customer PWA project'
Require-Path $packageLock 'Customer PWA package lock'

if ($DryRun) {
    Write-Step 'Customer PWA production deployment plan (no commands executed)'
    Write-Host "Release ID: $ReleaseId"
    Write-Host 'Selected component: SHOP only'
    Write-Host "Build: npm ci; npm run build:production in $customerPwaRoot"
    Write-Host 'Required artifacts: index.html, manifest.webmanifest, ngsw-worker.js, ngsw.json'
    Write-Host "Remote stage: $remoteStage"
    Write-Host "Immutable release: $remoteRelease"
    Write-Host "Atomic live link: $remoteLive -> releases/$ReleaseId"
    Write-Host "Verification: $shopUrl and $storeUrl"
    Write-Host 'API/service/database actions: none'
    return
}

Require-Path $sshKey 'SSH private key'
New-Item -ItemType Directory -Force -Path $localWork | Out-Null

try {
    Write-Step '1. Install Customer PWA dependencies'
    Push-Location $customerPwaRoot
    try {
        Invoke-Native 'npm.cmd' @('ci')
        Write-Step '2. Build Customer PWA production bundle'
        Invoke-Native 'npm.cmd' @('run', 'build:production')
    }
    finally { Pop-Location }

    foreach ($artifact in @('index.html', 'manifest.webmanifest', 'ngsw-worker.js', 'ngsw.json')) {
        Require-Path (Join-Path $customerPwaDist $artifact) "Customer PWA artifact $artifact"
    }

    Write-Step '3. Package complete Customer PWA bundle'
    Push-Location $customerPwaDist
    try { Invoke-Native 'tar' @('-czf', $localArchive, '.') }
    finally { Pop-Location }
    Require-Path $localArchive 'Customer PWA release archive'

    Write-Step '4. Stage Customer PWA release on VPS'
    Invoke-Ssh "set -eu; test -d '$remoteRoot'; mkdir -p '$remoteStage' '$remoteReleases'; test ! -e '$remoteRelease'; test ! -e '$remoteIncoming'"
    Invoke-Native 'scp' ($sshOptions + @($localArchive, "$sshTarget`:$remoteArchive"))
    $prepare = @"
set -eu
mkdir -p '$remoteIncoming'
tar -xzf '$remoteArchive' -C '$remoteIncoming'
test -f '$remoteIncoming/index.html'
test -f '$remoteIncoming/manifest.webmanifest'
test -f '$remoteIncoming/ngsw-worker.js'
test -f '$remoteIncoming/ngsw.json'
find '$remoteIncoming' -type d -exec chmod 0755 {} +
find '$remoteIncoming' -type f -exec chmod 0644 {} +
chown -R root:root '$remoteIncoming'
printf '%s\n' 'ReleaseId=$ReleaseId' 'Component=SHOP' > '$remoteIncoming/RELEASE-SHOP.txt'
mv '$remoteIncoming' '$remoteRelease'
"@
    Invoke-Ssh $prepare

    try {
        Write-Step '5. Publish Customer PWA release safely'
        Invoke-Ssh (Get-PublishCommand $ReleaseId)

        Write-Step '6. Verify public Shop URLs'
        Test-PublicUrl $shopUrl
        Test-PublicUrl $storeUrl
    }
    catch {
        Write-Warning 'Customer PWA publication or URL verification failed. Restoring the previous Shop release.'
        Restore-PreviousRelease
        throw 'Customer PWA deployment failed during publication or URL validation; the previous Shop release was restored.'
    }

    Write-Step "7. Retain latest $maxReleases Shop releases"
    $retention = @"
set -eu
current=`$(readlink -f '$remoteLive')
count=0
for d in `$(ls -1dt '$remoteReleases'/20* 2>/dev/null || true); do
  [ -d "`$d" ] || continue
  [ "`$(readlink -f "`$d")" = "`$current" ] && continue
  count=`$((count+1))
  [ "`$count" -lt $maxReleases ] || rm -rf -- "`$d"
done
rm -rf '$remoteStage'
"@
    Invoke-Ssh $retention

    Write-Step 'CUSTOMER PWA PRODUCTION DEPLOYMENT COMPLETE'
    Write-Host "Release ID: $ReleaseId"
    Write-Host "Shop: $shopUrl"
    Write-Host "Store route: $storeUrl"
    Write-Host 'API service was not restarted or deployed. Database was not accessed.'
}
finally {
    if (Test-Path -LiteralPath $resolvedLocalWork) { Remove-Item -LiteralPath $resolvedLocalWork -Recurse -Force }
}
