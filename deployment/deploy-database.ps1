[CmdletBinding()]
param(
 [Parameter(Mandatory)][ValidateSet('QA','PROD')][string]$Environment,
 [switch]$DryRun,[switch]$Status,[switch]$InitializeBaseline,[switch]$AdoptExisting,[int]$ThroughVersion,
 [switch]$ConfirmProduction,[switch]$ResumeAfterBackupVerification,
 [string]$VerifiedBackupPath,[string]$VerificationToken,[string]$AdminVerificationReference,
 [string]$BackupDirectory='/var/opt/mssql/backups',
 [string]$QaSshHost='93.127.198.50',[string]$QaSshUser='root',
 [string]$QaSshKey="$env:USERPROFILE\.ssh\khatadhari_qa_deploy"
)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot 'DatabaseMigration.Core.psm1') -Force
$script:BaselineVersion=40;$script:ExpectedServer='srv1930195'
$script:ExpectedDatabase=if($Environment -eq 'QA'){'WhatsBizERP_QA'}else{'WhatsBizERP_PROD'}
$script:MigrationDirectory=Join-Path (Split-Path -Parent $PSScriptRoot) 'database\migrations'
$script:BaselineValidator=Join-Path $script:MigrationDirectory 'validators\Baseline-V40.validate.sql'
$script:TargetBuilder=$null;$script:Connection=$null;$script:Tunnel=$null;$script:Tx=$null
$script:NonTransactional=$false;$script:BackupPathForHistory=$null
$script:StateDirectory=if($env:LOCALAPPDATA){Join-Path $env:LOCALAPPDATA 'WhatsBiz\DatabaseMigrations'}else{Join-Path $env:TEMP 'WhatsBiz\DatabaseMigrations'}
$script:PendingManifest=Join-Path $script:StateDirectory "$($Environment.ToLowerInvariant())-$($script:ExpectedDatabase)-pending-backup.json"
$script:Actor=[Environment]::UserDomainName+'\'+[Environment]::UserName

function Get-SshCapture([string]$Command){
 $opts=@('-i',$QaSshKey,'-o','IdentitiesOnly=yes','-o','BatchMode=yes','-o','ConnectTimeout=15')
 $v=& ssh @opts "$QaSshUser@$QaSshHost" $Command 2>$null
 if($LASTEXITCODE -ne 0){throw 'Established QA deployment SSH operation failed.'}
 return ($v -join [Environment]::NewLine)
}
function Connect-Target {
 if($Environment -eq 'QA'){
  if(-not(Test-Path -LiteralPath $QaSshKey -PathType Leaf)){throw 'Established QA SSH key not found.'}
  $files=Get-SshCapture 'systemctl show whatsbiz-qa --property=EnvironmentFiles --value'
  if($files -notlike '*/etc/whatsbiz/qa.env*'){throw 'QA service is not configured with /etc/whatsbiz/qa.env.'}
  $raw=Get-SshCapture "sed -n 's/^ConnectionStrings__DefaultConnection=//p' /etc/whatsbiz/qa.env | tail -n 1"
  if([string]::IsNullOrWhiteSpace($raw)){throw 'QA SQL configuration is unavailable.'}
  $b=New-DatabaseSqlConnectionStringBuilder -ConnectionString $raw
  if($b['Initial Catalog'] -cne 'WhatsBizERP_QA'){throw 'Configured QA database is not WhatsBizERP_QA.'}
  $server=([string]$b['Data Source']) -replace '^(?i)tcp:','';$port=1433
  if($server -match '^(.*),([0-9]+)$'){$server=$Matches[1];$port=[int]$Matches[2]}
  $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0);$listener.Start();$localPort=([Net.IPEndPoint]$listener.LocalEndpoint).Port;$listener.Stop()
  $ssh=(Get-Command ssh -ErrorAction Stop).Source
  $args=@('-i',$QaSshKey,'-o','IdentitiesOnly=yes','-o','BatchMode=yes','-o','ConnectTimeout=15','-N','-L',"$($localPort):$($server):$($port)","$QaSshUser@$QaSshHost")
  $script:Tunnel=Start-Process -FilePath $ssh -ArgumentList $args -PassThru -WindowStyle Hidden
  $ready=$false
  for($i=0;$i -lt 80 -and -not $ready;$i++){Start-Sleep -Milliseconds 250;try{$c=[Net.Sockets.TcpClient]::new('127.0.0.1',$localPort);$c.Dispose();$ready=$true}catch{}}
  if(-not $ready -or $script:Tunnel.HasExited){throw 'QA SQL SSH tunnel could not be established.'}
  $b['Data Source']="127.0.0.1,$localPort";$b['Encrypt']=$false;$b['TrustServerCertificate']=$true;$script:TargetBuilder=$b
 }else{
  $raw=[Environment]::GetEnvironmentVariable('WHATSBIZ_PROD_SQL_CONNECTION')
  if([string]::IsNullOrWhiteSpace($raw)){throw 'Set WHATSBIZ_PROD_SQL_CONNECTION in the process environment; no tracked credential file is read.'}
  $b=New-DatabaseSqlConnectionStringBuilder -ConnectionString $raw
  if($b['Initial Catalog'] -cne 'WhatsBizERP_PROD'){throw 'Configured production database is not WhatsBizERP_PROD.'}
  $script:TargetBuilder=$b
 }
 $script:Connection=[System.Data.SqlClient.SqlConnection]::new($script:TargetBuilder.ConnectionString)
 try{$script:Connection.Open()}catch{$detail=Format-SafeSqlConnectionException -Exception $_.Exception -SensitiveValues @($raw,$script:TargetBuilder.ConnectionString);throw "Could not connect to the selected $Environment SQL database. $detail"}
 $cmd=$script:Connection.CreateCommand();$cmd.CommandText='SELECT @@SERVERNAME,DB_NAME(),@@VERSION;';$r=$cmd.ExecuteReader()
 try{if(-not $r.Read()){throw 'SQL identity query returned no row.'};$serverName=[string]$r.GetValue(0);$dbName=[string]$r.GetValue(1);$sqlVersion=[string]$r.GetValue(2)}
 finally{$r.Dispose();$cmd.Dispose()}
 Assert-DatabaseMigrationIdentity -Environment $Environment -ServerName $serverName -DatabaseName $dbName
 Assert-TargetMetadataVisibility
 Write-Host "Environment: $Environment";Write-Host "Server: $serverName";Write-Host "Database: $dbName"
 Write-Host "SQL Server: $(($sqlVersion -split [Environment]::NewLine | Select-Object -First 1))"
}
function Assert-TargetMetadataVisibility {
 $cmd=$script:Connection.CreateCommand();$cmd.CommandText="SELECT HAS_PERMS_BY_NAME(DB_NAME(),N'DATABASE',N'VIEW DEFINITION');"
 try{$permission=$cmd.ExecuteScalar()}finally{$cmd.Dispose()}
 Assert-DatabaseMigrationMetadataVisibility -CanViewDatabaseDefinition ([int]$permission -eq 1)
}function Get-DbState {
 $cmd=$script:Connection.CreateCommand();$cmd.CommandText="SELECT OBJECT_ID(N'deployment.DatabaseMigrationHistory',N'U'),OBJECT_ID(N'deployment.DatabaseMigrationBaseline',N'U');";$r=$cmd.ExecuteReader()
 try{$null=$r.Read();$h=-not $r.IsDBNull(0);$b=-not $r.IsDBNull(1)}finally{$r.Dispose();$cmd.Dispose()}
 if(-not $h -and -not $b){return [pscustomobject]@{Initialized=$false;BaselineVersion=$null;BaselineHash=$null;BaselineVerified=$false;History=[object[]]@()}}
 if(-not $h -or -not $b){throw 'Migration history is partially initialized; stop and inspect manually.'}
 $cmd=$script:Connection.CreateCommand();$cmd.CommandText='SELECT BaselineVersion,BaselineValidatorFile,BaselineValidatorHash FROM deployment.DatabaseMigrationBaseline WHERE SingletonId=1;';$r=$cmd.ExecuteReader()
 try{
  if(-not $r.Read()){throw 'Baseline row is missing.'};$version=$r.GetInt32(0);$validatorFile=$r.GetString(1);$hash=$r.GetString(2);if($r.Read()){throw 'Multiple baseline rows exist.'}
 }finally{$r.Dispose();$cmd.Dispose()}
 Assert-DatabaseMigrationBaselineVersion -ActualVersion $version -ExpectedVersion $script:BaselineVersion
 if($validatorFile -cne (Split-Path -Leaf $script:BaselineValidator) -or $hash -cne (Get-DatabaseMigrationHash $script:BaselineValidator)){throw 'Recorded V40 baseline validator has changed.'}
 $cmd=$script:Connection.CreateCommand();$cmd.CommandText='SELECT MigrationVersion,MigrationName,ScriptHash,ValidatorHash,IsAdopted FROM deployment.DatabaseMigrationHistory ORDER BY MigrationVersion;';$r=$cmd.ExecuteReader();$history=[Collections.Generic.List[object]]::new()
 try{while($r.Read()){$history.Add([pscustomobject]@{MigrationVersion=$r.GetInt32(0);MigrationName=$r.GetString(1);ScriptHash=$r.GetString(2);ValidatorHash=if($r.IsDBNull(3)){$null}else{$r.GetString(3)};IsAdopted=$r.GetBoolean(4)})}}
 finally{$r.Dispose();$cmd.Dispose()}
 return [pscustomobject]@{Initialized=$true;BaselineVersion=$version;BaselineHash=$hash;BaselineVerified=$false;History=[object[]]$history.ToArray()}
}
function Get-SqlBatches([string]$Text){
 if($Text -match '(?im)^\s*:\s*(r|setvar)\b'){throw 'Managed migrations cannot use SQLCMD directives; use SQL batches separated by GO.'}
 return @([regex]::Split($Text,'(?im)^\s*GO\s*(?:--.*)?$')|Where-Object{-not [string]::IsNullOrWhiteSpace($_)})
}
function Invoke-SqlFile([string]$Path,[System.Data.SqlClient.SqlTransaction]$Transaction=$null,[switch]$ReadOnly){
 if(-not(Test-Path -LiteralPath $Path -PathType Leaf)){throw "Required SQL file is missing: $Path"}
 $text=[IO.File]::ReadAllText($Path);if($ReadOnly){Assert-DatabaseMigrationValidatorReadOnly -Text $text -FileName (Split-Path -Leaf $Path)}
 foreach($batch in (Get-SqlBatches $text)){
  $cmd=$script:Connection.CreateCommand();$cmd.CommandText=$batch;$cmd.CommandTimeout=0;if($Transaction){$cmd.Transaction=$Transaction}
  try{$null=$cmd.ExecuteNonQuery()}finally{$cmd.Dispose()}
 }
}
function Invoke-BaselineCheck {Invoke-SqlFile -Path $script:BaselineValidator -ReadOnly}
function Add-History([object]$Migration,[bool]$Adopted,[string]$BackupPath,[System.Data.SqlClient.SqlTransaction]$Transaction){
 $cmd=$script:Connection.CreateCommand();$cmd.Transaction=$Transaction;$cmd.CommandText='INSERT deployment.DatabaseMigrationHistory(MigrationVersion,MigrationName,ScriptHash,ValidatorHash,AppliedBy,IsAdopted,BackupPath) VALUES(@v,@n,@h,@vh,@actor,@adopted,@backup);'
 foreach($pair in @(
  @('@v',[System.Data.SqlDbType]::Int,[int]$Migration.Version),@('@n',[System.Data.SqlDbType]::NVarChar,[string]$Migration.FileName),
  @('@h',[System.Data.SqlDbType]::Char,[string]$Migration.Hash),@('@vh',[System.Data.SqlDbType]::Char,$Migration.ValidatorHash),
  @('@actor',[System.Data.SqlDbType]::NVarChar,$script:Actor),@('@adopted',[System.Data.SqlDbType]::Bit,$Adopted),@('@backup',[System.Data.SqlDbType]::NVarChar,$BackupPath)
 )){
  $param=$cmd.Parameters.Add($pair[0],$pair[1]);if($pair[1] -eq [System.Data.SqlDbType]::NVarChar){$param.Size=switch($pair[0]){'@n'{260}'@actor'{256}'@backup'{4000}default{throw 'Unexpected string parameter.'}}}
  if($pair[1] -eq [System.Data.SqlDbType]::Char){$param.Size=64}
  $param.Value=if($null -eq $pair[2]){[DBNull]::Value}else{$pair[2]}
 }
 try{$null=$cmd.ExecuteNonQuery()}finally{$cmd.Dispose()}
}
function Acquire-MigrationLock {
 $cmd=$script:Connection.CreateCommand();$cmd.CommandText="DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=N'WhatsBiz.DatabaseMigrationRunner',@LockMode=N'Exclusive',@LockOwner=N'Session',@LockTimeout=0; IF @r<0 THROW 52190,N'Another database migration runner holds the deployment lock.',1;";try{$null=$cmd.ExecuteNonQuery()}finally{$cmd.Dispose()}
}

function Get-BackupFingerprint([object[]]$Migrations,[string]$Purpose){
 $p=@($Environment,$script:ExpectedServer,$script:ExpectedDatabase,$Purpose)+@($Migrations|Sort-Object Version|ForEach-Object{"$($_.Version)|$($_.FileName)|$($_.Hash)|$($_.ValidatorHash)"})
 $sha=[System.Security.Cryptography.SHA256]::Create();try{return [BitConverter]::ToString($sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes(($p -join [Environment]::NewLine)))).Replace('-','')}finally{$sha.Dispose()}
}
function Get-BackupMetadata([string]$Path,[string]$Name){
 $b=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($script:TargetBuilder.ConnectionString);$b['Initial Catalog']='master';$c=[System.Data.SqlClient.SqlConnection]::new($b.ConnectionString)
 try{
  $c.Open();$cmd=$c.CreateCommand();$cmd.CommandText="SELECT TOP(1) CONVERT(nvarchar(36),bs.backup_set_uuid),bs.backup_size,CONVERT(nvarchar(33),bs.backup_finish_date,126),bs.has_backup_checksums,bs.is_copy_only FROM msdb.dbo.backupset bs JOIN msdb.dbo.backupmediafamily mf ON mf.media_set_id=bs.media_set_id WHERE bs.database_name=@db AND bs.name=@name AND mf.physical_device_name=@path AND bs.type=N'D' AND bs.backup_finish_date IS NOT NULL ORDER BY bs.backup_finish_date DESC;"
  foreach($pair in @(@('@db',128,$script:ExpectedDatabase),@('@name',128,$Name),@('@path',4000,$Path))){$p=$cmd.Parameters.Add($pair[0],[System.Data.SqlDbType]::NVarChar,$pair[1]);$p.Value=$pair[2]}
   $r=$cmd.ExecuteReader();try{if(-not $r.Read()){throw 'Backup verification failed: no successfully completed backup-history row matches the requested database, backup name, and media path.'};$metadata=ConvertTo-DatabaseBackupMetadata -BackupSetGuid $r.GetValue(0) -BackupSize $r.GetValue(1) -BackupFinishDate $r.GetValue(2) -HasChecksum $r.GetValue(3) -IsCopyOnly $r.GetValue(4);$metadata|Add-Member -NotePropertyName Path -NotePropertyValue $Path;$metadata|Add-Member -NotePropertyName Name -NotePropertyValue $Name;return $metadata}finally{$r.Dispose();$cmd.Dispose()}
 }finally{$c.Dispose()}
}
function Invoke-SqlBackup([string]$Path,[string]$Name){
 $b=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($script:TargetBuilder.ConnectionString);$b['Initial Catalog']='master';$c=[System.Data.SqlClient.SqlConnection]::new($b.ConnectionString)
 try{$c.Open();$cmd=$c.CreateCommand();$cmd.CommandTimeout=0;$cmd.CommandText="BACKUP DATABASE [$($script:ExpectedDatabase)] TO DISK=N'$($Path.Replace("'","''"))' WITH COPY_ONLY,CHECKSUM,NOINIT,NAME=N'$($Name.Replace("'","''"))';";try{$null=$cmd.ExecuteNonQuery()}finally{$cmd.Dispose()}}finally{$c.Dispose()}
}
function Invoke-BackupGate([object[]]$Migrations,[string]$Purpose){
 if($ResumeAfterBackupVerification){
  if(-not(Test-Path -LiteralPath $script:PendingManifest -PathType Leaf)){throw 'No pending external-verification record exists for this target.'}
  $rawRecord=Get-Content -LiteralPath $script:PendingManifest -Raw|ConvertFrom-Json
  $m=Assert-DatabaseBackupResumeEvidence -Record $rawRecord -Environment $Environment -Server $script:ExpectedServer -Database $script:ExpectedDatabase -Purpose $Purpose -PlanFingerprint (Get-BackupFingerprint $Migrations $Purpose) -VerifiedBackupPath $VerifiedBackupPath -VerificationToken $VerificationToken -AdminVerificationReference $AdminVerificationReference
  $metadata=Get-BackupMetadata $m.BackupPath $m.BackupName
  if($metadata.BackupSetGuid -cne $m.BackupSetGuid -or $metadata.BackupSize -ne [long]$m.BackupSize -or $metadata.BackupFinishDate -cne $m.BackupFinishDate -or -not $metadata.HasChecksum -or -not $metadata.IsCopyOnly){throw 'The recorded backup media metadata is incomplete, changed, or not COPY_ONLY with CHECKSUM; resume is blocked.'}
  $m.State='ADMIN_VERIFIED';$m.AdminVerificationReference=$AdminVerificationReference;$m.VerifiedAtUtc=[DateTime]::UtcNow.ToString('o');$m|ConvertTo-Json|Set-Content -LiteralPath $script:PendingManifest -Encoding UTF8
  Write-Host "Administrator verification reference recorded: $AdminVerificationReference";return $metadata
 }
 $range=if($Migrations.Count){$sorted=@($Migrations|Sort-Object Version);"V$($sorted[0].Version)_V$($sorted[-1].Version)"}else{$Purpose}
 $stamp=[DateTime]::UtcNow.ToString('yyyyMMdd_HHmmss');$suffix=[Guid]::NewGuid().ToString('N').Substring(0,8)
 $path=$BackupDirectory.TrimEnd([char[]]@('/','\'))+'/'+$($script:ExpectedDatabase)+'_pre_'+$range+'_'+$stamp+'_'+$suffix+'.bak'
 if($path.Contains("'")){throw 'Backup path cannot contain a single quote.'}
 $name="$($script:ExpectedDatabase) pre $range $stamp"
 Write-Host "Creating COPY_ONLY CHECKSUM backup: $path"
 Invoke-SqlBackup $path $name
  try{$metadata=Get-BackupMetadata $path $name}catch{throw "BACKUP DATABASE completed, but backup metadata verification failed. A backup file may exist and was not deleted. No baseline/history or migration changes occurred. Path: $path. $($_.Exception.Message)"}
  if(-not $metadata.IsCopyOnly){throw "Backup is not recorded as COPY_ONLY; no changes will run. Backup path: $path"}
 if(-not $metadata.HasChecksum){throw "Backup is recorded without CHECKSUM; no changes will run. Backup path: $path"}
 try{
  $b=[System.Data.SqlClient.SqlConnectionStringBuilder]::new($script:TargetBuilder.ConnectionString);$b['Initial Catalog']='master';$c=[System.Data.SqlClient.SqlConnection]::new($b.ConnectionString)
  try{$c.Open();$cmd=$c.CreateCommand();$cmd.CommandTimeout=0;$cmd.CommandText="RESTORE VERIFYONLY FROM DISK=N'$($path.Replace("'","''"))' WITH CHECKSUM;";try{$null=$cmd.ExecuteNonQuery()}finally{$cmd.Dispose()}}finally{$c.Dispose()}
  Write-Host "Backup: SUCCESS ($path)";Write-Host 'RESTORE VERIFYONLY: SUCCESS'
  if(Test-Path -LiteralPath $script:PendingManifest){Remove-Item -LiteralPath $script:PendingManifest -Force}
  return $metadata
 }catch{
  $token=[Guid]::NewGuid().ToString('N');New-Item -ItemType Directory -Path $script:StateDirectory -Force|Out-Null
  [pscustomobject]@{Environment=$Environment;Server=$script:ExpectedServer;Database=$script:ExpectedDatabase;Purpose=$Purpose;PlanFingerprint=(Get-BackupFingerprint $Migrations $Purpose);BackupPath=$path;BackupName=$name;BackupSetGuid=$metadata.BackupSetGuid;BackupSize=$metadata.BackupSize;BackupFinishDate=$metadata.BackupFinishDate;HasChecksum=$metadata.HasChecksum;IsCopyOnly=$metadata.IsCopyOnly;Token=$token;State='AWAITING_ADMIN_VERIFYONLY';AdminVerificationReference=$null;VerifiedAtUtc=$null}|ConvertTo-Json|Set-Content -LiteralPath $script:PendingManifest -Encoding UTF8
  Write-Host "Backup path: $path";Write-Host "Verification token: $token"
  throw "RESTORE VERIFYONLY failed. No migration/history write occurred. Administrator: RESTORE VERIFYONLY FROM DISK=N'$path' WITH CHECKSUM;. Resume with -ResumeAfterBackupVerification -VerifiedBackupPath '$path' -VerificationToken '$token' -AdminVerificationReference '<operator, UTC time, and output reference>'."
 }
}
function Initialize-BaselineHistory {
 $tx=$script:Connection.BeginTransaction([System.Data.IsolationLevel]::Serializable)
 try{
  foreach($sql in @(
   "IF SCHEMA_ID(N'deployment') IS NULL EXEC(N'CREATE SCHEMA deployment AUTHORIZATION dbo');",
   "CREATE TABLE deployment.DatabaseMigrationBaseline(SingletonId tinyint NOT NULL CONSTRAINT PK_DatabaseMigrationBaseline PRIMARY KEY CONSTRAINT CK_DatabaseMigrationBaseline_Singleton CHECK(SingletonId=1),BaselineVersion int NOT NULL,BaselineName nvarchar(100) NOT NULL,BaselineValidatorFile nvarchar(260) NOT NULL,BaselineValidatorHash char(64) NOT NULL,InitializedAt datetimeoffset NOT NULL CONSTRAINT DF_DatabaseMigrationBaseline_InitializedAt DEFAULT(SYSUTCDATETIME()),InitializedBy nvarchar(256) NOT NULL);",
   "CREATE TABLE deployment.DatabaseMigrationHistory(MigrationVersion int NOT NULL CONSTRAINT PK_DatabaseMigrationHistory PRIMARY KEY,MigrationName nvarchar(260) NOT NULL CONSTRAINT UQ_DatabaseMigrationHistory_Name UNIQUE,ScriptHash char(64) NOT NULL,ValidatorHash char(64) NULL,AppliedAt datetimeoffset NOT NULL CONSTRAINT DF_DatabaseMigrationHistory_AppliedAt DEFAULT(SYSUTCDATETIME()),AppliedBy nvarchar(256) NOT NULL,IsAdopted bit NOT NULL CONSTRAINT DF_DatabaseMigrationHistory_IsAdopted DEFAULT(0),BackupPath nvarchar(4000) NULL);"
  )){$cmd=$script:Connection.CreateCommand();$cmd.Transaction=$tx;$cmd.CommandText=$sql;try{$null=$cmd.ExecuteNonQuery()}finally{$cmd.Dispose()}}
  $cmd=$script:Connection.CreateCommand();$cmd.Transaction=$tx;$cmd.CommandText='INSERT deployment.DatabaseMigrationBaseline(SingletonId,BaselineVersion,BaselineName,BaselineValidatorFile,BaselineValidatorHash,InitializedBy) VALUES(1,40,N''Historical V40 schema'',@file,@hash,@actor);'
  foreach($pair in @(@('@file',[System.Data.SqlDbType]::NVarChar,260,(Split-Path -Leaf $script:BaselineValidator)),@('@hash',[System.Data.SqlDbType]::Char,64,(Get-DatabaseMigrationHash $script:BaselineValidator)),@('@actor',[System.Data.SqlDbType]::NVarChar,256,$script:Actor))){$p=$cmd.Parameters.Add($pair[0],$pair[1],$pair[2]);$p.Value=$pair[3]}
  try{$null=$cmd.ExecuteNonQuery()}finally{$cmd.Dispose()};$tx.Commit()
 }catch{try{$tx.Rollback()}catch{};throw}finally{$tx.Dispose()}
}
function Write-MigrationRecord([object]$Migration,[bool]$Adopted,[string]$BackupPath,[System.Data.SqlClient.SqlTransaction]$Transaction){Add-History $Migration $Adopted $BackupPath $Transaction}
function Invoke-OneMigration([object]$Migration){
 $header=Get-Content -LiteralPath $Migration.Path -TotalCount 6
 $script:NonTransactional=@($header|Where-Object{$_ -match '^\s*--\s*WhatsBiz-Migration-Transaction:\s*NONE\s*$'}).Count -gt 0
 $script:Tx=if($script:NonTransactional){$null}else{$script:Connection.BeginTransaction([System.Data.IsolationLevel]::Serializable)}
}
function Show-Plan([object[]]$Plan,[object]$State,[string]$Mode){
 $summary=Get-DatabaseMigrationStatusSummary -Plan $Plan -State $State
 Write-Host "Baseline: $($summary.Baseline)";Write-Host "Migration history: $($summary.MigrationHistory)";if($summary.BaselineEligible){Write-Host 'Baseline initialization: eligible (V40 schema verification passed)'};$applied=$summary.Applied;$pending=$summary.Pending
 Write-Host "Applied after baseline: $(if($applied.Count){($applied|ForEach-Object{"V$($_.Version)"}) -join ', '}else{'none'})"
 if($pending.Count){Write-Host 'Pending:';foreach($m in $pending){Write-Host ("  V{0} {1} {2}" -f $m.Version,$m.Name,$m.Status)}}else{Write-Host 'Pending: none'}
 if($Mode -eq 'DryRun'){if(-not $State.Initialized){Write-Host 'Baseline history is uninitialized; initialize/adopt it explicitly before migration deployment.'};$canDeploy=$State.Initialized -and $pending.Count -gt 0;Write-Host "Would create and verify backup: $([bool]$canDeploy)";Write-Host "Would execute: $(if($canDeploy){($pending|ForEach-Object{"V$($_.Version)"}) -join ', '}else{'none until baseline is initialized'})";Write-Host 'DATABASE CHANGES: NONE'}
}
try{
 $null=Assert-DatabaseMigrationModes -DryRun:$($DryRun.IsPresent) -Status:$($Status.IsPresent) -InitializeBaseline:$($InitializeBaseline.IsPresent) -AdoptExisting:$($AdoptExisting.IsPresent) -ConfirmProduction:$($ConfirmProduction.IsPresent)
 if($AdoptExisting -and $ThroughVersion -lt 1){throw '-AdoptExisting requires -ThroughVersion.'};if(-not $AdoptExisting -and $ThroughVersion){throw '-ThroughVersion is only valid with -AdoptExisting.'}
 if($ResumeAfterBackupVerification -and (-not $VerifiedBackupPath -or -not $VerificationToken -or -not $AdminVerificationReference)){throw 'Resume requires the exact backup path, verification token, and administrator verification reference.'}
 $readonly=$DryRun -or $Status
 if(-not $readonly){Assert-ProductionConfirmation -Environment $Environment -ConfirmProduction:$ConfirmProduction}
 elseif($ConfirmProduction){throw '-ConfirmProduction is only valid for write operations.'}
 if($readonly -and ($ResumeAfterBackupVerification -or $VerifiedBackupPath -or $VerificationToken -or $AdminVerificationReference)){throw 'Backup-resume parameters cannot be used with read-only modes.'}
 Connect-Target
 $catalog=Get-DatabaseMigrationCatalog -Directory $script:MigrationDirectory;if(-not $catalog.Count){throw 'No managed migration files were found.'}
 $state=Get-DbState
 if(-not $state.Initialized){Invoke-BaselineCheck;$state.BaselineVerified=$true;Write-Host 'V40 schema verification: PASSED (read-only).'}
 $effectiveBaseline=if($state.Initialized){$state.BaselineVersion}else{$script:BaselineVersion};$history=if($state.Initialized){[object[]]$state.History}else{[object[]]@()}
 $plan=Get-DatabaseMigrationPlan -Migrations $catalog -BaselineVersion $effectiveBaseline -History $history
 if($Status){Show-Plan $plan $state 'Status';Write-Host 'STATUS: READ-ONLY';return}
 if($DryRun){Show-Plan $plan $state 'DryRun';return}
 if($InitializeBaseline){
  if($state.Initialized){throw 'Baseline is already initialized.'};Assert-DatabaseMigrationSequence -Migrations $catalog -BaselineVersion $script:BaselineVersion
  Acquire-MigrationLock;$state=Get-DbState;if($state.Initialized){throw 'Another operator initialized the baseline while this command was starting.'};Invoke-BaselineCheck
  $null=Invoke-BackupGate @() 'baseline_V40';Initialize-BaselineHistory;Write-Host 'V40 BASELINE INITIALIZED';return
 }
 if(-not $state.Initialized){throw 'Explicit -InitializeBaseline is required before deployment.'}
 Acquire-MigrationLock
 $state=Get-DbState;Assert-DatabaseMigrationBaselineInitialized -State $state;$plan=Get-DatabaseMigrationPlan -Migrations $catalog -BaselineVersion $state.BaselineVersion -History $state.History
 if($AdoptExisting){
  $adopt=@($plan|Where-Object{$_.Status -eq 'PENDING' -and $_.Version -le $ThroughVersion}|Sort-Object Version);if(-not $adopt.Count){throw 'No unapplied migrations are eligible for adoption.'}
  foreach($m in $adopt){if(-not $m.ValidatorPath){throw "V$($m.Version) cannot be adopted without a read-only validator."};Invoke-SqlFile $m.ValidatorPath -ReadOnly}
  $meta=Invoke-BackupGate $adopt 'adoption';$tx=$script:Connection.BeginTransaction([System.Data.IsolationLevel]::Serializable)
  try{foreach($m in $adopt){Write-MigrationRecord $m $true $meta.Path $tx};$tx.Commit()}catch{try{$tx.Rollback()}catch{};throw}finally{$tx.Dispose()}
  foreach($m in $adopt){Write-Host "V$($m.Version) $($m.Name) ADOPTED AND HASH-REGISTERED"}
 }else{
  $pending=@($plan|Where-Object Status -eq 'PENDING')
  if($pending.Count){
   $meta=Invoke-BackupGate $pending 'deployment';$script:BackupPathForHistory=$meta.Path
   foreach($m in $pending){
    Invoke-OneMigration $m
    try{
     Invoke-SqlFile $m.Path -Transaction $script:Tx
     Write-Host "V$($m.Version) EXECUTED"
     if($m.ValidatorPath){Invoke-SqlFile $m.ValidatorPath -Transaction $script:Tx -ReadOnly;Write-Host "V$($m.Version) VALIDATED"}
     if(-not $script:Tx){$script:Tx=$script:Connection.BeginTransaction([System.Data.IsolationLevel]::Serializable)}
     Write-MigrationRecord $m $false $script:BackupPathForHistory $script:Tx
     $script:Tx.Commit();Write-Host "V$($m.Version) RECORDED"
    }catch{if($script:Tx){try{$script:Tx.Rollback()}catch{}};throw}finally{if($script:Tx){$script:Tx.Dispose()};$script:Tx=$null}
   }
  }else{Write-Host 'No pending migrations; no backup was created.'}
 }
 $final=Get-DbState;$finalPlan=Get-DatabaseMigrationPlan -Migrations $catalog -BaselineVersion $final.BaselineVersion -History $final.History;Show-Plan $finalPlan $final 'Status'
 Write-Host 'DATABASE DEPLOYMENT: SUCCESS';Write-Host 'Application deployment: NOT PERFORMED; deploy applications separately.'
}catch{
 if($script:Tx){try{$script:Tx.Rollback()}catch{};try{$script:Tx.Dispose()}catch{};$script:Tx=$null}
 throw
}finally{
 if($script:Connection){$script:Connection.Dispose()}
 if($script:Tunnel -and -not $script:Tunnel.HasExited){Stop-Process -Id $script:Tunnel.Id -Force -ErrorAction SilentlyContinue}
}
