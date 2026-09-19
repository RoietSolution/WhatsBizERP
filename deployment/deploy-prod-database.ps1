[CmdletBinding()]
param(
    [string] $Configuration = 'Release',
    [switch] $PlanOnly,
    [switch] $ApproveProductionDeployment,
    [switch] $ApproveTenantOwnershipBackfill,
    [switch] $ApproveTableRebuild,
    [string] $BackupPath,
    [string] $SshHost,
    [string] $SshUser,
    [string] $SshKey
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$expectedDatabase = 'WhatsBizERP_PROD'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'database\WhatsBiz.Database\WhatsBiz.Database.sqlproj'
$dacpac = Join-Path $repositoryRoot "database\WhatsBiz.Database\bin\$Configuration\WhatsBiz.Database.dacpac"
$scriptRoot = Join-Path $repositoryRoot 'database\WhatsBiz.Database\Scripts'
$classificationModule = Join-Path $PSScriptRoot 'ProdDacFx\ProdDatabaseClassification.psm1'
$productionDatabaseOptions = Join-Path $PSScriptRoot 'sql\Production_DatabaseOptions.sql'
Import-Module -Name $classificationModule -Force
$dacFxProject = Join-Path $PSScriptRoot 'ProdDacFx\ProdDacFx.csproj'
$dacFxHelper = Join-Path $PSScriptRoot 'ProdDacFx\bin\Release\net8.0\ProdDacFx.dll'
$sqlCmd = (Get-Command sqlcmd -ErrorAction Stop).Source
$ssh = $null
$targetConnection = [Environment]::GetEnvironmentVariable('WHATSBIZ_PROD_SQL_CONNECTION')
$targetBuilder = $null
$oldTargetConnection = $env:WHATSBIZ_PROD_SQL_CONNECTION
$oldSqlCmdPassword = $env:SQLCMDPASSWORD
$sqlArguments = @()
$serviceStopped = $false

function Invoke-ProdSql([string] $Database, [string] $Query, [switch] $Quiet) {
    $arguments = @('-S', $script:targetBuilder.DataSource, '-d', $Database, '-b', '-Q', $Query)
    if ($script:targetBuilder.UserID) { $arguments += @('-U', $script:targetBuilder.UserID) }
    if ($Quiet) { $arguments += @('-h', '-1', '-W') }
    & $script:sqlCmd @arguments
    if ($LASTEXITCODE -ne 0) { throw "SQL operation failed against verified $script:expectedDatabase target." }
}

function Invoke-ProdSqlFile([string] $Path, [switch] $FreshProductionInitialization) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "Required production SQL script is missing: $Path" }
    Invoke-ProdSql $script:expectedDatabase "SET NOCOUNT ON; IF DB_NAME()<>N'$script:expectedDatabase' THROW 51910,N'Wrong production database target.',1; SELECT N'PRODUCTION_TARGET_VERIFIED';" -Quiet | Out-Null
    Write-Host "Applying canonical migration $(Split-Path -Leaf $Path) to $script:expectedDatabase"
    $arguments = @('-S', $script:targetBuilder.DataSource, '-d', $script:expectedDatabase, '-b', '-i', $Path)
    if ($script:targetBuilder.UserID) { $arguments += @('-U', $script:targetBuilder.UserID) }
    if ($FreshProductionInitialization) { $arguments += @('-v', 'FreshProductionInitialization=True') }
    & $script:sqlCmd @arguments
    if ($LASTEXITCODE -ne 0) { throw "Production migration failed: $(Split-Path -Leaf $Path)" }
}

function Invoke-ProdDacFx([string[]] $Arguments, [string] $FailureMessage) {
    # The SQL credential is inherited through the process environment only.
    # Never put a password-bearing connection string in child-process argv.
    $output = & dotnet $script:dacFxHelper @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    foreach ($line in $output) {
        $safe = [string]$line
        if ($safe -match '(?i)(?:password|pwd)\s*=') { $safe = '[REDACTED_DACFX_OUTPUT_CONTAINING_CREDENTIALS]' }
        Write-Host $safe
    }
    if ($exitCode -ne 0) { throw $FailureMessage }
}

function Invoke-ProdSsh([string] $Command) {
    $sshOptions = @('-i', $script:SshKey, '-o', 'IdentitiesOnly=yes', '-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15')
    $result = & $script:ssh @sshOptions "$script:SshUser@$script:SshHost" $Command
    if ($LASTEXITCODE -ne 0) { throw 'Production SSH operation failed.' }
    return ($result -join "`n")
}

try {
    if ($PlanOnly -eq $ApproveProductionDeployment) {
        throw 'Choose exactly one mode: -PlanOnly OR -ApproveProductionDeployment.'
    }
    if ([string]::IsNullOrWhiteSpace($targetConnection)) {
        throw 'Set WHATSBIZ_PROD_SQL_CONNECTION in the deployment process environment; do not put it in this script or a tracked file.'
    }
if (-not (Test-Path -LiteralPath $project)) { throw 'Canonical database project is missing.' }
    if (-not (Test-Path -LiteralPath $dacFxProject)) { throw 'The production DacFx helper project is missing.' }

    $targetBuilder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($targetConnection)
    if ($targetBuilder.InitialCatalog -cne $expectedDatabase) {
        throw "Production deployment target must be exactly $expectedDatabase; no other database is allowed."
    }
    if ($targetBuilder.DataSource -ine 'sql.khatadhari.com,14330') {
        throw 'Production SQL deployments must use the verified SQL TLS hostname through the approved tunnel endpoint.'
    }
    if ($targetBuilder.DataSource -match '(?i)DESKTOP-DQ0868S' -or $targetBuilder.InitialCatalog -in @('WhatsBizERP', 'WhatsBizERP_QA')) {
        throw 'Development and QA SQL targets are forbidden.'
    }
    if (-not $targetBuilder.Encrypt -or $targetBuilder.TrustServerCertificate) {
        throw 'Production SQL connections must use Encrypt=True and TrustServerCertificate=False.'
    }
    # Normalize through the SQL provider builder before handing the environment
    # variable to the DacFx helper; keep the password out of argv and files.
    $env:WHATSBIZ_PROD_SQL_CONNECTION = $targetBuilder.ConnectionString
    if ($targetBuilder.UserID) { $env:SQLCMDPASSWORD = $targetBuilder.Password }

    Write-Host "Verified production SQL target: $($targetBuilder.DataSource) / $expectedDatabase"
    & dotnet build $dacFxProject --configuration Release --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'The production DacFx helper build failed.' }
    if ($PlanOnly) {
        & dotnet build $project --configuration $Configuration --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'Canonical database project build failed.' }
    }
    if (-not (Test-Path -LiteralPath $dacpac)) { throw "Canonical DACPAC not found: $dacpac" }

    $existsText = (Invoke-ProdSql 'master' "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'$expectedDatabase') IS NULL THEN 0 ELSE 1 END;" -Quiet | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or $existsText -notin @('0', '1')) { throw 'Could not safely determine whether the production database exists.' }

    $freshProductionDatabase = $existsText -eq '0'
    $userTableCount = 0L
    $userObjectCount = 0L
    $applicationSchemaCount = 0L
    if ($existsText -eq '1') {
        $applicationSchemaSqlList = Get-WhatsBizApplicationSchemaSqlList
        $stateText = (Invoke-ProdSql $expectedDatabase "SET NOCOUNT ON; SELECT CONVERT(varchar(20),(SELECT COUNT_BIG(*) FROM sys.tables WHERE is_ms_shipped=0))+'|'+CONVERT(varchar(20),(SELECT COUNT_BIG(*) FROM sys.objects WHERE is_ms_shipped=0))+'|'+COALESCE((SELECT STRING_AGG(CONVERT(nvarchar(max),name),N',') FROM sys.schemas WHERE name IN($applicationSchemaSqlList)),N'');" -Quiet | Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or $stateText -notmatch '^(?<tables>\d+)\|(?<objects>\d+)\|(?<schemas>[A-Za-z0-9_,]*)$') { throw 'Could not safely determine the production database initialization state.' }
        $userTableCount = [long]$Matches['tables']
        $userObjectCount = [long]$Matches['objects']
        $applicationSchemaNames = if ([string]::IsNullOrEmpty($Matches['schemas'])) { @() } else { @($Matches['schemas'].Split(',')) }
        $applicationSchemaCount = Get-WhatsBizApplicationSchemaCount -SchemaNames $applicationSchemaNames
        $freshProductionDatabase = Test-WhatsBizFreshDatabase -UserTableCount $userTableCount -UserObjectCount $userObjectCount -SchemaNames $applicationSchemaNames
    }
    Write-Host "Production database initialization state: $(if ($freshProductionDatabase) { 'FRESH EMPTY' } else { 'EXISTING/POPULATED' }) (user tables=$userTableCount; user objects=$userObjectCount; WhatsBiz application schemas=$applicationSchemaCount)"

    # InitialCatalog has already been validated exactly above. Do not assign it
    # back through System.Data.SqlClient.SqlConnectionStringBuilder: the
    # Windows PowerShell/.NET Framework setter uses the unsupported
    # "InitialCatalog" keyword (without the SQL connection-string space).
    $planDirectory = Join-Path ([System.IO.Path]::GetTempPath()) "whatsbiz-prod-db-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))"
    New-Item -ItemType Directory -Path $planDirectory -Force | Out-Null
    $report = Join-Path $planDirectory 'deploy-report.xml'
    $deployScript = Join-Path $planDirectory 'deploy.sql'
    $createDatabase = $existsText -eq '0'
    $helperArguments = @('--operation', 'report', '--dacpac', $dacpac, '--database', $expectedDatabase, '--create-database', ([string]$createDatabase), '--fresh-production', ([string]$freshProductionDatabase), '--output', $report)
    Invoke-ProdDacFx $helperArguments 'Production DACPAC deployment report generation failed.'
    if (-not (Test-Path -LiteralPath $report)) { throw 'Production DACPAC deployment report generation failed.' }
    $helperArguments = @('--operation', 'script', '--dacpac', $dacpac, '--database', $expectedDatabase, '--create-database', ([string]$createDatabase), '--fresh-production', ([string]$freshProductionDatabase), '--output', $deployScript)
    Invoke-ProdDacFx $helperArguments 'Production DACPAC SQL script generation failed.'
    if (-not (Test-Path -LiteralPath $deployScript)) { throw 'Production DACPAC SQL script generation failed.' }

    $reportText = Get-Content -LiteralPath $report -Raw
    $deployText = Get-Content -LiteralPath $deployScript -Raw
    $postDeployment = Get-Content -LiteralPath (Join-Path $scriptRoot 'PostDeployment.sql') -Raw
    $demoEnablement = Get-Content -LiteralPath (Join-Path $scriptRoot 'V2-WC-DEMO-003-DemoTenantEnablement.sql') -Raw
    if ($postDeployment -match '(?im)^\s*:r\s+.*(?:Bootstrap_QA|DemoData|ApplyRealDemoImages)\.sql\s*$' -or
        $deployText -match '(?i)QA Test Supplier|QA demo tenant' -or
        ($postDeployment -match '(?im)^\s*:r\s+.*V2-WC-DEMO-003-DemoTenantEnablement\.sql\s*$' -and
         -not $demoEnablement.Contains("IF N'`$(ProductionDeployment)' = N'True'"))) {
        throw "Production execution chain contains QA/demo fixtures; review $deployScript. No production changes were applied."
    }
    $unsafeDrops = [regex]::Matches($reportText, '(?i)<Operation Name="Drop">(?<body>.*?)</Operation>') | ForEach-Object {
        [regex]::Matches($_.Groups['body'].Value, '<Item Value="(?<name>[^"]+)" Type="(?<type>[^"]+)"')
    } | ForEach-Object { $_ } | Where-Object { $_.Groups['type'].Value -notmatch 'Constraint$' }
    if (@($unsafeDrops).Count -gt 0) {
        throw "DACPAC plan contains object drops; review $report and $deployScript. No production changes were applied."
    }
    $tableRebuilds = [regex]::Matches($reportText, '(?i)<Operation Name="TableRebuild">(?<body>.*?)</Operation>') | ForEach-Object {
        [regex]::Matches($_.Groups['body'].Value, '<Item Value="(?<name>[^"]+)" Type="SqlTable"')
    } | ForEach-Object { $_.Groups['name'].Value }
    Write-Host "DACPAC report: $report"
    Write-Host "DACPAC script: $deployScript"
    Write-Host "Table rebuilds: $(@($tableRebuilds) -join ', ')"
    Write-Host 'Production database options planned: PAGE_VERIFY=CHECKSUM; TARGET_RECOVERY_TIME=60 SECONDS.'
    Write-Host 'Post-DACPAC path: fresh targets skip legacy ownership backfills; existing targets require explicit backfill approval. No QA bootstrap or QA fixtures are included.'
    if (@($tableRebuilds).Count -gt 0 -and -not $ApproveTableRebuild) {
        throw 'The plan contains table rebuilds. Review them and rerun with -ApproveTableRebuild only after explicit approval.'
    }
    if ($PlanOnly) {
        Write-Host 'PlanOnly is non-mutating; Production_DatabaseOptions.sql will not be executed.'
        return
    }
    if ([string]::IsNullOrWhiteSpace($SshHost) -or [string]::IsNullOrWhiteSpace($SshUser) -or
        [string]::IsNullOrWhiteSpace($SshKey) -or -not (Test-Path -LiteralPath $SshKey)) {
        throw 'Applying a production database plan requires an explicitly supplied production SSH host, user, and private-key path; QA SSH settings are never reused.'
    }
    $ssh = (Get-Command ssh -ErrorAction Stop).Source
    $environmentFiles = Invoke-ProdSsh 'systemctl show whatsbiz-prod --property=EnvironmentFiles --value'
    if ($environmentFiles -notlike '*/etc/whatsbiz/prod.env*') {
        throw 'The whatsbiz-prod service must load /etc/whatsbiz/prod.env before a production database change.'
    }
    if (-not $freshProductionDatabase -and -not $ApproveTenantOwnershipBackfill) {
        throw 'Existing production data requires explicit review/approval of V7, V8 and V26 tenant ownership backfills. Rerun with -ApproveTenantOwnershipBackfill only after reviewing the production ownership reports.'
    }

    if ($existsText -eq '1') {
        if ([string]::IsNullOrWhiteSpace($BackupPath)) { throw 'Existing production database requires an explicit SQL Server backup path before deployment.' }
        if ($BackupPath.Contains("'")) { throw 'Backup path may not contain a single quote.' }
        $stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
        Invoke-ProdSql 'master' "BACKUP DATABASE [$expectedDatabase] TO DISK=N'$BackupPath' WITH COPY_ONLY,CHECKSUM,INIT,NAME=N'WhatsBizERP_PROD pre-deployment $stamp'; RESTORE VERIFYONLY FROM DISK=N'$BackupPath' WITH CHECKSUM;"
    }

    Invoke-ProdSsh 'systemctl stop whatsbiz-prod'
    $serviceStopped = $true

    Invoke-ProdSqlFile $productionDatabaseOptions
    $publishArguments = @('--operation', 'publish', '--dacpac', $dacpac, '--database', $expectedDatabase, '--create-database', ([string]$createDatabase), '--fresh-production', ([string]$freshProductionDatabase))
    Invoke-ProdDacFx $publishArguments 'DACPAC publish failed; do not proceed to production application rollout.'

    # These are the canonical post-DACPAC tenant hardening/finance scripts used by
    # the established QA sequence. QA bootstrap/fixtures are deliberately omitted.
    Invoke-ProdSqlFile (Join-Path $scriptRoot 'V7-SafeInventoryTenantColumns.sql') -FreshProductionInitialization:$freshProductionDatabase
    # V8 schema ownership objects are needed on fresh databases too; the
    # migration skips historical ownership UPDATEs when this flag is true.
    Invoke-ProdSqlFile (Join-Path $scriptRoot 'V8-TenantOwnershipPhase1.sql') -FreshProductionInitialization:$freshProductionDatabase
    Invoke-ProdSqlFile (Join-Path $scriptRoot 'V24-RecreateOperationalTenantGuards-WithRequiredSetOptions.sql')
    Invoke-ProdSqlFile (Join-Path $scriptRoot 'V25-FinanceTenantOwnershipAudit.sql')
    Invoke-ProdSqlFile (Join-Path $scriptRoot 'V26-FinanceTenantIsolationAndPostingRepair.sql') -FreshProductionInitialization:$freshProductionDatabase
    Invoke-ProdSqlFile (Join-Path $scriptRoot 'Production_PostValidation.sql')
    Invoke-ProdSsh 'systemctl restart whatsbiz-prod && systemctl is-active --quiet whatsbiz-prod'
    $serviceStopped = $false
    Write-Host 'Production database deployment and canonical schema/tenant-guard validation completed.'
}
finally {
    $env:SQLCMDPASSWORD = $oldSqlCmdPassword
    $env:WHATSBIZ_PROD_SQL_CONNECTION = $oldTargetConnection
    if ($serviceStopped) { Write-Warning 'Production API remains stopped because the database change did not finish and validate safely.' }
}
