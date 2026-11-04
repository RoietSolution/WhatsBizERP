[CmdletBinding()]
param(
    [Parameter()][ValidateSet('Status','DryRun','Apply','AuditBaseline','RecoverBaseline')][string]$Mode = 'Status',
    [Parameter()][AllowEmptyString()][string]$ConnectionString = [Environment]::GetEnvironmentVariable('ConnectionStrings__IntegrationTests'),
    [Parameter()][AllowNull()][string]$ConfirmTarget,
    [Parameter()][AllowNull()][string]$ConfirmRecovery,
    [Parameter()][switch]$CreateIfAbsent,
    [Parameter()][ValidateSet('Debug','Release')][string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$migrationDirectory = Join-Path $root 'database/migrations'
$baselineValidator = Join-Path $migrationDirectory 'validators/Baseline-V40.validate.sql'
$migrationCorePath = Join-Path $PSScriptRoot 'DatabaseMigration.Core.psm1'
$initializerCorePath = Join-Path $PSScriptRoot 'IntegrationDatabase.Initializer.Core.psm1'
Import-Module $migrationCorePath -Force
Import-Module $initializerCorePath -Force
$integrationTarget = Get-IntegrationDatabaseConfig
$expectedServer = $integrationTarget.Server
$expectedDatabase = $null
$baselineVersion = [int]$integrationTarget.BaselineVersion
$throughVersion = $null
$actor = [Environment]::UserDomainName + '\' + [Environment]::UserName
$script:Connection = $null
$script:LockConnection = $null
$script:LockSessionId = $null
$script:DisposableMarkerName = 'WhatsBiz.DisposableIntegrationDatabase'
$script:DisposableMarkerValue = $null

$baselineVersion = ConvertTo-IntegrationVersionScalar -Value $baselineVersion -Name 'BaselineVersion' -Minimum 1 -Maximum 2147483647
Assert-IntegrationInitializationConfirmation -Mode $Mode -ConfirmTarget $ConfirmTarget
$modeDisposition = Get-IntegrationModeDisposition -Mode $Mode
$targetBuilder = New-IntegrationSqlConnectionStringBuilder -ConnectionString $ConnectionString
$expectedDatabase = $targetBuilder.InitialCatalog
$script:DisposableMarkerValue = "Approved:$expectedServer/$expectedDatabase"
$script:HistoricalV40SourceCommit = $integrationTarget.HistoricalBaselineCommit
$script:HistoricalV40DacpacSha256 = $integrationTarget.HistoricalBaselineDacpacSha256

if ($Mode -eq 'RecoverBaseline' -and $ConfirmRecovery -cne "REGISTER-V40:$expectedServer/$expectedDatabase") {
    throw "RecoverBaseline requires the separate exact recovery confirmation: REGISTER-V40:$expectedServer/$expectedDatabase"
}

if ($CreateIfAbsent -and $Mode -ne 'Apply') { throw '-CreateIfAbsent is valid only with explicitly confirmed Apply mode.' }

function Open-TargetConnection {
    $connection = [System.Data.SqlClient.SqlConnection]::new($targetBuilder.ConnectionString)
    try { $connection.Open(); return $connection }
    catch { $exception = $_.Exception; $connection.Dispose(); throw (Format-IntegrationSqlFailure -Operation 'Open approved integration SQL connection' -Exception $exception) }
}

function Open-LockConnection {
    $builder = New-IntegrationMasterConnectionStringBuilder -ConnectionString $targetBuilder.ConnectionString
    $builder.Pooling = $false
    $builder.ConnectRetryCount = 0
    $connection = [System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
    try { $connection.Open(); return $connection }
    catch { $exception = $_.Exception; $connection.Dispose(); throw (Format-IntegrationSqlFailure -Operation 'Open dedicated initializer lock connection in master' -Exception $exception) }
}

function Ensure-ApprovedTargetDatabase {
    $adminBuilder = New-IntegrationMasterConnectionStringBuilder -ConnectionString $targetBuilder.ConnectionString
    $adminConnection = [System.Data.SqlClient.SqlConnection]::new($adminBuilder.ConnectionString)
    try {
        $adminConnection.Open()
        $identityCommand = $adminConnection.CreateCommand()
        $identityCommand.CommandText = "SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')),DB_ID(@database),CONVERT(int,COALESCE(HAS_PERMS_BY_NAME(NULL,'SERVER','CREATE ANY DATABASE'),0)),SUSER_SNAME();"
        $null = $identityCommand.Parameters.Add('@database',[System.Data.SqlDbType]::NVarChar,128)
        $identityCommand.Parameters['@database'].Value = $expectedDatabase
        try {
            $reader = $identityCommand.ExecuteReader()
            try {
                if (-not $reader.Read() -or $reader.GetString(0) -cne $expectedServer) { throw 'SQL Server identity does not match the centralized integration target.' }
                $databaseExists = -not $reader.IsDBNull(1)
                $canCreate = $reader.GetInt32(2) -eq 1
                $executingPrincipal = $reader.GetString(3)
            } finally { $reader.Dispose() }
        } finally { $identityCommand.Dispose() }

        if (-not $databaseExists -and $Mode -in @('Status','DryRun')) {
            Write-Host ('Provisioning permission: ' + (Get-IntegrationProvisioningDiagnostic -DatabaseExists $databaseExists -HasCreatePermission $canCreate -Principal $executingPrincipal))
        } elseif ($databaseExists -and $Mode -in @('Status','DryRun')) {
            Write-Host ('Provisioning permission: ' + (Get-IntegrationProvisioningDiagnostic -DatabaseExists $databaseExists -HasCreatePermission $canCreate -Principal $executingPrincipal))
        }
        if (-not $databaseExists -and $Mode -eq 'Apply' -and $CreateIfAbsent -and -not $canCreate) {
            throw "One-time provisioning denied: SQL principal '$executingPrincipal' lacks CREATE ANY DATABASE for '$expectedDatabase'. An authorized administrator must provision only this configured database. Ongoing schema migration separately requires database-level CONTROL; no permission was changed."
        }

        $creationDecision = Get-IntegrationDatabaseCreationDecision -Mode $Mode -DatabaseExists $databaseExists -CreateIfAbsent ([bool]$CreateIfAbsent) -HasCreatePermission $canCreate
        if ($creationDecision -eq 'ABSENT_READ_ONLY') {
            Write-Host ("Target database is absent: {0}/{1}. Status/DryRun performed no writes; confirmed Apply with -CreateIfAbsent is required for first-time creation." -f $expectedServer,$expectedDatabase)
            return $false
        }
        if ($creationDecision -eq 'CREATE_NEW') {
            # Recheck immediately before the non-destructive create. If another
            # actor creates it concurrently, SQL Server fails; this code never overwrites it.
            $createCommand = $adminConnection.CreateCommand()
            $createCommand.CommandText = "IF DB_ID(@database) IS NULL EXEC(N'CREATE DATABASE ' + QUOTENAME(@database));"
            $null = $createCommand.Parameters.Add('@database',[System.Data.SqlDbType]::NVarChar,128)
            $createCommand.Parameters['@database'].Value = $expectedDatabase
            try { $null = $createCommand.ExecuteNonQuery() } finally { $createCommand.Dispose() }
            Write-Host ('Created previously absent approved integration database: ' + $expectedDatabase)
        }
        return $true
    } catch {
        throw (Format-IntegrationSqlFailure -Operation 'Verify or create the approved integration database in master' -Exception $_.Exception)
    } finally { $adminConnection.Dispose() }
}

function Close-WorkConnection {
    if ($script:Connection) { $script:Connection.Dispose(); $script:Connection = $null }
}

function Refresh-WorkConnection([string]$Operation) {
    Close-WorkConnection
    $script:Connection = Open-TargetConnection
    $null = Assert-LiveTarget -Connection $script:Connection -Operation $Operation
}

function Assert-LiveTarget([System.Data.SqlClient.SqlConnection]$Connection,[string]$Operation='Verify live SQL identity') {
    Assert-IntegrationConnectionUsable -State $Connection.State.ToString() -Operation $Operation
    $command = $Connection.CreateCommand()
    $command.CommandText = "SELECT CONVERT(nvarchar(128),SERVERPROPERTY('ServerName')),DB_NAME(),DB_ID();"
    try {
        $reader = $command.ExecuteReader()
        try {
            if (-not $reader.Read() -or $reader.GetString(0) -cne $expectedServer -or $reader.GetString(1) -cne $expectedDatabase -or (Convert-IntegrationSqlIntegerToInt32 -Value $reader.GetValue(2) -SqlType $reader.GetDataTypeName(2) -ColumnName 'DB_ID()') -le 4) {
                throw 'Live SQL identity rejected; expected the approved user database on the approved server.'
            }
            return [pscustomobject]@{ Server = $reader.GetString(0); Database = $reader.GetString(1) }
        } finally { $reader.Dispose() }
    } catch { throw (Format-IntegrationSqlFailure -Operation $Operation -Exception $_.Exception) } finally { $command.Dispose() }
}

function Invoke-Scalar([string]$Sql,[System.Data.SqlClient.SqlTransaction]$Transaction=$null,[string]$Operation='Execute scalar SQL query') {
    Assert-IntegrationConnectionUsable -State $script:Connection.State.ToString() -Operation $Operation
    $command = $script:Connection.CreateCommand()
    $command.CommandText = $Sql
    $command.Transaction = $Transaction
    $command.CommandTimeout = 120
    try { return $command.ExecuteScalar() } catch { throw (Format-IntegrationSqlFailure -Operation $Operation -Exception $_.Exception) } finally { $command.Dispose() }
}

function Invoke-Reader([string]$Sql,[string]$Operation='Execute SQL reader query') {
    Assert-IntegrationConnectionUsable -State $script:Connection.State.ToString() -Operation $Operation
    $command = $script:Connection.CreateCommand()
    $command.CommandText = $Sql
    $command.CommandTimeout = 120
    try {
        $reader = $command.ExecuteReader()
        try {
            if (-not $reader.Read()) { throw 'SQL status query returned no row.' }
            $values = [object[]]::new($reader.FieldCount)
            for ($index = 0; $index -lt $reader.FieldCount; $index++) { $values[$index] = if ($reader.IsDBNull($index)) { $null } else { $reader.GetValue($index) } }
            return $values
        } finally { $reader.Dispose() }
    } catch { throw (Format-IntegrationSqlFailure -Operation $Operation -Exception $_.Exception) } finally { $command.Dispose() }
}

function Get-TargetState([string]$Operation='Read integration database schema and ledger state') {
    $stateSql = @"
SELECT
 (SELECT COUNT(*) FROM sys.objects WHERE is_ms_shipped=0),
 CASE WHEN OBJECT_ID(N'deployment.DatabaseMigrationBaseline',N'U') IS NULL THEN 0 ELSE 1 END,
 CASE WHEN OBJECT_ID(N'deployment.DatabaseMigrationHistory',N'U') IS NULL THEN 0 ELSE 1 END,
 (SELECT CONVERT(nvarchar(4000),value) FROM sys.extended_properties WHERE class=0 AND name=N'$script:DisposableMarkerName'),
 CASE WHEN OBJECT_ID(N'core.Tenants',N'U') IS NOT NULL OR OBJECT_ID(N'master.Products',N'U') IS NOT NULL OR OBJECT_ID(N'sales.Customers',N'U') IS NOT NULL THEN 1 ELSE 0 END;
"@
    $row = Invoke-Reader -Sql $stateSql -Operation $Operation
    $userTableCount = [int]$row[0]
    $hasBaseline = [int]$row[1] -eq 1
    $hasHistory = [int]$row[2] -eq 1
    if ($row[3] -and [string]$row[3] -cne $script:DisposableMarkerValue) { throw 'The database has a conflicting disposable-designation marker; refusing to proceed.' }

    $tenantSchema = Invoke-Reader -Sql @"
SELECT
 CASE WHEN OBJECT_ID(N'core.Tenants',N'U') IS NULL THEN 0 ELSE 1 END,
 CASE WHEN COL_LENGTH(N'core.Tenants',N'TenantKey') IS NULL THEN 0 ELSE 1 END;
"@ -Operation "${Operation}: inspect canonical tenant schema"
    $tenantObjectPresent = [int]$tenantSchema[0] -eq 1
    $tenantKeyColumnPresent = [int]$tenantSchema[1] -eq 1
    $tenantDataClassification = if (-not $tenantObjectPresent -or -not $tenantKeyColumnPresent) {
        Get-IntegrationTenantDataClassification -TenantObjectPresent $tenantObjectPresent -TenantKeyColumnPresent $tenantKeyColumnPresent
    } else {
        $tenantCounts = Invoke-Reader -Sql (Get-IntegrationTenantClassificationSql) -Operation "${Operation}: aggregate tenant data classification"
        Get-IntegrationTenantDataClassification -TenantObjectPresent $true -TenantKeyColumnPresent $true -TenantCount ([int]$tenantCounts[0]) -UnknownTenantKeyCount ([int]$tenantCounts[1]) -NonTestTenantCount ([int]$tenantCounts[2]) -CanonicalDefaultSeedCount ([int]$tenantCounts[3])
    }

    return [pscustomobject]@{
        UserObjectCount = $userTableCount
        HasBaseline = $hasBaseline
        HasHistory = $hasHistory
        MarkerPresent = [bool]$row[3]
        NonTestTenantCount = $tenantDataClassification.NonTestKeyCount
        NonSeedNonTestTenantCount = $tenantDataClassification.NonSeedNonTestCount
        UnknownTenantKeyCount = $tenantDataClassification.UnknownKeyCount
        TenantDataClassification = $tenantDataClassification
        TenantTablePresent = $tenantObjectPresent -and $tenantKeyColumnPresent
        CanonicalBaselineFootprint = ([int]$row[4] -eq 1)
        LedgerPartial = ($hasBaseline -xor $hasHistory)
    }
}

function Get-MigrationHistory {
    if (-not (Get-TargetState -Operation 'Verify migration history table before reading ledger').HasHistory) { throw 'Migration history table is missing; the schema is not initialized.' }
    $command = $script:Connection.CreateCommand()
    $command.CommandText = 'SELECT MigrationVersion,MigrationName,ScriptHash,ValidatorHash FROM deployment.DatabaseMigrationHistory ORDER BY MigrationVersion;'
    try {
        $reader = $command.ExecuteReader()
        $rows = [System.Collections.Generic.List[object]]::new()
        try {
            $expectedColumns = @('MigrationVersion','MigrationName','ScriptHash','ValidatorHash')
            if ($reader.FieldCount -ne $expectedColumns.Count) { throw 'Migration history query returned an unexpected column count.' }
            for ($index = 0; $index -lt $expectedColumns.Count; $index++) {
                if ($reader.GetName($index) -cne $expectedColumns[$index]) { throw "Migration history query returned an unexpected column at ordinal $index." }
            }
            while ($reader.Read()) {
                if ($reader.IsDBNull(0) -or $reader.IsDBNull(1) -or $reader.IsDBNull(2) -or $reader.IsDBNull(3)) {
                    throw 'Migration history contains NULL in a required field.'
                }
                $rows.Add([pscustomobject]@{
                    MigrationVersion = Convert-IntegrationSqlIntegerToInt32 -Value $reader.GetValue(0) -SqlType $reader.GetDataTypeName(0) -ColumnName 'DatabaseMigrationHistory.MigrationVersion'
                    MigrationName = $reader.GetString(1)
                    ScriptHash = $reader.GetString(2)
                    ValidatorHash = if ($reader.IsDBNull(3)) { $null } else { $reader.GetString(3) }
                })
            }
        } finally { $reader.Dispose() }
        $history = ConvertTo-IntegrationMigrationHistoryCollection -HistoryTablePresent $true -Records $rows.ToArray()
        return ,$history
    } catch { throw (Format-IntegrationSqlFailure -Operation 'Read deployment.DatabaseMigrationHistory' -Exception $_.Exception) } finally { $command.Dispose() }
}

function Get-BaselineRecord {
    if (-not (Get-TargetState).HasBaseline) { return $null }
    $command = $script:Connection.CreateCommand()
    $command.CommandText = 'SELECT BaselineVersion,BaselineValidatorFile,BaselineValidatorHash FROM deployment.DatabaseMigrationBaseline WHERE SingletonId=1;'
    try {
        $reader = $command.ExecuteReader()
        try {
            if (-not $reader.Read()) { throw 'V40 baseline ledger row is missing.' }
            $record = [pscustomobject]@{ Version = Convert-IntegrationSqlIntegerToInt32 -Value $reader.GetValue(0) -SqlType $reader.GetDataTypeName(0) -ColumnName 'DatabaseMigrationBaseline.BaselineVersion'; Validator = $reader.GetString(1); Hash = $reader.GetString(2) }
            if ($reader.Read()) { throw 'Multiple V40 baseline ledger rows exist.' }
            return $record
        } finally { $reader.Dispose() }
    } catch { throw (Format-IntegrationSqlFailure -Operation 'Read deployment.DatabaseMigrationBaseline' -Exception $_.Exception) } finally { $command.Dispose() }
}

function Invoke-SqlText([string]$Text,[System.Data.SqlClient.SqlTransaction]$Transaction=$null,[string]$Label='SQL script') {
    foreach ($batch in (Split-IntegrationSqlBatches -Text $Text)) {
        $command = $script:Connection.CreateCommand()
        $command.CommandText = $batch
        $command.Transaction = $Transaction
        $command.CommandTimeout = 0
        try { $null = $command.ExecuteNonQuery() } catch { throw (Format-IntegrationSqlFailure -Operation $Label -Exception $_.Exception) } finally { $command.Dispose() }
    }
}

function Get-ValidatorSql([string]$Path) {
    $text = [IO.File]::ReadAllText($Path)
    Assert-DatabaseMigrationValidatorReadOnly -Text $text -FileName (Split-Path -Leaf $Path)
    return ConvertTo-IntegrationValidatorText -Text $text -FileName (Split-Path -Leaf $Path) -DatabaseName $expectedDatabase
}

function Invoke-Validator([string]$Path,[System.Data.SqlClient.SqlTransaction]$Transaction=$null) {
    Invoke-SqlText -Text (Get-ValidatorSql $Path) -Transaction $Transaction -Label (Split-Path -Leaf $Path)
}

function Get-CanonicalMigrations {
    $catalog = @(Get-DatabaseMigrationCatalog -Directory $migrationDirectory | Where-Object { $_.Version -gt $baselineVersion } | Sort-Object Version)
    $catalog = ConvertTo-IntegrationMigrationCatalogCollection -Migrations $catalog
    Assert-DatabaseMigrationSequence -Migrations $catalog -BaselineVersion $baselineVersion
    if ($catalog.Count -eq 0) { throw 'Canonical migration catalog contains no migrations after V40.' }
    $script:throughVersion = [int](($catalog | Measure-Object -Property Version -Maximum).Maximum)
    $expected = @(($baselineVersion + 1)..$throughVersion)
    if (($catalog.Version -join ',') -cne ($expected -join ',')) { throw "Canonical migration sequence V$($baselineVersion + 1) through V$throughVersion is incomplete." }
    $preflight = Assert-DatabaseMigrationPreflight -Migrations $catalog -Pending $catalog -BaselineVersion $baselineVersion
    if ($preflight.Status -cne 'PASS') { throw 'Canonical migration source/validator preflight failed.' }
    foreach ($migration in $catalog) {
        if ((Get-Content -LiteralPath $migration.Path -TotalCount 6) -match '^\s*--\s*WhatsBiz-Migration-Transaction:\s*NONE\s*$') {
            throw "V$($migration.Version) requires non-transactional execution; the integration initializer does not support it safely."
        }
        $null = Split-IntegrationSqlBatches -Text ([IO.File]::ReadAllText($migration.Path))
        $null = Split-IntegrationSqlBatches -Text (Get-ValidatorSql $migration.ValidatorPath)
    }
    return $catalog
}

function Invoke-V40BaselineValidator {
    Invoke-Validator -Path $baselineValidator
}

function Assert-NoUntrackedMigrationArtifacts($Migrations) {
    $passing = @(Get-UntrackedMigrationValidatorVersions -Migrations $Migrations)
    $unexpected = @($passing | Where-Object { $_ -ne 62 })
    if ($unexpected.Count -gt 0) {
        throw 'Existing schema has post-V40 migration objects without trustworthy ledger entries; refusing baseline adoption.'
    }
}

function Get-UntrackedMigrationValidatorVersions($Migrations) {
    $passing = [System.Collections.Generic.List[int]]::new()
    foreach ($migration in $Migrations) {
        try { Invoke-Validator -Path $migration.ValidatorPath; $passing.Add([int]$migration.Version) }
        catch { }
    }
    return [int[]]$passing.ToArray()
}

function ConvertTo-DeterministicDacpac([string]$Path) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $replacement = $Path + '.deterministic'
    $source = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $payloads = [System.Collections.Generic.List[object]]::new()
        foreach ($entry in @($source.Entries | Where-Object { $_.Name } | Sort-Object FullName)) {
            $stream = $entry.Open()
            try {
                $memory = [IO.MemoryStream]::new()
                $stream.CopyTo($memory)
                $payloads.Add([pscustomobject]@{ Name=$entry.FullName; Bytes=$memory.ToArray() })
            } finally { $stream.Dispose(); if ($memory) { $memory.Dispose() } }
        }
    } finally { $source.Dispose() }
    $file = [IO.File]::Open($replacement,[IO.FileMode]::CreateNew,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
    try {
        $archive = [IO.Compression.ZipArchive]::new($file,[IO.Compression.ZipArchiveMode]::Create,$true)
        try {
            foreach ($payload in $payloads) {
                $entry = $archive.CreateEntry($payload.Name,[IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [datetimeoffset]::new(2000,1,1,0,0,0,[timespan]::Zero)
                $stream = $entry.Open()
                try { $stream.Write($payload.Bytes,0,$payload.Bytes.Length) } finally { $stream.Dispose() }
            }
        } finally { $archive.Dispose() }
    } finally { $file.Dispose() }
    Move-Item -LiteralPath $replacement -Destination $Path -Force
}

function Get-DacpacModelSha256([string]$Path) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.GetEntry('model.xml')
        if (-not $entry) { throw 'Historical DACPAC has no model.xml payload.' }
        $stream = $entry.Open()
        try {
            $reader = [IO.StreamReader]::new($stream,[Text.Encoding]::UTF8,$true,4096,$true)
            try { $model = $reader.ReadToEnd() } finally { $reader.Dispose() }
            $model = [regex]::Replace($model,'whatsbiz-v40-source-[a-f0-9]{32}','whatsbiz-v40-source-PINNED',[Text.RegularExpressions.RegexOptions]::IgnoreCase)
            $bytes = [Text.Encoding]::UTF8.GetBytes($model)
            $sha = [Security.Cryptography.SHA256]::Create()
            try { return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-','') }
            finally { $sha.Dispose() }
        } finally { $stream.Dispose() }
    } finally { $archive.Dispose() }
}

function Build-HistoricalV40Dacpac {
    $sourceRoot = Join-Path ([IO.Path]::GetTempPath()) ('whatsbiz-v40-source-' + [guid]::NewGuid().ToString('N'))
    $archivePath = "$sourceRoot.zip"
    $projectRelativePath = 'database/WhatsBiz.Database/WhatsBiz.Database.sqlproj'
    $postDeployRelativePath = 'database/WhatsBiz.Database/Scripts/PostDeployment.sql'
    try {
        $git = (Get-Command git -ErrorAction Stop).Source
        $projectSpec = '{0}:{1}' -f $script:HistoricalV40SourceCommit,$projectRelativePath
        $postDeploySpec = '{0}:{1}' -f $script:HistoricalV40SourceCommit,$postDeployRelativePath
        & $git -C $root cat-file -e $projectSpec 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'Pinned historical V40 SQL project source is unavailable in the repository.' }
        & $git -C $root cat-file -e $postDeploySpec 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'Pinned historical V40 post-deployment source is unavailable in the repository.' }
        $null = & $git -C $root archive --format=zip --output=$archivePath $script:HistoricalV40SourceCommit 'database/WhatsBiz.Database' 2>&1
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $archivePath -PathType Leaf)) { throw 'Could not extract the pinned historical V40 database source; baseline build was not attempted.' }
        Expand-Archive -LiteralPath $archivePath -DestinationPath $sourceRoot -Force
        $projectPath = Join-Path $sourceRoot $projectRelativePath
        $postDeployPath = Join-Path $sourceRoot $postDeployRelativePath
        if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf) -or -not (Test-Path -LiteralPath $postDeployPath -PathType Leaf)) { throw 'Historical V40 source archive is incomplete; baseline build was not attempted.' }
        $postDeploy = [IO.File]::ReadAllText($postDeployPath)
        if ($postDeploy -notmatch '(?im)^\s*:r\s+\.\\V40-StorefrontAllCategoryImage\.sql\s*$' -or $postDeploy -match '(?im)^\s*:r\s+\.\\V(?:4[1-9]|[5-9]\d|\d{3,})-[^\r\n]+') { throw 'Pinned source post-deployment chain is not an exact V40 baseline chain.' }
        $buildOutput = Join-Path $sourceRoot 'build-output'
        $buildLog = @(& dotnet build $projectPath --configuration $Configuration --output $buildOutput 2>&1 | ForEach-Object { [string]$_ })
        if ($LASTEXITCODE -ne 0) { throw ('Pinned historical V40 SQL project build failed; no report or publish was attempted. ' + (($buildLog | Select-Object -Last 8) -join ' | ')) }
        $dacpac = Get-ChildItem -LiteralPath $buildOutput -Filter 'WhatsBiz.Database.dacpac' -File -Recurse | Select-Object -First 1
        if (-not $dacpac) { throw 'Pinned historical V40 build produced no DACPAC.' }
        ConvertTo-DeterministicDacpac -Path $dacpac.FullName
        # DACPAC containers carry build-specific ZIP/origin metadata. Pin the SHA-256
        # of model.xml, the actual schema payload consumed by DacFx, after making the
        # container ordering/timestamps deterministic for repeatable diagnostics.
        $hash = Get-DacpacModelSha256 -Path $dacpac.FullName
        if ($script:HistoricalV40DacpacSha256 -notmatch '^[A-F0-9]{64}$' -or $hash -cne $script:HistoricalV40DacpacSha256) {
            throw "Pinned historical V40 DACPAC SHA-256 mismatch. Expected $script:HistoricalV40DacpacSha256; built $hash."
        }
        return [pscustomobject]@{ Root=$sourceRoot; Archive=$archivePath; Path=$dacpac.FullName; SourceCommit=$script:HistoricalV40SourceCommit; Sha256=$hash }
    } catch {
        Remove-Item -LiteralPath $sourceRoot -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
        throw
    }
}

function Remove-HistoricalV40Build($Artifact) {
    if (-not $Artifact -or -not $Artifact.Root) { return }
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $artifactRoot = [IO.Path]::GetFullPath([string]$Artifact.Root)
    if (-not $artifactRoot.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -or (Split-Path -Leaf $artifactRoot) -notmatch '^whatsbiz-v40-source-[a-f0-9]{32}$') { throw 'Refusing to remove a historical baseline build outside its generated temporary directory.' }
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
    Remove-Item -LiteralPath ($artifactRoot + '.zip') -Force -ErrorAction SilentlyContinue
}

function Get-V40DacpacSchemaAudit {
    if (-not $targetBuilder.IntegratedSecurity -or -not [string]::IsNullOrWhiteSpace($targetBuilder.UserID) -or -not [string]::IsNullOrWhiteSpace($targetBuilder.Password)) {
        return [pscustomobject]@{ OperationCount=-1; Error='Schema comparison requires Windows Integrated Authentication; no DACPAC child process was started.' }
    }
    $reportDirectory = Join-Path $PSScriptRoot '.artifacts/integration-initializer'
    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    $reportPath = Join-Path $reportDirectory ('v40-schema-audit-' + $expectedDatabase + '.xml')
    $historical = $null
    try {
        $historical = Build-HistoricalV40Dacpac
        Write-Host ("V40 source: {0}; built DACPAC SHA-256: {1}" -f $historical.SourceCommit,$historical.Sha256)
        $sqlPackage = (Get-Command SqlPackage -ErrorAction Stop).Source
        $arguments = @('/Action:DeployReport',("/SourceFile:$($historical.Path)"),('/TargetConnectionString:' + $targetBuilder.ConnectionString),("/OutputPath:$reportPath"),'/p:BlockOnPossibleDataLoss=True','/p:DropObjectsNotInSource=True')
        $null = @(& $sqlPackage @arguments 2>&1)
        if ($LASTEXITCODE -ne 0) { return [pscustomobject]@{ OperationCount=-1; ReportPath=$reportPath; Error="SqlPackage DeployReport failed with exit code $LASTEXITCODE; schema comparison is inconclusive." } }
        if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { return [pscustomobject]@{ OperationCount=-1; ReportPath=$reportPath; Error='SqlPackage did not produce a schema comparison report.' } }
        try { Assert-IntegrationDacpacReportSafe -ReportPath $reportPath }
        catch { return [pscustomobject]@{ OperationCount=-1; ReportPath=$reportPath; Error=$_.Exception.Message } }
        [xml]$report = Get-Content -LiteralPath $reportPath -Raw
        return [pscustomobject]@{ OperationCount=@($report.SelectNodes("//*[local-name()='Operation']")).Count; ReportPath=$reportPath; Error=$null }
    } catch {
        return [pscustomobject]@{ OperationCount=-1; ReportPath=$reportPath; Error='Schema comparison could not complete: ' + $_.Exception.Message }
    } finally {
        if ($historical) { Remove-HistoricalV40Build $historical }
    }
}

function Get-V40GlobalDataAudit {
    $sql = @"
DECLARE @Problems table(Problem nvarchar(1000) NOT NULL);
CREATE TABLE #Unexpected(Problem nvarchar(1000) NOT NULL);
IF (SELECT COUNT(*) FROM purchase.SupplierPaymentTerms)<>5 OR EXISTS(SELECT PaymentTermCode,PaymentTermName,DueDays,IsDefault,IsActive FROM purchase.SupplierPaymentTerms EXCEPT SELECT * FROM (VALUES(N'IMMEDIATE',N'Immediate',0,CONVERT(bit,1),CONVERT(bit,1)),(N'NET15',N'Net 15 Days',15,CONVERT(bit,0),CONVERT(bit,1)),(N'NET30',N'Net 30 Days',30,CONVERT(bit,0),CONVERT(bit,1)),(N'NET45',N'Net 45 Days',45,CONVERT(bit,0),CONVERT(bit,1)),(N'NET60',N'Net 60 Days',60,CONVERT(bit,0),CONVERT(bit,1)))E(Code,Name,Days,IsDefault,IsActive)) INSERT @Problems VALUES(N'purchase.SupplierPaymentTerms differs from V40 source seed.');
IF (SELECT COUNT(*) FROM sales.CustomerPaymentTerms)<>5 OR EXISTS(SELECT PaymentTermCode,PaymentTermName,DueDays,IsDefault,IsActive FROM sales.CustomerPaymentTerms EXCEPT SELECT * FROM (VALUES(N'IMMEDIATE',N'Immediate',0,CONVERT(bit,1),CONVERT(bit,1)),(N'NET15',N'Net 15 Days',15,CONVERT(bit,0),CONVERT(bit,1)),(N'NET30',N'Net 30 Days',30,CONVERT(bit,0),CONVERT(bit,1)),(N'NET45',N'Net 45 Days',45,CONVERT(bit,0),CONVERT(bit,1)),(N'NET60',N'Net 60 Days',60,CONVERT(bit,0),CONVERT(bit,1)))E(Code,Name,Days,IsDefault,IsActive)) INSERT @Problems VALUES(N'sales.CustomerPaymentTerms differs from V40 source seed.');
IF (SELECT COUNT(*) FROM inventory.WarehouseTypes)<>5 OR (SELECT COUNT(*) FROM inventory.WarehouseTypes WHERE TypeCode IN(N'GENERAL',N'DISTRIBUTION',N'COLD',N'BONDED',N'TRANSIT') AND IsActive=1)<>5 INSERT @Problems VALUES(N'inventory.WarehouseTypes differs from V40 source seed.');
IF (SELECT COUNT(*) FROM inventory.InventorySettings)<>1 OR NOT EXISTS(SELECT 1 FROM inventory.InventorySettings WHERE NegativeStockAllowed=0 AND BatchTrackingEnabled=1 AND SerialTrackingEnabled=1 AND ValuationMethod=N'AVERAGE') INSERT @Problems VALUES(N'inventory.InventorySettings differs from V40 source seed.');
IF (SELECT COUNT(*) FROM sales.PaymentMethods)<>7 OR (SELECT COUNT(*) FROM sales.PaymentMethods WHERE MethodCode IN(N'CASH',N'UPI',N'CARD',N'BANK',N'WALLET',N'CREDIT',N'RAZORPAY') AND IsActive=1)<>7 INSERT @Problems VALUES(N'sales.PaymentMethods differs from V40 source seed.');
IF (SELECT COUNT(*) FROM finance.PaymentModes)<>7 OR (SELECT COUNT(*) FROM finance.PaymentModes WHERE ModeCode IN(N'CASH',N'UPI',N'CARD',N'BANK',N'WALLET',N'CREDIT',N'RAZORPAY') AND IsActive=1)<>7 INSERT @Problems VALUES(N'finance.PaymentModes differs from V40 source seed.');
IF (SELECT COUNT(*) FROM finance.AccountGroups)<>4 OR (SELECT COUNT(*) FROM finance.AccountGroups WHERE GroupCode IN(N'ASSET',N'LIABILITY',N'INCOME',N'EXPENSE') AND IsActive=1)<>4 INSERT @Problems VALUES(N'finance.AccountGroups differs from V40 source seed.');
IF (SELECT COUNT(*) FROM finance.Accounts)<>12 OR (SELECT COUNT(*) FROM finance.Accounts WHERE AccountCode IN(N'CASH',N'BANK',N'CUSTOMER',N'SUPPLIER',N'INVENTORY',N'INPUT_GST',N'OUTPUT_GST',N'DELIVERY_CLEARING',N'SALES',N'PURCHASE_RETURN',N'SALES_RETURN',N'STOCK_ADJUST') AND IsSystem=1 AND IsActive=1)<>12 INSERT @Problems VALUES(N'finance.Accounts differs from V40 source seed.');
IF (SELECT COUNT(*) FROM core.Plans)<>2 OR NOT EXISTS(SELECT 1 FROM core.Plans WHERE PlanKey=N'V1_DEFAULT' AND Name=N'V1 Default' AND IsActive=1) OR NOT EXISTS(SELECT 1 FROM core.Plans WHERE PlanKey=N'V2_COMMERCE' AND Name=N'V1 + V2 Commerce' AND IsActive=1) INSERT @Problems VALUES(N'core.Plans differs from V40 source seed.');
IF (SELECT COUNT(*) FROM core.Features)<>31 OR (SELECT COUNT(*) FROM core.Features WHERE IsActive=1 AND FeatureKey IN(N'V1',N'DASHBOARD',N'PRODUCTS',N'INVENTORY',N'POS',N'CUSTOMERS',N'SUPPLIERS',N'PURCHASE',N'WAREHOUSES',N'FINANCE',N'REPORTS',N'GST',N'ANALYTICS',N'PRINTING',N'USERS_ROLES',N'ADMINISTRATION',N'V2',N'WHATSAPP_COMMERCE',N'WHATSAPP_CONFIGURATION',N'WHATSAPP_COMMERCE_DEMO',N'COMMERCE_PRODUCT_SEARCH',N'COMMERCE_COLLECTIONS',N'COMMERCE_ORDERS',N'COMMERCE_ANALYTICS',N'META_WHATSAPP_INTEGRATION',N'WEBHOOK_DIAGNOSTICS',N'ADVANCED_WAREHOUSE',N'AI_ASSISTANT',N'INTEGRATIONS',N'CUSTOMER_REFERRAL_REWARDS',N'DELIVERY_MANAGEMENT'))<>31 INSERT @Problems VALUES(N'core.Features differs from the 31 source-derived V40 features.');
IF (SELECT COUNT(*) FROM core.PlanFeatures)<>62 OR EXISTS(SELECT 1 FROM core.Plans p CROSS JOIN core.Features f LEFT JOIN core.PlanFeatures pf ON pf.PlanId=p.PlanId AND pf.FeatureId=f.FeatureId WHERE pf.PlanFeatureId IS NULL OR pf.IsEnabled<>CONVERT(bit,CASE WHEN p.PlanKey=N'V2_COMMERCE' OR f.Version=N'V1' THEN 1 ELSE 0 END)) INSERT @Problems VALUES(N'core.PlanFeatures differs from the source-derived V40 two-plan entitlement matrix.');
IF (SELECT COUNT(*) FROM printing.DocumentTemplates)<>3 OR (SELECT COUNT(*) FROM printing.DocumentTemplates WHERE TemplateCode IN(N'GST-INVOICE-A4',N'GST-INVOICE-80MM',N'GST-INVOICE-58MM') AND IsActive=1)<>3 INSERT @Problems VALUES(N'printing.DocumentTemplates differs from V40 source seed.');
IF (SELECT COUNT(*) FROM admin.ApplicationSettings)<>19 OR (SELECT COUNT(*) FROM admin.ApplicationSettings s JOIN admin.Companies c ON c.CompanyId=s.CompanyId AND c.TenantId='11111111-1111-1111-1111-111111111111' WHERE (s.SettingKey=N'CUSTOMER_CODE_PREFIX' AND s.SettingValue=N'CUS') OR (s.SettingKey=N'CUSTOMER_CODE_SEPARATOR' AND s.SettingValue=N'-') OR (s.SettingKey=N'CUSTOMER_CODE_PADDING' AND s.SettingValue=N'6') OR (s.SettingKey=N'CUSTOMER_CODE_NEXT_NUMBER' AND s.SettingValue=N'1') OR (s.SettingKey=N'SUPPLIER_CODE_PREFIX' AND s.SettingValue=N'SUP') OR (s.SettingKey=N'SUPPLIER_CODE_SEPARATOR' AND s.SettingValue=N'-') OR (s.SettingKey=N'SUPPLIER_CODE_PADDING' AND s.SettingValue=N'6') OR (s.SettingKey=N'SUPPLIER_CODE_NEXT_NUMBER' AND s.SettingValue=N'1') OR (s.SettingKey=N'PRODUCT_CODE_PREFIX' AND s.SettingValue=N'PRD') OR (s.SettingKey=N'PRODUCT_CODE_SEPARATOR' AND s.SettingValue=N'-') OR (s.SettingKey=N'PRODUCT_CODE_PADDING' AND s.SettingValue=N'6') OR (s.SettingKey=N'PRODUCT_CODE_NEXT_NUMBER' AND s.SettingValue=N'1') OR (s.SettingKey=N'CustomerNotifications.Enabled' AND s.SettingValue=N'False') OR (s.SettingKey=N'CustomerNotifications.WhatsApp.Enabled' AND s.SettingValue=N'False') OR (s.SettingKey=N'CustomerNotifications.Sms.Enabled' AND s.SettingValue=N'False') OR (s.SettingKey=N'CustomerNotifications.Events.SuccessfulSale' AND s.SettingValue=N'True') OR (s.SettingKey=N'CustomerNotifications.Events.SuccessfulPayment' AND s.SettingValue=N'True') OR s.SettingKey=N'CustomerNotifications.WhatsApp.Template' OR s.SettingKey=N'CustomerNotifications.Sms.Template')<>19 INSERT @Problems VALUES(N'admin.ApplicationSettings differs from the 19 source-derived V40 DEFAULT-company settings.');
IF (SELECT COUNT(*) FROM purchase.PurchaseSeries)<>1 OR NOT EXISTS(SELECT 1 FROM purchase.PurchaseSeries WHERE SeriesCode=N'PUR' AND Prefix=N'PUR' AND NextNumber=1 AND NumberLength=6 AND IsDefault=1 AND IsActive=1) INSERT @Problems VALUES(N'purchase.PurchaseSeries differs from V40 source seed.');
IF (SELECT COUNT(*) FROM sales.InvoiceSeries)<>1 OR NOT EXISTS(SELECT 1 FROM sales.InvoiceSeries WHERE SeriesCode=N'POS' AND Prefix=N'POS' AND NextNumber=1 AND NumberLength=6 AND IsDefault=1) INSERT @Problems VALUES(N'sales.InvoiceSeries differs from the untouched V40 source seed.');

DECLARE @Allowed table(TableName nvarchar(517) PRIMARY KEY);
INSERT @Allowed VALUES(N'purchase.SupplierPaymentTerms'),(N'sales.CustomerPaymentTerms'),(N'inventory.WarehouseTypes'),(N'inventory.InventorySettings'),(N'sales.PaymentMethods'),(N'finance.PaymentModes'),(N'finance.AccountGroups'),(N'finance.Accounts'),(N'core.Tenants'),(N'core.Plans'),(N'core.Features'),(N'core.PlanFeatures'),(N'core.Subscriptions'),(N'core.TenantFeatures'),(N'commerce.TenantDeliverySettings'),(N'admin.Companies'),(N'admin.ApplicationSettings'),(N'printing.DocumentTemplates'),(N'purchase.PurchaseSeries'),(N'sales.InvoiceSeries');
DECLARE @Fragments nvarchar(max);
SELECT @Fragments=STRING_AGG(CONVERT(nvarchar(max),N'IF EXISTS(SELECT TOP(1)1 FROM '+QUOTENAME(s.name)+N'.'+QUOTENAME(t.name)+N') INSERT #Unexpected VALUES(N''Unexpected business/seed records exist in '+REPLACE(s.name+N'.'+t.name,N'''',N'''''')+N'.'');'),NCHAR(10))
FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id LEFT JOIN @Allowed a ON a.TableName=s.name+N'.'+t.name WHERE t.is_ms_shipped=0 AND a.TableName IS NULL AND s.name<>N'deployment';
IF @Fragments IS NOT NULL EXEC sys.sp_executesql @Fragments;
INSERT @Problems SELECT Problem FROM #Unexpected;
SELECT Problem FROM @Problems ORDER BY Problem FOR JSON PATH;
"@
    $json = [string](Invoke-Scalar -Sql $sql -Operation 'Compare global V40 seeds and reject unexpected business records')
    if ([string]::IsNullOrWhiteSpace($json)) { throw 'Global V40 data audit returned no JSON result.' }
    try { $rows = @(ConvertFrom-Json -InputObject $json -ErrorAction Stop) } catch { throw 'Global V40 data audit returned malformed JSON.' }
    return [string[]]@($rows | ForEach-Object { [string]$_.Problem })
}

function Invoke-ReadOnlyV40BaselineAudit($TargetState,$BaselineRecord,[bool]$BaselineValidated,$History,$Migrations) {
    $blockers = [System.Collections.Generic.List[string]]::new()
    $controlAvailable = Test-DatabaseControlPermission
    if (-not $controlAvailable) { $blockers.Add('Database-level CONTROL permission is required for recovery and is not available.') }
    $migrationMatches = @(Get-UntrackedMigrationValidatorVersions -Migrations $Migrations)
    $migrationLedgerState = if ($TargetState.LedgerPartial) { 'PARTIAL' }
        elseif ($History.Count -gt 0) { 'POPULATED' }
        elseif ($TargetState.HasHistory) { 'EMPTY' }
        else { 'ABSENT' }
    $baselineLedgerState = 'ABSENT'
    if ($TargetState.HasBaseline) {
        if ($BaselineRecord -and $BaselineRecord.Version -eq 40 -and $BaselineRecord.Validator -ceq (Split-Path -Leaf $baselineValidator) -and $BaselineRecord.Hash -ceq (Get-DatabaseMigrationHash -Path $baselineValidator)) { $baselineLedgerState='CONSISTENT' }
        else { $baselineLedgerState='MISMATCHED' }
    }

    $schemaAudit = Get-V40DacpacSchemaAudit
    if ($schemaAudit.Error) { $blockers.Add($schemaAudit.Error) }
    $inventoryJson = [string](Invoke-Scalar -Sql (Get-IntegrationTenantScopedInventorySql) -Operation 'Inventory tenant-scoped baseline data using aggregate counts')
    $inventory = ConvertFrom-IntegrationTenantScopedInventoryJson -Json $inventoryJson
    $inventoryBlockers = @(Test-IntegrationTenantScopedInventory -Inventory $inventory)
    $globalDataBlockers = @(Get-V40GlobalDataAudit)
    $allDataBlockers = @($inventoryBlockers + $globalDataBlockers)

    $auditBlockers = @(Get-IntegrationBaselineAuditBlockers -SchemaState (Get-IntegrationSchemaStateLabel -UserObjectCount $TargetState.UserObjectCount -HasBaseline $TargetState.HasBaseline -HasHistory $TargetState.HasHistory -MarkerPresent $TargetState.MarkerPresent -CanonicalBaselineFootprint $TargetState.CanonicalBaselineFootprint -BaselineValidated $BaselineValidated) -BaselineValidatorPassed $BaselineValidated -BaselineLedgerState $baselineLedgerState -MarkerPresent $TargetState.MarkerPresent -MigrationLedgerState $migrationLedgerState -AppliedMigrationCount $History.Count -UntrackedMigrationVersions $migrationMatches -DacpacOperationCount ([Math]::Max(0,$schemaAudit.OperationCount)) -TenantState $TargetState.TenantDataClassification.State -TenantCount ([int]$TargetState.TenantDataClassification.TenantCount) -CanonicalSeedCount ([int]$TargetState.TenantDataClassification.CanonicalDefaultSeedCount) -UnknownTenantKeyCount ([int]$TargetState.TenantDataClassification.UnknownKeyCount) -NonTenantSeedDataVerified ($globalDataBlockers.Count -eq 0) -InventoryBlockers $allDataBlockers)
    foreach ($item in $auditBlockers) { $blockers.Add($item) }
    if ($schemaAudit.OperationCount -lt 0 -and -not $schemaAudit.Error) { $blockers.Add('DACPAC schema operation count is unavailable.') }

    Write-Host 'Read-only V40 baseline reconciliation audit'
    Write-Host ('Target: ' + $expectedServer + '/' + $expectedDatabase)
    Write-Host ('Database CONTROL: ' + $(if ($controlAvailable) { 'PASS' } else { 'FAIL' }))
    Write-Host ('V40 validator: ' + $(if ($BaselineValidated) { 'PASS' } else { 'FAIL/NOT VERIFIED' }))
    Write-Host ('Baseline ledger: ' + $baselineLedgerState)
    Write-Host ('Migration ledger: ' + $migrationLedgerState + '; applied rows=' + $History.Count)
    Write-Host ('Disposable marker: ' + $(if ($TargetState.MarkerPresent) { 'present' } else { 'absent' }))
    Write-Host ('Tenant inventory: ' + $TargetState.TenantDataClassification.State + '; total=' + $TargetState.TenantDataClassification.TenantCount + '; canonical seed=' + $TargetState.TenantDataClassification.CanonicalDefaultSeedCount + '; unknown keys=' + $TargetState.TenantDataClassification.UnknownKeyCount)
    if ($schemaAudit.ReportPath) { Write-Host ('DACPAC report: ' + $schemaAudit.ReportPath) }
    if ($schemaAudit.OperationCount -ge 0) { Write-Host ('DACPAC schema operations: ' + $schemaAudit.OperationCount) }
    if ($migrationMatches.Count -gt 0) { Write-Host ('Post-V40 validators matched: V' + (($migrationMatches | Sort-Object) -join ', V')) }
    foreach ($entry in $inventory) { Write-Host ('Tenant-scoped table aggregate: {0}; rows={1}; expected seed={2}; unowned={3}; other tenants={4}' -f $entry.TableName,$entry.TotalRows,$entry.ExpectedSeedRows,$entry.UnownedRows,$entry.OtherTenantRows) }
    Write-Host ('Global seed/business-data audit: ' + $(if ($globalDataBlockers.Count -eq 0) { 'PASS' } else { 'FAIL' }))
    if ($blockers.Count -eq 0) { Write-Host 'Audit result: ELIGIBLE FOR A SEPARATELY AUTHORIZED BASELINE RECONCILIATION. No writes performed.' }
    else { Write-Host 'Audit result: BLOCKED; no baseline metadata or marker may be registered.'; foreach ($item in $blockers) { Write-Host ('Blocker: ' + $item) } }
    return [pscustomobject]@{ Eligible=($blockers.Count -eq 0); Blockers=[string[]]$blockers.ToArray(); BaselineLedgerState=$baselineLedgerState; MigrationLedgerState=$migrationLedgerState; SchemaOperationCount=$schemaAudit.OperationCount; DacpacSha256=$script:HistoricalV40DacpacSha256 }
}

function Initialize-V40Baseline {
    if ((Get-TargetState).UserObjectCount -gt 0) {
        throw 'A non-empty database without a complete migration ledger is ambiguous; no baseline was inferred or recorded.'
    }
    if (-not $targetBuilder.IntegratedSecurity -or -not [string]::IsNullOrWhiteSpace($targetBuilder.UserID) -or -not [string]::IsNullOrWhiteSpace($targetBuilder.Password)) {
        throw 'Fresh DACPAC baseline publishing requires integrated authentication so credentials are not passed to a child process.'
    }
    $historical = Build-HistoricalV40Dacpac
    try {
        Write-Host ("Using historical V40 source {0}; DACPAC SHA-256 {1}" -f $historical.SourceCommit,$historical.Sha256)
        $sqlPackage = (Get-Command SqlPackage -ErrorAction Stop).Source
        $artifactDirectory = Join-Path $PSScriptRoot '.artifacts/integration-initializer'
        New-Item -ItemType Directory -Path $artifactDirectory -Force | Out-Null
        $reportPath = Join-Path $artifactDirectory ('baseline-v40-report-' + $expectedDatabase + '.xml')
        $arguments = @('/Action:DeployReport',"/SourceFile:$($historical.Path)",('/TargetConnectionString:' + $targetBuilder.ConnectionString),"/OutputPath:$reportPath",'/p:BlockOnPossibleDataLoss=True','/p:DropObjectsNotInSource=True')
        $reportOutput = @(& $sqlPackage @arguments 2>&1 | ForEach-Object { [string]$_ })
        if ($LASTEXITCODE -ne 0) { throw ("V40 DACPAC DeployReport failed with exit code $LASTEXITCODE; no publish was attempted. " + (($reportOutput | Select-Object -Last 8) -join ' | ')) }
        Assert-IntegrationDacpacReportSafe -ReportPath $reportPath
        $arguments = @('/Action:Publish',"/SourceFile:$($historical.Path)",('/TargetConnectionString:' + $targetBuilder.ConnectionString),'/p:BlockOnPossibleDataLoss=True','/p:DropObjectsNotInSource=False')
        Assert-MigrationLockHeld
        Close-WorkConnection
        $publishOutput = @(& $sqlPackage @arguments 2>&1 | ForEach-Object { [string]$_ })
        if ($LASTEXITCODE -ne 0) {
            $detail = (($publishOutput | Select-Object -Last 8) -join ' | ')
            throw "V40 DACPAC Publish failed with exit code $LASTEXITCODE; the baseline may be partially published. No retry was attempted. Run Status to reconcile before any further Apply. $detail"
        }
        Refresh-WorkConnection -Operation 'Revalidate approved SQL target after V40 DACPAC Publish'
        Assert-MigrationLockHeld
        Invoke-V40BaselineValidator
        Write-Host 'Canonical historical V40 baseline published to the previously empty approved database and validated.'
    } finally { Remove-HistoricalV40Build $historical }
}

function Register-DisposableAndBaseline {
    $state = Get-TargetState
    if ($state.UnknownTenantKeyCount -gt 0) { throw 'Database contains tenant rows with missing/blank keys; disposable designation refused.' }
    if ($state.NonSeedNonTestTenantCount -gt 0) { throw 'Database contains non-seed tenant rows not identifiable as test/integration data; disposable designation refused.' }
    if ($state.UserObjectCount -gt 0 -and -not $state.TenantTablePresent) { throw 'Existing schema cannot be classified as test-only; disposable designation refused.' }
    if (-not $state.HasBaseline) {
        if ($state.UserObjectCount -eq 0) { Initialize-V40Baseline }
        else { Invoke-V40BaselineValidator }
    }

    $baseline = Get-BaselineRecord
    if ($baseline) {
        if ($baseline.Version -ne $baselineVersion -or $baseline.Validator -cne (Split-Path -Leaf $baselineValidator) -or $baseline.Hash -cne (Get-DatabaseMigrationHash -Path $baselineValidator)) {
            throw 'Recorded V40 baseline metadata/hash mismatch; no migration was attempted.'
        }
    }
    if (-not (Get-TargetState).HasHistory) {
        Invoke-V40BaselineValidator
        Assert-NoUntrackedMigrationArtifacts $allMigrations
        Initialize-BaselineLedger
    } else {
        Invoke-V40BaselineValidator
        if (-not $baseline) { throw 'Migration history exists without a V40 baseline row.' }
    }
}

function Initialize-BaselineLedger {
    Assert-MigrationLockHeld
    $markerPresent = (Get-TargetState).MarkerPresent
    $transaction = $script:Connection.BeginTransaction([System.Data.IsolationLevel]::Serializable)
    try {
        if ([int](Invoke-Scalar -Sql "SELECT CASE WHEN SCHEMA_ID(N'deployment') IS NULL THEN 0 ELSE 1 END;" -Transaction $transaction) -eq 0) {
            Invoke-SqlText -Text "CREATE SCHEMA deployment AUTHORIZATION dbo;" -Transaction $transaction -Label 'V40 ledger schema creation'
        }
        $ledgerSql = @"
CREATE TABLE deployment.DatabaseMigrationBaseline(
 SingletonId tinyint NOT NULL CONSTRAINT PK_DatabaseMigrationBaseline PRIMARY KEY CONSTRAINT CK_DatabaseMigrationBaseline_Singleton CHECK(SingletonId=1),
 BaselineVersion int NOT NULL,BaselineName nvarchar(100) NOT NULL,BaselineValidatorFile nvarchar(260) NOT NULL,BaselineValidatorHash char(64) NOT NULL,
 InitializedAt datetimeoffset NOT NULL CONSTRAINT DF_DatabaseMigrationBaseline_InitializedAt DEFAULT(SYSUTCDATETIME()),InitializedBy nvarchar(256) NOT NULL);
CREATE TABLE deployment.DatabaseMigrationHistory(
 MigrationVersion int NOT NULL CONSTRAINT PK_DatabaseMigrationHistory PRIMARY KEY,MigrationName nvarchar(260) NOT NULL CONSTRAINT UQ_DatabaseMigrationHistory_Name UNIQUE,
 ScriptHash char(64) NOT NULL,ValidatorHash char(64) NULL,AppliedAt datetimeoffset NOT NULL CONSTRAINT DF_DatabaseMigrationHistory_AppliedAt DEFAULT(SYSUTCDATETIME()),
 AppliedBy nvarchar(256) NOT NULL,IsAdopted bit NOT NULL CONSTRAINT DF_DatabaseMigrationHistory_IsAdopted DEFAULT(0),BackupPath nvarchar(4000) NULL);
"@
        Invoke-SqlText -Text $ledgerSql -Transaction $transaction -Label 'V40 migration ledger initialization'
        $insert = $script:Connection.CreateCommand()
        $insert.Transaction = $transaction
        $insert.CommandText = 'INSERT deployment.DatabaseMigrationBaseline(SingletonId,BaselineVersion,BaselineName,BaselineValidatorFile,BaselineValidatorHash,InitializedBy) VALUES(1,40,@baselineName,N''Baseline-V40.validate.sql'',@hash,@actor);'
        $null = $insert.Parameters.Add('@baselineName',[System.Data.SqlDbType]::NVarChar,100);$insert.Parameters['@baselineName'].Value = "Historical V40 source $script:HistoricalV40SourceCommit"
        $null = $insert.Parameters.Add('@hash',[System.Data.SqlDbType]::Char,64);$insert.Parameters['@hash'].Value = Get-DatabaseMigrationHash -Path $baselineValidator
        $null = $insert.Parameters.Add('@actor',[System.Data.SqlDbType]::NVarChar,256);$insert.Parameters['@actor'].Value = $actor
        try { $null = $insert.ExecuteNonQuery() } finally { $insert.Dispose() }
        if (-not $markerPresent) {
            Invoke-SqlText -Text "EXEC sys.sp_addextendedproperty @name=N'$script:DisposableMarkerName',@value=N'$script:DisposableMarkerValue';" -Transaction $transaction -Label 'Disposable database designation'
        }
        Assert-MigrationLockHeld
        $transaction.Commit()
    } catch { try { $transaction.Rollback() } catch {}; throw } finally { $transaction.Dispose() }
}

function Ensure-DisposableMarker {
    if ((Get-TargetState).MarkerPresent) { return }
    Assert-MigrationLockHeld
    $transaction = $script:Connection.BeginTransaction([System.Data.IsolationLevel]::Serializable)
    try {
        Invoke-SqlText -Text "EXEC sys.sp_addextendedproperty @name=N'$script:DisposableMarkerName',@value=N'$script:DisposableMarkerValue';" -Transaction $transaction -Label 'Disposable database designation'
        Assert-MigrationLockHeld
        $transaction.Commit()
    } catch { try { $transaction.Rollback() } catch {}; throw } finally { $transaction.Dispose() }
}

function Get-V82Prerequisites {
    $command = $script:Connection.CreateCommand()
    $command.CommandText = @"
SELECT N'core.Tenants' WHERE OBJECT_ID(N'core.Tenants',N'U') IS NULL
UNION ALL SELECT N'core.Features' WHERE OBJECT_ID(N'core.Features',N'U') IS NULL
UNION ALL SELECT N'core.Plans' WHERE OBJECT_ID(N'core.Plans',N'U') IS NULL
UNION ALL SELECT N'core.PlanFeatures' WHERE OBJECT_ID(N'core.PlanFeatures',N'U') IS NULL
UNION ALL SELECT N'core.TenantFeatures' WHERE OBJECT_ID(N'core.TenantFeatures',N'U') IS NULL
UNION ALL SELECT N'commerce.AnalyticsEvents' WHERE OBJECT_ID(N'commerce.AnalyticsEvents',N'U') IS NULL
UNION ALL SELECT N'commerce.ProductSearchEvents' WHERE OBJECT_ID(N'commerce.ProductSearchEvents',N'U') IS NULL
UNION ALL SELECT N'commerce.MissingProductRequests' WHERE OBJECT_ID(N'commerce.MissingProductRequests',N'U') IS NULL
UNION ALL SELECT N'commerce.AnalyticsEvents.CreatedOn' WHERE COL_LENGTH(N'commerce.AnalyticsEvents',N'CreatedOn') IS NULL
UNION ALL SELECT N'Parent ANALYTICS feature' WHERE NOT EXISTS(SELECT 1 FROM core.Features WHERE FeatureKey=N'ANALYTICS');
"@
    try {
        $reader = $command.ExecuteReader()
        $missing = [System.Collections.Generic.List[string]]::new()
        try { while ($reader.Read()) { $missing.Add($reader.GetString(0)) } } finally { $reader.Dispose() }
        return [string[]]$missing.ToArray()
    } finally { $command.Dispose() }
}

function Add-MigrationHistory($Migration,[System.Data.SqlClient.SqlTransaction]$Transaction) {
    Assert-MigrationLockHeld
    $command = $script:Connection.CreateCommand()
    $command.Transaction = $Transaction
    $command.CommandText = 'INSERT deployment.DatabaseMigrationHistory(MigrationVersion,MigrationName,ScriptHash,ValidatorHash,AppliedBy,IsAdopted) VALUES(@version,@name,@hash,@validatorHash,@actor,0);'
    foreach ($entry in @(
        @('@version',[System.Data.SqlDbType]::Int,[int]$Migration.Version,0),
        @('@name',[System.Data.SqlDbType]::NVarChar,[string]$Migration.FileName,260),
        @('@hash',[System.Data.SqlDbType]::Char,[string]$Migration.Hash,64),
        @('@validatorHash',[System.Data.SqlDbType]::Char,[string]$Migration.ValidatorHash,64),
        @('@actor',[System.Data.SqlDbType]::NVarChar,$actor,256)
    )) {
        $parameter = if ($entry[1] -eq [System.Data.SqlDbType]::Int) { $command.Parameters.Add($entry[0],$entry[1]) } else { $command.Parameters.Add($entry[0],$entry[1],[int]$entry[3]) }
        $parameter.Value = $entry[2]
    }
    try { $null = $command.ExecuteNonQuery() } finally { $command.Dispose() }
}

function Start-Migration($Migration) {
    Assert-MigrationLockHeld
    $script:MigrationTransaction = $script:Connection.BeginTransaction([System.Data.IsolationLevel]::Serializable)
    Invoke-SqlText -Text ([IO.File]::ReadAllText($Migration.Path)) -Transaction $script:MigrationTransaction -Label $Migration.FileName
}

function Validate-Migration($Migration) {
    Invoke-Validator -Path $Migration.ValidatorPath -Transaction $script:MigrationTransaction
}

function Record-Migration($Migration) {
    try {
        Assert-MigrationLockHeld
        Add-MigrationHistory -Migration $Migration -Transaction $script:MigrationTransaction
        $script:MigrationTransaction.Commit()
        Write-Host "Applied and validated V$($Migration.Version)."
    } catch {
        $failure = Format-IntegrationSqlFailure -Operation "Commit migration V$($Migration.Version) and record its validated ledger row" -Exception $_.Exception
        try { $script:MigrationTransaction.Rollback() } catch { }
        throw "$failure Commit outcome may be unknown; no retry was attempted. Run Status and reconcile the migration ledger before resuming."
    } finally {
        $script:MigrationTransaction.Dispose()
        $script:MigrationTransaction = $null
    }
}

function Assert-DatabaseControlPermission {
    if (-not (Test-DatabaseControlPermission)) { throw 'Apply requires CONTROL-equivalent permission scoped to the approved database; no write was attempted.' }
}

function Test-DatabaseControlPermission {
    $allowed = [int](Invoke-Scalar -Sql "SELECT COALESCE(CONVERT(int,HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CONTROL')),0);" -Operation 'Read database-level schema migration permission')
    return $allowed -eq 1
}

function Acquire-MigrationLock {
    Assert-IntegrationConnectionUsable -State $script:LockConnection.State.ToString() -Operation 'Acquire integration initializer lock'
    $command = $script:LockConnection.CreateCommand()
    $resource = "WhatsBiz.IntegrationDatabaseInitializer:$expectedServer/$expectedDatabase"
    $command.CommandText = "DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=@resource,@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0,@DbPrincipal=N'public'; SELECT @result,@@SPID,DB_NAME();"
    $null = $command.Parameters.Add('@resource',[System.Data.SqlDbType]::NVarChar,255)
    $command.Parameters['@resource'].Value = $resource
    try {
        $reader = $command.ExecuteReader()
        try {
            if (-not $reader.Read()) { throw 'Lock acquisition returned no row.' }
            $result = [int]$reader.GetValue(0)
            $script:LockSessionId = [int]$reader.GetValue(1)
            if ($reader.GetString(2) -cne 'master') { throw 'Initializer lock was not acquired in master.' }
        } finally { $reader.Dispose() }
        if ($result -lt 0) { throw 'Another integration schema initializer holds the database lock.' }
    } catch { throw (Format-IntegrationSqlFailure -Operation 'Acquire integration initializer application lock' -Exception $_.Exception) } finally { $command.Dispose() }
}

function Assert-MigrationLockHeld {
    Assert-IntegrationConnectionUsable -State $script:LockConnection.State.ToString() -Operation 'Verify integration initializer lock after external SQL operation'
    $command = $script:LockConnection.CreateCommand()
    $resource = "WhatsBiz.IntegrationDatabaseInitializer:$expectedServer/$expectedDatabase"
    $command.CommandText = "SELECT @@SPID,DB_NAME(),APPLOCK_MODE(N'public',@resource,N'Session');"
    $null = $command.Parameters.Add('@resource',[System.Data.SqlDbType]::NVarChar,255)
    $command.Parameters['@resource'].Value = $resource
    try {
        $reader = $command.ExecuteReader()
        try {
            if (-not $reader.Read()) { throw 'Initializer lock verification returned no row.' }
            $sessionId = [int]$reader.GetValue(0)
            $databaseName = $reader.GetString(1)
            $mode = if ($reader.IsDBNull(2)) { '' } else { $reader.GetString(2) }
        } finally { $reader.Dispose() }
        if ($sessionId -ne $script:LockSessionId -or $databaseName -cne 'master' -or $mode -cne 'Exclusive') {
            throw 'Initializer lock ownership changed or was lost; all further database writes are refused.'
        }
    } catch { throw (Format-IntegrationSqlFailure -Operation 'Verify integration initializer lock' -Exception $_.Exception) } finally { $command.Dispose() }
}

try {
    if (-not (Ensure-ApprovedTargetDatabase)) { return }
    $script:Connection = Open-TargetConnection
    $identity = Assert-LiveTarget $script:Connection
    if ($Mode -in @('Status','DryRun')) {
        $schemaPermission = if (Test-DatabaseControlPermission) { 'database-level CONTROL available for schema migration' } else { 'database-level CONTROL is missing; provisioning access alone does not authorize schema migrations' }
        Write-Host ('Schema migration permission: ' + $schemaPermission)
    }
    $allMigrations = @(Get-CanonicalMigrations)
    $targetState = Get-TargetState
    if ($targetState.LedgerPartial -and $Mode -ne 'AuditBaseline') { throw 'Schema state PARTIAL_MIGRATION_LEDGER: only one V40 ledger table exists; read-only reconciliation is required.' }
    if ($targetState.MarkerPresent -and -not $targetState.TenantTablePresent -and $targetState.UserObjectCount -gt 0 -and $Mode -ne 'AuditBaseline') {
        throw 'Schema state PARTIAL_V40_INITIALIZATION: disposable marker exists but canonical tenant schema is absent.'
    }
    if ($targetState.MarkerPresent -and -not $targetState.HasBaseline -and $Mode -ne 'AuditBaseline') {
        throw 'Schema state PARTIAL_V40_INITIALIZATION: disposable marker exists without the V40 baseline ledger.'
    }
    if ($Mode -eq 'Apply' -and $targetState.UnknownTenantKeyCount -gt 0) {
        throw 'Target contains tenant rows with missing/blank keys; disposable approval is refused.'
    }
    if ($Mode -eq 'Apply' -and $targetState.NonSeedNonTestTenantCount -gt 0) {
        throw 'Target contains non-seed tenant rows not classified as test/integration data; disposable approval is refused.'
    }
    Assert-IntegrationTargetClassified -Mode $Mode -UserObjectCount $targetState.UserObjectCount -MarkerPresent $targetState.MarkerPresent

    $baselineReadError = $null
    try { $baseline = Get-BaselineRecord }
    catch {
        if ($Mode -ne 'AuditBaseline') { throw }
        $baseline = $null
        $baselineReadError = 'Baseline ledger row could not be read consistently.'
    }
    $baselineValidated = $false
    if ($targetState.HasBaseline) {
        if ((-not $baseline -or $baseline.Version -ne $baselineVersion -or $baseline.Validator -cne (Split-Path -Leaf $baselineValidator) -or $baseline.Hash -cne (Get-DatabaseMigrationHash -Path $baselineValidator)) -and $Mode -ne 'AuditBaseline') {
            throw 'V40 baseline metadata or validator hash is invalid.'
        }
        try { Invoke-V40BaselineValidator; $baselineValidated = $true }
        catch { if ($Mode -ne 'AuditBaseline') { throw }; $baselineValidated = $false }
    } elseif ($targetState.CanonicalBaselineFootprint -and -not $targetState.HasHistory) {
        try {
            Invoke-V40BaselineValidator
            $baselineValidated = $true
        } catch {
            if ($script:Connection.State -ne [System.Data.ConnectionState]::Open) {
                throw (Format-IntegrationSqlFailure -Operation 'Classify unledgered V40 baseline schema' -Exception $_.Exception)
            }
            $baselineValidated = $false
        }
    } elseif ($targetState.HasHistory -and $Mode -ne 'AuditBaseline') {
        throw 'Migration history exists without a V40 baseline; no migration was attempted.'
    }

    if ($targetState.HasHistory) { $history = Get-MigrationHistory }
    else { $history = [object[]]@() }
    if ($Mode -eq 'RecoverBaseline' -and $targetState.HasBaseline -and $targetState.HasHistory -and $targetState.MarkerPresent -and $baselineValidated -and $history.Count -eq 0) {
        Assert-NoUntrackedMigrationArtifacts $allMigrations
        $idempotentInventoryJson = [string](Invoke-Scalar -Sql (Get-IntegrationTenantScopedInventorySql) -Operation 'Re-audit tenant data for idempotent recovery')
        $idempotentInventory = ConvertFrom-IntegrationTenantScopedInventoryJson -Json $idempotentInventoryJson
        $idempotentBlockers = @((Test-IntegrationTenantScopedInventory -Inventory $idempotentInventory) + (Get-V40GlobalDataAudit))
        if ($idempotentBlockers.Count -gt 0) { throw 'RecoverBaseline found canonical metadata but the V40 data audit is no longer clean: ' + ($idempotentBlockers -join ' | ') }
        Write-Host 'RecoverBaseline is already complete and canonical; idempotent verification performed no writes and did not apply V41+ migrations.'
        return
    }
    try { $plan = @(Get-DatabaseMigrationPlan -Migrations $allMigrations -BaselineVersion $baselineVersion -History $history) }
    catch { throw "Schema state PARTIAL_OR_INCONSISTENT_MIGRATION_SEQUENCE: ledger or checksum validation failed; no migration was attempted. $($_.Exception.Message)" }
    foreach ($record in $history) {
        $migration = $allMigrations | Where-Object { $_.Version -eq $record.MigrationVersion } | Select-Object -First 1
        if (-not $migration) { throw "Applied V$($record.MigrationVersion) is not present in the canonical migration catalog." }
        try { Invoke-Validator -Path $migration.ValidatorPath }
        catch { throw "Schema state PARTIAL_OR_INCONSISTENT_MIGRATION_SEQUENCE: recorded V$($record.MigrationVersion) validator failed. $($_.Exception.Message)" }
    }
    $pending = @($plan | Where-Object Status -eq 'PENDING' | Sort-Object Version)
    $schemaState = Get-IntegrationSchemaStateLabel -UserObjectCount $targetState.UserObjectCount -HasBaseline $targetState.HasBaseline -HasHistory $targetState.HasHistory -MarkerPresent $targetState.MarkerPresent -CanonicalBaselineFootprint $targetState.CanonicalBaselineFootprint -BaselineValidated $baselineValidated
    $tenantDataSummary = '{0}; total rows={1}; canonical DEFAULT seed rows={2}; unknown/blank key rows={3}; non-test key rows={4}; test/integration-pattern rows={5}' -f $targetState.TenantDataClassification.State,$targetState.TenantDataClassification.TenantCount,$targetState.TenantDataClassification.CanonicalDefaultSeedCount,$targetState.TenantDataClassification.UnknownKeyCount,$targetState.TenantDataClassification.NonTestKeyCount,$targetState.TenantDataClassification.TestKeyCount
    $baselineLabel = if ($targetState.HasBaseline) { 'validated' } elseif ($targetState.UserObjectCount -eq 0) { 'not installed; empty database' } else { 'not established; schema requires review' }
    $markerLabel = if ($targetState.MarkerPresent) { 'present' } else { 'not yet registered' }
    $identity | Format-List
    if ($modeDisposition -eq 'ReadOnly') {
        $statusLines = Get-IntegrationStatusLines -Mode $Mode -BaselineLabel $baselineLabel -MarkerLabel $markerLabel -SchemaState $schemaState -TenantDataSummary $tenantDataSummary -ThroughVersion $throughVersion -Plan $plan
        foreach ($line in $statusLines) { Write-Host $line }
    } elseif ($modeDisposition -eq 'ReadOnlyAudit') {
        Write-Host ('Mode: ' + $Mode)
        Write-Host ('V40 baseline: ' + $baselineLabel)
        Write-Host ('Disposable marker: ' + $markerLabel)
        Write-Host ('Schema state: ' + $schemaState)
        Write-Host ('Tenant data: ' + $tenantDataSummary)
        Write-Host ("V41-V$throughVersion applied: " + $history.Count + '; pending: ' + $pending.Count)
    } else {
        Write-Host ('Mode: ' + $Mode)
        Write-Host ('V40 baseline: ' + $baselineLabel)
        Write-Host ('Disposable marker: ' + $markerLabel)
        Write-Host ('Schema state: ' + $schemaState)
        Write-Host ('Tenant data: ' + $tenantDataSummary)
        Write-Host ("Canonical migrations V41 to V$throughVersion pending: " + $pending.Count)
        if ($pending.Count -gt 0) { Write-Host ('Pending versions: ' + (($pending | ForEach-Object { $_.Version }) -join ', ')) }
    }

    if ($Mode -eq 'Status') { return }
    if ($Mode -eq 'AuditBaseline') {
        if ($baselineReadError) { Write-Host ('Ledger read issue: ' + $baselineReadError) }
        $null = Invoke-ReadOnlyV40BaselineAudit -TargetState $targetState -BaselineRecord $baseline -BaselineValidated $baselineValidated -History $history -Migrations $allMigrations
        return
    }
    if ($Mode -eq 'DryRun') {
        if ($targetState.UserObjectCount -gt 0 -and -not $targetState.HasBaseline) { Write-Host 'DryRun: existing unledgered schema is ambiguous and cannot be adopted automatically.' }
        if (-not $targetState.MarkerPresent) { Write-Host 'DryRun: Apply will require the exact typed target and test-only tenant classification.' }
        Write-Host 'DryRun: no database writes performed.'
        return
    }

    if ($Mode -eq 'RecoverBaseline') {
        Write-Host 'RecoverBaseline phase 1: running read-only diagnostics before acquiring any write authority.'
        $audit = Invoke-ReadOnlyV40BaselineAudit -TargetState $targetState -BaselineRecord $baseline -BaselineValidated $baselineValidated -History $history -Migrations $allMigrations
        if (-not $audit.Eligible) { throw 'RecoverBaseline blocked by the read-only audit; no database writes were attempted.' }

        Refresh-WorkConnection -Operation 'Revalidate approved SQL target before RecoverBaseline lock'
        $script:LockConnection = Open-LockConnection
        Assert-DatabaseControlPermission
        Acquire-MigrationLock
        Assert-MigrationLockHeld
        $targetState = Get-TargetState -Operation 'Revalidate recovery target after acquiring exclusive lock'
        $baseline = Get-BaselineRecord
        $history = if ($targetState.HasHistory) { Get-MigrationHistory } else { [object[]]@() }
        $baselineValidated = $false
        try { Invoke-V40BaselineValidator; $baselineValidated = $true } catch { throw 'RecoverBaseline V40 validator failed during the locked re-audit. ' + $_.Exception.Message }
        Write-Host 'RecoverBaseline phase 2: repeating every recovery prerequisite while the exclusive lock is held.'
        $lockedAudit = Invoke-ReadOnlyV40BaselineAudit -TargetState $targetState -BaselineRecord $baseline -BaselineValidated $baselineValidated -History $history -Migrations $allMigrations
        if (-not $lockedAudit.Eligible) { throw 'RecoverBaseline state changed or failed its locked re-audit; no metadata was registered.' }
        Assert-MigrationLockHeld
        Initialize-BaselineLedger
        Assert-MigrationLockHeld
        $registeredState = Get-TargetState -Operation 'Verify recovered V40 baseline registration'
        $registeredBaseline = Get-BaselineRecord
        if (-not $registeredState.HasBaseline -or -not $registeredState.HasHistory -or -not $registeredState.MarkerPresent -or
            -not $registeredBaseline -or $registeredBaseline.Version -ne $baselineVersion -or
            $registeredBaseline.Validator -cne (Split-Path -Leaf $baselineValidator) -or
            $registeredBaseline.Hash -cne (Get-DatabaseMigrationHash -Path $baselineValidator) -or (Get-MigrationHistory).Count -ne 0) {
            throw 'RecoverBaseline registration verification failed; transaction outcome requires read-only Status review before any retry.'
        }
        Write-Host 'RecoverBaseline succeeded: canonical V40 metadata and disposable marker were atomically registered. V41+ migrations were not applied.'
        return
    }

    Refresh-WorkConnection -Operation 'Revalidate approved SQL target before Apply preflight'
    $script:LockConnection = Open-LockConnection
    if ($targetState.NonSeedNonTestTenantCount -gt 0) { throw 'Apply refused because the database contains non-seed tenant rows not classified as test/integration data.' }
    Assert-DatabaseControlPermission
    Acquire-MigrationLock
    $targetState = Get-TargetState -Operation 'Revalidate schema and ledger after acquiring Apply lock'
    if ($targetState.LedgerPartial) { throw 'Apply stopped: schema state PARTIAL_MIGRATION_LEDGER; reconcile with Status before resuming.' }
    Assert-IntegrationTargetClassified -Mode $Mode -UserObjectCount $targetState.UserObjectCount -MarkerPresent $targetState.MarkerPresent
    if ($targetState.UnknownTenantKeyCount -gt 0) { throw 'Apply refused because tenant rows contain missing/blank keys.' }
    if ($targetState.NonSeedNonTestTenantCount -gt 0) { throw 'Apply refused because the database contains non-seed tenant rows not classified as test/integration data.' }
    if ($targetState.UserObjectCount -eq 0 -and -not $targetState.HasBaseline) { Initialize-V40Baseline }
    Register-DisposableAndBaseline
    if (-not (Get-TargetState).HasBaseline) { throw 'V40 baseline could not be established.' }
    Ensure-DisposableMarker
    $history = Get-MigrationHistory
    $plan = @(Get-DatabaseMigrationPlan -Migrations $allMigrations -BaselineVersion $baselineVersion -History $history)
    $pending = @($plan | Where-Object Status -eq 'PENDING' | Sort-Object Version)
    $null = Assert-DatabaseMigrationPreflight -Migrations $allMigrations -Pending $pending -BaselineVersion $baselineVersion
    try {
        Invoke-DatabaseMigrationWorkflow -Pending $pending -Mode Deploy -EnsureBackup { } `
            -ExecuteMigration { param($migration) Start-Migration $migration } `
            -ValidateMigration { param($migration) Validate-Migration $migration } `
            -RecordMigration { param($migration) Record-Migration $migration }
    } catch {
        if ($script:MigrationTransaction) { try { $script:MigrationTransaction.Rollback() } catch {}; $script:MigrationTransaction.Dispose(); $script:MigrationTransaction = $null }
        throw
    }
    $null = Assert-LiveTarget $script:Connection
    $missingPrerequisites = @(Get-V82Prerequisites)
    if ($missingPrerequisites.Count -gt 0) { throw ('Guest analytics prerequisites are missing after the canonical migration sequence: ' + ($missingPrerequisites -join ', ')) }
    Write-Host ("Canonical schema and migrations V41 to V$throughVersion are valid.")
} finally {
    if ($script:Connection) { $script:Connection.Dispose() }
    if ($script:LockConnection) { $script:LockConnection.Dispose() }
}
