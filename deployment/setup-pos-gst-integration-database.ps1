[CmdletBinding()]
param(
    [string] $Server = 'DESKTOP-DQ0868S',
    [string] $Database = 'WhatsBizERP_PosGstIntegrationTest',
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$expectedDatabase = 'WhatsBizERP_PosGstIntegrationTest'
$expectedServer = 'DESKTOP-DQ0868S'
$forbidden = @('WhatsBizERP', 'WhatsBizERP_QA', 'WhatsBizERP_PROD')
if ($Database -cne $expectedDatabase) { throw "This helper is restricted to $expectedDatabase." }
if ($forbidden -contains $Database) { throw 'Refusing to operate on a protected database.' }

$root = Split-Path -Parent $PSScriptRoot
$migrationDirectory = Join-Path $root 'database/migrations'
$migrationCore = Join-Path $PSScriptRoot 'DatabaseMigration.Core.psm1'
$baselineValidator = Join-Path $migrationDirectory 'validators/Baseline-V40.validate.sql'
$project = Join-Path $root 'database/WhatsBiz.Database/WhatsBiz.Database.sqlproj'
$dacpac = Join-Path $root "database/WhatsBiz.Database/bin/$Configuration/WhatsBiz.Database.dacpac"
$v64 = Join-Path $root 'database/migrations/V64-ProductGstPricingType.sql'
if (-not (Test-Path -LiteralPath $v64)) { throw 'V64 migration artifact is missing.' }
if (-not (Test-Path -LiteralPath $baselineValidator)) { throw 'V40 baseline validator is missing.' }
$sqlPackage = (Get-Command SqlPackage -ErrorAction Stop).Source
$sqlCmd = (Get-Command sqlcmd -ErrorAction Stop).Source
$reportDirectory = Join-Path $root 'deployment/.artifacts/pos-gst-integration'
$baselineVersion = 40
$actor = [Environment]::UserDomainName + '\' + [Environment]::UserName
Import-Module $migrationCore -Force

function Invoke-Sql([string] $targetDatabase, [string] $query) {
    & $sqlCmd -S $Server -d $targetDatabase -E -C -b -Q $query
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd failed for the approved target database." }
}

function Get-SqlValue([string] $targetDatabase, [string] $query) {
    $raw = @(& $sqlCmd -S $Server -d $targetDatabase -E -C -b -h -1 -W -Q $query 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) { throw "sqlcmd query failed for the approved target database with exit code $exitCode." }
    $values = @($raw | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ })
    if ($values.Count -ne 1) { throw 'Expected one machine-readable SQL result row.' }
    return $values[0]
}

function Get-SqlConnection([string] $targetDatabase) {
    $connection = [System.Data.SqlClient.SqlConnection]::new("Server=$Server;Database=$targetDatabase;Integrated Security=True;Encrypt=True;TrustServerCertificate=True")
    $connection.Open()
    return $connection
}

function Invoke-SqlBatches([string] $text, [System.Data.SqlClient.SqlConnection] $connection, [System.Data.SqlClient.SqlTransaction] $transaction, [string] $label) {
    if ($text -match '(?im)^\s*:\s*(r|setvar)\b') { throw "$label contains unsupported SQLCMD directives." }
    foreach ($batch in @([regex]::Split($text, '(?im)^\s*GO\s*(?:--.*)?$') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 0
        $command.CommandText = $batch
        $command.Transaction = $transaction
        try { $null = $command.ExecuteNonQuery() } finally { $command.Dispose() }
    }
}

function Get-MigrationState([System.Data.SqlClient.SqlConnection] $connection) {
    $command = $connection.CreateCommand()
    $command.CommandText = @"
SELECT
    CASE WHEN OBJECT_ID(N'deployment.DatabaseMigrationHistory', N'U') IS NULL THEN 0 ELSE 1 END,
    CASE WHEN OBJECT_ID(N'deployment.DatabaseMigrationBaseline', N'U') IS NULL THEN 0 ELSE 1 END,
    COALESCE((SELECT COUNT(*) FROM sys.objects WHERE type = 'U'), 0),
    CASE WHEN OBJECT_ID(N'master.Products', N'U') IS NULL THEN 0 ELSE 1 END,
    CASE WHEN OBJECT_ID(N'sales.Customers', N'U') IS NULL THEN 0 ELSE 1 END,
    CASE WHEN COL_LENGTH(N'sales.Customers', N'MobileNormalized') IS NULL THEN 0 ELSE 1 END;
"@
    try {
        $reader = $command.ExecuteReader()
        try {
            if (-not $reader.Read()) { throw 'Could not read isolated database schema state.' }
            return [pscustomobject]@{
                HistoryExists = $reader.GetInt32(0) -eq 1
                BaselineExists = $reader.GetInt32(1) -eq 1
                UserTableCount = $reader.GetInt32(2)
                ProductsExists = $reader.GetInt32(3) -eq 1
                CustomersExists = $reader.GetInt32(4) -eq 1
                MobileNormalizedExists = $reader.GetInt32(5) -eq 1
            }
        } finally { $reader.Dispose() }
    } finally { $command.Dispose() }
}

function Invoke-ReadOnlyBaselineValidation([System.Data.SqlClient.SqlConnection] $connection) {
    $text = Get-Content -LiteralPath $baselineValidator -Raw
    Invoke-SqlBatches $text $connection $null 'Baseline-V40 validator'
}

function Get-IsolatedValidatorText([string] $path) {
    $lines = [IO.File]::ReadAllLines($path)
    $kept = [System.Collections.Generic.List[string]]::new()
    $skipEnvironmentGuard = $false
    foreach ($line in $lines) {
        if ($line -match '(?i)\bIF\s+DB_NAME\(\)\s+NOT\s+IN\s*\(') {
            $skipEnvironmentGuard = $true
            if ($line -match '(?i)\bTHROW\b.*;\s*$') { $skipEnvironmentGuard = $false }
            continue
        }
        if ($skipEnvironmentGuard) {
            if ($line -match '(?i)\bTHROW\b.*;\s*$') { $skipEnvironmentGuard = $false }
            continue
        }
        $kept.Add($line)
    }
    if ($skipEnvironmentGuard) { throw "Could not safely isolate the database-name guard in validator '$path'." }
    return ($kept -join [Environment]::NewLine)
}

function Invoke-IsolatedValidator([System.Data.SqlClient.SqlConnection] $connection, [string] $path) {
    Invoke-SqlBatches (Get-IsolatedValidatorText $path) $connection $null (Split-Path -Leaf $path)
}

function Get-UntrackedMigrationValidation([System.Data.SqlClient.SqlConnection] $connection) {
    $catalog = @(Get-DatabaseMigrationCatalog -Directory $migrationDirectory | Where-Object Version -gt $baselineVersion | Sort-Object Version)
    $passed = [System.Collections.Generic.List[object]]::new()
    $failed = [System.Collections.Generic.List[object]]::new()
    foreach ($migration in $catalog) {
        if ($null -eq $migration.ValidatorPath) { throw "V$($migration.Version) has no read-only validator." }
        try {
            Invoke-IsolatedValidator $connection $migration.ValidatorPath
            $passed.Add($migration)
        } catch {
            $failed.Add($migration)
        }
    }
    [pscustomobject]@{ Passed = @($passed.ToArray()); Failed = @($failed.ToArray()) }
}

function Initialize-MigrationLedger([System.Data.SqlClient.SqlConnection] $connection) {
    $transaction = $connection.BeginTransaction([System.Data.IsolationLevel]::Serializable)
    try {
        $sql = @"
IF SCHEMA_ID(N'deployment') IS NULL EXEC(N'CREATE SCHEMA deployment AUTHORIZATION dbo');
CREATE TABLE deployment.DatabaseMigrationBaseline(
    SingletonId tinyint NOT NULL CONSTRAINT PK_DatabaseMigrationBaseline PRIMARY KEY
        CONSTRAINT CK_DatabaseMigrationBaseline_Singleton CHECK(SingletonId = 1),
    BaselineVersion int NOT NULL,
    BaselineName nvarchar(100) NOT NULL,
    BaselineValidatorFile nvarchar(260) NOT NULL,
    BaselineValidatorHash char(64) NOT NULL,
    InitializedAt datetimeoffset NOT NULL CONSTRAINT DF_DatabaseMigrationBaseline_InitializedAt DEFAULT(SYSUTCDATETIME()),
    InitializedBy nvarchar(256) NOT NULL
);
CREATE TABLE deployment.DatabaseMigrationHistory(
    MigrationVersion int NOT NULL CONSTRAINT PK_DatabaseMigrationHistory PRIMARY KEY,
    MigrationName nvarchar(260) NOT NULL CONSTRAINT UQ_DatabaseMigrationHistory_Name UNIQUE,
    ScriptHash char(64) NOT NULL,
    ValidatorHash char(64) NULL,
    AppliedAt datetimeoffset NOT NULL CONSTRAINT DF_DatabaseMigrationHistory_AppliedAt DEFAULT(SYSUTCDATETIME()),
    AppliedBy nvarchar(256) NOT NULL,
    IsAdopted bit NOT NULL CONSTRAINT DF_DatabaseMigrationHistory_IsAdopted DEFAULT(0),
    BackupPath nvarchar(4000) NULL
);
INSERT deployment.DatabaseMigrationBaseline(SingletonId, BaselineVersion, BaselineName, BaselineValidatorFile, BaselineValidatorHash, InitializedBy)
VALUES(1, @baselineVersion, N'Historical V40 schema', @validatorFile, @validatorHash, @actor);
"@
        $command = $connection.CreateCommand(); $command.Transaction = $transaction; $command.CommandText = $sql
        foreach ($pair in @(
            @('@baselineVersion', [System.Data.SqlDbType]::Int, $baselineVersion),
            @('@validatorFile', [System.Data.SqlDbType]::NVarChar, (Split-Path -Leaf $baselineValidator)),
            @('@validatorHash', [System.Data.SqlDbType]::Char, (Get-DatabaseMigrationHash $baselineValidator)),
            @('@actor', [System.Data.SqlDbType]::NVarChar, $actor)
        )) {
            $parameter = $command.Parameters.Add($pair[0], $pair[1])
            $parameter.Value = $pair[2]
        }
        try { $null = $command.ExecuteNonQuery() } finally { $command.Dispose() }
        $transaction.Commit()
    } catch {
        try { $transaction.Rollback() } catch {}
        throw
    } finally { $transaction.Dispose() }
}

function Get-RecordedMigrations([System.Data.SqlClient.SqlConnection] $connection) {
    $command = $connection.CreateCommand()
    $command.CommandText = 'SELECT MigrationVersion,MigrationName,ScriptHash,ValidatorHash FROM deployment.DatabaseMigrationHistory ORDER BY MigrationVersion;'
    $rows = [System.Collections.Generic.List[object]]::new()
    try {
        $reader = $command.ExecuteReader()
        try {
            while ($reader.Read()) {
                $rows.Add([pscustomobject]@{
                    MigrationVersion = $reader.GetInt32(0)
                    MigrationName = $reader.GetString(1)
                    ScriptHash = $reader.GetString(2)
                    ValidatorHash = if ($reader.IsDBNull(3)) { $null } else { $reader.GetString(3) }
                })
            }
        } finally { $reader.Dispose() }
    } finally { $command.Dispose() }
    return @($rows.ToArray())
}

function Apply-MissingMigrations([System.Data.SqlClient.SqlConnection] $connection) {
    $catalog = @(Get-DatabaseMigrationCatalog -Directory $migrationDirectory)
    Assert-DatabaseMigrationSequence -Migrations $catalog -BaselineVersion $baselineVersion
    $recorded = @(Get-RecordedMigrations $connection)
    $recordedVersions = @{}
    $expected = $baselineVersion + 1
    foreach ($row in $recorded) {
        if ($row.MigrationVersion -ne $expected) { throw "Migration history is inconsistent: expected V$expected but found V$($row.MigrationVersion)." }
        $migration = $catalog | Where-Object Version -eq $row.MigrationVersion
        if ($null -eq $migration -or $migration.FileName -cne $row.MigrationName -or $migration.Hash -cne $row.ScriptHash -or $migration.ValidatorHash -cne $row.ValidatorHash) {
            throw "Migration history integrity check failed for V$($row.MigrationVersion)."
        }
        if ($null -eq $migration.ValidatorPath) { throw "Recorded V$($row.MigrationVersion) has no read-only validator in the repository." }
        Invoke-IsolatedValidator $connection $migration.ValidatorPath
        $recordedVersions[$row.MigrationVersion] = $true
        $expected++
    }
    foreach ($migration in ($catalog | Where-Object Version -gt $baselineVersion | Sort-Object Version)) {
        if ($recordedVersions.ContainsKey($migration.Version)) { continue }
        if ($null -eq $migration.ValidatorPath) { throw "V$($migration.Version) has no read-only validator." }
        $transaction = $connection.BeginTransaction([System.Data.IsolationLevel]::Serializable)
        try {
            Invoke-SqlBatches (Get-Content -LiteralPath $migration.Path -Raw) $connection $transaction $migration.FileName
            Invoke-SqlBatches (Get-IsolatedValidatorText $migration.ValidatorPath) $connection $transaction (Split-Path -Leaf $migration.ValidatorPath)
            $command = $connection.CreateCommand(); $command.Transaction = $transaction
            $command.CommandText = 'INSERT deployment.DatabaseMigrationHistory(MigrationVersion,MigrationName,ScriptHash,ValidatorHash,AppliedBy,IsAdopted) VALUES(@v,@n,@h,@vh,@actor,0);'
            foreach ($pair in @(
                @('@v', [System.Data.SqlDbType]::Int, $migration.Version),
                @('@n', [System.Data.SqlDbType]::NVarChar, $migration.FileName),
                @('@h', [System.Data.SqlDbType]::Char, $migration.Hash),
                @('@vh', [System.Data.SqlDbType]::Char, $migration.ValidatorHash),
                @('@actor', [System.Data.SqlDbType]::NVarChar, $actor)
            )) { $parameter = $command.Parameters.Add($pair[0], $pair[1]); $parameter.Value = $pair[2] }
            try { $null = $command.ExecuteNonQuery() } finally { $command.Dispose() }
            $transaction.Commit()
            Write-Host "V$($migration.Version) executed and recorded."
        } catch {
            try { $transaction.Rollback() } catch {}
            throw
        } finally { $transaction.Dispose() }
    }
}

function Read-SqlIdentity([string] $targetDatabase) {
    $raw = @(& $sqlCmd -S $Server -d $targetDatabase -E -C -b -h -1 -W -Q "SET NOCOUNT ON; SELECT CONCAT(@@SERVERNAME, N'|', DB_NAME());" 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "SQL identity query failed with exit code $exitCode for the approved target database."
    }
    $line = @($raw | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ } | Select-Object -Last 1)
    if ($line.Count -ne 1) { throw 'SQL identity query returned no usable identity row.' }
    $parts = $line[0].Split('|', 2)
    if ($parts.Count -ne 2) { throw 'SQL identity query returned an unexpected field shape.' }
    [pscustomobject]@{ Server = $parts[0].Trim(); Database = $parts[1].Trim() }
}

function Invoke-SqlPackageReport([string] $connectionString, [string] $sourceDacpac, [string] $outputPath) {
    $reportArguments = @(
        '/Action:DeployReport'
        "/SourceFile:$sourceDacpac"
        "/TargetConnectionString:$connectionString"
        "/OutputPath:$outputPath"
        '/p:BlockOnPossibleDataLoss=True'
        '/p:DropObjectsNotInSource=False'
    )

    & $sqlPackage @reportArguments
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Isolated database DACPAC deployment report failed with exit code $exitCode."
    }
    if (-not (Test-Path -LiteralPath $outputPath)) {
        throw "SqlPackage completed without producing a deployment report: $outputPath"
    }
}

function Assert-SafeDacpacPlan([string] $reportPath) {
    # SqlPackage's report is intentionally treated as advisory input. The
    # publish is blocked when it contains destructive operations or touches
    # known drift-prone objects. DropObjectsNotInSource=False does not prevent
    # column-level changes inside an object that exists in both models.
    $reportText = Get-Content -LiteralPath $reportPath -Raw
    $unsafeMatches = @(
        [regex]::Matches($reportText, '(?im)\b(drop|remove|delete)\b')
        [regex]::Matches($reportText, '(?im)MobileNormalized')
        [regex]::Matches($reportText, '(?im)UX_Customers_TenantMobileNormalized')
        [regex]::Matches($reportText, '(?im)sales[.]Customers')
        [regex]::Matches($reportText, '(?im)finance[.]PostPayment')
    ) | Where-Object { $_.Success }

    if ($unsafeMatches.Count -gt 0) {
        $names = @($unsafeMatches | ForEach-Object { $_.Value } | Sort-Object -Unique) -join ', '
        throw "DACPAC plan was rejected before publish. The report contains destructive or drift-sensitive operations ($names). Review '$reportPath'; no publish or migration was attempted."
    }
}

Write-Host "Verifying SQL identity for $Server ..."
$identity = Read-SqlIdentity 'master'
if ($identity.Server -cne $expectedServer -or $identity.Database -cne 'master') {
    throw "Unexpected SQL identity. Observed server '$($identity.Server)' and database '$($identity.Database)'; expected server '$expectedServer' and database 'master'."
}
Write-Host 'SQL identity check completed.'

$targetConnectionString = "Server=$Server;Database=$expectedDatabase;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
$databaseExists = [int](Get-SqlValue 'master' "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'$expectedDatabase') IS NULL THEN 0 ELSE 1 END;")
$freshDatabase = $false
if ($databaseExists -eq 0) {
    Invoke-Sql 'master' "CREATE DATABASE [$expectedDatabase];"
    $freshDatabase = $true
    Write-Host "Database state: FRESHLY CREATED ($expectedDatabase)."
} else {
    Write-Host "Database state: EXISTING ($expectedDatabase); DACPAC publish is disabled for this path."
}

if ($freshDatabase) {
    dotnet build $project --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Database project build failed.' }

    New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
    $reportPath = Join-Path $reportDirectory 'dacpac-deployment-report.xml'
    Write-Host "Generating a read-only DACPAC deployment report: $reportPath"
    Invoke-SqlPackageReport $targetConnectionString $dacpac $reportPath
    Assert-SafeDacpacPlan $reportPath

    $publishArguments = @(
        '/Action:Publish'
        "/SourceFile:$dacpac"
        "/TargetConnectionString:$targetConnectionString"
        '/p:BlockOnPossibleDataLoss=True'
        '/p:DropObjectsNotInSource=False'
    )
    & $sqlPackage @publishArguments
    if ($LASTEXITCODE -ne 0) { throw 'Isolated database DACPAC publish failed.' }
}

$connection = $null
try {
    $connection = Get-SqlConnection $expectedDatabase
    $targetIdentity = Read-SqlIdentity $expectedDatabase
    if ($targetIdentity.Server -cne $expectedServer -or $targetIdentity.Database -cne $expectedDatabase) {
        throw "Unexpected isolated SQL identity. Observed server '$($targetIdentity.Server)' and database '$($targetIdentity.Database)'."
    }

    $state = Get-MigrationState $connection
    if (-not $freshDatabase -and $state.UserTableCount -eq 0) {
        throw 'Existing isolated database has no user tables but was not newly created by this run; refusing ambiguous initialization.'
    }
    if ($state.HistoryExists -xor $state.BaselineExists) {
        throw 'Existing isolated database has only part of the migration ledger; manual review is required.'
    }
    if (-not $freshDatabase) { Invoke-ReadOnlyBaselineValidation $connection }

    if ($freshDatabase) {
        Invoke-ReadOnlyBaselineValidation $connection
        Initialize-MigrationLedger $connection
        Write-Host 'Fresh DACPAC baseline: V40 validator passed and migration ledger initialized.'
    } elseif (-not $state.HistoryExists) {
        try { Invoke-ReadOnlyBaselineValidation $connection }
        catch { throw "Existing isolated database is not a validated V40 baseline; no DACPAC or migration was attempted. $($_.Exception.Message)" }
        $untracked = Get-UntrackedMigrationValidation $connection
        # V62 only tightens admin.Companies.StateCode capacity. The current
        # database-project baseline already has CHAR(2), so V62's validator
        # can pass without V62 having run. It is safe to execute V62 again;
        # every other passing post-V40 validator is treated as evidence of an
        # untracked migration and blocks automatic recovery.
        $unexpectedUntracked = @($untracked.Passed | Where-Object Version -ne 62)
        if ($unexpectedUntracked.Count -gt 0) {
            if ($untracked.Failed.Count -gt 0) {
                throw 'Existing isolated database has migration objects without ledger records and is partially applied; manual review is required.'
            }
            throw 'Existing isolated database appears fully or manually migrated without ledger records; refusing to guess or adopt it automatically.'
        }
        Initialize-MigrationLedger $connection
        Write-Host 'Existing database state: validated V40 baseline; migration ledger initialized.'
    } else {
        $baselineCommand = $connection.CreateCommand()
        $baselineCommand.CommandText = 'SELECT BaselineVersion,BaselineValidatorFile,BaselineValidatorHash FROM deployment.DatabaseMigrationBaseline WHERE SingletonId=1;'
        try {
            $reader = $baselineCommand.ExecuteReader()
            try {
                if (-not $reader.Read()) { throw 'Migration baseline row is missing.' }
                $version = $reader.GetInt32(0); $file = $reader.GetString(1); $hash = $reader.GetString(2)
                if ($reader.Read()) { throw 'Multiple migration baseline rows exist.' }
            } finally { $reader.Dispose() }
        } finally { $baselineCommand.Dispose() }
        if ($version -ne $baselineVersion -or $file -cne (Split-Path -Leaf $baselineValidator) -or $hash -cne (Get-DatabaseMigrationHash $baselineValidator)) {
            throw 'Existing migration baseline ledger is invalid or was created from a different V40 validator.'
        }
        Write-Host 'Existing database state: migration ledger and V40 baseline validated.'
    }

    Apply-MissingMigrations $connection
    Write-Host 'Migration state: canonical migrations through the repository maximum are applied and hash-validated.'
} finally {
    if ($connection) { $connection.Dispose() }
}

$postPublishIdentity = Read-SqlIdentity $expectedDatabase
if ($postPublishIdentity.Server -cne $expectedServer -or $postPublishIdentity.Database -cne $expectedDatabase) {
    throw "Unexpected post-migration SQL identity. Observed server '$($postPublishIdentity.Server)' and database '$($postPublishIdentity.Database)'."
}
Invoke-Sql $expectedDatabase "IF COL_LENGTH(N'master.Products', N'GstPricingType') IS NULL THROW 64091, 'Required product GST mode column is missing.', 1; SELECT DB_NAME() AS DatabaseName, COL_LENGTH(N'master.Products', N'GstPricingType') AS GstPricingTypeColumn;"
Write-Host "Isolated POS GST integration database is initialized through the available canonical migrations: $expectedDatabase"
