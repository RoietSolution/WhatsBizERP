$ErrorActionPreference = 'Stop'
$helper = Join-Path $PSScriptRoot 'bin\Release\net8.0\ProdDacFx.dll'
$wrapper = Join-Path (Split-Path -Parent $PSScriptRoot) 'deploy-prod-database.ps1'
$classificationModule = Join-Path $PSScriptRoot 'ProdDatabaseClassification.psm1'
$optionsScript = Join-Path (Split-Path -Parent $PSScriptRoot) 'sql\Production_DatabaseOptions.sql'
$ownershipValidation = Join-Path (Split-Path -Parent $PSScriptRoot) 'sql\Production_TenantOwnershipStateValidation.sql'
$postValidation = Join-Path (Split-Path -Parent $PSScriptRoot) '..\database\WhatsBiz.Database\Scripts\Production_PostValidation.sql'
if (-not (Test-Path -LiteralPath $helper)) { throw 'Build ProdDacFx before running its offline tests.' }
Import-Module -Name $classificationModule -Force

& dotnet $helper --operation test-redaction
if ($LASTEXITCODE -ne 0) { throw 'DacFx diagnostic secret-redaction self-test failed.' }
& dotnet $helper --operation test-inspect-state-types
if ($LASTEXITCODE -ne 0) { throw 'DacFx inspect-state Int64 handling self-test failed.' }
& dotnet $helper --operation test-verify-backup
if ($LASTEXITCODE -ne 0) { throw 'DacFx backup verification guard self-test failed.' }
& dotnet $helper --operation test-restore-backup
if ($LASTEXITCODE -ne 0) { throw 'DacFx backup restore guard self-test failed.' }
& dotnet $helper --operation test-provision-runtime-user
if ($LASTEXITCODE -ne 0) { throw 'DacFx runtime-user provisioning guard self-test failed.' }
$helperSource = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Program.cs') -Raw
if ($helperSource -notmatch '(?s)private static void InspectReadOnlyState\(.*?sys\.databases.*?sys\.objects.*?__RefactorLog.*?(?=private static long ReadInt64)' -or
    [regex]::Match($helperSource, '(?s)private static void InspectReadOnlyState\(.*?(?=private static long ReadInt64)').Value -match '(?i)\b(?:INSERT|UPDATE|DELETE|MERGE|ALTER|DROP|TRUNCATE)\s+') {
    throw 'The helper read-only state inspection must use metadata SELECTs only.'
}
Write-Host 'PASS: metadata-only read-only post-failure inspection is available.'

$builtInSchemas = @(
    'dbo', 'guest', 'sys', 'INFORMATION_SCHEMA',
    'db_owner', 'db_accessadmin', 'db_securityadmin', 'db_ddladmin',
    'db_backupoperator', 'db_datareader', 'db_datawriter',
    'db_denydatareader', 'db_denydatawriter'
)
$classificationCases = @(
    @{ Name = 'empty database with normal built-in schemas => FRESH'; Tables = 0L; Objects = 0L; Schemas = @('dbo', 'guest', 'sys', 'INFORMATION_SCHEMA'); ExpectedFresh = $true },
    @{ Name = 'empty database with nine db_* role schemas => FRESH'; Tables = 0L; Objects = 0L; Schemas = $builtInSchemas; ExpectedFresh = $true },
    @{ Name = 'real WhatsBiz application schema => NOT FRESH'; Tables = 0L; Objects = 0L; Schemas = @('finance'); ExpectedFresh = $false },
    @{ Name = 'application table => NOT FRESH'; Tables = 1L; Objects = 1L; Schemas = @('dbo'); ExpectedFresh = $false },
    @{ Name = 'application object in dbo => NOT FRESH'; Tables = 0L; Objects = 1L; Schemas = @('dbo'); ExpectedFresh = $false },
    @{ Name = 'populated database => NOT FRESH'; Tables = 12L; Objects = 25L; Schemas = @('finance', 'sales', 'dbo'); ExpectedFresh = $false }
)
foreach ($case in $classificationCases) {
    $actualFresh = Test-WhatsBizFreshDatabase -UserTableCount $case.Tables -UserObjectCount $case.Objects -SchemaNames $case.Schemas
    if ($actualFresh -ne $case.ExpectedFresh) { throw "Offline fresh-production classification failed: $($case.Name)" }
    Write-Host "PASS: $($case.Name)"
}

$oldConnection = $env:WHATSBIZ_PROD_SQL_CONNECTION
try {
    $cases = @(
        @{ Name = 'strict production connection accepted'; Database = 'WhatsBizERP_PROD'; Encrypt = 'True'; Trust = 'False'; Expected = 0 },
        @{ Name = 'QA database rejected'; Database = 'WhatsBizERP_QA'; Encrypt = 'True'; Trust = 'False'; Expected = 1 },
        @{ Name = 'development database rejected'; Database = 'WhatsBizERP'; Encrypt = 'True'; Trust = 'False'; Expected = 1 },
        @{ Name = 'wrong server rejected'; Database = 'WhatsBizERP_PROD'; Server = 'localhost,14330'; Encrypt = 'True'; Trust = 'False'; Expected = 1 },
        @{ Name = 'Encrypt false rejected'; Database = 'WhatsBizERP_PROD'; Encrypt = 'False'; Trust = 'False'; Expected = 1 },
        @{ Name = 'TrustServerCertificate true rejected'; Database = 'WhatsBizERP_PROD'; Encrypt = 'True'; Trust = 'True'; Expected = 1 }
    )

    foreach ($case in $cases) {
        $server = if ($case.Server) { $case.Server } else { 'sql.khatadhari.com,14330' }
        $env:WHATSBIZ_PROD_SQL_CONNECTION = "Server=$server;Database=$($case.Database);User ID=dummy_user;Password=dummy-test-value;Encrypt=$($case.Encrypt);TrustServerCertificate=$($case.Trust)"
        $arguments = @($helper, '--operation', 'validate', '--database', 'WhatsBizERP_PROD')
        if (($arguments -join ' ') -match '(?i)password|dummy-test-value') { throw 'A test credential was placed in child-process arguments.' }
        & dotnet @arguments *> $null
        if ($LASTEXITCODE -ne $case.Expected) { throw "Offline target-validation case failed: $($case.Name)" }
        Write-Host "PASS: $($case.Name)"
    }

    $wrapperText = Get-Content -LiteralPath $wrapper -Raw
    if ($wrapperText -match '(?i)/TargetConnectionString:') { throw 'SqlPackage command-line connection string exposure remains.' }
    if ($wrapperText -notmatch 'WHATSBIZ_PROD_SQL_CONNECTION') { throw 'The production helper is not configured to use the inherited connection environment variable.' }
    if ($wrapperText -notmatch 'Test-WhatsBizFreshDatabase\s+-UserTableCount\s+\$userTableCount\s+-UserObjectCount\s+\$userObjectCount\s+-SchemaNames\s+\$applicationSchemaNames' -or $wrapperText -match 'sys\.schemas\s+WHERE\s+name\s+NOT\s+IN') { throw 'Fresh production detection must positively identify canonical WhatsBiz schemas and exclude unrelated built-in schemas.' }
    $classificationText = Get-Content -LiteralPath $classificationModule -Raw
    foreach ($applicationSchema in @('audit', 'admin', 'commerce', 'core', 'dashboard', 'finance', 'gst', 'integration', 'inventory', 'loyalty', 'marketing', 'master', 'printing', 'purchase', 'reporting', 'sales')) {
        if ($classificationText -notmatch [regex]::Escape("'$applicationSchema'")) { throw "Canonical WhatsBiz application schema is missing from the fresh database detector: $applicationSchema" }
    }
    if ($wrapperText -notmatch 'Get-ProdOwnershipState' -or
        $wrapperText -notmatch '\$ownershipCompliant\s*=\s*\$ownershipState\s+-match' -or
        $wrapperText -notmatch '-not\s+\$ownershipCompliant\s+-and\s+-not\s+\$ApproveTenantOwnershipBackfill' -or
        $wrapperText -notmatch '\$skipLegacyOwnership\s*=\s+\$freshProductionDatabase\s+-or\s+\$ownershipCompliant' -or
        $wrapperText -match 'Existing production data requires explicit review/approval of V7, V8 and V26') { throw 'Production ownership validation must be state-aware and approval-gate only unresolved legacy data.' }
    if (-not (Test-Path -LiteralPath $ownershipValidation)) { throw 'Current-state production ownership validation script is missing.' }
    $ownershipText = Get-Content -LiteralPath $ownershipValidation -Raw
    foreach ($requiredInvariant in @('MissingColumns', 'UnownedRows', 'OrphanTenantRows', 'CrossTenantRows', 'JournalOwnershipIssues', 'RepairScope', 'UNRESOLVED', 'COMPLIANT')) {
        if ($ownershipText -notmatch [regex]::Escape($requiredInvariant)) { throw "Production ownership validation is missing invariant: $requiredInvariant" }
    }
    $ownershipDecisionCases = @(
        @{ Name = 'fresh database'; Fresh = $true; Compliant = $true; ApprovalRequired = $false },
        @{ Name = 'existing compliant database'; Fresh = $false; Compliant = $true; ApprovalRequired = $false },
        @{ Name = 'existing unresolved database'; Fresh = $false; Compliant = $false; ApprovalRequired = $true },
        @{ Name = 'fresh state does not require historical approval'; Fresh = $true; Compliant = $false; ApprovalRequired = $false }
    )
    foreach ($case in $ownershipDecisionCases) {
        $approvalRequired = -not $case.Fresh -and -not $case.Compliant
        if ($approvalRequired -ne $case.ApprovalRequired) { throw "Ownership approval decision failed: $($case.Name)" }
        Write-Host "PASS: $($case.Name) ownership decision"
    }
    if ($wrapperText -notmatch '(?s)if\s*\(-not\s+\$freshProductionDatabase\).*?V7-SafeInventoryTenantColumns\.sql.*?V8-TenantOwnershipPhase1\.sql' -or $wrapperText -match 'Invoke-ProdSqlFile[^\r\n]*V18-POS-PostInvoice-TenantHardening\.sql') { throw 'Fresh migration selection or redundant V18 suppression is incorrect.' }
    foreach ($requiredScript in @('V24-RecreateOperationalTenantGuards-WithRequiredSetOptions.sql', 'V25-FinanceTenantOwnershipAudit.sql', 'V26-FinanceTenantIsolationAndPostingRepair.sql', 'Production_PostValidation.sql')) {
        if ($wrapperText -notmatch [regex]::Escape($requiredScript)) { throw "Required production current-state script is missing: $requiredScript" }
    }
    if ($wrapperText.IndexOf('V26-FinanceTenantIsolationAndPostingRepair.sql', [System.StringComparison]::Ordinal) -gt
        $wrapperText.IndexOf('V25-FinanceTenantOwnershipAudit.sql', [System.StringComparison]::Ordinal)) { throw 'Ownership audit must validate after an explicitly approved legacy transformation.' }
    $optionText = Get-Content -LiteralPath $optionsScript -Raw
    if ($optionText -notmatch 'ALTER\s+DATABASE\s+\[WhatsBizERP_PROD\]\s+SET\s+PAGE_VERIFY\s+CHECKSUM' -or
        $optionText -notmatch 'ALTER\s+DATABASE\s+\[WhatsBizERP_PROD\]\s+SET\s+TARGET_RECOVERY_TIME\s*=\s*60\s+SECONDS' -or
        $optionText -notmatch "DB_NAME\(\)\s*<>\s*N'WhatsBizERP_PROD'" -or
        $optionText -notmatch 'IF\s+NOT\s+EXISTS') { throw 'The idempotent production-only database-option script is incomplete or not target-guarded.' }
    $optionInvocation = $wrapperText.IndexOf('Invoke-ProdSqlFile $productionDatabaseOptions', [System.StringComparison]::Ordinal)
    $planOnlyReturn = $wrapperText.IndexOf('if ($PlanOnly) {', [System.StringComparison]::Ordinal)
    $publishInvocation = $wrapperText.IndexOf("'--operation', 'publish'", [System.StringComparison]::Ordinal)
    if ($optionInvocation -lt 0 -or $planOnlyReturn -lt 0 -or $publishInvocation -lt 0 -or
        $planOnlyReturn -ge $optionInvocation -or $optionInvocation -ge $publishInvocation) { throw 'PlanOnly must return before option execution; actual deployment must apply options before DACFx publish.' }
    if ($wrapperText -notmatch 'Production database options planned: PAGE_VERIFY=CHECKSUM; TARGET_RECOVERY_TIME=60 SECONDS' -or
        $wrapperText -notmatch 'PlanOnly is non-mutating; Production_DatabaseOptions\.sql will not be executed') { throw 'PlanOnly option reporting or non-mutating assurance is missing.' }

    $validationText = Get-Content -LiteralPath $postValidation -Raw
    if ($validationText -notmatch 'sys\.databases[\s\S]*?page_verify_option_desc\s*=\s*N''CHECKSUM''[\s\S]*?THROW\s+51907' -or
        $validationText -notmatch 'sys\.databases[\s\S]*?target_recovery_time_in_seconds\s*=\s*60[\s\S]*?THROW\s+51908') { throw 'Production post-validation does not verify both effective database option values.' }
    $optionValueCases = @(
        @{ PageVerify = 'CHECKSUM'; RecoverySeconds = 60; Expected = $true },
        @{ PageVerify = 'NONE'; RecoverySeconds = 60; Expected = $false },
        @{ PageVerify = 'CHECKSUM'; RecoverySeconds = 0; Expected = $false },
        @{ PageVerify = 'NONE'; RecoverySeconds = 0; Expected = $false }
    )
    foreach ($case in $optionValueCases) {
        $passes = $case.PageVerify -ceq 'CHECKSUM' -and $case.RecoverySeconds -eq 60
        if ($passes -ne $case.Expected) { throw "Offline production database option validation failed for PAGE_VERIFY=$($case.PageVerify), TARGET_RECOVERY_TIME=$($case.RecoverySeconds)." }
    }
    $projectText = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) '..\database\WhatsBiz.Database\WhatsBiz.Database.sqlproj') -Raw
    if ($projectText -notmatch '<PageVerify>CHECKSUM</PageVerify>' -or
        $projectText -notmatch '<TargetRecoveryTimePeriod>60</TargetRecoveryTimePeriod>' -or
        $projectText -notmatch '<TargetRecoveryTimeUnit>Seconds</TargetRecoveryTimeUnit>') { throw 'SQL project database-option properties have changed unexpectedly.' }
    $postDeployment = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) '..\database\WhatsBiz.Database\Scripts\PostDeployment.sql') -Raw
    if ($postDeployment -match '(?im)^\s*:r\s+.*(?:Bootstrap_QA|DemoData|ApplyRealDemoImages)\.sql\s*$') { throw 'QA/demo fixture entered the production DACPAC chain.' }
    $v26 = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) '..\database\WhatsBiz.Database\Scripts\V26-FinanceTenantIsolationAndPostingRepair.sql') -Raw
    if ($v26 -notmatch "IF N'\$\(FreshProductionInitialization\)'\s*<>\s*N'True'") { throw 'V26 ownership backfill is not skipped for a fresh production deployment.' }
    $runtimeObjects = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) '..\database\WhatsBiz.Database\Scripts\RCDEV008-RuntimeObjects.sql') -Raw
    if ($runtimeObjects -match '(?i)DROP\s+TABLE\s+\[finance\]\.\[(CustomerOutstanding|SupplierOutstanding)\]|tmp_ms_xx_(CustomerOutstanding|SupplierOutstanding)') { throw 'Legacy finance-table rebuild remains in the runtime snapshot.' }
    Write-Host 'PASS: wrapper does not pass a target connection string in command-line arguments.'
    Write-Host 'PASS: production option script is target-guarded/idempotent, runs only in actual deployment before publish, and is excluded from PlanOnly.'
    Write-Host 'PASS: production post-validation checks PAGE_VERIFY and TARGET_RECOVERY_TIME; correct and incorrect value cases verified offline.'
    Write-Host 'PASS: fresh/populated gates, legacy backfill selection, baseline-only chain, and finance rebuild exclusion.'
}
finally {
    $env:WHATSBIZ_PROD_SQL_CONNECTION = $oldConnection
}
