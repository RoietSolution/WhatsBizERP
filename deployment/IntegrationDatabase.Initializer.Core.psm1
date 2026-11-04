Set-StrictMode -Version Latest

$script:IntegrationDatabaseConfigPath = Join-Path $PSScriptRoot 'integration-database.config.json'
if (-not (Test-Path -LiteralPath $script:IntegrationDatabaseConfigPath -PathType Leaf)) {
    throw 'Shared integration database configuration is missing.'
}
try { $script:IntegrationDatabaseConfig = Get-Content -LiteralPath $script:IntegrationDatabaseConfigPath -Raw | ConvertFrom-Json -ErrorAction Stop }
catch { throw 'Shared integration database configuration is invalid.' }
$script:ApprovedIntegrationServer = [string]$script:IntegrationDatabaseConfig.server
$script:ApprovedIntegrationDatabase = [string]$script:IntegrationDatabaseConfig.database
$script:ProtectedIntegrationDatabases = @($script:IntegrationDatabaseConfig.protectedDatabases | ForEach-Object { [string]$_ })
if ([string]::IsNullOrWhiteSpace($script:ApprovedIntegrationServer) -or
    [string]::IsNullOrWhiteSpace($script:ApprovedIntegrationDatabase) -or
    $script:ProtectedIntegrationDatabases.Count -eq 0 -or
    $script:ApprovedIntegrationDatabase -in $script:ProtectedIntegrationDatabases) {
    throw 'Shared integration database configuration violates the target safety policy.'
}
if ([string]$script:IntegrationDatabaseConfig.historicalBaselineCommit -notmatch '^[a-f0-9]{40}$' -or
    [string]$script:IntegrationDatabaseConfig.historicalBaselineDacpacSha256 -notmatch '^[A-F0-9]{64}$') {
    throw 'Shared integration database configuration does not pin a valid historical commit and DACPAC SHA-256.'
}
$script:ApprovedIntegrationDatabases = @($script:ApprovedIntegrationDatabase)
$script:DisposableMarkerName = 'WhatsBiz.DisposableIntegrationDatabase'

function Get-IntegrationDatabaseConfig {
    [CmdletBinding()]
    param()
    return [pscustomobject]@{
        Server = $script:ApprovedIntegrationServer
        Database = $script:ApprovedIntegrationDatabase
        ProtectedDatabases = [string[]]$script:ProtectedIntegrationDatabases
        BaselineVersion = [int]$script:IntegrationDatabaseConfig.baselineVersion
        HistoricalBaselineCommit = [string]$script:IntegrationDatabaseConfig.historicalBaselineCommit
        HistoricalBaselineDacpacSha256 = [string]$script:IntegrationDatabaseConfig.historicalBaselineDacpacSha256
    }
}

function New-IntegrationSqlConnectionStringBuilder {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$ConnectionString)

    if ([string]::IsNullOrWhiteSpace($ConnectionString) -or
        $ConnectionString -match '(?i)\$env\s*:[^;\r\n]*=' -or
        $ConnectionString -match '(?i)\bConnectionStrings__IntegrationTests\s*=') {
        throw 'Provide the SQL connection-string value only; PowerShell assignment text is not accepted.'
    }

    try { $builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($ConnectionString) }
    catch { throw 'The integration SQL connection string is malformed; no SQL connection was opened.' }

    if ($builder.DataSource -cne $script:ApprovedIntegrationServer -or
        $builder.InitialCatalog -cnotin $script:ApprovedIntegrationDatabases -or
        [string]::IsNullOrWhiteSpace($builder.InitialCatalog) -or
        $builder.AttachDBFilename -or
        $builder.UserInstance) {
        throw 'Connection-string target rejected; only the explicitly approved disposable integration server/database is allowed.'
    }

    if ($builder.InitialCatalog -in $script:ProtectedIntegrationDatabases) {
        throw 'Refusing protected application database.'
    }

    $builder.PersistSecurityInfo = $false
    return $builder
}

function New-IntegrationMasterConnectionStringBuilder {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$ConnectionString)

    $targetBuilder = New-IntegrationSqlConnectionStringBuilder -ConnectionString $ConnectionString
    $masterBuilder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($targetBuilder.ConnectionString)
    # PowerShell treats property assignment on this IDictionary-like builder as
    # a keyword named "InitialCatalog". Call the actual SqlClient property setter.
    $masterBuilder.set_InitialCatalog('master')
    return $masterBuilder
}

function Assert-IntegrationInitializationConfirmation {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('Status','DryRun','Apply','AuditBaseline','RecoverBaseline')][string]$Mode,
          [Parameter()][AllowNull()][string]$ConfirmTarget)

    $approvedConfirmations = @($script:ApprovedIntegrationDatabases | ForEach-Object { "$script:ApprovedIntegrationServer/$_" })
    if ($Mode -in @('Apply','RecoverBaseline') -and $ConfirmTarget -cnotin $approvedConfirmations) {
        throw "$Mode requires an exact approved disposable target confirmation: $($approvedConfirmations -join ' or ')."
    }
}

function Get-IntegrationDatabaseCreationDecision {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('Status','DryRun','Apply','AuditBaseline','RecoverBaseline')][string]$Mode,
          [Parameter(Mandatory)][bool]$DatabaseExists,
          [Parameter(Mandatory)][bool]$CreateIfAbsent,
          [Parameter(Mandatory)][bool]$HasCreatePermission)
    if ($DatabaseExists) { return 'USE_EXISTING' }
    if ($Mode -ne 'Apply' -or -not $CreateIfAbsent) { return 'ABSENT_READ_ONLY' }
    if (-not $HasCreatePermission) { throw 'SQL principal lacks CREATE ANY DATABASE permission.' }
    return 'CREATE_NEW'
}

function Get-IntegrationProvisioningDiagnostic {
    [CmdletBinding()]
    param([Parameter(Mandatory)][bool]$DatabaseExists,
          [Parameter(Mandatory)][bool]$HasCreatePermission,
          [Parameter(Mandatory)][string]$Principal)
    if ($DatabaseExists) {
        return 'Target database exists; CREATE ANY DATABASE is not required. The initializer separately checks database-level CONTROL for schema migrations.'
    }
    if ($HasCreatePermission) {
        return 'Target database is absent; creation remains gated by explicit Apply and exact target confirmation.'
    }
    return "Target database is absent; SQL principal '$Principal' lacks CREATE ANY DATABASE. An authorized administrator must provision only the configured database once. Ongoing schema migration separately requires database-level CONTROL."
}

function Assert-IntegrationTargetClassified {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('Status','DryRun','Apply','AuditBaseline','RecoverBaseline')][string]$Mode,
          [Parameter(Mandatory)][int]$UserObjectCount,
          [Parameter(Mandatory)][bool]$MarkerPresent)

    if ($Mode -eq 'Apply' -and $UserObjectCount -gt 0 -and -not $MarkerPresent) {
        throw 'Apply refuses to adopt a pre-existing unmarked schema. Initialize only an empty database or a database already designated by this initializer.'
    }
}

function Convert-IntegrationSqlIntegerToInt32 {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowNull()][object]$Value,
          [Parameter(Mandatory)][string]$SqlType,
          [Parameter(Mandatory)][string]$ColumnName)

    if ($null -eq $Value -or $Value -is [DBNull]) {
        throw "SQL integer column '$ColumnName' unexpectedly returned NULL."
    }

    switch -Regex ($SqlType.ToLowerInvariant()) {
        '^int$' {
            if ($Value -isnot [int]) { throw "SQL column '$ColumnName' reports INT but returned CLR type '$($Value.GetType().FullName)' ." }
            return [int]$Value
        }
        '^bigint$' {
            if ($Value -isnot [long]) { throw "SQL column '$ColumnName' reports BIGINT but returned CLR type '$($Value.GetType().FullName)' ." }
            if ($Value -lt [int]::MinValue -or $Value -gt [int]::MaxValue) { throw "SQL column '$ColumnName' exceeds the supported Int32 range." }
            return [int]$Value
        }
        '^smallint$' {
            if ($Value -isnot [System.Int16]) { throw "SQL column '$ColumnName' reports SMALLINT but returned CLR type '$($Value.GetType().FullName)' ." }
            return [int]$Value
        }
        '^tinyint$' {
            if ($Value -isnot [byte]) { throw "SQL column '$ColumnName' reports TINYINT but returned CLR type '$($Value.GetType().FullName)' ." }
            return [int]$Value
        }
        default { throw "SQL integer column '$ColumnName' has unsupported SQL type '$SqlType'." }
    }
}

function ConvertTo-IntegrationMigrationHistoryCollection {
    [CmdletBinding()]
    param([Parameter(Mandatory)][bool]$HistoryTablePresent,
          [Parameter()][AllowEmptyCollection()][AllowNull()][object[]]$Records = @())

    if (-not $HistoryTablePresent) {
        throw 'Migration history table is missing; the schema is not initialized.'
    }

    $validated = [System.Collections.Generic.List[object]]::new()
    foreach ($record in $Records) {
        if ($null -eq $record -or $record -is [string] -or $record -is [System.Array]) {
            throw 'Migration history query returned an unexpected record object.'
        }
        $required = @('MigrationVersion','MigrationName','ScriptHash','ValidatorHash')
        foreach ($name in $required) {
            if ($null -eq $record.PSObject.Properties[$name]) {
                throw "Migration history record is missing required field '$name'."
            }
        }
        if ($record.MigrationVersion -isnot [int] -or $record.MigrationVersion -le 0) {
            throw 'Migration history record has an invalid MigrationVersion.'
        }
        if ($record.MigrationName -isnot [string] -or [string]::IsNullOrWhiteSpace($record.MigrationName)) {
            throw "Migration history V$($record.MigrationVersion) has an invalid MigrationName."
        }
        foreach ($hashField in @('ScriptHash','ValidatorHash')) {
            $hash = $record.$hashField
            if ($hash -isnot [string] -or $hash -notmatch '^[A-Fa-f0-9]{64}$') {
                throw "Migration history V$($record.MigrationVersion) has an invalid $hashField."
            }
        }
        $validated.Add($record)
    }

    return ,([object[]]$validated.ToArray())
}

function ConvertTo-IntegrationVersionScalar {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowNull()][object]$Value,
          [Parameter(Mandatory)][string]$Name,
          [Parameter(Mandatory)][int]$Minimum,
          [Parameter(Mandatory)][int]$Maximum)

    if ($null -eq $Value -or $Value -is [System.Array] -or $Value -isnot [int]) {
        throw "$Name must be a scalar Int32 value."
    }
    if ($Value -lt $Minimum -or $Value -gt $Maximum) {
        throw "$Name is outside the supported version range."
    }
    return [int]$Value
}

function ConvertTo-IntegrationMigrationCatalogCollection {
    [CmdletBinding()]
    param([Parameter()][AllowEmptyCollection()][AllowNull()][object[]]$Migrations = @())

    $validated = [System.Collections.Generic.List[object]]::new()
    foreach ($migration in $Migrations) {
        if ($null -eq $migration -or $migration -is [System.Array]) {
            throw 'Migration catalog contains a null or nested array entry.'
        }
        foreach ($field in @('Version','Name','FileName','Path','Hash','ValidatorPath','ValidatorHash')) {
            if ($null -eq $migration.PSObject.Properties[$field]) {
                throw "Migration catalog entry is missing required field '$field'."
            }
        }
        if ($migration.Version -isnot [int] -or $migration.Version -lt 1) {
            throw 'Migration catalog entry has an invalid scalar Int32 Version.'
        }
        foreach ($field in @('Name','FileName','Path','ValidatorPath')) {
            if ($migration.$field -isnot [string] -or [string]::IsNullOrWhiteSpace($migration.$field)) {
                throw "Migration catalog V$($migration.Version) has an invalid $field."
            }
        }
        foreach ($field in @('Hash','ValidatorHash')) {
            if ($migration.$field -isnot [string] -or $migration.$field -notmatch '^[A-Fa-f0-9]{64}$') {
                throw "Migration catalog V$($migration.Version) has an invalid $field."
            }
        }
        $validated.Add($migration)
    }
    return ,([object[]]$validated.ToArray())
}

function Get-IntegrationStatusLines {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('Status','DryRun')][string]$Mode,
          [Parameter(Mandatory)][string]$BaselineLabel,
          [Parameter(Mandatory)][string]$MarkerLabel,
          [Parameter(Mandatory)][string]$SchemaState,
          [Parameter(Mandatory)][string]$TenantDataSummary,
          [Parameter(Mandatory)][int]$ThroughVersion,
          [Parameter(Mandatory)][AllowEmptyCollection()][object[]]$Plan)

    foreach ($entry in $Plan) {
        if ($null -eq $entry -or $entry -is [System.Array] -or $entry.Version -isnot [int] -or $entry.Status -notin @('APPLIED','PENDING')) {
            throw 'Migration plan contains a malformed or nested entry.'
        }
    }
    $pending = @($Plan | Where-Object Status -eq 'PENDING' | Sort-Object Version)
    $applied = @($Plan | Where-Object Status -eq 'APPLIED' | Sort-Object Version)
    $lines = [System.Collections.Generic.List[string]]::new()
    $lines.Add("Mode: $Mode")
    $lines.Add('V40 baseline: ' + $BaselineLabel)
    $lines.Add('Disposable marker: ' + $MarkerLabel)
    $lines.Add('Schema state: ' + $SchemaState)
    $lines.Add('Tenant data: ' + $TenantDataSummary)
    $lines.Add("Canonical migrations V41 to V$ThroughVersion applied: " + $applied.Count)
    if ($applied.Count -gt 0) { $lines.Add('Applied versions: ' + (($applied | ForEach-Object { $_.Version }) -join ', ')) }
    $lines.Add("Canonical migrations V41 to V$ThroughVersion pending: " + $pending.Count)
    if ($pending.Count -gt 0) { $lines.Add('Pending versions: ' + (($pending | ForEach-Object { $_.Version }) -join ', ')) }
    return ,([string[]]$lines.ToArray())
}

function Get-IntegrationModeDisposition {
    [CmdletBinding()]
    param([Parameter(Mandatory)][ValidateSet('Status','DryRun','Apply','AuditBaseline','RecoverBaseline')][string]$Mode)

    if ($Mode -in @('Status','DryRun')) { return 'ReadOnly' }
    if ($Mode -eq 'AuditBaseline') { return 'ReadOnlyAudit' }
    if ($Mode -eq 'RecoverBaseline') { return 'Recovery' }
    return 'Apply'
}

function Assert-IntegrationConnectionUsable {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$State,
          [Parameter(Mandatory)][string]$Operation)
    if ($State -cne 'Open') { throw "SQL operation '$Operation' refused because the current connection state is '$State'; no automatic retry was attempted." }
}

function Get-IntegrationSchemaStateLabel {
    [CmdletBinding()]
    param([Parameter(Mandatory)][int]$UserObjectCount,
          [Parameter(Mandatory)][bool]$HasBaseline,
          [Parameter(Mandatory)][bool]$HasHistory,
          [Parameter(Mandatory)][bool]$MarkerPresent,
          [Parameter(Mandatory)][bool]$CanonicalBaselineFootprint,
          [Parameter(Mandatory)][bool]$BaselineValidated)

    if ($UserObjectCount -eq 0 -and -not $HasBaseline -and -not $HasHistory -and -not $MarkerPresent) { return 'EMPTY_DATABASE' }
    if ($HasBaseline -xor $HasHistory) { return 'PARTIAL_MIGRATION_LEDGER' }
    if ($MarkerPresent -and -not $HasBaseline) { return 'PARTIAL_V40_INITIALIZATION' }
    if (-not $MarkerPresent -and -not $HasBaseline -and -not $HasHistory) {
        if ($CanonicalBaselineFootprint -and $BaselineValidated) { return 'V40_SCHEMA_PUBLISHED_LEDGER_MISSING' }
        if ($CanonicalBaselineFootprint) { return 'PARTIAL_V40_BASELINE' }
        return 'UNEXPECTED_UNMARKED_SCHEMA'
    }
    if (-not $MarkerPresent) { return 'UNMARKED_SCHEMA_REQUIRES_REVIEW' }
    if ($HasBaseline -and $HasHistory -and $BaselineValidated) { return 'VALIDATED_V40_BASELINE' }
    return 'PARTIAL_OR_INCONSISTENT_SCHEMA'
}

function Get-IntegrationTenantDataClassification {
    [CmdletBinding()]
    param([Parameter(Mandatory)][bool]$TenantObjectPresent,
          [Parameter(Mandatory)][bool]$TenantKeyColumnPresent,
          [Parameter()][AllowNull()][object]$TenantCount,
          [Parameter()][AllowNull()][object]$UnknownTenantKeyCount,
          [Parameter()][AllowNull()][object]$NonTestTenantCount,
          [Parameter()][AllowNull()][object]$CanonicalDefaultSeedCount = 0)

    if (-not $TenantObjectPresent) {
        return [pscustomobject]@{ State='TENANT_TABLE_ABSENT'; TenantCount=0; UnknownKeyCount=0; NonTestKeyCount=0; TestKeyCount=0; CanonicalDefaultSeedCount=0; NonSeedNonTestCount=0 }
    }
    if (-not $TenantKeyColumnPresent) {
        return [pscustomobject]@{ State='TENANT_SCHEMA_INCOMPLETE'; TenantCount=$null; UnknownKeyCount=$null; NonTestKeyCount=$null; TestKeyCount=$null; CanonicalDefaultSeedCount=$null; NonSeedNonTestCount=$null }
    }

    $counts = @{}
    foreach ($item in @(
        @{ Name='TenantCount'; Value=$TenantCount },
        @{ Name='UnknownTenantKeyCount'; Value=$UnknownTenantKeyCount },
        @{ Name='NonTestTenantCount'; Value=$NonTestTenantCount },
        @{ Name='CanonicalDefaultSeedCount'; Value=$CanonicalDefaultSeedCount }
    )) {
        if ($null -eq $item.Value -or $item.Value -isnot [int] -or $item.Value -lt 0) {
            throw "Tenant classification aggregate '$($item.Name)' must be a non-negative SQL INT scalar."
        }
        $counts[$item.Name] = [int]$item.Value
    }

    $total = $counts.TenantCount
    $unknown = $counts.UnknownTenantKeyCount
    $nonTest = $counts.NonTestTenantCount
    $seed = $counts.CanonicalDefaultSeedCount
    if ($unknown -gt $total -or $nonTest -gt ($total - $unknown) -or $seed -gt $nonTest) {
        throw 'Tenant classification aggregates are inconsistent; refusing to classify the database.'
    }
    $testCount = $total - $unknown - $nonTest
    $nonSeedNonTest = $nonTest - $seed
    $state = if ($total -eq 0) { 'NO_TENANT_ROWS' }
        elseif ($unknown -gt 0) { 'UNKNOWN_TENANT_KEYS' }
        elseif ($nonSeedNonTest -gt 0) { 'NON_DISPOSABLE_TENANT_DATA' }
        elseif ($seed -eq 1 -and $total -eq 1) { 'CANONICAL_DEFAULT_SEED_ONLY' }
        elseif ($seed -eq 1) { 'CANONICAL_SEED_AND_TEST_TENANTS' }
        else { 'TEST_ONLY_TENANT_ROWS' }

    return [pscustomobject]@{ State=$state; TenantCount=$total; UnknownKeyCount=$unknown; NonTestKeyCount=$nonTest; TestKeyCount=$testCount; CanonicalDefaultSeedCount=$seed; NonSeedNonTestCount=$nonSeedNonTest }
}

function Get-IntegrationTenantClassificationSql {
    [CmdletBinding()]
    param()

    return @"
SELECT
 COUNT(*) AS TenantCount,
 COALESCE(SUM(CASE WHEN T.TenantKey IS NULL OR NULLIF(LTRIM(RTRIM(T.TenantKey)),N'') IS NULL THEN 1 ELSE 0 END),0) AS UnknownTenantKeyCount,
 COALESCE(SUM(CASE WHEN T.TenantKey IS NOT NULL AND NULLIF(LTRIM(RTRIM(T.TenantKey)),N'') IS NOT NULL
           AND T.TenantKey NOT LIKE N'%TEST%' AND T.TenantKey NOT LIKE N'%INTEGRATION%' THEN 1 ELSE 0 END),0) AS NonTestTenantCount,
 COALESCE(SUM(CASE WHEN T.TenantId='11111111-1111-1111-1111-111111111111'
           AND T.TenantKey=N'DEFAULT' AND T.Name=N'Default V1 Tenant' AND T.IsActive=1 THEN 1 ELSE 0 END),0) AS CanonicalDefaultSeedCount
FROM core.Tenants AS T;
"@
}

function Get-IntegrationTenantScopedInventorySql {
    [CmdletBinding()]
    param()
    return @"
DECLARE @DefaultTenantId uniqueidentifier='11111111-1111-1111-1111-111111111111';
DECLARE @DefaultPlanId uniqueidentifier='22222222-2222-2222-2222-222222222222';
DECLARE @Fragments nvarchar(max);
SELECT @Fragments=STRING_AGG(CONVERT(nvarchar(max),
 N'SELECT N''' + REPLACE(s.name+N'.'+t.name,N'''',N'''''') + N''' AS TableName, COUNT_BIG(*) AS TotalRows,'+
 N' SUM(CASE WHEN R.TenantId IS NULL THEN CONVERT(bigint,1) ELSE CONVERT(bigint,0) END) AS UnownedRows,'+
 N' SUM(CASE WHEN R.TenantId=@DefaultTenantId THEN CONVERT(bigint,1) ELSE CONVERT(bigint,0) END) AS CanonicalTenantRows,'+
 N' SUM(R.ExpectedSeedRow) AS ExpectedSeedRows,'+
 N' SUM(CASE WHEN R.TenantId IS NOT NULL AND R.TenantId<>@DefaultTenantId THEN CONVERT(bigint,1) ELSE CONVERT(bigint,0) END) AS OtherTenantRows'+
 N' FROM (SELECT T.TenantId,CASE WHEN '+CASE
   WHEN s.name=N'core' AND t.name=N'Subscriptions' THEN N'T.TenantId=@DefaultTenantId AND T.PlanId=@DefaultPlanId'
   WHEN s.name=N'core' AND t.name=N'TenantFeatures' THEN N'T.TenantId=@DefaultTenantId AND T.IsActive=1 AND ((T.CreatedBy=N''V12 feature hierarchy'' AND T.Reason=N''Initialized from active subscription plan'' AND EXISTS(SELECT 1 FROM core.Features F OUTER APPLY(SELECT TOP(1) S.PlanId FROM core.Subscriptions S WHERE S.TenantId=T.TenantId AND S.IsActive=1 AND S.StartDate<=SYSUTCDATETIME() AND (S.EndDate IS NULL OR S.EndDate>=SYSUTCDATETIME()) ORDER BY S.StartDate DESC) S LEFT JOIN core.PlanFeatures PF ON PF.PlanId=S.PlanId AND PF.FeatureId=F.FeatureId WHERE F.FeatureId=T.FeatureId AND F.IsActive=1 AND T.IsEnabled=ISNULL(PF.IsEnabled,0))) OR (T.CreatedBy=N''V15 referral rewards'' AND T.Reason=N''Opt-in customer referral program'' AND T.IsEnabled=0 AND EXISTS(SELECT 1 FROM core.Features F WHERE F.FeatureId=T.FeatureId AND F.FeatureKey=N''CUSTOMER_REFERRAL_REWARDS'')) OR (T.CreatedBy=N''V17 delivery management'' AND T.Reason=N''Initialized as core delivery capability'' AND T.IsEnabled=1 AND EXISTS(SELECT 1 FROM core.Features F WHERE F.FeatureId=T.FeatureId AND F.FeatureKey=N''DELIVERY_MANAGEMENT'')))'
   WHEN s.name=N'commerce' AND t.name=N'TenantDeliverySettings' THEN N'T.TenantId=@DefaultTenantId AND T.DeliveryEnabled=1 AND T.AutomaticAssignmentEnabled=1 AND T.DefaultDeliveryAgentId IS NULL AND T.RequireDeliveryOtp=1 AND T.DeliveryOtpExpiryMinutes=10 AND T.MaxOtpAttempts=5 AND T.OtpResendCooldownSeconds=60 AND T.MaxOtpResends=5 AND T.NotifyOnAssigned=0 AND T.NotifyOnOutForDelivery=1 AND T.NotifyOnDelivered=1 AND T.NotifyOnDeliveryFailed=1 AND T.RequireCodConfirmation=1 AND T.AssignedTemplateName IS NULL AND T.OutForDeliveryTemplateName IS NULL AND T.OtpTemplateName IS NULL AND T.DeliveredTemplateName IS NULL AND T.FailedTemplateName IS NULL AND T.RescheduledTemplateName IS NULL AND T.UpdatedBy IS NULL'
   WHEN s.name=N'admin' AND t.name=N'Companies' THEN N'T.TenantId=@DefaultTenantId AND T.CompanyCode=N''RT-111111111111111111111111111'' AND T.CompanyName=N''Default V1 Tenant'' AND T.LegalName=N''Default V1 Tenant'' AND T.Country=N''India'' AND T.IsActive=1'
   ELSE N'1=0' END + N' THEN CONVERT(bigint,1) ELSE CONVERT(bigint,0) END AS ExpectedSeedRow'+
 N' FROM '+QUOTENAME(s.name)+N'.'+QUOTENAME(t.name)+N' AS T) AS R HAVING COUNT_BIG(*)>0'),N' UNION ALL ')
FROM sys.tables t
JOIN sys.schemas s ON s.schema_id=t.schema_id
JOIN sys.columns c ON c.object_id=t.object_id AND c.name=N'TenantId'
WHERE t.is_ms_shipped=0 AND NOT (s.name=N'core' AND t.name=N'Tenants');
IF @Fragments IS NULL SET @Fragments=N'SELECT CAST(NULL AS nvarchar(517)) TableName,CONVERT(bigint,0) TotalRows,CONVERT(bigint,0) UnownedRows,CONVERT(bigint,0) CanonicalTenantRows,CONVERT(bigint,0) ExpectedSeedRows,CONVERT(bigint,0) OtherTenantRows WHERE 1=0';
DECLARE @Sql nvarchar(max)=N'SELECT TableName,TotalRows,UnownedRows,CanonicalTenantRows,ExpectedSeedRows,OtherTenantRows FROM ('+@Fragments+N') AS Inventory FOR JSON PATH;';
EXEC sys.sp_executesql @Sql,N'@DefaultTenantId uniqueidentifier,@DefaultPlanId uniqueidentifier',@DefaultTenantId,@DefaultPlanId;
"@
}

function ConvertFrom-IntegrationTenantScopedInventoryJson {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Json)

    if ([string]::IsNullOrWhiteSpace($Json)) {
        throw 'Tenant-scoped aggregate inventory returned an empty SQL result.'
    }

    try { $parsed = ConvertFrom-Json -InputObject $Json -ErrorAction Stop }
    catch { throw 'Tenant-scoped aggregate inventory returned malformed JSON; audit is inconclusive.' }

    if ($parsed -isnot [System.Array]) {
        throw 'Tenant-scoped aggregate inventory returned an unexpected SQL result shape; expected a JSON array.'
    }

    $rows = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in $parsed) {
        if ($null -eq $entry -or $entry -is [System.Array]) {
            throw 'Tenant inventory contains a null or nested row.'
        }
        foreach ($field in @('TableName','TotalRows','UnownedRows','CanonicalTenantRows','ExpectedSeedRows','OtherTenantRows')) {
            if ($null -eq $entry.PSObject.Properties[$field]) {
                throw "Tenant inventory row is missing '$field'."
            }
        }
        $rows.Add($entry)
    }

    return ,([object[]]$rows.ToArray())
}

function Test-IntegrationTenantScopedInventory {
    [CmdletBinding()]
    param([Parameter()][AllowNull()][AllowEmptyCollection()][object[]]$Inventory = @())

    $expected = @{ 'core.Subscriptions'=$true; 'core.TenantFeatures'=$true; 'commerce.TenantDeliverySettings'=$true; 'admin.Companies'=$true }
    $observed = @{}
    foreach ($entry in $Inventory) {
        if ($null -eq $entry -or $entry -is [System.Array]) { throw 'Tenant inventory contains a null or nested row.' }
        foreach ($field in @('TableName','TotalRows','UnownedRows','CanonicalTenantRows','ExpectedSeedRows','OtherTenantRows')) {
            if ($null -eq $entry.PSObject.Properties[$field]) { throw "Tenant inventory row is missing '$field'." }
        }
        if ($entry.TableName -isnot [string] -or [string]::IsNullOrWhiteSpace($entry.TableName) -or $observed.ContainsKey($entry.TableName)) { throw 'Tenant inventory contains an invalid or duplicate table name.' }
        foreach ($field in @('TotalRows','UnownedRows','CanonicalTenantRows','ExpectedSeedRows','OtherTenantRows')) {
            $value = $entry.$field
            if ($value -isnot [long] -and $value -isnot [int]) { throw "Tenant inventory '$($entry.TableName)' has an invalid $field aggregate." }
            if ($value -lt 0) { throw "Tenant inventory '$($entry.TableName)' has a negative $field aggregate." }
        }
        $observed[$entry.TableName] = $entry
    }

    $blockers = [System.Collections.Generic.List[string]]::new()
    foreach ($table in $expected.Keys) {
        $entry = $observed[$table]
        if ($null -eq $entry) { $blockers.Add("Expected baseline seed row missing from $table."); continue }
        if ($entry.TotalRows -le 0 -or $entry.UnownedRows -ne 0 -or $entry.CanonicalTenantRows -ne $entry.TotalRows -or $entry.ExpectedSeedRows -ne $entry.TotalRows -or $entry.OtherTenantRows -ne 0) {
            $blockers.Add("Unexpected tenant-scoped records in $table (aggregate counts differ from V40 seed expectation).")
        }
    }
    foreach ($table in $observed.Keys) {
        if (-not $expected.ContainsKey($table)) { $blockers.Add("Unexpected tenant-scoped data in $table.") }
    }
    return [string[]]$blockers.ToArray()
}

function Get-IntegrationBaselineAuditBlockers {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$SchemaState,
          [Parameter(Mandatory)][bool]$BaselineValidatorPassed,
          [Parameter(Mandatory)][ValidateSet('ABSENT','CONSISTENT','MISMATCHED','PARTIAL')][string]$BaselineLedgerState,
          [Parameter(Mandatory)][bool]$MarkerPresent,
          [Parameter(Mandatory)][ValidateSet('ABSENT','EMPTY','POPULATED','PARTIAL')][string]$MigrationLedgerState,
          [Parameter(Mandatory)][int]$AppliedMigrationCount,
          [Parameter()][AllowEmptyCollection()][int[]]$UntrackedMigrationVersions = @(),
          [Parameter(Mandatory)][int]$DacpacOperationCount,
          [Parameter(Mandatory)][string]$TenantState,
          [Parameter(Mandatory)][int]$TenantCount,
          [Parameter(Mandatory)][int]$CanonicalSeedCount,
          [Parameter(Mandatory)][int]$UnknownTenantKeyCount,
          [Parameter(Mandatory)][bool]$NonTenantSeedDataVerified,
          [Parameter()][AllowEmptyCollection()][string[]]$InventoryBlockers = @())

    $blockers = [System.Collections.Generic.List[string]]::new()
    if ($SchemaState -cne 'V40_SCHEMA_PUBLISHED_LEDGER_MISSING') { $blockers.Add('Schema is not the validated, unledgered V40 state.') }
    if (-not $BaselineValidatorPassed) { $blockers.Add('Canonical V40 validator did not pass.') }
    if ($BaselineLedgerState -cne 'ABSENT') { $blockers.Add("V40 baseline ledger state is $BaselineLedgerState, not absent.") }
    if ($MigrationLedgerState -cne 'ABSENT') { $blockers.Add("Migration ledger state is $MigrationLedgerState, not absent.") }
    if ($MarkerPresent) { $blockers.Add('Disposable marker already exists and requires reconciliation review.') }
    if ($AppliedMigrationCount -ne 0) { $blockers.Add('Migration history contains applied versions.') }
    $laterArtifacts = @($UntrackedMigrationVersions | Where-Object { $_ -ne 62 })
    if ($laterArtifacts.Count -gt 0) { $blockers.Add('Unledgered later migration schema detected: V' + (($laterArtifacts | Sort-Object -Unique) -join ', V') + '.') }
    if ($DacpacOperationCount -ne 0) { $blockers.Add('Canonical V40 DACPAC reports schema operations; exact baseline schema is not established.') }
    if ($TenantState -cne 'CANONICAL_DEFAULT_SEED_ONLY' -or $TenantCount -ne 1 -or $CanonicalSeedCount -ne 1 -or $UnknownTenantKeyCount -ne 0) { $blockers.Add('Tenant rows do not consist solely of the canonical DEFAULT seed.') }
    if (-not $NonTenantSeedDataVerified) { $blockers.Add('Global/non-tenant V40 seed and business data have not been compared with a complete canonical seed manifest.') }
    foreach ($inventoryBlocker in $InventoryBlockers) { $blockers.Add($inventoryBlocker) }
    return [string[]]$blockers.ToArray()
}

function Format-IntegrationSqlFailure {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Operation,
          [Parameter(Mandatory)][System.Exception]$Exception)

    $exception = $Exception
    while ($exception.InnerException) { $exception = $exception.InnerException }
    if ($exception -is [System.Data.SqlClient.SqlException]) {
        return "$Operation failed (SQL error $($exception.Number), state $($exception.State), class $($exception.Class)): $($exception.Message) No automatic retry was attempted."
    }
    return "$Operation failed ($($exception.GetType().Name)): $($exception.Message) No automatic retry was attempted."
}

function Split-IntegrationSqlBatches {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$Text)
    if ($Text -match '(?im)^\s*:\s*(r|setvar)\b') {
        throw 'Managed SQL contains unsupported SQLCMD directives.'
    }
    return @([regex]::Split($Text, '(?im)^\s*GO\s*(?:--.*)?$') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function ConvertTo-IntegrationValidatorText {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Text,
          [Parameter(Mandatory)][string]$FileName,
          [Parameter()][string]$DatabaseName=$script:ApprovedIntegrationDatabase)

    if ($DatabaseName -cne $script:ApprovedIntegrationDatabase) {
        throw "Validator '$FileName' target is not the centrally approved integration database."
    }

    # Earlier validators intentionally allow only QA/PROD. Rewrite only that exact
    # guard for this dedicated test run, preserving its THROW and requiring the
    # approved disposable database at runtime.
    $guard = "(?i)IF\s+DB_NAME\(\)\s+NOT\s+IN\s*\(\s*N'WhatsBizERP_QA'\s*,\s*N'WhatsBizERP_PROD'\s*\)"
    $updated = [regex]::Replace($Text, $guard, "IF DB_NAME() <> N'$DatabaseName'")
    if ($updated -match '(?i)IF\s+DB_NAME\(\)\s+NOT\s+IN\s*\(') { throw "Validator '$FileName' retains an unsupported database allowlist guard." }
    return $updated
}

function Assert-IntegrationDacpacReportSafe {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$ReportPath)
    if (-not (Test-Path -LiteralPath $ReportPath -PathType Leaf)) { throw 'DACPAC deployment report was not created.' }
    try { [xml]$report = Get-Content -LiteralPath $ReportPath -Raw }
    catch { throw 'DACPAC deployment report is malformed; publish is blocked.' }
    $operations = $report.SelectNodes("//*[local-name()='Operation']")
    foreach ($operation in $operations) {
        $name = [string]$operation.GetAttribute('Name')
        $type = [string]$operation.GetAttribute('Type')
        if (($name + ' ' + $type) -match '(?i)(Drop|Delete|Remove)') {
            $items = @($operation.SelectNodes("./*[local-name()='Item']") | ForEach-Object {
                $value = [string]$_.GetAttribute('Value')
                $itemType = [string]$_.GetAttribute('Type')
                if ($value) { "$value ($itemType)" } else { "<unspecified> ($itemType)" }
            })
            if ($items.Count -eq 0) { $items = @('<operation item not provided by SqlPackage>') }
            throw "DACPAC destructive operation '$name' (type '$type') targets: $($items -join ', '). Publish is blocked."
        }
    }
}

Export-ModuleMember -Function Get-IntegrationDatabaseConfig,Get-IntegrationDatabaseCreationDecision,Get-IntegrationProvisioningDiagnostic,New-IntegrationSqlConnectionStringBuilder,New-IntegrationMasterConnectionStringBuilder,Assert-IntegrationInitializationConfirmation,Assert-IntegrationTargetClassified,Convert-IntegrationSqlIntegerToInt32,ConvertTo-IntegrationMigrationHistoryCollection,ConvertTo-IntegrationVersionScalar,ConvertTo-IntegrationMigrationCatalogCollection,Get-IntegrationStatusLines,Get-IntegrationModeDisposition,Assert-IntegrationConnectionUsable,Get-IntegrationSchemaStateLabel,Get-IntegrationTenantDataClassification,Get-IntegrationTenantClassificationSql,Get-IntegrationTenantScopedInventorySql,ConvertFrom-IntegrationTenantScopedInventoryJson,Test-IntegrationTenantScopedInventory,Get-IntegrationBaselineAuditBlockers,Format-IntegrationSqlFailure,Split-IntegrationSqlBatches,ConvertTo-IntegrationValidatorText,Assert-IntegrationDacpacReportSafe
