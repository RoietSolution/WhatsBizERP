<#
KhataDhari / WhatsBiz QA - Deployment & Release Manager
=======================================================

Supports:
- Full deploy: API + Angular + Website
- Optional guarded database deployment from the resolved QA environment
- Component deploy: API / ANGULAR / WEBSITE
- Pre-deployment tests
- Dry run
- Status of currently deployed release markers
- List successful releases
- Roll back ALL components or a selected component
- Automatic safety backup before rollback
- Automatic API rollback if restored API fails health check
- Retain latest 10 successful release snapshots

Usage examples:
  .\deploy-khatadhari-qa.ps1
  .\deploy-khatadhari-qa.ps1 -RunTests
  .\deploy-khatadhari-qa.ps1 -Status
  .\deploy-khatadhari-qa.ps1 -ListReleases
  .\deploy-khatadhari-qa.ps1 -Rollback 20260901-184501
  .\deploy-khatadhari-qa.ps1 -Rollback 20260901-184501 -Component API
  .\deploy-khatadhari-qa.ps1 -Rollback 20260901-184501 -Component ANGULAR
  .\deploy-khatadhari-qa.ps1 -Rollback 20260901-184501 -Component WEBSITE
  .\deploy-khatadhari-qa.ps1 -Rollback 20260901-184501 -Component ALL
  .\deploy-khatadhari-qa.ps1 -SkipApi
  .\deploy-khatadhari-qa.ps1 -SkipAngular
  .\deploy-khatadhari-qa.ps1 -SkipWebsite
  .\deploy-khatadhari-qa.ps1 -DryRun
  .\deploy-khatadhari-qa.ps1 -Database
  .\deploy-khatadhari-qa.ps1 -Database -SkipApi -SkipAngular -SkipWebsite

Security:
- No passwords/tokens/connection strings/secrets are stored here.
- QA environment secrets remain in /etc/whatsbiz/qa.env.
- ASP.NET Data Protection keys remain in /var/lib/whatsbiz-qa/data-protection-keys.

QUICK COMMAND REFERENCE
-----------------------
Run from the folder containing this script.

Allow scripts in current PowerShell session:
  Set-ExecutionPolicy -Scope Process Bypass

FULL DEPLOY / PUBLISH:
  .\deploy-khatadhari-qa.ps1

FULL DEPLOY WITH TESTS:
  .\deploy-khatadhari-qa.ps1 -RunTests

DRY RUN:
  .\deploy-khatadhari-qa.ps1 -DryRun

API ONLY:
  .\deploy-khatadhari-qa.ps1 -SkipAngular -SkipWebsite

ANGULAR ONLY:
  .\deploy-khatadhari-qa.ps1 -SkipApi -SkipWebsite

WEBSITE ONLY:
  .\deploy-khatadhari-qa.ps1 -SkipApi -SkipAngular

API + ANGULAR:
  .\deploy-khatadhari-qa.ps1 -SkipWebsite

API + WEBSITE:
  .\deploy-khatadhari-qa.ps1 -SkipAngular

ANGULAR + WEBSITE:
  .\deploy-khatadhari-qa.ps1 -SkipApi

CURRENT DEPLOYED STATUS:
  .\deploy-khatadhari-qa.ps1 -Status

LIST AVAILABLE SUCCESSFUL RELEASES:
  .\deploy-khatadhari-qa.ps1 -ListReleases

ROLLBACK ALL COMPONENTS:
  .\deploy-khatadhari-qa.ps1 -Rollback RELEASE_ID -Component ALL
Example:
  .\deploy-khatadhari-qa.ps1 -Rollback 20260901-184501 -Component ALL

ROLLBACK API ONLY:
  .\deploy-khatadhari-qa.ps1 -Rollback RELEASE_ID -Component API

ROLLBACK ANGULAR ONLY:
  .\deploy-khatadhari-qa.ps1 -Rollback RELEASE_ID -Component ANGULAR

ROLLBACK WEBSITE ONLY:
  .\deploy-khatadhari-qa.ps1 -Rollback RELEASE_ID -Component WEBSITE

RECOMMENDED ROLLBACK FLOW:
  1. .\deploy-khatadhari-qa.ps1 -ListReleases
  2. .\deploy-khatadhari-qa.ps1 -Status
  3. .\deploy-khatadhari-qa.ps1 -Rollback RELEASE_ID -Component API|ANGULAR|WEBSITE|ALL
  4. .\deploy-khatadhari-qa.ps1 -Status

RELEASE STORAGE ON VPS:
  /var/backups/khatadhari-deploy/releases/<RELEASE_ID>/

ROLLBACK SAFETY STORAGE:
  /var/backups/khatadhari-deploy/rollback-safety/

RETENTION:
  Maximum 10 successful release snapshots.

USEFUL VPS DIAGNOSTICS:
  ssh root@93.127.198.50
  systemctl status whatsbiz-qa
  systemctl restart whatsbiz-qa
  journalctl -u whatsbiz-qa -n 100 --no-pager
  journalctl -u whatsbiz-qa -f
  nginx -t
  curl -i -H "Host: qa-api.khatadhari.com" http://127.0.0.1:5001/health
  ls -lah /var/backups/khatadhari-deploy/releases
  du -sh /var/backups/khatadhari-deploy/releases
  df -h

PUBLIC CHECKS:
  API:     https://qa-api.khatadhari.com/health
  Angular: https://qa.khatadhari.com
  Website: https://www.khatadhari.com
  Landing: https://www.khatadhari.com/retail-erp.html

FAILURE BEHAVIOR:
  - Build/test failure stops deployment before live replacement.
  - Failed deployment is not saved as a successful release.
  - API deployment keeps a pre-deploy safety copy.
  - API startup/health failure automatically restores the previous API.
  - Rollback creates a safety snapshot before restoring the selected release.

SECURITY:
  No SQL passwords, JWT keys, SMTP passwords, Meta secrets/tokens, or AWS
  credentials are stored in this script.
  QA secrets remain in /etc/whatsbiz/qa.env.
  Data Protection keys remain in /var/lib/whatsbiz-qa/data-protection-keys.

#>

[CmdletBinding(DefaultParameterSetName="Deploy")]
param(
    [Parameter(ParameterSetName="Deploy")]
    [switch]$SkipApi,

    [Parameter(ParameterSetName="Deploy")]
    [switch]$SkipAngular,

    [Parameter(ParameterSetName="Deploy")]
    [switch]$SkipWebsite,

    [Parameter(ParameterSetName="Deploy")]
    [switch]$RunTests,

    [Parameter(ParameterSetName="Deploy")]
    [switch]$DryRun,

    [Parameter(ParameterSetName="Deploy")]
    [switch]$Database,

    [Parameter(ParameterSetName="Deploy")]
    [switch]$ApproveDatabaseTableRebuild,

    [Parameter(ParameterSetName="Status", Mandatory=$true)]
    [switch]$Status,

    [Parameter(ParameterSetName="List", Mandatory=$true)]
    [switch]$ListReleases,

    [Parameter(ParameterSetName="Rollback", Mandatory=$true)]
    [string]$Rollback,

    [Parameter(ParameterSetName="Rollback")]
    [ValidateSet("ALL","API","ANGULAR","WEBSITE")]
    [string]$Component = "ALL"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------
# Configuration
# ---------------------------
$Server = "93.127.198.50"
$SshUser = "root"
$SshKey = "$env:USERPROFILE\.ssh\khatadhari_qa_deploy"
if (-not (Test-Path $SshKey)) {
    throw "SSH private key not found: $SshKey"
}
$SshOptions = @(
    "-i", $SshKey,
    "-o", "IdentitiesOnly=yes",
    "-o", "BatchMode=yes"
)
$SshTarget = "$SshUser@$Server"

$RepoRoot = "G:\Saas1\WhatsBizERP"
$ApiProject = Join-Path $RepoRoot "backend\src\WhatsBiz.Api\WhatsBiz.Api.csproj"
$DatabaseDeploy = Join-Path $RepoRoot "deployment\deploy-qa-database.ps1"
$ApiPublish = Join-Path $RepoRoot "publish\qa-api"
$AngularRoot = Join-Path $RepoRoot "frontend\WhatsBiz.Web"
$AngularDist = Join-Path $AngularRoot "dist\WhatsBiz.Web\browser"
$WebsiteRoot = "G:\Saas1\KhataDhari_Website\khatadhari"

$RemoteApi = "/var/www/whatsbiz-qa"
$RemoteAngular = "/var/www/whatsbiz-qa-web"
$RemoteWebsite = "/var/www/khatadhari"

$RemoteStage = "/tmp/khatadhari-deploy"
$RemoteReleases = "/var/backups/khatadhari-deploy/releases"
$RemoteSafety = "/var/backups/khatadhari-deploy/rollback-safety"

$ApiService = "whatsbiz-qa"

$ApiUrl = "https://qa-api.khatadhari.com"
$AngularUrl = "https://qa.khatadhari.com"
$WebsiteUrl = "https://www.khatadhari.com"

$MaxBackups = 10
$Stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$Work = Join-Path $env:TEMP "khatadhari-deploy-$Stamp"

# ---------------------------
# Helpers
# ---------------------------
function Step([string]$Message) {
    Write-Host ""
    Write-Host "============================================================"
    Write-Host $Message
    Write-Host "============================================================"
}

function Run([string]$File, [string[]]$Arguments) {
    $display = "$File " + ($Arguments -join " ")
    Write-Host ">> $display"

    if ($script:DryRun -and $PSCmdlet.ParameterSetName -eq "Deploy") {
        return
    }

    & $File @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code $LASTEXITCODE`: $display"
    }
}

function Run-Ssh([string]$Command) {
    Run "ssh" @($SshOptions + @($SshTarget, $Command))
}

function Require-Path([string]$Path, [string]$Description) {
    if (-not (Test-Path $Path)) {
        throw "$Description not found: $Path"
    }
}

function Test-Url([string]$Url) {
    Write-Host "Checking $Url"
    try {
        $response = Invoke-WebRequest -Uri $Url -Method Head -UseBasicParsing -TimeoutSec 20
        Write-Host "HTTP $($response.StatusCode) - $Url"
    }
    catch {
        $response = Invoke-WebRequest -Uri $Url -Method Get -UseBasicParsing -TimeoutSec 20
        Write-Host "HTTP $($response.StatusCode) - $Url"
    }
}

function Component-Selected([string]$Name) {
    return ($Component -eq "ALL" -or $Component -eq $Name)
}

function Release-File([string]$ReleaseId, [string]$Name) {
    return "$RemoteReleases/$ReleaseId/$Name-$ReleaseId.tar.gz"
}

function Marker-Path([string]$Name) {
    switch ($Name) {
        "API"     { return "$RemoteApi/RELEASE-API.txt" }
        "ANGULAR" { return "$RemoteAngular/RELEASE-ANGULAR.txt" }
        "WEBSITE" { return "$RemoteWebsite/RELEASE-WEBSITE.txt" }
    }
}

function Write-Remote-Marker([string]$Name, [string]$ReleaseId) {
    $path = Marker-Path $Name
    Run-Ssh "printf '%s\n' 'ReleaseId=$ReleaseId' 'Component=$Name' > $path"
}

# ---------------------------
# Status mode
# ---------------------------
if ($PSCmdlet.ParameterSetName -eq "Status") {
    Step "Currently deployed QA releases"
    $cmd = @(
        "echo 'API:'",
        "if [ -f $RemoteApi/RELEASE-API.txt ]; then cat $RemoteApi/RELEASE-API.txt; else echo 'No release marker'; fi",
        "echo",
        "echo 'ANGULAR:'",
        "if [ -f $RemoteAngular/RELEASE-ANGULAR.txt ]; then cat $RemoteAngular/RELEASE-ANGULAR.txt; else echo 'No release marker'; fi",
        "echo",
        "echo 'WEBSITE:'",
        "if [ -f $RemoteWebsite/RELEASE-WEBSITE.txt ]; then cat $RemoteWebsite/RELEASE-WEBSITE.txt; else echo 'No release marker'; fi"
    ) -join "; "
    Run-Ssh $cmd
    exit 0
}

# ---------------------------
# List releases mode
# ---------------------------
if ($PSCmdlet.ParameterSetName -eq "List") {
    Step "Available successful releases"
    $cmd = @"
set -e
mkdir -p $RemoteReleases
printf '%-18s %-8s %-9s %-9s\n' 'RELEASE ID' 'API' 'ANGULAR' 'WEBSITE'
printf '%-18s %-8s %-9s %-9s\n' '------------------' '--------' '---------' '---------'
for d in `$(ls -1dt $RemoteReleases/20* 2>/dev/null || true); do
  r=`$(basename "`$d")
  a='No'; g='No'; w='No'
  [ -f "`$d/API-`$r.tar.gz" ] && a='Yes'
  [ -f "`$d/ANGULAR-`$r.tar.gz" ] && g='Yes'
  [ -f "`$d/WEBSITE-`$r.tar.gz" ] && w='Yes'
  printf '%-18s %-8s %-9s %-9s\n' "`$r" "`$a" "`$g" "`$w"
done
"@
    Run-Ssh $cmd
    exit 0
}

# ---------------------------
# Rollback mode
# ---------------------------
if ($PSCmdlet.ParameterSetName -eq "Rollback") {
    if ($Rollback -notmatch '^\d{8}-\d{6}$') {
        throw "Invalid release id '$Rollback'. Expected format yyyyMMdd-HHmmss, for example 20260901-184501."
    }

    Step "Rollback to release $Rollback ($Component)"

    # Validate requested release snapshot(s) exist before touching live files.
    $checks = @("set -e")
    if (Component-Selected "API") {
        $checks += "test -f $(Release-File $Rollback 'API')"
    }
    if (Component-Selected "ANGULAR") {
        $checks += "test -f $(Release-File $Rollback 'ANGULAR')"
    }
    if (Component-Selected "WEBSITE") {
        $checks += "test -f $(Release-File $Rollback 'WEBSITE')"
    }
    Run-Ssh ($checks -join "; ")

    $SafetyId = "safety-$Stamp"
    Run-Ssh "mkdir -p $RemoteSafety/$SafetyId"

    # Create safety snapshots of currently running content.
    Step "Create rollback safety snapshot $SafetyId"
    $safety = @("set -e")
    if (Component-Selected "API") {
        $safety += "tar -czf $RemoteSafety/$SafetyId/API-current.tar.gz -C $RemoteApi ."
    }
    if (Component-Selected "ANGULAR") {
        $safety += "tar -czf $RemoteSafety/$SafetyId/ANGULAR-current.tar.gz -C $RemoteAngular ."
    }
    if (Component-Selected "WEBSITE") {
        $safety += "tar -czf $RemoteSafety/$SafetyId/WEBSITE-current.tar.gz -C $RemoteWebsite ."
    }
    Run-Ssh ($safety -join "; ")

    # API rollback with automatic safety restore if health check fails.
    if (Component-Selected "API") {
        Step "Rollback API to $Rollback"
        $cmd = @(
            "set -e",
            "systemctl stop $ApiService",
            "rm -rf $RemoteApi/*",
            "tar -xzf $(Release-File $Rollback 'API') -C $RemoteApi",
            "chown -R whatsbiz:whatsbiz $RemoteApi",
            "printf '%s\n' 'ReleaseId=$Rollback' 'Component=API' > $RemoteApi/RELEASE-API.txt",
            "systemctl start $ApiService",
            "sleep 10",
            "systemctl is-active $ApiService >/dev/null",
            "curl -fsS -H 'Host: qa-api.khatadhari.com' http://127.0.0.1:5001/health >/dev/null"
        ) -join "; "

        try {
            Run-Ssh $cmd
        }
        catch {
            Write-Host "API rollback health check failed. Restoring pre-rollback API automatically..."
            $restore = @(
                "systemctl stop $ApiService || true",
                "rm -rf $RemoteApi/*",
                "tar -xzf $RemoteSafety/$SafetyId/API-current.tar.gz -C $RemoteApi",
                "chown -R whatsbiz:whatsbiz $RemoteApi",
                "systemctl start $ApiService",
                "sleep 10",
                "systemctl is-active $ApiService >/dev/null",
                "curl -fsS -H 'Host: qa-api.khatadhari.com' http://127.0.0.1:5001/health >/dev/null"
            ) -join "; "
            Run-Ssh $restore
            throw "Requested API rollback failed health validation and the previous API was restored."
        }
    }

    if (Component-Selected "ANGULAR") {
        Step "Rollback Angular to $Rollback"
        Run-Ssh "set -e; rm -rf $RemoteAngular/*; tar -xzf $(Release-File $Rollback 'ANGULAR') -C $RemoteAngular; printf '%s\n' 'ReleaseId=$Rollback' 'Component=ANGULAR' > $RemoteAngular/RELEASE-ANGULAR.txt"
    }

    if (Component-Selected "WEBSITE") {
        Step "Rollback Website to $Rollback"
        Run-Ssh "set -e; rm -rf $RemoteWebsite/*; tar -xzf $(Release-File $Rollback 'WEBSITE') -C $RemoteWebsite; printf '%s\n' 'ReleaseId=$Rollback' 'Component=WEBSITE' > $RemoteWebsite/RELEASE-WEBSITE.txt"
    }

    Step "Rollback validation"
    Run-Ssh "nginx -t"

    if (Component-Selected "API") {
        Test-Url "$ApiUrl/health"
    }
    if (Component-Selected "ANGULAR") {
        Test-Url $AngularUrl
    }
    if (Component-Selected "WEBSITE") {
        Test-Url $WebsiteUrl
        Test-Url "$WebsiteUrl/retail-erp.html"
    }

    Step "ROLLBACK COMPLETE"
    Write-Host "Selected release: $Rollback"
    Write-Host "Component: $Component"
    Write-Host "Safety snapshot retained at: $RemoteSafety/$SafetyId"
    exit 0
}

# ---------------------------
# Deploy mode preflight
# ---------------------------
Step "0. Preflight checks"

Require-Path $RepoRoot "WhatsBizERP repository"
if (-not $SkipWebsite) { Require-Path $WebsiteRoot "KhataDhari website repository" }
if (-not $SkipApi) { Require-Path $ApiProject "WhatsBiz API project" }
if (-not $SkipAngular) { Require-Path $AngularRoot "Angular application" }
if ($Database) { Require-Path $DatabaseDeploy "Guarded QA database deployment helper" }

Run "dotnet" @("--version")
if (-not $SkipAngular) {
    Run "node" @("--version")
    Run "npm.cmd" @("--version")
}
Run "ssh" @("-V")

if (-not $DryRun) {
    New-Item -ItemType Directory -Force -Path $Work | Out-Null
}

Write-Host "Release ID: $Stamp"
Write-Host "Target VPS: $SshTarget"
Write-Host "Environment: QA"

Run-Ssh "set -e; systemctl is-active nginx >/dev/null; systemctl is-active $ApiService >/dev/null; test -f /etc/whatsbiz/qa.env; test -d /var/lib/whatsbiz-qa/data-protection-keys; mkdir -p $RemoteStage/$Stamp $RemoteReleases/$Stamp $RemoteSafety; echo 'VPS preflight OK'"

# ---------------------------
# Optional tests
# ---------------------------
if ($RunTests) {
    Step "1. Run pre-deployment tests"

    Push-Location $RepoRoot
    try {
        Run "dotnet" @("test", ".\backend\tests\WhatsBiz.Tests\WhatsBiz.Tests.csproj", "-c", "Release", "--no-restore")
    }
    finally {
        Pop-Location
    }

    $WebsiteTests = Get-ChildItem -Path (Join-Path $WebsiteRoot "tests") -Filter "*.test.mjs" -File -ErrorAction SilentlyContinue
    if ($WebsiteTests.Count -gt 0) {
        Push-Location $WebsiteRoot
        try {
            foreach ($test in $WebsiteTests) {
                Run "node" @("--test", $test.FullName)
            }
        }
        finally {
            Pop-Location
        }
    }
    else {
        Write-Host "No website *.test.mjs files found; skipping website tests."
    }
}
else {
    Step "1. Tests skipped"
    Write-Host "Use -RunTests to execute tests before deployment."
}

# ---------------------------
# Optional database deployment
# ---------------------------
if ($Database) {
    Step "2. Deploy guarded QA database"
    $databaseArguments = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$DatabaseDeploy)
    if ($DryRun) { $databaseArguments += '-PlanOnly' }
    if ($ApproveDatabaseTableRebuild) { $databaseArguments += '-ApproveTableRebuild' }
    Run 'powershell.exe' $databaseArguments
}

# ---------------------------
# Build / package
# ---------------------------
if (-not $SkipApi) {
    Step "2. Publish ASP.NET Core QA API"

    if (-not $DryRun -and (Test-Path $ApiPublish)) {
        Remove-Item -Recurse -Force $ApiPublish
    }

    Push-Location $RepoRoot
    try {
        Run "dotnet" @("publish", $ApiProject, "-c", "Release", "-o", $ApiPublish)
    }
    finally {
        Pop-Location
    }

    Require-Path (Join-Path $ApiPublish "WhatsBiz.Api.dll") "Published API"

    $ApiArchive = Join-Path $Work "API-$Stamp.tar.gz"
    Push-Location $ApiPublish
    try {
        Run "tar" @("-czf", $ApiArchive, ".")
    }
    finally {
        Pop-Location
    }
}

if (-not $SkipAngular) {
    Step "3. Build Angular QA"

    Push-Location $AngularRoot
    try {
        Run "npm.cmd" @("run", "build", "--", "--configuration", "qa")
    }
    finally {
        Pop-Location
    }

    Require-Path (Join-Path $AngularDist "index.html") "Angular QA build output"

    $AngularArchive = Join-Path $Work "ANGULAR-$Stamp.tar.gz"
    Push-Location $AngularDist
    try {
        Run "tar" @("-czf", $AngularArchive, ".")
    }
    finally {
        Pop-Location
    }
}

if (-not $SkipWebsite) {
    Step "4. Package KhataDhari public website"

    Require-Path (Join-Path $WebsiteRoot "index.html") "Website index.html"

    $WebsiteArchive = Join-Path $Work "WEBSITE-$Stamp.tar.gz"
    Push-Location $WebsiteRoot
    try {
        Run "tar" @(
            "-czf", $WebsiteArchive,
            "--exclude=.git",
            "--exclude=.gitignore",
            "--exclude=tests",
            "--exclude=node_modules",
            "--exclude=README.md",
            "--exclude=*.test.mjs",
            "."
        )
    }
    finally {
        Pop-Location
    }
}

# ---------------------------
# Upload staged packages
# ---------------------------
Step "5. Upload release packages"

if (-not $SkipApi) {
    Run "scp" @($SshOptions + @($ApiArchive, "$SshTarget`:$RemoteStage/$Stamp/API-$Stamp.tar.gz"))
}

if (-not $SkipAngular) {
    Run "scp" @($SshOptions + @($AngularArchive, "$SshTarget`:$RemoteStage/$Stamp/ANGULAR-$Stamp.tar.gz"))
}

if (-not $SkipWebsite) {
    Run "scp" @($SshOptions + @($WebsiteArchive, "$SshTarget`:$RemoteStage/$Stamp/WEBSITE-$Stamp.tar.gz"))
}

# ---------------------------
# Deploy
# ---------------------------
if (-not $SkipApi) {
    Step "6. Deploy QA API"

    # Pre-deploy current-state safety backup.
    Run-Ssh "tar -czf $RemoteSafety/API-pre-$Stamp.tar.gz -C $RemoteApi ."

    $cmd = @(
        "set -e",
        "systemctl stop $ApiService",
        "rm -rf $RemoteApi/*",
        "tar -xzf $RemoteStage/$Stamp/API-$Stamp.tar.gz -C $RemoteApi",
        "chown -R whatsbiz:whatsbiz $RemoteApi",
        "printf '%s\n' 'ReleaseId=$Stamp' 'Component=API' > $RemoteApi/RELEASE-API.txt",
        "systemctl start $ApiService",
        "sleep 10",
        "systemctl is-active $ApiService >/dev/null",
        "curl -fsS -H 'Host: qa-api.khatadhari.com' http://127.0.0.1:5001/health >/dev/null"
    ) -join "; "

    try {
        Run-Ssh $cmd
    }
    catch {
        Write-Host "API deployment failed. Restoring previous API automatically..."
        $rollback = @(
            "systemctl stop $ApiService || true",
            "rm -rf $RemoteApi/*",
            "tar -xzf $RemoteSafety/API-pre-$Stamp.tar.gz -C $RemoteApi",
            "chown -R whatsbiz:whatsbiz $RemoteApi",
            "systemctl start $ApiService",
            "sleep 10",
            "systemctl is-active $ApiService >/dev/null",
            "curl -fsS -H 'Host: qa-api.khatadhari.com' http://127.0.0.1:5001/health >/dev/null"
        ) -join "; "
        Run-Ssh $rollback
        throw "API deployment failed health validation and the previous API was restored."
    }
}

if (-not $SkipAngular) {
    Step "7. Deploy Angular QA"
    Run-Ssh "set -e; rm -rf $RemoteAngular/*; tar -xzf $RemoteStage/$Stamp/ANGULAR-$Stamp.tar.gz -C $RemoteAngular; printf '%s\n' 'ReleaseId=$Stamp' 'Component=ANGULAR' > $RemoteAngular/RELEASE-ANGULAR.txt"
}

if (-not $SkipWebsite) {
    Step "8. Deploy public KhataDhari website"
    Run-Ssh "set -e; rm -rf $RemoteWebsite/*; tar -xzf $RemoteStage/$Stamp/WEBSITE-$Stamp.tar.gz -C $RemoteWebsite; printf '%s\n' 'ReleaseId=$Stamp' 'Component=WEBSITE' > $RemoteWebsite/RELEASE-WEBSITE.txt"
}

# ---------------------------
# Validate deployed result
# ---------------------------
Step "9. Validate Nginx and public URLs"
Run-Ssh "nginx -t"

if (-not $SkipApi) {
    Test-Url "$ApiUrl/health"
}
if (-not $SkipAngular) {
    Test-Url $AngularUrl
}
if (-not $SkipWebsite) {
    Test-Url $WebsiteUrl
    Test-Url "$WebsiteUrl/retail-erp.html"
}

# ---------------------------
# Store immutable successful release snapshot
# ---------------------------
Step "10. Save successful release snapshot"

$releaseCmd = @("set -e", "mkdir -p $RemoteReleases/$Stamp")
if (-not $SkipApi) {
    $releaseCmd += "cp $RemoteStage/$Stamp/API-$Stamp.tar.gz $RemoteReleases/$Stamp/API-$Stamp.tar.gz"
}
if (-not $SkipAngular) {
    $releaseCmd += "cp $RemoteStage/$Stamp/ANGULAR-$Stamp.tar.gz $RemoteReleases/$Stamp/ANGULAR-$Stamp.tar.gz"
}
if (-not $SkipWebsite) {
    $releaseCmd += "cp $RemoteStage/$Stamp/WEBSITE-$Stamp.tar.gz $RemoteReleases/$Stamp/WEBSITE-$Stamp.tar.gz"
}
$releaseCmd += "printf '%s\n' 'ReleaseId=$Stamp' 'CreatedAt=$Stamp' > $RemoteReleases/$Stamp/RELEASE.txt"
Run-Ssh ($releaseCmd -join "; ")

# ---------------------------
# Retention
# ---------------------------
Step "11. Apply release retention (keep latest $MaxBackups)"

Run-Ssh "set -e; cd $RemoteReleases; ls -1dt 20* 2>/dev/null | tail -n +$($MaxBackups + 1) | xargs -r rm -rf --; echo 'Release retention OK'"

# Keep rollback-safety files bounded too: latest 10 entries.
Run-Ssh "set -e; mkdir -p $RemoteSafety; cd $RemoteSafety; ls -1dt * 2>/dev/null | tail -n +11 | xargs -r rm -rf -- || true"

# ---------------------------
# Cleanup
# ---------------------------
Step "12. Cleanup temporary deployment files"

Run-Ssh "rm -rf $RemoteStage/$Stamp"

if (-not $DryRun -and (Test-Path $Work)) {
    Remove-Item -Recurse -Force $Work
}

# ---------------------------
# Summary
# ---------------------------
Step "DEPLOYMENT COMPLETE"

Write-Host "Release ID: $Stamp"
if (-not $SkipApi) {
    Write-Host "API:      $ApiUrl"
}
if (-not $SkipAngular) {
    Write-Host "Angular:  $AngularUrl"
}
if (-not $SkipWebsite) {
    Write-Host "Website:  $WebsiteUrl"
    Write-Host "Landing:  $WebsiteUrl/retail-erp.html"
}

Write-Host ""
Write-Host "Release snapshot:"
Write-Host "$RemoteReleases/$Stamp"

Write-Host ""
Write-Host "Retention:"
Write-Host "Latest $MaxBackups successful releases."

Write-Host ""
Write-Host "Useful commands:"
Write-Host "  .\deploy-khatadhari-qa.ps1 -Status"
Write-Host "  .\deploy-khatadhari-qa.ps1 -ListReleases"
Write-Host "  .\deploy-khatadhari-qa.ps1 -Rollback $Stamp -Component ALL"
