$ErrorActionPreference='Stop'
Import-Module (Join-Path (Split-Path -Parent $PSScriptRoot) 'DatabaseMigration.Core.psm1') -Force
$script:passed=0
function Assert-True([bool]$condition,[string]$name){if(-not $condition){throw "FAILED: $name"};$script:passed++;Write-Host "PASS $name"}
function Assert-Throws([scriptblock]$action,[string]$name){$threw=$false;try{& $action}catch{$threw=$true};Assert-True $threw $name}
function New-Migration([int]$version,[string]$fileName,[string]$hash,[string]$validator=$null,[string]$validatorHash=$null){
 [pscustomobject]@{Version=$version;Name=($fileName -replace '^V\d+-','' -replace '\.sql$','');FileName=$fileName;Path=$fileName;Hash=$hash;ValidatorPath=$validator;ValidatorHash=if([string]::IsNullOrEmpty($validatorHash)){$null}else{$validatorHash}}
}
$root=Join-Path $env:TEMP ('whatsbiz-migration-tests-'+[guid]::NewGuid().ToString('N'));New-Item -ItemType Directory -Path $root|Out-Null
try{
 # 1 numeric sorting
 'x'|Set-Content (Join-Path $root 'V9-Nine.sql');'x'|Set-Content (Join-Path $root 'V10-Ten.sql')
 $catalog=Get-DatabaseMigrationCatalog $root
 Assert-True (($catalog.Version -join ',') -eq '9,10') 'numeric ordering V9 before V10'
 # 2 V100 follows V99
 $m99=New-Migration 99 V99-NinetyNine.sql h99;$m100=New-Migration 100 V100-Century.sql h100
 Assert-DatabaseMigrationSequence @($m99,$m100) 98
 Assert-True $true 'V100 follows V99 numerically'
 # 3 duplicate versions rejected
 'x'|Set-Content (Join-Path $root 'V43-FeatureA.sql');'x'|Set-Content (Join-Path $root 'V043-FeatureB.sql')
 Assert-Throws {Get-DatabaseMigrationCatalog $root} 'duplicate version rejection'
 Remove-Item (Join-Path $root 'V43-FeatureA.sql'),(Join-Path $root 'V043-FeatureB.sql')
 # 4 malformed migration filenames rejected
 'x'|Set-Content (Join-Path $root 'Vbad.sql')
 Assert-Throws {Get-DatabaseMigrationCatalog $root} 'malformed filename rejection'
 Remove-Item (Join-Path $root 'Vbad.sql')
 # 5 explicit baseline before writes
 Assert-Throws {Assert-DatabaseMigrationBaselineInitialized ([pscustomobject]@{Initialized=$false})} 'explicit baseline requirement'
 # 6 invalid V40 baseline rejected
 Assert-Throws {Assert-DatabaseMigrationBaselineVersion 39 40} 'invalid V40 baseline rejection'
 # 7 pending calculation
 $m41=New-Migration 41 V41-Feature.sql h41;$m42=New-Migration 42 V42-Feature.sql h42
 $plan=Get-DatabaseMigrationPlan @($m41,$m42) 40 @()
 Assert-True (($plan.Status -join ',') -eq 'PENDING,PENDING') 'pending migration calculation'
 $uninitializedPlan=Get-DatabaseMigrationPlan -Migrations @($m41,$m42) -BaselineVersion 40 -History $null
 Assert-True (($uninitializedPlan.Status -join ',') -eq 'PENDING,PENDING') 'history tables absent is treated as no migration rows'
 $emptyHistoryPlan=Get-DatabaseMigrationPlan -Migrations @($m41,$m42) -BaselineVersion 40 -History ([object[]]@())
 Assert-True (($emptyHistoryPlan.Status -join ',') -eq 'PENDING,PENDING') 'initialized but empty history produces pending migrations'
 $emptyHistoryList=[Collections.Generic.List[object]]::new()
 $emptyListPlan=Get-DatabaseMigrationPlan -Migrations @($m41,$m42) -BaselineVersion 40 -History $emptyHistoryList
 Assert-True ($emptyListPlan.Status -join ',' -eq 'PENDING,PENDING') 'empty generic history list is enumerated as zero records'
 $oneHistory=@([pscustomobject]@{MigrationVersion=41;MigrationName=$m41.FileName;ScriptHash=$m41.Hash;ValidatorHash=$null})
 $oneHistoryPlan=Get-DatabaseMigrationPlan -Migrations @($m41,$m42) -BaselineVersion 40 -History $oneHistory
 Assert-True (($oneHistoryPlan.Status -join ',') -eq 'APPLIED,PENDING') 'one valid history row is processed'
 $scalarHistoryPlan=Get-DatabaseMigrationPlan -Migrations @($m41,$m42) -BaselineVersion 40 -History $oneHistory[0]
 Assert-True ($scalarHistoryPlan[0].Status -eq 'APPLIED') 'single scalar migration row is normalized as one record'
 $twoHistory=@($oneHistory[0],[pscustomobject]@{MigrationVersion=42;MigrationName=$m42.FileName;ScriptHash=$m42.Hash;ValidatorHash=$null})
 $twoHistoryPlan=Get-DatabaseMigrationPlan -Migrations @($m41,$m42) -BaselineVersion 40 -History $twoHistory
 Assert-True (($twoHistoryPlan.Status -join ',') -eq 'APPLIED,APPLIED') 'multiple valid history rows are processed'
 $malformedHistoryError=$null;try{Get-DatabaseMigrationPlan -Migrations @($m41,$m42) -BaselineVersion 40 -History @([pscustomobject]@{MigrationName='V41-Feature.sql';ScriptHash='h41'})}catch{$malformedHistoryError=$_.Exception.Message}
 Assert-True ($malformedHistoryError -match 'Migration history is malformed: record 1 is missing required property ''MigrationVersion''') 'malformed history row fails with explicit history error'
 $sentinelHistoryError=$null;try{Get-DatabaseMigrationPlan -Migrations @($m41,$m42) -BaselineVersion 40 -History ([pscustomobject]@{Initialized=$false;History=@()})}catch{$sentinelHistoryError=$_.Exception.Message}
 Assert-True ($sentinelHistoryError -match 'Migration history is malformed') 'history metadata sentinel is not silently ignored'
 $emptyStatus=Get-DatabaseMigrationStatusSummary -Plan $emptyHistoryPlan -State ([pscustomobject]@{Initialized=$false;BaselineVersion=$null;BaselineVerified=$true})
 Assert-True ($emptyStatus.MigrationHistory -eq 'not initialized' -and $emptyStatus.BaselineEligible -and (($emptyStatus.Pending|ForEach-Object Version) -join ',' -eq '41,42')) 'Status summarizes verified V40 with uninitialized history and pending migrations'
 # 8 already applied skipped
 $history=@([pscustomobject]@{MigrationVersion=41;MigrationName='V41-Feature.sql';ScriptHash='h41';ValidatorHash=$null})
 $plan=Get-DatabaseMigrationPlan @($m41,$m42) 40 $history
 Assert-True (($plan.Status -join ',') -eq 'APPLIED,PENDING') 'already-applied migration skip'
 # 9 matching applied hash accepted
 Assert-True ($plan[0].Status -eq 'APPLIED') 'applied hash verification success'
 # 10 modified applied migration rejected
 $changed=New-Migration 41 V41-Feature.sql changed
 Assert-Throws {Get-DatabaseMigrationPlan @($changed) 40 $history} 'modified applied hash rejection'
 $historyWithValidator=@([pscustomobject]@{MigrationVersion=41;MigrationName='V41-Feature.sql';ScriptHash='h41';ValidatorHash='old-validator'})
 $migrationWithValidator=New-Migration 41 V41-Feature.sql h41 validator current-validator
 Assert-Throws {Get-DatabaseMigrationPlan @($migrationWithValidator) 40 $historyWithValidator} 'modified applied validator hash rejection'
 Assert-Throws {Get-DatabaseMigrationPlan @($migrationWithValidator) 40 $history} 'new validator on applied migration rejection'
 # 11 applied file missing rejected
 Assert-Throws {Get-DatabaseMigrationPlan @($m42) 40 $history} 'missing applied migration file rejection'
 # 12 chain gap rejected
 Assert-Throws {Assert-DatabaseMigrationSequence @($m41,(New-Migration 43 V43-Feature.sql h43)) 40} 'migration sequence gap rejection'
 # 13 wrong database rejected
 Assert-Throws {Assert-DatabaseMigrationIdentity QA srv1930195 WhatsBizERP_PROD} 'wrong database identity rejection'
 # 14 wrong server rejected
 Assert-Throws {Assert-DatabaseMigrationIdentity PROD other-server WhatsBizERP_PROD} 'wrong server identity rejection'
 # 15 PROD requires explicit confirmation
 Assert-Throws {Assert-ProductionConfirmation PROD} 'production confirmation requirement'
 Assert-ProductionConfirmation PROD -ConfirmProduction
 Assert-True $true 'confirmed PROD write gate'
 Assert-True ((Assert-DatabaseMigrationModes -Status) -eq 'Status') 'Status alone is a valid mode'
 Assert-True ((Assert-DatabaseMigrationModes -DryRun) -eq 'DryRun') 'DryRun alone is a valid mode'
 Assert-True ((Assert-DatabaseMigrationModes -InitializeBaseline) -eq 'InitializeBaseline') 'InitializeBaseline alone is a valid mode'
 Assert-True ((Assert-DatabaseMigrationModes -AdoptExisting) -eq 'AdoptExisting') 'AdoptExisting alone is a valid mode'
 Assert-True ((Assert-DatabaseMigrationModes -InitializeBaseline -ConfirmProduction) -eq 'InitializeBaseline') 'PROD confirmation is not counted as an operation mode'
 Assert-Throws {Assert-DatabaseMigrationModes -Status -DryRun} 'two operation modes are rejected'
 Assert-True ((Assert-DatabaseMigrationModes) -eq 'Deploy') 'omitted switches do not cause SwitchParameter-to-int conversion' Assert-DatabaseMigrationMetadataVisibility -CanViewDatabaseDefinition $true
 Assert-True $true 'metadata visibility available permits validators'
 $metadataError=$null;try{Assert-DatabaseMigrationMetadataVisibility -CanViewDatabaseDefinition $false}catch{$metadataError=$_.Exception.Message}
 Assert-True ($metadataError -eq 'Database migration validation requires VIEW DEFINITION permission. Current login cannot inspect required schema metadata.') 'missing VIEW DEFINITION reports a clear prerequisite error'
 Assert-True ($metadataError -notmatch 'Historical V40|index.*missing|schema.*missing') 'missing metadata permission is not reported as schema mismatch'
 $baselineValidatorText=Get-Content (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'database/migrations/validators/Baseline-V40.validate.sql') -Raw
 Assert-True ($baselineValidatorText -match 'i\.is_unique=1\s+AND i\.has_filter=1\s+AND i\.is_disabled=0' -and $baselineValidatorText -match 'i\.filter_definition LIKE N''%MobileNormalized%''') 'V40 normalized-mobile uniqueness/filter validation remains intact'
 $syntheticConnection='  "Server=tcp:sql.example.test,15433;Initial Catalog=WhatsBizERP_PROD;User ID=runner_user;Password=synthetic-only;Encrypt=True;TrustServerCertificate=True"  '
 $parsedConnection=New-DatabaseSqlConnectionStringBuilder -ConnectionString $syntheticConnection
 Assert-True ($parsedConnection.DataSource -eq 'tcp:sql.example.test,15433' -and $parsedConnection.InitialCatalog -eq 'WhatsBizERP_PROD') 'SQL Server host and explicit port are parsed'
 Assert-True ($parsedConnection.DataSource -eq 'tcp:sql.example.test,15433') 'environment-variable surrounding whitespace and quotes are normalized'
 Assert-True (-not $parsedConnection.IntegratedSecurity -and $parsedConnection.UserID -eq 'runner_user') 'SQL authentication connection string is preserved'
 Assert-True ($parsedConnection.Encrypt -and $parsedConnection.TrustServerCertificate) 'explicit Encrypt and TrustServerCertificate settings are preserved'
 $safeError=Format-SafeSqlConnectionException -Exception ([InvalidOperationException]::new("Login failed for user 'synthetic-user' while using $syntheticConnection")) -SensitiveValues @($syntheticConnection)
 Assert-True ($safeError -match 'InvalidOperationException' -and $safeError -match 'Login failed') 'connection errors retain safe type and diagnostic message'
 Assert-True (-not $safeError.Contains('synthetic-only') -and -not $safeError.Contains('synthetic-user') -and -not $safeError.Contains($syntheticConnection)) 'connection errors do not expose password or full connection string'
 $fieldError=Format-SafeSqlConnectionException -Exception ([InvalidOperationException]::new('Login failed for user runner_user; Password=synthetic-only'))
 Assert-True (-not $fieldError.Contains('runner_user') -and -not $fieldError.Contains('synthetic-only')) 'connection error credential fields are redacted'
 Assert-Throws {New-DatabaseSqlConnectionStringBuilder -ConnectionString 'Server=sql.example.test;BogusKeyword=value;Password=synthetic-only'} 'malformed connection string is rejected'
 $backupGuid='0d0e0a02-e111-4234-b733-b53fb06309e2';$backupFinished='2026-09-29T11:33:03.000'
 $backupMetadata=ConvertTo-DatabaseBackupMetadata -BackupSetGuid $backupGuid -BackupSize ([decimal]1234567890123) -BackupFinishDate $backupFinished -HasChecksum $true -IsCopyOnly $true
 Assert-True ($backupMetadata.BackupSetGuid -eq $backupGuid -and $backupMetadata.BackupSize -eq 1234567890123L -and $backupMetadata.HasChecksum -and $backupMetadata.IsCopyOnly) 'decimal backup-size SQL numeric metadata parses successfully'
 foreach($sizeCase in @([pscustomobject]@{Value=[int]123;Expected=123L},[pscustomobject]@{Value=[long]456;Expected=456L},[pscustomobject]@{Value=[decimal]789;Expected=789L})){
  $parsed=ConvertTo-DatabaseBackupMetadata -BackupSetGuid $backupGuid -BackupSize $sizeCase.Value -BackupFinishDate $backupFinished -HasChecksum ([byte]1) -IsCopyOnly ([int]1)
  Assert-True ($parsed.BackupSize -eq $sizeCase.Expected -and $parsed.HasChecksum -and $parsed.IsCopyOnly) "backup metadata converts $($sizeCase.Value.GetType().Name) and bit flags safely"
 }
 foreach($field in @('BackupSetGuid','BackupSize','BackupFinishDate','HasChecksum','IsCopyOnly')){
  $metadataArgs=@{BackupSetGuid=$backupGuid;BackupSize=[decimal]1;BackupFinishDate=$backupFinished;HasChecksum=$true;IsCopyOnly=$true};$metadataArgs[$field]=[DBNull]::Value
  Assert-Throws {ConvertTo-DatabaseBackupMetadata @metadataArgs} "DBNull $field backup metadata is rejected"
 }
 Assert-Throws {ConvertTo-DatabaseBackupMetadata -BackupSetGuid $backupGuid -BackupSize ([decimal]1.5) -BackupFinishDate $backupFinished -HasChecksum $true -IsCopyOnly $true} 'fractional backup size is rejected'
 Assert-Throws {ConvertTo-DatabaseBackupMetadata -BackupSetGuid $backupGuid -BackupSize '123' -BackupFinishDate $backupFinished -HasChecksum $true -IsCopyOnly $true} 'incompatible backup size type is rejected'
 Assert-Throws {ConvertTo-DatabaseBackupMetadata -BackupSetGuid 'not-a-guid' -BackupSize ([decimal]123) -BackupFinishDate $backupFinished -HasChecksum $true -IsCopyOnly $true} 'malformed backup set identifier is rejected'
 Assert-Throws {ConvertTo-DatabaseBackupMetadata -BackupSetGuid $backupGuid -BackupSize ([decimal]123) -BackupFinishDate 'not-a-date' -HasChecksum $true -IsCopyOnly $true} 'malformed backup completion time is rejected'
 Assert-Throws {ConvertTo-DatabaseBackupMetadata -BackupSetGuid $backupGuid -BackupSize ([decimal]123) -BackupFinishDate $backupFinished -HasChecksum ([byte]2) -IsCopyOnly $true} 'incompatible checksum flag is rejected'
 Assert-Throws {ConvertTo-DatabaseBackupMetadata -BackupSetGuid $backupGuid -BackupSize ([decimal]123) -BackupFinishDate $backupFinished -HasChecksum $true -IsCopyOnly $null} 'missing copy-only flag is rejected'
 $runnerSource=Get-Content (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'deployment/deploy-database.ps1') -Raw
 Assert-True ($runnerSource -match 'WITH COPY_ONLY,CHECKSUM,NOINIT' -and $runnerSource -match 'bs\.backup_finish_date IS NOT NULL') 'backup remains COPY_ONLY, CHECKSUM and completed-only'
 Assert-True ($runnerSource -match 'RESTORE VERIFYONLY FROM DISK=.*WITH CHECKSUM') 'backup still requires RESTORE VERIFYONLY WITH CHECKSUM'
 $resumeRecord=[pscustomobject]@{Environment='PROD';Server='srv1930195';Database='WhatsBizERP_PROD';Purpose='baseline_V40';PlanFingerprint='fingerprint';BackupPath='/var/opt/mssql/backups/existing.bak';BackupName='existing';BackupSetGuid=$backupGuid;BackupSize=123L;BackupFinishDate=$backupFinished;HasChecksum=$true;IsCopyOnly=$true;Token='resume-token';State='AWAITING_ADMIN_VERIFYONLY'}
 $resumeArgs=@{Record=$resumeRecord;Environment='PROD';Server='srv1930195';Database='WhatsBizERP_PROD';Purpose='baseline_V40';PlanFingerprint='fingerprint';VerifiedBackupPath='/var/opt/mssql/backups/existing.bak';VerificationToken='resume-token';AdminVerificationReference='DBA ticket QA-123, 2026-09-29, VERIFYONLY valid'}
 $acceptedResume=Assert-DatabaseBackupResumeEvidence @resumeArgs
 Assert-True ($acceptedResume.PSObject.Properties['AdminVerificationReference'] -and $acceptedResume.PSObject.Properties['VerifiedAtUtc'] -and $acceptedResume.State -eq 'AWAITING_ADMIN_VERIFYONLY') 'valid resume metadata has explicit stable audit fields'
 Assert-True ([bool]((ConvertTo-DatabaseBackupResumeRecord $resumeRecord).PSObject.Properties['AdminVerificationReference'])) 'legacy pending manifest is normalized without losing verified backup evidence'
 $missingAuditArgs=$resumeArgs.Clone();$missingAuditArgs.AdminVerificationReference=$null
 Assert-Throws {Assert-DatabaseBackupResumeEvidence @missingAuditArgs} 'missing administrator verification reference is rejected'
 $blankAuditArgs=$resumeArgs.Clone();$blankAuditArgs.AdminVerificationReference='  '
 Assert-Throws {Assert-DatabaseBackupResumeEvidence @blankAuditArgs} 'blank administrator verification reference is rejected'
 $wrongTokenArgs=$resumeArgs.Clone();$wrongTokenArgs.VerificationToken='wrong-token'
 Assert-Throws {Assert-DatabaseBackupResumeEvidence @wrongTokenArgs} 'incorrect resume verification token is rejected'
 $wrongPathArgs=$resumeArgs.Clone();$wrongPathArgs.VerifiedBackupPath='/var/opt/mssql/backups/other.bak'
 Assert-Throws {Assert-DatabaseBackupResumeEvidence @wrongPathArgs} 'mismatched resume backup path is rejected'
 Assert-True ($acceptedResume.BackupPath -eq $resumeRecord.BackupPath -and $acceptedResume.Token -eq $resumeRecord.Token) 'the existing verified backup and token remain eligible for resume'
 $resumeBranch=$runnerSource.IndexOf('if($ResumeAfterBackupVerification)');$newBackupStep=$runnerSource.IndexOf('Invoke-SqlBackup $path $name');$resumeReturn=$runnerSource.IndexOf('return $metadata',$resumeBranch);$baselineWrite=$runnerSource.IndexOf('Initialize-BaselineHistory;Write-Host ''V40 BASELINE INITIALIZED''');$migrationLoop=$runnerSource.IndexOf('foreach($m in $pending)',$baselineWrite)
 Assert-True ($resumeBranch -ge 0 -and $resumeBranch -lt $newBackupStep -and $resumeReturn -gt $resumeBranch) 'resume gate reuses the recorded backup without creating another backup'
 Assert-True ($baselineWrite -gt $resumeReturn -and $migrationLoop -gt $baselineWrite) 'baseline initialization occurs after backup gate and returns before pending migrations execute'
 $backupStep=$runnerSource.IndexOf('Invoke-SqlBackup $path $name');$metadataStep=$runnerSource.IndexOf('Get-BackupMetadata $path $name');$verifyStep=$runnerSource.IndexOf('RESTORE VERIFYONLY FROM DISK');$baselineStep=$runnerSource.IndexOf('Initialize-BaselineHistory;Write-Host ''V40 BASELINE INITIALIZED''')
 Assert-True ($backupStep -ge 0 -and $backupStep -lt $metadataStep -and $metadataStep -lt $verifyStep -and $verifyStep -lt $baselineStep) 'backup metadata and VERIFYONLY gate precede baseline history writes'
 Assert-True ($runnerSource -match 'BACKUP DATABASE completed, but backup metadata verification failed.*backup file may exist.*No baseline/history or migration changes occurred') 'metadata failure clearly reports possible backup file and stops writes'
 # 16 dry run has no write callbacks
 $global:workflowCalls=[Collections.Generic.List[string]]::new()
 Invoke-DatabaseMigrationWorkflow @($m41) DryRun { $global:workflowCalls.Add('backup') } {param($m)$global:workflowCalls.Add('execute')} {param($m)$global:workflowCalls.Add('validate')} {param($m)$global:workflowCalls.Add('record')}
 Assert-True ($global:workflowCalls.Count -eq 0) 'DryRun performs zero writes'
 # 17 Status has no write callbacks
 Invoke-DatabaseMigrationWorkflow @($m41) Status { $global:workflowCalls.Add('backup') } {param($m)$global:workflowCalls.Add('execute')} {param($m)$global:workflowCalls.Add('validate')} {param($m)$global:workflowCalls.Add('record')}
 Assert-True ($global:workflowCalls.Count -eq 0) 'Status performs zero writes'
 # 18 no pending means no backup
 Invoke-DatabaseMigrationWorkflow @() Deploy { $global:workflowCalls.Add('backup') } {param($m)$global:workflowCalls.Add('execute')} {param($m)$global:workflowCalls.Add('validate')} {param($m)$global:workflowCalls.Add('record')}
 Assert-True ($global:workflowCalls.Count -eq 0) 'zero pending skips backup'
 # 19 backup failure stops migrations
 Assert-Throws {Invoke-DatabaseMigrationWorkflow @($m41) Deploy {throw 'backup failed'} {param($m)$global:workflowCalls.Add('execute')} {param($m)$global:workflowCalls.Add('validate')} {param($m)$global:workflowCalls.Add('record')}} 'backup failure blocks migration'
 Assert-True ($global:workflowCalls.Count -eq 0) 'backup failure executes no migrations'
 # 20 VERIFYONLY failure also blocks migrations
 Assert-Throws {Invoke-DatabaseMigrationWorkflow @($m41) Deploy {throw 'VERIFYONLY failed'} {param($m)$global:workflowCalls.Add('execute')} {param($m)$global:workflowCalls.Add('validate')} {param($m)$global:workflowCalls.Add('record')}} 'VERIFYONLY failure blocks migration'
 # 21 successful migration recorded after validation
 $global:workflowCalls.Clear();$m41v=New-Migration 41 V41-Feature.sql h41 validator vhash
 Invoke-DatabaseMigrationWorkflow @($m41v) Deploy {$global:workflowCalls.Add('backup')} {param($m)$global:workflowCalls.Add('execute')} {param($m)$global:workflowCalls.Add('validate')} {param($m)$global:workflowCalls.Add("record:$($m.Hash)")}
 Assert-True (($global:workflowCalls -join ',') -eq 'backup,execute,validate,record:h41') 'successful migration validated and recorded'
 # 22 failure stops later migration
 $global:workflowCalls.Clear()
 Assert-Throws {Invoke-DatabaseMigrationWorkflow @($m41v,$m42) Deploy {} {param($m)$global:workflowCalls.Add("exec:$($m.Version)");if($m.Version -eq 42){throw 'migration failed'}} {param($m)$global:workflowCalls.Add("validate:$($m.Version)")} {param($m)$global:workflowCalls.Add("record:$($m.Version)")}} 'migration failure stops sequence'
 Assert-True (($global:workflowCalls -join ',') -eq 'exec:41,validate:41,record:41,exec:42') 'later migration not attempted after failure'
 # 23 validator failure prevents history row
 $global:workflowCalls.Clear()
 Assert-Throws {Invoke-DatabaseMigrationWorkflow @($m41v) Deploy {} {param($m)$global:workflowCalls.Add('execute')} {throw 'validator failed'} {param($m)$global:workflowCalls.Add('record')} } 'validator failure stops history recording'
 Assert-True ($global:workflowCalls -join ',' -eq 'execute') 'validator failure leaves migration unrecorded'
 # 24 partial sequence resumes remaining migrations
 $m43=New-Migration 43 V43-Feature.sql h43;$m44=New-Migration 44 V44-Feature.sql h44
 $resumePlan=Get-DatabaseMigrationPlan @($m41,$m42,$m43,$m44) 40 @(
  [pscustomobject]@{MigrationVersion=41;MigrationName=$m41.FileName;ScriptHash=$m41.Hash;ValidatorHash=$null},
  [pscustomobject]@{MigrationVersion=42;MigrationName=$m42.FileName;ScriptHash=$m42.Hash;ValidatorHash=$null},
  [pscustomobject]@{MigrationVersion=43;MigrationName=$m43.FileName;ScriptHash=$m43.Hash;ValidatorHash=$null})
 Assert-True (($resumePlan|Where-Object Status -eq 'PENDING'|ForEach-Object Version) -join ',' -eq '44') 'partial applied sequence resumes at next migration'
 # 25 QA adoption runs validators
 $m41v=New-Migration 41 V41-Feature.sql h41 validator vhash;$m41v|Add-Member Status PENDING -Force;$m42v=New-Migration 42 V42-Feature.sql h42 validator2 vh2;$m42v|Add-Member Status PENDING
 $global:workflowCalls.Clear();Invoke-DatabaseMigrationAdoption @($m41v,$m42v) 42 {param($m)$global:workflowCalls.Add("validate:$($m.Version)")} {param($m)$global:workflowCalls.Add("adopt:$($m.Version)")}
 Assert-True (($global:workflowCalls -join ',') -eq 'validate:41,validate:42,adopt:41,adopt:42') 'adoption requires successful schema validators'
 # 26 adopted migration registers current hash
 Assert-True ($global:workflowCalls -contains 'adopt:41') 'adoption registers migration hash after validation'
 # Verify validators are statically read-only and reject writes.
 foreach($f in Get-ChildItem (Join-Path (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) 'database/migrations/validators') -Filter '*.validate.sql') { Assert-DatabaseMigrationValidatorReadOnly (Get-Content $f.FullName -Raw) $f.Name }
 Assert-True $true 'repository baseline/V41/V42 validators pass read-only screening'
 Assert-Throws {Assert-DatabaseMigrationValidatorReadOnly 'SELECT 1 INTO #unsafe;' 'unsafe.validate.sql'} 'validator write operation rejection'
 # QA adoption recognizes both already-applied bootstrap migrations by external validators, without running SQL.
 $repoRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot);$real=Get-DatabaseMigrationCatalog (Join-Path $repoRoot 'database/migrations');$real=@($real|Where-Object Version -le 42);foreach($m in $real){$m|Add-Member Status PENDING -Force}
 $global:workflowCalls.Clear();Invoke-DatabaseMigrationAdoption $real 42 {param($m)Assert-DatabaseMigrationValidatorReadOnly (Get-Content $m.ValidatorPath -Raw) (Split-Path -Leaf $m.ValidatorPath);$global:workflowCalls.Add("validated:$($m.Version)")} {param($m)$global:workflowCalls.Add("hash:$($m.Version):$($m.Hash)")}
 Assert-True ($global:workflowCalls.Count -eq 4 -and $global:workflowCalls[0] -eq 'validated:41' -and $global:workflowCalls[1] -eq 'validated:42') 'QA V41/V42 adoption validates both schemas before registering hashes'
 # 27 future V57 discovered generically
 $future=Join-Path $root 'future';New-Item -ItemType Directory $future|Out-Null;'SELECT 1;'|Set-Content (Join-Path $future 'V57-FutureFeature.sql')
 $futureCatalog=Get-DatabaseMigrationCatalog $future
 Assert-True (@($futureCatalog).Count -eq 1 -and @($futureCatalog)[0].Version -eq 57) 'future V57 discovered without runner changes'
 Write-Host "Focused migration-framework tests passed: $script:passed"
}finally{Remove-Item -LiteralPath $root -Recurse -Force}
