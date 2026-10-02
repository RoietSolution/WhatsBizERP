Set-StrictMode -Version Latest

function Get-DatabaseMigrationCatalog {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Directory)
    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) { throw "Migration directory does not exist: $Directory" }
    $items = [System.Collections.Generic.List[object]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $Directory -File -Filter '*.sql') {
        if ($file.Name -match '\.validate\.sql$') { continue }
        if ($file.Name -notmatch '^V(?<version>[0-9]+)-(?<name>[A-Za-z0-9][A-Za-z0-9._-]*)\.sql$') {
            if ($file.Name -match '^V') { throw "Malformed migration filename '$($file.Name)'. Expected V<number>-Description.sql." }
            continue
        }
        $version = [int]::Parse($Matches.version, [Globalization.CultureInfo]::InvariantCulture)
        if ($version -lt 1) { throw "Migration version must be positive: $($file.Name)" }
        $validatorPath = Join-Path $Directory "$($file.BaseName).validate.sql"
        if (-not (Test-Path -LiteralPath $validatorPath -PathType Leaf)) { $validatorPath = Join-Path (Join-Path $Directory 'validators') "$($file.BaseName).validate.sql" }
        $hasValidator = Test-Path -LiteralPath $validatorPath -PathType Leaf
        $items.Add([pscustomobject]@{
            Version = $version; Name = $Matches.name; FileName = $file.Name; Path = $file.FullName
            Hash = (Get-DatabaseMigrationHash -Path $file.FullName)
            ValidatorPath = if ($hasValidator) { $validatorPath } else { $null }
            ValidatorHash = if ($hasValidator) { Get-DatabaseMigrationHash -Path $validatorPath } else { $null }
        })
    }
    $duplicates = @($items | Group-Object Version | Where-Object Count -gt 1)
    if ($duplicates.Count -gt 0) { throw "Duplicate migration version(s): $(($duplicates | ForEach-Object Name) -join ', ')."}
    return @($items | Sort-Object Version)
}

function Get-DatabaseMigrationHash {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Path)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { $stream = [IO.File]::OpenRead($Path); try { return [BitConverter]::ToString($sha.ComputeHash($stream)).Replace([string][char]45,[string]::Empty) } finally { $stream.Dispose() } }
    finally { $sha.Dispose() }
}

function Assert-DatabaseMigrationSequence {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object[]]$Migrations,[Parameter(Mandatory)][int]$BaselineVersion)
    $expected = $BaselineVersion + 1
    foreach ($migration in ($Migrations | Sort-Object Version)) {
        if ($migration.Version -le $BaselineVersion) { continue }
        if ($migration.Version -ne $expected) { throw "Migration sequence gap: expected V$expected but found V$($migration.Version). Resolve the gap before deployment." }
        $expected++
    }
}

function ConvertTo-DatabaseMigrationHistoryRows {
    [CmdletBinding()]
    param([Parameter()][AllowNull()][object]$History)
    if ($null -eq $History) { return ,([object[]]@()) }
    if ($History -is [string]) { throw 'Migration history is malformed: expected migration row objects, received a scalar string.' }
    if ($History -is [Collections.IDictionary]) { $items=@($History) } elseif ($History -is [Collections.IEnumerable]) { $items=@($History) } else { $items=@($History) }
    if ($items.Count -eq 0) { return ,([object[]]@()) }
    $validated=[Collections.Generic.List[object]]::new()
    $position=0
    foreach($item in $items){
        $position++
        if($null -eq $item){throw "Migration history is malformed: record $position is null."}
        foreach($required in @('MigrationVersion','MigrationName','ScriptHash','ValidatorHash')){
            if($null -eq $item.PSObject.Properties[$required]){throw "Migration history is malformed: record $position is missing required property '$required'."}
        }
        $version=0
        if(-not [int]::TryParse([string]$item.MigrationVersion,[ref]$version) -or $version -lt 1){throw "Migration history is malformed: record $position has an invalid MigrationVersion."}
        if([string]::IsNullOrWhiteSpace([string]$item.MigrationName) -or [string]::IsNullOrWhiteSpace([string]$item.ScriptHash)){throw "Migration history is malformed: record V$version has an empty migration name or script hash."}
        $validated.Add($item)
    }
    return ,([object[]]$validated.ToArray())
}

# SQL projection types: uuid/finish are nvarchar, backup_size is numeric(20,0) (SqlClient Decimal), and both flags are bit.
function ConvertTo-DatabaseBackupMetadata {
    [CmdletBinding()]
    param(
        [Parameter()][AllowNull()][object]$BackupSetGuid,
        [Parameter()][AllowNull()][object]$BackupSize,
        [Parameter()][AllowNull()][object]$BackupFinishDate,
        [Parameter()][AllowNull()][object]$HasChecksum,
        [Parameter()][AllowNull()][object]$IsCopyOnly
    )
    foreach($field in @(@('BackupSetGuid',$BackupSetGuid),@('BackupSize',$BackupSize),@('BackupFinishDate',$BackupFinishDate),@('HasChecksum',$HasChecksum),@('IsCopyOnly',$IsCopyOnly))){
        if($null -eq $field[1] -or $field[1] -is [DBNull]){throw "Backup verification failed: SQL Server returned NULL for '$($field[0])' metadata."}
    }
    $guid=[guid]::Empty
    if($BackupSetGuid -is [guid]){$guid=$BackupSetGuid}elseif(-not [guid]::TryParse([string]$BackupSetGuid,[ref]$guid)){throw 'Backup verification failed: backup-set identifier metadata is malformed.'}
    $numericType=[Type]::GetTypeCode($BackupSize.GetType())
    if($numericType -notin @([TypeCode]::SByte,[TypeCode]::Byte,[TypeCode]::Int16,[TypeCode]::UInt16,[TypeCode]::Int32,[TypeCode]::UInt32,[TypeCode]::Int64,[TypeCode]::UInt64,[TypeCode]::Decimal)){throw 'Backup verification failed: backup-size metadata has an incompatible SQL/CLR type.'}
    try{$size=[Convert]::ToDecimal($BackupSize,[Globalization.CultureInfo]::InvariantCulture)}catch{throw 'Backup verification failed: backup-size metadata cannot be converted safely.'}
    if($size -le 0 -or $size -ne [decimal]::Truncate($size) -or $size -gt [decimal][long]::MaxValue){throw 'Backup verification failed: backup-size metadata is outside the supported positive Int64 range.'}
    $sizeInt64=[decimal]::ToInt64($size)
    if($BackupFinishDate -is [datetime]){$finish=$BackupFinishDate.ToString('o',[Globalization.CultureInfo]::InvariantCulture)}elseif($BackupFinishDate -is [datetimeoffset]){$finish=$BackupFinishDate.ToString('o',[Globalization.CultureInfo]::InvariantCulture)}elseif($BackupFinishDate -is [string]){$parsed=[datetime]::MinValue;if(-not [datetime]::TryParse($BackupFinishDate,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::RoundtripKind,[ref]$parsed)){throw 'Backup verification failed: backup completion-time metadata is malformed.'};$finish=$BackupFinishDate}else{throw 'Backup verification failed: backup completion-time metadata has an incompatible SQL/CLR type.'}
    foreach($flag in @(@('HasChecksum',$HasChecksum),@('IsCopyOnly',$IsCopyOnly))){
        $v=$flag[1]
        if($v -is [bool]){continue}
        $flagType=[Type]::GetTypeCode($v.GetType());if($flagType -notin @([TypeCode]::SByte,[TypeCode]::Byte,[TypeCode]::Int16,[TypeCode]::UInt16,[TypeCode]::Int32,[TypeCode]::UInt32,[TypeCode]::Int64,[TypeCode]::UInt64)){throw "Backup verification failed: '$($flag[0])' metadata has an incompatible SQL/CLR type."}
        $n=[Convert]::ToInt32($v,[Globalization.CultureInfo]::InvariantCulture);if($n -notin @(0,1)){throw "Backup verification failed: '$($flag[0])' metadata must be 0 or 1."}
    }
    $checksum=if($HasChecksum -is [bool]){$HasChecksum}else{[Convert]::ToInt32($HasChecksum,[Globalization.CultureInfo]::InvariantCulture) -eq 1}
    $copyOnly=if($IsCopyOnly -is [bool]){$IsCopyOnly}else{[Convert]::ToInt32($IsCopyOnly,[Globalization.CultureInfo]::InvariantCulture) -eq 1}
    [pscustomobject]@{BackupSetGuid=$guid.ToString('D');BackupSize=$sizeInt64;BackupFinishDate=$finish;HasChecksum=$checksum;IsCopyOnly=$copyOnly}
}
function ConvertTo-DatabaseBackupResumeRecord {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$Record)
    $required = @('Environment','Server','Database','Purpose','PlanFingerprint','BackupPath','BackupName','BackupSetGuid','BackupSize','BackupFinishDate','HasChecksum','IsCopyOnly','Token','State')
    foreach ($name in $required) {
        if ($null -eq $Record.PSObject.Properties[$name] -or $null -eq $Record.$name -or [string]::IsNullOrWhiteSpace([string]$Record.$name)) {
            throw "Pending backup record is malformed: required field '$name' is missing or empty."
        }
    }
    if ($Record.State -notin @('AWAITING_ADMIN_VERIFYONLY','ADMIN_VERIFIED')) { throw 'Pending backup record is malformed: state is not eligible for external verification resume.' }
    # Rebuild with a stable shape; old pending manifests predate the audit fields.
    [pscustomobject]@{
        Environment=[string]$Record.Environment; Server=[string]$Record.Server; Database=[string]$Record.Database
        Purpose=[string]$Record.Purpose; PlanFingerprint=[string]$Record.PlanFingerprint
        BackupPath=[string]$Record.BackupPath; BackupName=[string]$Record.BackupName
        BackupSetGuid=[string]$Record.BackupSetGuid; BackupSize=$Record.BackupSize
        BackupFinishDate=[string]$Record.BackupFinishDate; HasChecksum=[bool]$Record.HasChecksum
        IsCopyOnly=[bool]$Record.IsCopyOnly; Token=[string]$Record.Token; State=[string]$Record.State
        AdminVerificationReference=if ($null -ne $Record.PSObject.Properties['AdminVerificationReference']) { [string]$Record.AdminVerificationReference } else { $null }
        VerifiedAtUtc=if ($null -ne $Record.PSObject.Properties['VerifiedAtUtc']) { [string]$Record.VerifiedAtUtc } else { $null }
    }
}

function Assert-DatabaseBackupResumeEvidence {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][object]$Record,[Parameter(Mandatory)][string]$Environment,
        [Parameter(Mandatory)][string]$Server,[Parameter(Mandatory)][string]$Database,
        [Parameter(Mandatory)][string]$Purpose,[Parameter(Mandatory)][string]$PlanFingerprint,
        [Parameter(Mandatory)][string]$VerifiedBackupPath,[Parameter(Mandatory)][string]$VerificationToken,
        [Parameter(Mandatory)][AllowEmptyString()][string]$AdminVerificationReference
    )
    $stable=ConvertTo-DatabaseBackupResumeRecord -Record $Record
    if ($stable.Environment -cne $Environment -or $stable.Server -cne $Server -or $stable.Database -cne $Database -or $stable.Purpose -cne $Purpose -or $stable.PlanFingerprint -cne $PlanFingerprint) { throw 'Pending backup record does not match this target and exact migration plan.' }
    if ([IO.Path]::GetFullPath($VerifiedBackupPath) -cne [IO.Path]::GetFullPath($stable.BackupPath) -or $VerificationToken -cne $stable.Token) { throw 'Backup path/token does not match the pending verification record.' }
    if ([string]::IsNullOrWhiteSpace($AdminVerificationReference)) { throw 'Provide a nonblank audit reference for the administrator RESTORE VERIFYONLY result.' }
    return $stable
}
function Get-DatabaseMigrationStatusSummary {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object[]]$Plan,[Parameter(Mandatory)][object]$State)
    $applied=@($Plan|Where-Object Status -eq 'APPLIED')
    $pending=@($Plan|Where-Object Status -eq 'PENDING')
    [pscustomobject]@{
        Baseline=if($State.Initialized){'V'+$State.BaselineVersion}else{'V40 schema verified; baseline NOT INITIALIZED'}
        MigrationHistory=if($State.Initialized){'initialized'}else{'not initialized'}
        BaselineEligible=([bool]$State.BaselineVerified -and -not [bool]$State.Initialized)
        Applied=$applied
        Pending=$pending
    }
}
function Get-DatabaseMigrationPlan {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object[]]$Migrations,[Parameter(Mandatory)][int]$BaselineVersion,[Parameter()][AllowNull()][object]$History=@())
    $historyRows=ConvertTo-DatabaseMigrationHistoryRows -History $History
    Assert-DatabaseMigrationSequence -Migrations $Migrations -BaselineVersion $BaselineVersion
    $byVersion = @{}; foreach ($migration in $Migrations) { $byVersion[[int]$migration.Version] = $migration }
    $applied = @{}
    foreach ($row in $historyRows) {
        $v = [int]$row.MigrationVersion
        if ($v -le $BaselineVersion) { throw "Managed migration history contains V$v at or below baseline V$BaselineVersion." }
        if (-not $byVersion.ContainsKey($v)) { throw "Applied migration V$v ($($row.MigrationName)) is missing from the repository." }
        $file = $byVersion[$v]
        if ($file.FileName -cne $row.MigrationName) { throw "Applied migration V$v filename has changed from '$($row.MigrationName)' to '$($file.FileName)'." }
        if ($file.Hash -cne $row.ScriptHash) { throw "Applied migration V$v has been modified after deployment." }
        if ($row.PSObject.Properties.Name -contains 'ValidatorHash' -and $file.ValidatorHash -cne $row.ValidatorHash) { throw "Applied migration V$v validator has been added, modified or removed." }
        $applied[$v] = $true
    }
    $expected = $BaselineVersion + 1
    foreach ($v in @($historyRows | ForEach-Object { [int]$_.MigrationVersion } | Sort-Object)) {
        if ($v -ne $expected) { throw "Migration history gap: expected V$expected but history contains V$v." }
        $expected++
    }
    $plan = foreach ($migration in $Migrations) {
        if ($migration.Version -le $BaselineVersion) { continue }
        [pscustomobject]@{
            Version=$migration.Version; Name=$migration.Name; FileName=$migration.FileName; Path=$migration.Path
            Hash=$migration.Hash; ValidatorPath=$migration.ValidatorPath; ValidatorHash=$migration.ValidatorHash
            Status=if ($applied.ContainsKey([int]$migration.Version)) {'APPLIED'} else {'PENDING'}
        }
    }
    return @($plan)
}

function Assert-DatabaseMigrationIdentity {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Environment,[Parameter(Mandatory)][string]$ServerName,[Parameter(Mandatory)][string]$DatabaseName)
    $expectedDatabase = if ($Environment -ceq 'QA') {'WhatsBizERP_QA'} elseif ($Environment -ceq 'PROD') {'WhatsBizERP_PROD'} else { throw "Unsupported environment '$Environment'." }
    if ($DatabaseName -cne $expectedDatabase) { throw "Identity guard rejected database '$DatabaseName'; expected '$expectedDatabase'." }
    if ($ServerName -cne 'srv1930195') { throw "Identity guard rejected server '$ServerName'; expected 'srv1930195'." }
}


function Assert-DatabaseMigrationValidatorReadOnly {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Text,[Parameter(Mandatory)][string]$FileName)
    $safe=[regex]::Replace($Text,'(?s)/\*.*?\*/',' ')
    $safe=[regex]::Replace($safe,'(?m)--.*$',' ')
    $safe=[regex]::Replace($safe,"N?'(?:''|[^'])*'",' ')
    if($safe -match '(?i)\b(INSERT|UPDATE|DELETE|MERGE|CREATE|ALTER|DROP|TRUNCATE|EXEC|BACKUP|RESTORE|GRANT|REVOKE|DENY|DBCC|INTO|BULK|OPENROWSET|OPENDATASOURCE|WAITFOR)\b|\bNEXT\s+VALUE\s+FOR\b'){
        throw "Validator '$FileName' contains a write-capable or administrative SQL operation."
    }
}
function Assert-DatabaseMigrationBaselineVersion {
    [CmdletBinding()]
    param([Parameter(Mandatory)][int]$ActualVersion,[int]$ExpectedVersion=40)
    if($ActualVersion -ne $ExpectedVersion){throw "Database baseline V$ActualVersion differs from expected V$ExpectedVersion."}
}
function Assert-DatabaseMigrationBaselineInitialized {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object]$State)
    if(-not $State.Initialized){throw 'Explicit historical baseline initialization is required before managed migrations.'}
}
function Assert-ProductionConfirmation {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Environment,[switch]$ConfirmProduction)
    if ($Environment -ceq 'PROD' -and -not $ConfirmProduction) { throw 'Production writes require -ConfirmProduction.' }
}

function New-DatabaseSqlConnectionStringBuilder {
    [CmdletBinding()]
    param([Parameter(Mandatory)][AllowEmptyString()][string]$ConnectionString)
    $normalized = $ConnectionString.Trim()
    if ($normalized.Length -ge 2 -and (($normalized[0] -eq [char]34 -and $normalized[$normalized.Length - 1] -eq [char]34) -or ($normalized[0] -eq [char]39 -and $normalized[$normalized.Length - 1] -eq [char]39))) {
        $normalized = $normalized.Substring(1, $normalized.Length - 2).Trim()
    }
    try {
        return [System.Data.SqlClient.SqlConnectionStringBuilder]::new($normalized)
    } catch {
        throw (Format-SafeSqlConnectionException -Exception $_.Exception -SensitiveValues @($ConnectionString, $normalized))
    }
}

function Format-SafeSqlConnectionException {
    [CmdletBinding()]
    param([Parameter(Mandatory)][System.Exception]$Exception,[string[]]$SensitiveValues=@())
    $parts=[Collections.Generic.List[string]]::new()
    $current=$Exception
    while($null -ne $current){
        $message=[string]$current.Message
        foreach($sensitive in $SensitiveValues){if(-not [string]::IsNullOrEmpty($sensitive)){$message=$message.Replace($sensitive,'<connection string redacted>')}}
        $message=[regex]::Replace($message,'(?i)\b(Password|Pwd|User\s*ID|UID)\s*=\s*(?:"[^"]*"|''[^'']*''|[^;\s]+)','$1=<redacted>')
        $message=[regex]::Replace($message,'(?i)(Login failed for user\s+)[^.;]+','$1<redacted>')
        $parts.Add("$($current.GetType().FullName): $message")
        $current=$current.InnerException
    }
    return $parts -join ' --> '
}
function Assert-DatabaseMigrationMetadataVisibility {
    [CmdletBinding()]
    param([Parameter(Mandatory)][bool]$CanViewDatabaseDefinition)
    if (-not $CanViewDatabaseDefinition) {
        throw 'Database migration validation requires VIEW DEFINITION permission. Current login cannot inspect required schema metadata.'
    }
}
function Assert-DatabaseMigrationModes {
    [CmdletBinding()]
    param(
        [switch]$DryRun,
        [switch]$Status,
        [switch]$InitializeBaseline,
        [switch]$AdoptExisting,
        [switch]$ConfirmProduction
    )
    $modes = [Collections.Generic.List[string]]::new()
    if ($DryRun.IsPresent) { $modes.Add('DryRun') }
    if ($Status.IsPresent) { $modes.Add('Status') }
    if ($InitializeBaseline.IsPresent) { $modes.Add('InitializeBaseline') }
    if ($AdoptExisting.IsPresent) { $modes.Add('AdoptExisting') }
    if ($modes.Count -gt 1) {
        throw 'Choose one mode: -DryRun, -Status, -InitializeBaseline, or -AdoptExisting.'
    }
    if ($modes.Count -eq 0) { return 'Deploy' }
    return $modes[0]
}

function Invoke-DatabaseMigrationWorkflow {
    [CmdletBinding()]
    param(
        [Parameter()][AllowEmptyCollection()][object[]]$Pending = @(),[Parameter(Mandatory)][ValidateSet('DryRun','Status','Deploy')][string]$Mode,
        [Parameter(Mandatory)][scriptblock]$EnsureBackup,[Parameter(Mandatory)][scriptblock]$ExecuteMigration,
        [Parameter(Mandatory)][scriptblock]$ValidateMigration,[Parameter(Mandatory)][scriptblock]$RecordMigration
    )
    if ($Mode -ne 'Deploy' -or $Pending.Count -eq 0) { return }
    & $EnsureBackup
    foreach ($migration in $Pending) {
        & $ExecuteMigration $migration
        if ($migration.ValidatorPath) { & $ValidateMigration $migration }
        & $RecordMigration $migration
    }
}

function Invoke-DatabaseMigrationAdoption {
    [CmdletBinding()]
    param([Parameter(Mandatory)][object[]]$Migrations,[Parameter(Mandatory)][int]$ThroughVersion,[Parameter(Mandatory)][scriptblock]$ValidateMigration,[Parameter(Mandatory)][scriptblock]$RecordMigration)
    $adopt = @($Migrations | Where-Object { $_.Status -eq 'PENDING' -and $_.Version -le $ThroughVersion } | Sort-Object Version)
    if ($adopt.Count -eq 0) { throw "There are no pending migrations to adopt through V$ThroughVersion." }
    foreach ($migration in $adopt) {
        if (-not $migration.ValidatorPath) { throw "Cannot adopt V$($migration.Version): a read-only validator is required." }
        & $ValidateMigration $migration
    }
    foreach ($migration in $adopt) { & $RecordMigration $migration }
    return
}

Export-ModuleMember -Function ConvertTo-DatabaseBackupMetadata,ConvertTo-DatabaseBackupResumeRecord,Assert-DatabaseBackupResumeEvidence,ConvertTo-DatabaseMigrationHistoryRows,Get-DatabaseMigrationStatusSummary,Get-DatabaseMigrationCatalog,Get-DatabaseMigrationHash,Assert-DatabaseMigrationSequence,Get-DatabaseMigrationPlan,Assert-DatabaseMigrationIdentity,Assert-ProductionConfirmation,Assert-DatabaseMigrationBaselineVersion,Assert-DatabaseMigrationBaselineInitialized,Assert-DatabaseMigrationValidatorReadOnly,Assert-DatabaseMigrationModes,Assert-DatabaseMigrationMetadataVisibility,New-DatabaseSqlConnectionStringBuilder,Format-SafeSqlConnectionException,Invoke-DatabaseMigrationWorkflow,Invoke-DatabaseMigrationAdoption

