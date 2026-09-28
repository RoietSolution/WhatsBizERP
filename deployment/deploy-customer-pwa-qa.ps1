<#
KhataDhari Customer PWA - QA-only static component deployment.
Mirrors the build, archive, immutable release, atomic publish, and rollback
structure of deploy-customer-pwa-prod.ps1. No other component is deployed.

Examples:
  .\deployment\deploy-customer-pwa-qa.ps1 -DeployCustomerPwa -DryRun
  .\deployment\deploy-customer-pwa-qa.ps1 -DeployCustomerPwa -ValidateOnly
  .\deployment\deploy-customer-pwa-qa.ps1 -DeployCustomerPwa -ConfirmQa
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [switch] $DeployCustomerPwa,
    [switch] $ConfirmQa,
    [switch] $DryRun,
    [switch] $ValidateOnly,
    [ValidatePattern('^\d{8}-\d{6}$')] [string] $ReleaseId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$server = '93.127.198.50'
$sshUser = 'root'
$sshKey = Join-Path $env:USERPROFILE '.ssh\khatadhari_qa_deploy'
$sshOptions = @('-i', $sshKey, '-o', 'IdentitiesOnly=yes', '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15')
$sshTarget = "$sshUser@$server"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$customerPwaRoot = Join-Path $repositoryRoot 'frontend\KhataDhari.Customer'
$customerPwaDist = Join-Path $customerPwaRoot 'dist\KhataDhari.Customer\browser'
$packageLock = Join-Path $customerPwaRoot 'package-lock.json'
$qaEnvironment = Join-Path $customerPwaRoot 'src\environments\environment.qa.ts'
$remoteRoot = '/var/www/khatadhari-customer-qa'
$remoteLive = "$remoteRoot/browser"
$remoteReleases = "$remoteRoot/releases"
$remoteStageRoot = '/tmp/khatadhari-customer-qa-deploy'
$shopUrl = 'https://qa-shop.khatadhari.com'
$apiUrl = 'https://qa-api.khatadhari.com'

# The SSH host also serves PROD. A path/config edit must fail before SSH or build.
if ($remoteRoot -cne '/var/www/khatadhari-customer-qa' -or
    $remoteLive -cne '/var/www/khatadhari-customer-qa/browser' -or
    $remoteStageRoot -cne '/tmp/khatadhari-customer-qa-deploy' -or
    $shopUrl -cne 'https://qa-shop.khatadhari.com' -or
    $apiUrl -cne 'https://qa-api.khatadhari.com') {
    throw 'QA/PROD target guard failed.'
}
if ([string]::IsNullOrWhiteSpace($ReleaseId)) { $ReleaseId = Get-Date -Format 'yyyyMMdd-HHmmss' }
$localWork = Join-Path $env:TEMP "khatadhari-customer-pwa-qa-$ReleaseId"
$localArchive = Join-Path $localWork "QA-SHOP-$ReleaseId.tar.gz"
$resolvedTempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$resolvedLocalWork = [IO.Path]::GetFullPath($localWork)
if (-not $resolvedLocalWork.StartsWith($resolvedTempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing a work directory outside the system temporary directory.'
}
$remoteStage = "$remoteStageRoot/$ReleaseId"
$remoteArchive = "$remoteStage/QA-SHOP-$ReleaseId.tar.gz"
$remoteIncoming = "$remoteReleases/$ReleaseId.incoming"
$remoteRelease = "$remoteReleases/$ReleaseId"

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
function Test-QaRemoteRoot {
    $command = @"
set -eu
test -d '$remoteRoot'
test ! -L '$remoteRoot'
test "`$(readlink -f '$remoteRoot')" = '$remoteRoot'
id www-data >/dev/null
if [ -L '$remoteLive' ]; then
  current=`$(readlink -f '$remoteLive')
  case "`$current" in '$remoteReleases'/*) ;; *) echo 'QA live link leaves QA releases' >&2; exit 1 ;; esac
fi
"@
    Invoke-Ssh $command
}
function Test-PublicUrl([string] $Url) {
    try { $response = Invoke-WebRequest -Uri $Url -Method Head -UseBasicParsing -TimeoutSec 20 }
    catch { $response = Invoke-WebRequest -Uri $Url -Method Get -UseBasicParsing -TimeoutSec 20 }
    if ($response.StatusCode -lt 200 -or $response.StatusCode -ge 400) {
        throw "Unexpected HTTP $($response.StatusCode) from $Url"
    }
    Write-Host "HTTP $($response.StatusCode) - $Url"
}
function Restore-PreviousRelease {
    $command = @"
set -eu
test -f '$remoteStage/previous-target' || exit 0
previous=`$(cat '$remoteStage/previous-target' 2>/dev/null || true)
if [ -n "`$previous" ] && [ -d "`$previous" ]; then
  case "`$previous" in '$remoteReleases'/*) ;; *) exit 1 ;; esac
  ln -sfn "`$previous" '$remoteRoot/browser.rollback'
  mv -Tf '$remoteRoot/browser.rollback' '$remoteLive'
elif [ -L '$remoteLive' ]; then
  rm -f '$remoteLive'
fi
"@
    Invoke-Ssh $command
}

if ($DryRun -and $ValidateOnly) { throw 'Choose either -DryRun or -ValidateOnly.' }
Require-Path $customerPwaRoot 'Customer PWA project'
Require-Path $packageLock 'Customer PWA package lock'
Require-Path $qaEnvironment 'QA Angular environment'
if (-not (Select-String -LiteralPath $qaEnvironment -SimpleMatch $apiUrl -Quiet)) {
    throw 'QA Angular environment does not point at the expected QA API.'
}
if ($ValidateOnly) {
    Write-Host 'VALID: QA Customer PWA is the only selected component.'
    Write-Host 'VALID: Angular build configuration qa targets the QA API.'
    Write-Host "VALID: Browser output is $customerPwaDist"
    Write-Host "VALID: Live destination is $remoteLive"
    Write-Host 'VALID: No API, ERP frontend, website, database, or PROD action.'
    return
}
if ($DryRun) {
    Write-Host "QA release: $ReleaseId"
    Write-Host "Build: npm ci; npm run ng -- build --configuration qa in $customerPwaRoot"
    Write-Host "Only generated browser build is archived and staged at $remoteStage"
    Write-Host "Atomic QA live link: $remoteLive -> releases/$ReleaseId"
    Write-Host "Verification: $shopUrl; configured API: $apiUrl"
    Write-Host 'No command executed; PROD and other components are untouched.'
    return
}
if (-not $ConfirmQa) { throw 'QA Customer PWA deployment requires -ConfirmQa. Use -DryRun first.' }
Require-Path $sshKey 'SSH private key'
Test-QaRemoteRoot
New-Item -ItemType Directory -Force -Path $localWork | Out-Null
try {
    Push-Location $customerPwaRoot
    try {
        Invoke-Native 'npm.cmd' @('ci')
        Invoke-Native 'npm.cmd' @('run', 'ng', '--', 'build', '--configuration', 'qa')
    }
    finally { Pop-Location }
    foreach ($artifact in @('index.html', 'manifest.webmanifest', 'ngsw-worker.js', 'ngsw.json')) {
        Require-Path (Join-Path $customerPwaDist $artifact) "Customer PWA artifact $artifact"
    }
    $qaApiFound = Get-ChildItem -LiteralPath $customerPwaDist -Filter '*.js' -File -Recurse |
        Select-String -SimpleMatch $apiUrl -Quiet
    if (-not $qaApiFound) { throw 'QA API URL was not found in the generated browser JavaScript.' }
    Push-Location $customerPwaDist
    try { Invoke-Native 'tar' @('-czf', $localArchive, '.') }
    finally { Pop-Location }
    Require-Path $localArchive 'QA Customer PWA release archive'

    Invoke-Ssh "set -eu; test -d '$remoteRoot'; test ! -L '$remoteRoot'; mkdir -p '$remoteStage' '$remoteReleases'; test ! -e '$remoteRelease'; test ! -e '$remoteIncoming'"
    Invoke-Native 'scp' ($sshOptions + @($localArchive, "$sshTarget`:$remoteArchive"))
    $prepare = @"
set -eu
mkdir -p '$remoteIncoming'
tar -xzf '$remoteArchive' -C '$remoteIncoming'
test -f '$remoteIncoming/index.html'
test -f '$remoteIncoming/manifest.webmanifest'
test -f '$remoteIncoming/ngsw-worker.js'
test -f '$remoteIncoming/ngsw.json'
printf '%s\n' 'ReleaseId=$ReleaseId' 'Component=QA-SHOP' > '$remoteIncoming/RELEASE-QA-SHOP.txt'
find '$remoteIncoming' -type d -exec chmod 0755 {} +
find '$remoteIncoming' -type f -exec chmod 0644 {} +
chown -R www-data:www-data '$remoteIncoming'
mv '$remoteIncoming' '$remoteRelease'
"@
    Invoke-Ssh $prepare
    $publish = @"
set -eu
test -f '$remoteRelease/index.html'
previous=''
if [ -L '$remoteLive' ]; then
  previous=`$(readlink -f '$remoteLive')
  case "`$previous" in '$remoteReleases'/*) ;; *) exit 1 ;; esac
elif [ -d '$remoteLive' ]; then
  previous='$remoteReleases/legacy-$ReleaseId'
  test ! -e "`$previous"
fi
printf '%s' "`$previous" > '$remoteStage/previous-target'
if [ -d '$remoteLive' ] && [ ! -L '$remoteLive' ]; then
  mv '$remoteLive' "`$previous"
fi
ln -sfn 'releases/$ReleaseId' '$remoteRoot/browser.next'
mv -Tf '$remoteRoot/browser.next' '$remoteLive'
test "`$(readlink -f '$remoteLive')" = '$remoteRelease'
"@
    try {
        Invoke-Ssh $publish
        Test-PublicUrl $shopUrl
    }
    catch {
        Write-Warning 'QA Shop publication or URL verification failed; restoring the previous QA release.'
        Restore-PreviousRelease
        throw 'QA Customer PWA deployment failed; the previous QA release was restored.'
    }
    Invoke-Ssh "rm -rf -- '$remoteStage'"
    Write-Host "QA Customer PWA deployed: $shopUrl (release $ReleaseId)"
    Write-Host 'API, ERP frontend, website, database, and PROD were not deployed.'
}
finally {
    if (Test-Path -LiteralPath $resolvedLocalWork) { Remove-Item -LiteralPath $resolvedLocalWork -Recurse -Force }
}
