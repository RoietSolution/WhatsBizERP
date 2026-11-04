[CmdletBinding()]
param(
    [ValidateSet('Status','DryRun','Apply','Backup')][string]$Mode = 'Status',
    [switch]$ConfirmDev,
    [switch]$ConfirmImageUrlNarrowing,
    [switch]$ConfirmDestructiveMigration,
    [string]$BackupDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'DatabaseMigration.Core.psm1') -Force

$script:ExpectedServer = 'DESKTOP-DQ0868S'
$script:ExpectedInstance = 'MSSQLSERVER'
$script:ExpectedDataSource = 'lpc:DESKTOP-DQ0868S'
$script:ExpectedDatabase = 'WhatsBizERP'
$script:BaselineVersion = 40
$script:MigrationDirectory = Join-Path (Split-Path -Parent $PSScriptRoot) 'database\migrations'
$script:BaselineValidator = Join-Path $script:MigrationDirectory 'validators\Baseline-V40.validate.sql'
$script:Connection = $null
$script:TargetBuilder = $null
$script:Transaction = $null
$script:LockOwned = $false
$script:Actor = [Environment]::UserDomainName + '\' + [Environment]::UserName

function Invoke-TargetQuery([string]$Text,[System.Data.SqlClient.SqlTransaction]$Transaction=$null) {
    if ($null -eq $script:Connection -or $script:Connection.State -ne [System.Data.ConnectionState]::Open) {
        throw 'DEV SQL connection is not open; no further database operation is safe.'
    }
    $command = $script:Connection.CreateCommand()
    $command.CommandText = $Text
    $command.CommandTimeout = 0
    if ($Transaction) { $command.Transaction = $Transaction }
    try { return $command.ExecuteScalar() } finally { $command.Dispose() }
}

function Assert-MigrationLock([System.Data.SqlClient.SqlTransaction]$Transaction=$null) {
    if (-not $script:LockOwned -or $null -eq $script:Connection -or $script:Connection.State -ne [System.Data.ConnectionState]::Open) {
        $script:LockOwned = $false
        throw 'Exclusive DEV migration application lock is not owned on the active SQL session; stopping without further writes.'
    }
    try {
        $mode = [string](Invoke-TargetQuery "SELECT APPLOCK_MODE(N'public',N'WhatsBiz.DatabaseMigrationRunner',N'Session');" -Transaction $Transaction)
        if ($mode -cne 'Exclusive') { throw "Application lock mode is '$mode', expected Exclusive." }
    } catch {
        $script:LockOwned = $false
        throw "Could not verify exclusive DEV migration application lock; stopping without further writes. $($_.Exception.Message)"
    }
}

function Connect-DevTarget {
    $connectionText = "Data Source=$script:ExpectedDataSource;Initial Catalog=$script:ExpectedDatabase;Integrated Security=True;Encrypt=False;TrustServerCertificate=True;Pooling=False;Application Name=WhatsBiz DEV Migration Runner"
    $script:TargetBuilder = New-DatabaseSqlConnectionStringBuilder -ConnectionString $connectionText
    if ($script:TargetBuilder.DataSource -cne $script:ExpectedDataSource -or $script:TargetBuilder.InitialCatalog -cne $script:ExpectedDatabase -or -not $script:TargetBuilder.IntegratedSecurity) {
        throw 'DEV connection policy rejected a target other than the pinned Shared Memory Windows-authenticated database.'
    }
    $script:Connection = [System.Data.SqlClient.SqlConnection]::new($script:TargetBuilder.ConnectionString)
    try { $script:Connection.Open() }
    catch {
        $detail = Format-SafeSqlConnectionException -Exception $_.Exception -SensitiveValues @($connectionText)
        throw "DEV SQL connection failed. Run this command in an operator Windows PowerShell session with Windows Integrated authentication. $detail"
    }
    $command = $script:Connection.CreateCommand()
    $command.CommandText = "SELECT @@SERVERNAME,DB_NAME(),CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')),CONVERT(nvarchar(128),SERVERPROPERTY('InstanceName')),ORIGINAL_LOGIN(),CONVERT(nvarchar(40),CONNECTIONPROPERTY('auth_scheme')),CONVERT(nvarchar(40),CONNECTIONPROPERTY('net_transport'));"
    try {
        $reader = $command.ExecuteReader()
        try {
            if (-not $reader.Read()) { throw 'DEV identity query returned no row.' }
            $server = [string]$reader.GetValue(0); $database = [string]$reader.GetValue(1)
            $machine = [string]$reader.GetValue(2); $instance = if ($reader.IsDBNull(3)) { 'MSSQLSERVER' } else { [string]$reader.GetValue(3) }
            $login = [string]$reader.GetValue(4); $auth = [string]$reader.GetValue(5); $transport = [string]$reader.GetValue(6)
        } finally { $reader.Dispose() }
    } finally { $command.Dispose() }
    if ($server -cne $script:ExpectedServer -or $machine -cne $script:ExpectedServer -or $database -cne $script:ExpectedDatabase -or $instance -cne $script:ExpectedInstance) {
        throw "DEV identity guard rejected target '$server/$database' (machine '$machine', instance '$instance'). Expected '$($script:ExpectedServer)/$($script:ExpectedDatabase)' default instance."
    }
    if ($transport -cne 'Shared memory') { throw "DEV connection used '$transport'; expected Shared memory through $script:ExpectedDataSource." }
    if ($auth -cne 'NTLM') { throw "DEV connection used '$auth' authentication; expected the verified local Windows NTLM context." }
    Write-Host "Target identity: $server/$database; instance=$instance; login=$login; auth=$auth; transport=$transport"
}

function Assert-ReadMetadataPermission {
    $permission = Invoke-TargetQuery "SELECT HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'VIEW DEFINITION');"
    Assert-DatabaseMigrationMetadataVisibility -CanViewDatabaseDefinition ([int]$permission -eq 1)
}

function Invoke-SqlBatches([string]$Text,[System.Data.SqlClient.SqlTransaction]$Transaction=$null,[switch]$ReadOnly) {
    if ($ReadOnly) { Assert-DatabaseMigrationValidatorReadOnly -Text $Text -FileName 'DEV migration validator' }
    if ($Text -match '(?im)^\s*:\s*(r|setvar)\b') { throw 'Managed migrations cannot use SQLCMD directives; use SQL batches separated by GO.' }
    if (-not $ReadOnly -and $Text -match '(?i)sp_releaseapplock') { throw 'Managed migrations cannot release the DEV runner session lock; the migration is blocked.' }
    $batches = @([regex]::Split($Text,'(?im)^\s*GO\s*(?:--.*)?$') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    foreach ($batch in $batches) {
        if ($script:LockOwned) { Assert-MigrationLock -Transaction $Transaction }
        $command = $script:Connection.CreateCommand()
        $command.CommandText = $batch; $command.CommandTimeout = 0
        if ($Transaction) { $command.Transaction = $Transaction }
        try { $null = $command.ExecuteNonQuery() } finally { $command.Dispose() }
    }
}

function Invoke-MigrationValidator([string]$Path,[System.Data.SqlClient.SqlTransaction]$Transaction=$null) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Required migration validator is missing: $Path" }
    Invoke-SqlBatches -Text ([IO.File]::ReadAllText($Path)) -Transaction $Transaction -ReadOnly
}

function Get-DevMigrationState {
    $command = $script:Connection.CreateCommand()
    $command.CommandText = "SELECT OBJECT_ID(N'deployment.DatabaseMigrationHistory',N'U'),OBJECT_ID(N'deployment.DatabaseMigrationBaseline',N'U');"
    try { $reader=$command.ExecuteReader(); try { $null=$reader.Read();$hasHistory=-not $reader.IsDBNull(0);$hasBaseline=-not $reader.IsDBNull(1) } finally { $reader.Dispose() } } finally { $command.Dispose() }
    if (-not $hasHistory -and -not $hasBaseline) { return [pscustomobject]@{Initialized=$false;BaselineVersion=$null;History=[object[]]@()} }
    if (-not $hasHistory -or -not $hasBaseline) { throw 'Migration baseline/history metadata is only partially present; reconcile manually before continuing.' }
    $command=$script:Connection.CreateCommand();$command.CommandText='SELECT SingletonId,BaselineVersion,BaselineName,BaselineValidatorFile,BaselineValidatorHash FROM deployment.DatabaseMigrationBaseline;'
    try {
        $reader=$command.ExecuteReader()
        try {
            if (-not $reader.Read()) { throw 'Migration baseline row is missing.' }
            $singleton=$reader.GetByte(0);$version=$reader.GetInt32(1);$name=$reader.GetString(2);$validatorFile=$reader.GetString(3);$validatorHash=$reader.GetString(4)
            if ($reader.Read()) { throw 'Multiple migration baseline rows exist.' }
        } finally { $reader.Dispose() }
    } finally { $command.Dispose() }
    if ($singleton -ne 1 -or $name -cne 'Historical V40 schema' -or $validatorFile -cne (Split-Path -Leaf $script:BaselineValidator) -or $validatorHash -cne (Get-DatabaseMigrationHash $script:BaselineValidator)) {
        throw 'Recorded migration baseline metadata does not match canonical V40 values.'
    }
    Assert-DatabaseMigrationBaselineVersion -ActualVersion $version -ExpectedVersion $script:BaselineVersion
    $command=$script:Connection.CreateCommand();$command.CommandText='SELECT MigrationVersion,MigrationName,ScriptHash,ValidatorHash,IsAdopted FROM deployment.DatabaseMigrationHistory ORDER BY MigrationVersion;'
    $history=[Collections.Generic.List[object]]::new()
    try {
        $reader=$command.ExecuteReader()
        try { while ($reader.Read()) { $history.Add([pscustomobject]@{MigrationVersion=$reader.GetInt32(0);MigrationName=$reader.GetString(1);ScriptHash=$reader.GetString(2);ValidatorHash=if($reader.IsDBNull(3)){$null}else{$reader.GetString(3)};IsAdopted=$reader.GetBoolean(4)}) } }
        finally { $reader.Dispose() }
    } finally { $command.Dispose() }
    return [pscustomobject]@{Initialized=$true;BaselineVersion=$version;History=[object[]]$history.ToArray()}
}

function Invoke-BaselineValidator {
    if (-not (Test-Path -LiteralPath $script:BaselineValidator -PathType Leaf)) { throw 'Canonical V40 schema validator is missing.' }
    Invoke-MigrationValidator -Path $script:BaselineValidator
    Write-Host 'V40 baseline schema validator: PASS (read-only)'
}

function Invoke-AppliedMigrationValidators([object[]]$Plan) {
    foreach ($item in @($Plan | Where-Object { $_.Status -eq 'APPLIED' -and $_.Version -in @(82,83) } | Sort-Object Version)) {
        if (-not $item.ValidatorPath) { throw "Applied V$($item.Version) has no repository validator." }
        Invoke-MigrationValidator -Path $item.ValidatorPath
        Assert-ExpectedArtifacts -Version $item.Version
        Write-Host "V$($item.Version) applied schema validator: PASS (read-only)"
    }
}

function Get-ScalarResult([string]$Text,[System.Data.SqlClient.SqlTransaction]$Transaction=$null) { return Invoke-TargetQuery $Text -Transaction $Transaction }

function Assert-RequiredObjects([int]$Version,[System.Data.SqlClient.SqlTransaction]$Transaction=$null) {
    $objects = if ($Version -eq 82) {
        @('core.Tenants','core.Features','core.Plans','core.PlanFeatures','core.TenantFeatures','commerce.AnalyticsEvents','commerce.ProductSearchEvents')
    } elseif ($Version -eq 83) {
        @('core.Tenants','purchase.Suppliers','master.Products','master.Brands','master.UnitsOfMeasure')
    } else { return }
    $missing = [Collections.Generic.List[string]]::new()
    foreach ($objectName in $objects) {
        $cmd=$script:Connection.CreateCommand();$cmd.CommandText="SELECT OBJECT_ID(@name,N'U');";$cmd.Transaction=$Transaction;$p=$cmd.Parameters.Add('@name',[System.Data.SqlDbType]::NVarChar,256);$p.Value=$objectName
        try { $value=$cmd.ExecuteScalar();if ($null -eq $value -or $value -is [DBNull]) { $missing.Add($objectName) } } finally { $cmd.Dispose() }
    }
    if ($missing.Count) { throw "V$Version prerequisites are missing: $($missing -join ', ')." }
    if ($Version -eq 82) {
        $parent = [int](Get-ScalarResult "SELECT COUNT(*) FROM core.Features WHERE FeatureKey=N'ANALYTICS';" -Transaction $Transaction)
        if ($parent -ne 1) { throw "V82 prerequisite requires exactly one ANALYTICS parent feature; found $parent." }
    }
}

function Get-V82ArtifactCount([System.Data.SqlClient.SqlTransaction]$Transaction=$null) {
    return [int](Get-ScalarResult @'
SELECT
 (SELECT COUNT(*) FROM sys.tables WHERE object_id IN(OBJECT_ID(N'commerce.GuestVisitorAnalyticsPolicies'),OBJECT_ID(N'commerce.GuestVisitorAnalyticsSessions')))
 +(SELECT COUNT(*) FROM core.Features WHERE FeatureKey=N'GUEST_VISITOR_ANALYTICS')
 +(SELECT COUNT(*) FROM sys.columns WHERE (object_id=OBJECT_ID(N'commerce.AnalyticsEvents') AND name IN(N'GuestSessionId',N'ClientEventId',N'CategoryId',N'NormalizedSearchTerm',N'ResultCount')) OR (object_id=OBJECT_ID(N'commerce.ProductSearchEvents') AND name=N'GuestSessionId'))
 +(SELECT COUNT(*) FROM sys.indexes WHERE is_disabled=0 AND is_hypothetical=0 AND ((object_id=OBJECT_ID(N'commerce.GuestVisitorAnalyticsSessions') AND name IN(N'IX_GuestAnalyticsSessions_TenantStarted',N'IX_GuestAnalyticsSessions_TenantVisitor') AND is_unique=0) OR (object_id=OBJECT_ID(N'commerce.AnalyticsEvents') AND name=N'UX_AnalyticsEvents_TenantClientEvent' AND is_unique=1 AND has_filter=1) OR (object_id=OBJECT_ID(N'commerce.AnalyticsEvents') AND name=N'IX_AnalyticsEvents_GuestSessionDate' AND is_unique=0) OR (object_id=OBJECT_ID(N'commerce.ProductSearchEvents') AND name=N'IX_ProductSearchEvents_GuestSession' AND is_unique=0)))
  +(SELECT COUNT(*) FROM sys.key_constraints WHERE type=N'PK' AND ((parent_object_id=OBJECT_ID(N'commerce.GuestVisitorAnalyticsPolicies') AND name=N'PK_GuestVisitorAnalyticsPolicies') OR (parent_object_id=OBJECT_ID(N'commerce.GuestVisitorAnalyticsSessions') AND name=N'PK_GuestVisitorAnalyticsSessions')))
 +(SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled=0 AND is_not_trusted=0 AND ((parent_object_id=OBJECT_ID(N'commerce.GuestVisitorAnalyticsSessions') AND name=N'FK_GuestAnalyticsSessions_Tenant') OR (parent_object_id=OBJECT_ID(N'commerce.AnalyticsEvents') AND name=N'FK_AnalyticsEvents_GuestSession') OR (parent_object_id=OBJECT_ID(N'commerce.ProductSearchEvents') AND name=N'FK_ProductSearchEvents_GuestSession')))
   +(SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled=0 AND is_not_trusted=0 AND ((parent_object_id=OBJECT_ID(N'commerce.GuestVisitorAnalyticsPolicies') AND name IN(N'CK_GuestAnalyticsPolicy_Retention',N'CK_GuestAnalyticsPolicy_Review')) OR (parent_object_id=OBJECT_ID(N'commerce.GuestVisitorAnalyticsSessions') AND name=N'CK_GuestAnalytics_SessionValues') OR (parent_object_id=OBJECT_ID(N'commerce.AnalyticsEvents') AND name=N'CK_AnalyticsEvents_GuestAnalyticsData')));
'@ -Transaction $Transaction)
}

function Get-V83ArtifactCount([System.Data.SqlClient.SqlTransaction]$Transaction=$null) {
    return [int](Get-ScalarResult @'
SELECT
 (SELECT COUNT(*) FROM sys.tables WHERE object_id IN(OBJECT_ID(N'master.ProductExternalImages'),OBJECT_ID(N'purchase.SupplierCatalogImportProfiles'),OBJECT_ID(N'purchase.SupplierCatalogProducts'),OBJECT_ID(N'purchase.SupplierCatalogImportHistory')))
 +(SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID(N'master.Products') AND name IN(N'PackagingCost',N'ShippingCost',N'CombinedShippingPackagingCost'))
 +(SELECT COUNT(*) FROM sys.indexes WHERE is_disabled=0 AND is_hypothetical=0 AND ((object_id=OBJECT_ID(N'master.ProductExternalImages') AND name=N'IX_ProductExternalImages_TenantProduct' AND is_unique=0) OR (object_id=OBJECT_ID(N'purchase.SupplierCatalogProducts') AND name=N'UX_SupplierCatalogProducts_TenantSupplierSku' AND is_unique=1 AND has_filter=1) OR (object_id=OBJECT_ID(N'purchase.SupplierCatalogProducts') AND name=N'UX_SupplierCatalogProducts_SourceRow' AND is_unique=1) OR (object_id=OBJECT_ID(N'purchase.SupplierCatalogProducts') AND name=N'IX_SupplierCatalogProducts_TenantProduct' AND is_unique=0) OR (object_id=OBJECT_ID(N'purchase.SupplierCatalogImportHistory') AND name=N'IX_SupplierCatalogImportHistory_TenantSupplierDate' AND is_unique=0)))
 +(SELECT COUNT(*) FROM sys.key_constraints WHERE type=N'PK' AND ((parent_object_id=OBJECT_ID(N'master.ProductExternalImages') AND name=N'PK_ProductExternalImages') OR (parent_object_id=OBJECT_ID(N'purchase.SupplierCatalogImportProfiles') AND name=N'PK_SupplierCatalogImportProfiles') OR (parent_object_id=OBJECT_ID(N'purchase.SupplierCatalogProducts') AND name=N'PK_SupplierCatalogProducts') OR (parent_object_id=OBJECT_ID(N'purchase.SupplierCatalogImportHistory') AND name=N'PK_SupplierCatalogImportHistory')))
 +(SELECT COUNT(*) FROM sys.foreign_keys WHERE is_disabled=0 AND is_not_trusted=0 AND ((parent_object_id=OBJECT_ID(N'master.ProductExternalImages') AND name IN(N'FK_ProductExternalImages_Tenant',N'FK_ProductExternalImages_Product',N'FK_ProductExternalImages_Supplier')) OR (parent_object_id=OBJECT_ID(N'purchase.SupplierCatalogImportProfiles') AND name IN(N'FK_SupplierCatalogProfiles_Tenant',N'FK_SupplierCatalogProfiles_Supplier',N'FK_SupplierCatalogProfiles_Unit',N'FK_SupplierCatalogProfiles_Brand')) OR (parent_object_id=OBJECT_ID(N'purchase.SupplierCatalogProducts') AND name IN(N'FK_SupplierCatalogProducts_Tenant',N'FK_SupplierCatalogProducts_Supplier',N'FK_SupplierCatalogProducts_Product')) OR (parent_object_id=OBJECT_ID(N'purchase.SupplierCatalogImportHistory') AND name IN(N'FK_SupplierCatalogImportHistory_Tenant',N'FK_SupplierCatalogImportHistory_Supplier'))))
 +(SELECT COUNT(*) FROM sys.check_constraints WHERE is_disabled=0 AND is_not_trusted=0 AND ((parent_object_id=OBJECT_ID(N'master.ProductExternalImages') AND name IN(N'CK_ProductExternalImages_Https',N'CK_ProductExternalImages_Order')) OR (parent_object_id=OBJECT_ID(N'purchase.SupplierCatalogImportProfiles') AND name IN(N'CK_SupplierCatalogProfiles_Format',N'CK_SupplierCatalogProfiles_Mappings',N'CK_SupplierCatalogProfiles_Ownership',N'CK_SupplierCatalogProfiles_Gst')) OR (parent_object_id=OBJECT_ID(N'purchase.SupplierCatalogProducts') AND name IN(N'CK_SupplierCatalogProducts_Tags',N'CK_SupplierCatalogProducts_Images')) OR (parent_object_id=OBJECT_ID(N'purchase.SupplierCatalogImportHistory') AND name=N'CK_SupplierCatalogImportHistory_Counts')));
'@ -Transaction $Transaction)
}

function Get-ImageUrlSafety([System.Data.SqlClient.SqlTransaction]$Transaction=$null) {
    $command=$script:Connection.CreateCommand()
    $command.Transaction=$Transaction
    $command.CommandText=@'
DECLARE @objectId int=OBJECT_ID(N'master.Products',N'U'),@columnId int=COLUMNPROPERTY(OBJECT_ID(N'master.Products',N'U'),N'ImageUrl','ColumnId');
IF @objectId IS NULL OR @columnId IS NULL THROW 83020,N'V83 requires master.Products.ImageUrl.',1;
SELECT ty.name DataType,c.max_length MaxBytes,c.is_nullable IsNullable,
 (SELECT COUNT_BIG(*) FROM master.Products WHERE DATALENGTH(CONVERT(nvarchar(max),ImageUrl))>4096) TooLongRows,
 (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id=@objectId AND ic.column_id=@columnId AND ic.index_id>0) IndexDependencies,
 (SELECT COUNT(*) FROM sys.foreign_key_columns fkc WHERE (fkc.parent_object_id=@objectId AND fkc.parent_column_id=@columnId) OR (fkc.referenced_object_id=@objectId AND fkc.referenced_column_id=@columnId)) ForeignKeyDependencies,
 (SELECT COUNT(*) FROM sys.sql_expression_dependencies d WHERE d.referenced_id=@objectId AND d.referenced_minor_id=@columnId AND d.is_schema_bound_reference=1) SchemaBoundDependencies,
 (SELECT COUNT(*) FROM sys.default_constraints dc WHERE dc.parent_object_id=@objectId AND dc.parent_column_id=@columnId) DefaultDependencies,
 (SELECT COUNT(*) FROM sys.sql_expression_dependencies d JOIN sys.computed_columns cc ON cc.object_id=d.referencing_id AND cc.column_id=d.referencing_minor_id WHERE d.referencing_id=@objectId AND d.referenced_id=@objectId AND d.referenced_minor_id=@columnId) ComputedDependencies,
 (SELECT COUNT(*) FROM sys.check_constraints cc WHERE cc.parent_object_id=@objectId AND (cc.parent_column_id=@columnId OR EXISTS(SELECT 1 FROM sys.sql_expression_dependencies d WHERE d.referencing_id=cc.object_id AND d.referenced_minor_id=@columnId))) CheckDependencies
FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id WHERE c.object_id=@objectId AND c.column_id=@columnId;
'@
    try {
        $reader=$command.ExecuteReader()
        try {
            if (-not $reader.Read()) { throw 'V83 ImageUrl metadata query returned no row.' }
            $type=[string]$reader['DataType'];$bytes=[int]$reader['MaxBytes'];$nullable=[bool]$reader['IsNullable']
            $tooLong=[long]$reader['TooLongRows'];$indexes=[int]$reader['IndexDependencies'];$foreignKeys=[int]$reader['ForeignKeyDependencies'];$schemaBound=[int]$reader['SchemaBoundDependencies'];$defaults=[int]$reader['DefaultDependencies'];$computed=[int]$reader['ComputedDependencies'];$checks=[int]$reader['CheckDependencies']
        } finally { $reader.Dispose() }
    } finally { $command.Dispose() }
    if ($type -notin @('nvarchar','nchar','varchar','char')) { throw "V83 ImageUrl is '$type'; only char/varchar/nchar/nvarchar can be safely converted by this migration." }
    if ($tooLong -gt 0) { throw "V83 would narrow ImageUrl values: $tooLong existing product rows exceed 2048 Unicode characters. No migration is safe without data remediation approval." }
    if ($indexes -or $foreignKeys -or $schemaBound -or $defaults -or $computed -or $checks) { throw "V83 ImageUrl has dependent objects that require manual schema review (indexes=$indexes, foreignKeys=$foreignKeys, schemaBound=$schemaBound, defaults=$defaults, computed=$computed, checks=$checks)." }
    $currentCapacity = if ($bytes -eq -1) { [long]::MaxValue } elseif ($type -in @('nvarchar','nchar')) { [long]($bytes / 2) } else { [long]$bytes }
    $narrowing = $currentCapacity -gt 2048
    Write-Host "V83 ImageUrl: $type/$bytes bytes, nullable=$nullable, current capacity=$(if($currentCapacity -eq [long]::MaxValue){'MAX'}else{$currentCapacity}), rows over 2048 Unicode characters=$tooLong, dependent objects=0."
    if ($narrowing) { Write-Host 'V83 ImageUrl schema narrowing detected; Apply requires -ConfirmImageUrlNarrowing after operator review.' }
    return [pscustomobject]@{DataType=$type;MaxBytes=$bytes;IsNullable=$nullable;TooLongRows=$tooLong;Narrowing=$narrowing}
}

function Invoke-PendingMigrationPreflights([object[]]$Pending,[switch]$ForApply) {
    foreach ($migration in @($Pending | Sort-Object Version)) {
        if ($migration.Version -eq 82) {
            Assert-RequiredObjects 82
            $artifacts=Get-V82ArtifactCount
            if ($artifacts -gt 0) { throw "V82 is pending in the ledger but $artifacts V82-specific schema/seed artifacts already exist; possible partial application, stop for reconciliation." }
            Write-Host 'V82 prerequisites and zero partial V82 artifacts: PASS'
        } elseif ($migration.Version -eq 83) {
            Assert-RequiredObjects 83
            $artifacts=Get-V83ArtifactCount
            if ($artifacts -gt 0) { throw "V83 is pending in the ledger but $artifacts V83-specific schema artifacts already exist; possible partial application, stop for reconciliation." }
            $image=Get-ImageUrlSafety
            if ($ForApply -and $image.Narrowing -and -not $ConfirmImageUrlNarrowing) { throw 'Apply blocked: V83 narrows the existing ImageUrl capacity to 2048 characters. Review the Status/DryRun report and rerun Apply with -ConfirmImageUrlNarrowing only if that schema change is approved.' }
            Write-Host 'V83 prerequisites and zero partial V83 artifacts: PASS'
        }
    }
}

function Get-DefaultBackupDirectory {
    $instanceMap=Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL' -ErrorAction Stop
    $instanceId=[string]$instanceMap.MSSQLSERVER
    if ([string]::IsNullOrWhiteSpace($instanceId)) { throw 'Cannot resolve the local MSSQLSERVER instance from SQL client registry configuration.' }
    $instance=Get-ItemProperty "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$instanceId\MSSQLServer" -ErrorAction Stop
    $path=[string]$instance.BackupDirectory
    if ([string]::IsNullOrWhiteSpace($path) -or -not [IO.Path]::IsPathRooted($path)) { throw 'Local SQL Server backup directory is unavailable or not an absolute path; specify -BackupDirectory explicitly.' }
    return $path
}

function Get-BackupMetadata([string]$Path,[string]$Name) {
    Assert-MigrationLock
    $command=$script:Connection.CreateCommand()
    $command.CommandText="SELECT TOP(1) CONVERT(nvarchar(36),bs.backup_set_uuid),bs.backup_size,CONVERT(nvarchar(33),bs.backup_finish_date,126),bs.has_backup_checksums,bs.is_copy_only FROM msdb.dbo.backupset bs JOIN msdb.dbo.backupmediafamily mf ON mf.media_set_id=bs.media_set_id WHERE bs.database_name=@db AND bs.name=@name AND mf.physical_device_name=@path AND bs.type=N'D' AND bs.backup_finish_date IS NOT NULL ORDER BY bs.backup_finish_date DESC;"
        foreach ($pair in @(@('@db',128,$script:ExpectedDatabase),@('@name',128,$Name),@('@path',4000,$Path))) { $p=$command.Parameters.Add($pair[0],[System.Data.SqlDbType]::NVarChar,$pair[1]);$p.Value=$pair[2] }
        try {
            $reader=$command.ExecuteReader()
            try {
                if (-not $reader.Read()) { throw 'Backup gate failed: SQL Server has no completed full-backup history row for the new backup name and exact media path.' }
                $meta=ConvertTo-DatabaseBackupMetadata -BackupSetGuid $reader.GetValue(0) -BackupSize $reader.GetValue(1) -BackupFinishDate $reader.GetValue(2) -HasChecksum $reader.GetValue(3) -IsCopyOnly $reader.GetValue(4)
                $meta | Add-Member -NotePropertyName Path -NotePropertyValue $Path
                return $meta
            } finally { $reader.Dispose() }
        } finally { $command.Dispose() }
}

function Invoke-BackupGate([Parameter()][AllowEmptyCollection()][object[]]$Pending,[string]$Purpose) {
    Assert-MigrationLock
    if (-not $BackupDirectory) { $directory=Get-DefaultBackupDirectory } else { $directory=$BackupDirectory }
    if (-not [IO.Path]::IsPathRooted($directory)) { throw 'Backup directory must be an absolute path visible to SQL Server.' }
    $stamp=[DateTime]::UtcNow.ToString('yyyyMMdd_HHmmss');$suffix=[guid]::NewGuid().ToString('N').Substring(0,8)
    if ($Purpose) {
        if ($Purpose -notmatch '\A[A-Z0-9_]{1,32}\z') { throw 'Backup purpose must be a short uppercase identifier.' }
        $range=$Purpose
    } else {
        if (-not $Pending.Count) { throw 'A migration backup requires at least one pending migration.' }
        $range="V$($Pending[0].Version)_V$($Pending[-1].Version)"
    }
    $path=Join-Path $directory "WhatsBizERP_pre_${range}_${stamp}_${suffix}.bak"
    $name="WhatsBizERP pre $range $stamp"
    if ($path.Contains("'")) { throw 'Backup file path cannot contain a single quote.' }
    $escapedName=$name.Replace("'","''");$escapedPath=$path.Replace("'","''")
    Write-Host "Creating full COPY_ONLY CHECKSUM backup: $path"
    Assert-MigrationLock
    $command=$script:Connection.CreateCommand();$command.CommandText="BACKUP DATABASE [WhatsBizERP] TO DISK=N'$escapedPath' WITH COPY_ONLY,CHECKSUM,NOINIT,NAME=N'$escapedName';";$command.CommandTimeout=0
    try { Assert-MigrationLock; $null=$command.ExecuteNonQuery() } finally { $command.Dispose() }
    Assert-MigrationLock
    $metadata=Get-BackupMetadata -Path $path -Name $name
    if (-not $metadata.IsCopyOnly -or -not $metadata.HasChecksum -or $metadata.BackupSize -le 0) { throw "Backup metadata did not confirm a full COPY_ONLY CHECKSUM backup. No migrations executed. Path: $path" }
    $command=$script:Connection.CreateCommand();$command.CommandText="RESTORE VERIFYONLY FROM DISK=N'$escapedPath' WITH CHECKSUM;";$command.CommandTimeout=0
    try { Assert-MigrationLock; $null=$command.ExecuteNonQuery() } finally { $command.Dispose() }
    Assert-MigrationLock
    Write-Host "Backup metadata: PASS; size=$($metadata.BackupSize); checksum=$($metadata.HasChecksum); copyOnly=$($metadata.IsCopyOnly)"
    Write-Host 'RESTORE VERIFYONLY WITH CHECKSUM: PASS'
    return $path
}

function Assert-ApplyPermissions {
    $command=$script:Connection.CreateCommand()
    $command.CommandText="SELECT SUSER_SNAME(),COALESCE(CONVERT(int,HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'BACKUP DATABASE')),0),COALESCE(CONVERT(int,HAS_PERMS_BY_NAME(NULL,NULL,N'CREATE ANY DATABASE')),0),COALESCE(CONVERT(int,HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'CONTROL')),0);"
    try {
        $reader=$command.ExecuteReader()
        try {
            if (-not $reader.Read()) { throw 'DEV permission preflight returned no row.' }
            $principal=[string]$reader.GetValue(0);$backup=[int]$reader.GetValue(1)-eq 1;$verify=[int]$reader.GetValue(2)-eq 1;$migrate=[int]$reader.GetValue(3)-eq 1
        } finally { $reader.Dispose() }
    } finally { $command.Dispose() }
    $null=Assert-DatabaseDeploymentPreflight -Principal $principal -CanBackup:$backup -CanVerifyBackup:$verify -CanMigrate:$migrate
    Write-Host "Deployment principal: $principal; BACKUP/VERIFY/CONTROL permissions: PASS"
}

function Acquire-MigrationLock {
    $command=$script:Connection.CreateCommand()
    $command.CommandText="DECLARE @result int; EXEC @result=sys.sp_getapplock @Resource=N'WhatsBiz.DatabaseMigrationRunner',@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0,@DbPrincipal=N'public'; IF @result<0 THROW 52190,N'Another DEV migration runner holds the application lock.',1;"
    try { $null=$command.ExecuteNonQuery() } finally { $command.Dispose() }
    $script:LockOwned=$true
    Assert-MigrationLock
}

function Write-MigrationHistory([object]$Migration,[string]$BackupPath,[System.Data.SqlClient.SqlTransaction]$Transaction) {
    Assert-MigrationLock -Transaction $Transaction
    $command=$script:Connection.CreateCommand();$command.Transaction=$Transaction
    $command.CommandText='INSERT deployment.DatabaseMigrationHistory(MigrationVersion,MigrationName,ScriptHash,ValidatorHash,AppliedBy,IsAdopted,BackupPath) VALUES(@version,@name,@hash,@validatorHash,@actor,0,@backupPath);'
    foreach ($item in @(
        @('@version',[System.Data.SqlDbType]::Int,0,[int]$Migration.Version),
        @('@name',[System.Data.SqlDbType]::NVarChar,260,[string]$Migration.FileName),
        @('@hash',[System.Data.SqlDbType]::Char,64,[string]$Migration.Hash),
        @('@validatorHash',[System.Data.SqlDbType]::Char,64,[string]$Migration.ValidatorHash),
        @('@actor',[System.Data.SqlDbType]::NVarChar,256,$script:Actor),
        @('@backupPath',[System.Data.SqlDbType]::NVarChar,4000,$BackupPath)
    )) { $p=$command.Parameters.Add($item[0],$item[1]);if($item[2]){$p.Size=$item[2]};$p.Value=$item[3] }
    try { $null=$command.ExecuteNonQuery() } finally { $command.Dispose() }
}

function Test-NonTransactionalMigration([string]$Path) {
    $transactionMarkers = @(Get-Content -LiteralPath $Path -TotalCount 6 | Where-Object {
        $_ -match '^\s*--\s*WhatsBiz-Migration-Transaction:\s*NONE\s*$'
    })
    return $transactionMarkers.Count -gt 0
}

function Invoke-OneMigration([object]$Migration,[string]$BackupPath) {
    $text=[IO.File]::ReadAllText($Migration.Path)
    $nonTransactional=Test-NonTransactionalMigration -Path $Migration.Path
    $tx=$null
    if (-not $nonTransactional) { $tx=$script:Connection.BeginTransaction([System.Data.IsolationLevel]::Serializable) }
    try {
        Invoke-SqlBatches -Text $text -Transaction $tx
        Write-Host "V$($Migration.Version) EXECUTED"
        Invoke-MigrationValidator -Path $Migration.ValidatorPath -Transaction $tx
        Write-Host "V$($Migration.Version) VALIDATED"
        if ($Migration.Version -in @(82,83)) { Assert-ExpectedArtifacts -Version $Migration.Version -Transaction $tx }
        if (-not $tx) { $tx=$script:Connection.BeginTransaction([System.Data.IsolationLevel]::Serializable) }
        Write-MigrationHistory -Migration $Migration -BackupPath $BackupPath -Transaction $tx
        Assert-MigrationLock -Transaction $tx
        $tx.Commit()
        Assert-MigrationLock
        Write-Host "V$($Migration.Version) LEDGER RECORDED"
    } catch {
        if ($tx) { try { $tx.Rollback() } catch {} }
        throw
    } finally { if ($tx) { $tx.Dispose() } }
}

function Assert-ExpectedArtifacts([int]$Version,[System.Data.SqlClient.SqlTransaction]$Transaction=$null) {
    if ($Version -eq 82) {
        if ((Get-V82ArtifactCount -Transaction $Transaction) -lt 2+1+6+5+2+3+4) { throw 'V82 post-migration verification failed: one or more expected tables, seed records, columns, indexes, keys, foreign keys or checks are missing.' }
        $seedCheck=@'
DECLARE @feature uniqueidentifier=(SELECT FeatureId FROM core.Features WHERE FeatureKey=N'GUEST_VISITOR_ANALYTICS');
IF EXISTS(SELECT 1 FROM core.Plans p WHERE NOT EXISTS(SELECT 1 FROM core.PlanFeatures pf WHERE pf.PlanId=p.PlanId AND pf.FeatureId=@feature))
 THROW 82032,N'V82 verification failed: a plan is missing its analytics feature seed.',1;
IF (SELECT COUNT(*) FROM core.PlanFeatures WHERE FeatureId=@feature)<>(SELECT COUNT(*) FROM core.Plans)
 THROW 82030,N'V82 verification failed: plan feature seeds do not cover every plan.',1;
IF EXISTS(SELECT 1 FROM core.Tenants t WHERE NOT EXISTS(SELECT 1 FROM core.TenantFeatures tf WHERE tf.TenantId=t.TenantId AND tf.FeatureId=@feature))
 THROW 82033,N'V82 verification failed: a tenant is missing its analytics feature seed.',1;
IF (SELECT COUNT(*) FROM core.TenantFeatures WHERE FeatureId=@feature)<>(SELECT COUNT(*) FROM core.Tenants)
 THROW 82031,N'V82 verification failed: tenant feature seeds do not cover every tenant.',1;
'@
        $null=Invoke-TargetQuery -Text $seedCheck -Transaction $Transaction
    } elseif ($Version -eq 83) {
        if ((Get-V83ArtifactCount -Transaction $Transaction) -lt 4+3+5+4+12+9) { throw 'V83 post-migration verification failed: one or more expected tables, product columns, indexes, keys, foreign keys or checks are missing.' }
        $costColumns=[int](Get-ScalarResult "SELECT COUNT(*) FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id WHERE c.object_id=OBJECT_ID(N'master.Products') AND c.name IN(N'PackagingCost',N'ShippingCost',N'CombinedShippingPackagingCost') AND ty.name=N'decimal' AND c.precision=18 AND c.scale=4 AND c.is_nullable=1;" -Transaction $Transaction)
        if ($costColumns -ne 3) { throw 'V83 post-migration verification failed: product cost breakdown columns must be nullable decimal(18,4).' }
        $cmd=$script:Connection.CreateCommand();$cmd.CommandText="SELECT ty.name,c.max_length,c.is_nullable FROM sys.columns c JOIN sys.types ty ON ty.user_type_id=c.user_type_id WHERE c.object_id=OBJECT_ID(N'master.Products') AND c.name=N'ImageUrl';"
        $cmd.Transaction=$Transaction
        try { $r=$cmd.ExecuteReader();try{if(-not $r.Read() -or [string]$r['name'] -cne 'nvarchar' -or [int]$r['max_length'] -ne 4096 -or -not [bool]$r['is_nullable']){throw 'V83 post-migration verification failed: master.Products.ImageUrl must be nvarchar(2048) NULL.'}}finally{$r.Dispose()} } finally { $cmd.Dispose() }
    }
}

function Show-Plan([object[]]$Plan,[object]$State) {
    if (-not $State.Initialized) { Write-Host 'Baseline: absent (V40 schema validator is read-only; no automatic baseline initialization is supported).' }
    else { Write-Host "Baseline: V$($State.BaselineVersion) canonical metadata PASS" }
    $applied=@($Plan|Where-Object Status -eq 'APPLIED'|Sort-Object Version);$pending=@($Plan|Where-Object Status -eq 'PENDING'|Sort-Object Version)
    Write-Host "Latest applied migration: $(if($applied.Count){'V'+$applied[-1].Version}else{'none after V40'})"
    Write-Host "Applied migrations: $(if($applied.Count){($applied|ForEach-Object{"V$($_.Version)"}) -join ', '}else{'none'})"
    if ($pending.Count) { Write-Host 'Pending migrations:';foreach($m in $pending){Write-Host ("  V{0} {1} migrationHash={2} validatorHash={3}" -f $m.Version,$m.FileName,$m.Hash,$m.ValidatorHash)} } else { Write-Host 'Pending migrations: none' }
}

try {
    if ($Mode -eq 'Apply' -and -not $ConfirmDev) { throw 'Apply requires the explicit -ConfirmDev switch. Run Status and DryRun first.' }
    if ($Mode -ne 'Apply' -and ($ConfirmDev -or $ConfirmImageUrlNarrowing -or $ConfirmDestructiveMigration)) { throw 'Confirmation switches are valid only with -Mode Apply.' }
    Connect-DevTarget
    Assert-ReadMetadataPermission
    $catalog=@(Get-DatabaseMigrationCatalog -Directory $script:MigrationDirectory | Where-Object Version -gt $script:BaselineVersion | Sort-Object Version)
    if (-not $catalog.Count) { throw 'No canonical migrations after V40 were found.' }
    Assert-DatabaseMigrationSequence -Migrations $catalog -BaselineVersion $script:BaselineVersion
    $state=Get-DevMigrationState
    if ($state.Initialized) {
        $plan=@(Get-DatabaseMigrationPlan -Migrations $catalog -BaselineVersion $state.BaselineVersion -History $state.History)
        Write-Host 'Recorded migration names, script hashes and validator hashes: PASS (matched against repository catalog).'
    } else {
        if ($catalog.Count -ne 0 -and (ConvertTo-DatabaseMigrationHistoryRows -History $state.History).Count -gt 0) { throw 'Migration history exists without a baseline; metadata is partial.' }
        $plan=@(Get-DatabaseMigrationPlan -Migrations $catalog -BaselineVersion $script:BaselineVersion -History @())
    }
    if ($state.Initialized) { Invoke-AppliedMigrationValidators -Plan $plan }
    else { Invoke-BaselineValidator }
    Show-Plan -Plan $plan -State $state
    if (-not $state.Initialized) {
        if ($Mode -eq 'Apply') { throw 'V40 baseline metadata is absent; this runner never creates or recovers a baseline.' }
        if ($Mode -eq 'Status') { Write-Host 'STATUS: READ-ONLY; Apply blocked because V40 baseline metadata is absent.' }
        else { Write-Host 'DryRun blocked: V40 baseline metadata is absent; no database writes performed.' }
        return
    }
    $pending=@($plan|Where-Object Status -eq 'PENDING'|Sort-Object Version)
    $null=Assert-DatabaseMigrationPreflight -Migrations $catalog -Pending $pending -BaselineVersion $script:BaselineVersion
    if ($pending.Count) { Invoke-PendingMigrationPreflights -Pending $pending }
    if ($Mode -eq 'Status') { Write-Host 'STATUS: READ-ONLY'; return }
    if ($Mode -eq 'DryRun') { Write-Host 'DryRun: no database writes performed.';return }
    if ($Mode -eq 'Backup') {
        if ($pending.Count) { throw 'Backup mode requires the canonical migration plan to be fully applied; pending migrations were found.' }
        Assert-ApplyPermissions
        Acquire-MigrationLock
        $state=Get-DevMigrationState
        if (-not $state.Initialized) { throw 'Backup mode requires canonical V40 baseline metadata.' }
        $plan=@(Get-DatabaseMigrationPlan -Migrations $catalog -BaselineVersion $state.BaselineVersion -History $state.History)
        Invoke-AppliedMigrationValidators -Plan $plan
        $pending=@($plan|Where-Object Status -eq 'PENDING'|Sort-Object Version)
        if ($pending.Count) { throw 'Backup mode stopped because the migration plan changed while acquiring the exclusive runner lock.' }
        $null=Assert-DatabaseMigrationPreflight -Migrations $catalog -Pending $pending -BaselineVersion $script:BaselineVersion
        $backupPath=Invoke-BackupGate -Pending $pending -Purpose 'DEV_SMOKE'
        Write-Host "DEV smoke-test backup: $backupPath"
        return
    }
    if (-not $pending.Count) { Write-Host 'Already up to date. No backup created; no schema or migration-ledger changes made.';return }

    Assert-ApplyPermissions
    foreach ($migration in $pending) {
        $sqlText=[IO.File]::ReadAllText($migration.Path)
        if ($sqlText -match '(?im)\b(DROP\s+(TABLE|COLUMN|CONSTRAINT|INDEX)|TRUNCATE\s+TABLE|DELETE\s+FROM|ALTER\s+TABLE[\s\S]{0,300}?ALTER\s+COLUMN)\b' -and -not $ConfirmDestructiveMigration) {
            if ($migration.Version -eq 83 -and $sqlText -match '(?im)ALTER\s+TABLE\s+master\.Products\s+ALTER\s+COLUMN\s+ImageUrl\s+nvarchar\(2048\)\s+NULL' -and $ConfirmImageUrlNarrowing) { continue }
            throw "Apply blocked: V$($migration.Version) contains a potentially destructive schema/data operation. Review the SQL and rerun with -ConfirmDestructiveMigration only after approving its impact."
        }
    }
    Acquire-MigrationLock
    $state=Get-DevMigrationState
    if (-not $state.Initialized) { throw 'V40 baseline metadata disappeared before Apply; stopping.' }
    $plan=@(Get-DatabaseMigrationPlan -Migrations $catalog -BaselineVersion $state.BaselineVersion -History $state.History)
    $pending=@($plan|Where-Object Status -eq 'PENDING'|Sort-Object Version)
    Invoke-AppliedMigrationValidators -Plan $plan
    if (-not $pending.Count) { Show-Plan -Plan $plan -State $state;Write-Host 'Already up to date. No backup created; no schema or migration-ledger changes made.';return }
    $null=Assert-DatabaseMigrationPreflight -Migrations $catalog -Pending $pending -BaselineVersion $script:BaselineVersion
    Invoke-PendingMigrationPreflights -Pending $pending -ForApply
    $backupPath=Invoke-BackupGate -Pending $pending
    foreach ($migration in $pending) { Invoke-OneMigration -Migration $migration -BackupPath $backupPath }
    $finalState=Get-DevMigrationState
    $finalPlan=@(Get-DatabaseMigrationPlan -Migrations $catalog -BaselineVersion $finalState.BaselineVersion -History $finalState.History)
    Invoke-AppliedMigrationValidators -Plan $finalPlan
    Show-Plan -Plan $finalPlan -State $finalState
    Write-Host 'DEV DATABASE MIGRATIONS: SUCCESS'
} finally {
    if ($script:Transaction) { try { $script:Transaction.Rollback() } catch {};try { $script:Transaction.Dispose() } catch {} }
    if ($script:Connection) { $script:Connection.Dispose() }
}
