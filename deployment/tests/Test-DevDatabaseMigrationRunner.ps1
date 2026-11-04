$ErrorActionPreference='Stop'
Import-Module (Join-Path $PSScriptRoot '..\DatabaseMigration.Core.psm1') -Force
$script:passed=0
function Assert-True([bool]$Condition,[string]$Name){if(-not $Condition){throw "FAILED: $Name"};$script:passed++;Write-Host "PASS $Name"}

$repoRoot=Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runnerPath=Join-Path $repoRoot 'deployment\deploy-dev-database.ps1'
$runner=Get-Content -LiteralPath $runnerPath -Raw
$tokens=$null;$parseErrors=$null
[System.Management.Automation.Language.Parser]::ParseFile($runnerPath,[ref]$tokens,[ref]$parseErrors)|Out-Null
Assert-True ($parseErrors.Count -eq 0) 'DEV runner parses without PowerShell syntax errors'
$runnerAst=[System.Management.Automation.Language.Parser]::ParseFile($runnerPath,[ref]$tokens,[ref]$parseErrors)
$transactionHelper=$runnerAst.Find({param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq 'Test-NonTransactionalMigration'},$true)
Assert-True ($null -ne $transactionHelper) 'migration transaction marker detector is available for focused regression coverage'
Invoke-Expression $transactionHelper.Extent.Text
$markerTestDirectory=Join-Path ([IO.Path]::GetTempPath()) ('WhatsBiz-TransactionMarker-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $markerTestDirectory | Out-Null
try {
    $emptyMarkerPath=Join-Path $markerTestDirectory 'empty.sql'
    $singleMarkerPath=Join-Path $markerTestDirectory 'single.sql'
    $multipleMarkerPath=Join-Path $markerTestDirectory 'multiple.sql'
    [IO.File]::WriteAllLines($emptyMarkerPath,@('-- V82 migration','SELECT 1;'))
    [IO.File]::WriteAllLines($singleMarkerPath,@('-- WhatsBiz-Migration-Transaction: NONE','SELECT 1;'))
    [IO.File]::WriteAllLines($multipleMarkerPath,@('-- WhatsBiz-Migration-Transaction: NONE','-- WhatsBiz-Migration-Transaction: NONE','SELECT 1;'))
    Assert-True (-not (Test-NonTransactionalMigration -Path $emptyMarkerPath)) 'zero transaction markers normalize to an empty collection and select transactional execution'
    Assert-True (Test-NonTransactionalMigration -Path $singleMarkerPath) 'one transaction marker is detected without scalar Count failure'
    Assert-True (Test-NonTransactionalMigration -Path $multipleMarkerPath) 'multiple transaction markers are detected as a collection'
} finally { Remove-Item -LiteralPath $markerTestDirectory -Recurse -Force }
$sqlBalanced=$true
$aggregateExpressionsBalanced=$true
$aggregateOperatorsPresent=$true
foreach($template in [regex]::Matches($runner,"(?s)@'\r?\n(.*?)\r?\n'@")){
    $sql=[regex]::Replace($template.Groups[1].Value,"N?'(?:''|[^'])*'",' ');$balance=0
    foreach($character in $sql.ToCharArray()){if($character -eq '('){$balance++}elseif($character -eq ')'){$balance--};if($balance -lt 0){$sqlBalanced=$false}}
    if($balance -ne 0){$sqlBalanced=$false}
    foreach($expression in @($sql -split "`r?`n" | Where-Object { $_ -match '^\s*\+?\s*\(\s*SELECT\s+COUNT\(' })){
        $expressionBalance=0
        foreach($character in $expression.ToCharArray()){if($character -eq '('){$expressionBalance++}elseif($character -eq ')'){$expressionBalance--}}
        if($expressionBalance -ne 0){$aggregateExpressionsBalanced=$false}
    }
}
Assert-True $sqlBalanced 'embedded read-only SQL templates have balanced parentheses'
Assert-True $aggregateExpressionsBalanced 'each dynamic scalar COUNT expression is independently balanced'
foreach($functionName in @('Get-V82ArtifactCount','Get-V83ArtifactCount')){
    $function=[regex]::Match($runner,"(?s)function $functionName\b.*?(?=\r?\nfunction |\z)").Value
    $sqlTemplate=[regex]::Match($function,"(?s)@'\r?\n(.*?)\r?\n'@").Groups[1].Value
    $expressions=@($sqlTemplate -split "`r?`n" | Where-Object { $_ -match '^\s*\+?\s*\(\s*SELECT\s+COUNT\(' })
    if($expressions.Count -lt 2){$aggregateOperatorsPresent=$false;continue}
    for($i=1;$i -lt $expressions.Count;$i++){if($expressions[$i] -notmatch '^\s*\+\s*\('){$aggregateOperatorsPresent=$false}}
}
Assert-True $aggregateOperatorsPresent 'V82/V83 aggregate scalar subqueries are joined by explicit addition operators'
Assert-True ($runner -match 'ValidateSet\(''Status'',''DryRun'',''Apply'',''Backup''\)\]\[string\]\$Mode = ''Status''') 'Status is the default supported operation and Backup is explicit'
Assert-True ($runner.Contains('if ($Mode -eq ''Apply'' -and -not $ConfirmDev)')) 'Apply requires explicit DEV confirmation'
Assert-True ($runner.Contains('$script:ExpectedDataSource = ''lpc:DESKTOP-DQ0868S''') -and $runner.Contains('$script:ExpectedDatabase = ''WhatsBizERP''')) 'endpoint and database are pinned to the verified local DEV target'
Assert-True ($runner.Contains('SERVERPROPERTY(''InstanceName'')') -and $runner.Contains('$transport -cne ''Shared memory''') -and $runner.Contains('$auth -cne ''NTLM''')) 'runtime verifies exact server, default instance, NTLM and Shared Memory'
Assert-True ($runner.Contains('Get-DatabaseMigrationCatalog') -and $runner.Contains('Get-DatabaseMigrationPlan') -and $runner.Contains('Assert-DatabaseMigrationPreflight')) 'runner reuses canonical catalog, hash plan and migration preflight'
Assert-True ($runner.Contains('WITH COPY_ONLY,CHECKSUM,NOINIT') -and $runner.Contains('RESTORE VERIFYONLY') -and $runner.Contains('WITH CHECKSUM')) 'Apply creates a fresh full checksum backup and verifies it before migration'
Assert-True ($runner.Contains('APPLOCK_MODE') -and $runner.Contains('sp_getapplock') -and $runner.Contains('stopping without further writes')) 'writes are protected by verified session application-lock ownership'
Assert-True ($runner.Contains('sp_releaseapplock') -and $runner.Contains('Invoke-SqlBatches')) 'migrations are prevented from releasing the runner lock between batch checks'
Assert-True ($runner.Contains('Get-V82ArtifactCount') -and $runner.Contains('Get-V83ArtifactCount') -and $runner.Contains('Assert-ExpectedArtifacts')) 'V82 and V83 partial and post-migration schema artifacts are checked'
Assert-True ($runner.Contains('DATALENGTH(CONVERT(nvarchar(max),ImageUrl))>4096') -and $runner.Contains('IndexDependencies') -and $runner.Contains('ForeignKeyDependencies') -and $runner.Contains('SchemaBoundDependencies')) 'V83 ImageUrl width, stored-value and dependency safety is checked'
Assert-True ($runner.Contains('-ConfirmImageUrlNarrowing') -and $runner.Contains('-ConfirmDestructiveMigration')) 'narrowing and potentially destructive operations require explicit confirmation'
Assert-True ($runner -notmatch '(?i)RecoverBaseline|InitializeBaseline|AdoptExisting|WhatsBizERP_QA|WhatsBizERP_PROD') 'DEV runner cannot recover baselines or target QA/PROD'

$catalog=@(Get-DatabaseMigrationCatalog -Directory (Join-Path $repoRoot 'database\migrations') | Where-Object Version -gt 40 | Sort-Object Version)
$m82=@($catalog|Where-Object Version -eq 82)[0];$m83=@($catalog|Where-Object Version -eq 83)[0]
Assert-True ($null -ne $m82 -and $m82.ValidatorPath -and $m82.Hash -eq (Get-DatabaseMigrationHash $m82.Path) -and $m82.ValidatorHash -eq (Get-DatabaseMigrationHash $m82.ValidatorPath)) 'V82 migration and validator hashes match repository files'
Assert-True ($null -ne $m83 -and $m83.ValidatorPath -and $m83.Hash -eq (Get-DatabaseMigrationHash $m83.Path) -and $m83.ValidatorHash -eq (Get-DatabaseMigrationHash $m83.ValidatorPath)) 'V83 migration and validator hashes match repository files'
$pendingPreflight=Assert-DatabaseMigrationPreflight -Migrations $catalog -Pending @($m82,$m83) -BaselineVersion 40
Assert-True ($pendingPreflight.Status -eq 'PASS' -and $pendingPreflight.PendingCount -eq 2) 'pending V82/V83 pass canonical migration and read-only validator preflight'
$historyThrough82=@($catalog|Where-Object Version -le 82|ForEach-Object{[pscustomobject]@{MigrationVersion=$_.Version;MigrationName=$_.FileName;ScriptHash=$_.Hash;ValidatorHash=$_.ValidatorHash}})
$plan83=Get-DatabaseMigrationPlan -Migrations $catalog -BaselineVersion 40 -History $historyThrough82
$onePending=@($plan83|Where-Object Status -eq 'PENDING'|Sort-Object Version)
$onePendingPreflight=Assert-DatabaseMigrationPreflight -Migrations $catalog -Pending $onePending -BaselineVersion 40
Assert-True ($onePendingPreflight.Status -eq 'PASS' -and $onePendingPreflight.PendingCount -eq 1) 'shared preflight accepts one pending migration'
$allAppliedPreflight=Assert-DatabaseMigrationPreflight -Migrations $catalog -Pending @() -BaselineVersion 40
Assert-True ($allAppliedPreflight.Status -eq 'PASS' -and $allAppliedPreflight.PendingCount -eq 0) 'shared preflight accepts an empty pending array after all migrations are applied'
$applyNoPendingBranch=$runner.IndexOf("Already up to date. No backup created; no schema or migration-ledger changes made.")
Assert-True ($applyNoPendingBranch -ge 0 -and $runner.IndexOf('if (-not $pending.Count)', $applyNoPendingBranch) -ge 0) 'DEV Apply reports already up to date and returns before backup when no migrations are pending'
$lockedNoPending=$runner.IndexOf('if (-not $pending.Count) { Show-Plan -Plan $plan -State $state;Write-Host ''Already up to date.')
Assert-True ($lockedNoPending -gt $runner.IndexOf('Invoke-AppliedMigrationValidators -Plan $plan', $runner.IndexOf('Acquire-MigrationLock'))) 'post-lock zero-pending path validates applied schemas before returning'
$statusBranch=$runner.IndexOf('if ($Mode -eq ''Status'') { Write-Host ''STATUS: READ-ONLY''; return }')
$dryRunBranch=$runner.IndexOf('if ($Mode -eq ''DryRun'') { Write-Host ''DryRun: no database writes performed.'';return }')
$backupBranch=$runner.IndexOf('Invoke-BackupGate -Pending $pending')
Assert-True ($statusBranch -gt $runner.IndexOf('Assert-DatabaseMigrationPreflight -Migrations $catalog -Pending $pending') -and $dryRunBranch -gt $statusBranch -and $backupBranch -gt $dryRunBranch) 'Status and DryRun finish after preflight and before the backup gate for zero, one, or multiple pending migrations'
$backupModeStart=$runner.IndexOf("if (`$Mode -eq 'Backup') {")
$backupModeEnd=$runner.IndexOf("if (-not `$pending.Count) { Write-Host 'Already up to date.",$backupModeStart)
$backupMode=if($backupModeStart -ge 0 -and $backupModeEnd -gt $backupModeStart){$runner.Substring($backupModeStart,$backupModeEnd-$backupModeStart)}else{''}
Assert-True ($backupMode.Contains("if (`$pending.Count) { throw 'Backup mode requires") -and $backupMode.Contains('Assert-ApplyPermissions') -and $backupMode.Contains('Acquire-MigrationLock') -and $backupMode.Contains('Invoke-AppliedMigrationValidators') -and $backupMode.Contains("Invoke-BackupGate -Pending `$pending -Purpose 'DEV_SMOKE'") -and $backupMode -notmatch 'Invoke-OneMigration|Write-MigrationHistory') 'DEV smoke backup rechecks zero pending migrations and applied validators under the exclusive lock without running migrations'

$appliedThrough81=@($catalog|Where-Object Version -le 81|ForEach-Object{[pscustomobject]@{MigrationVersion=$_.Version;MigrationName=$_.FileName;ScriptHash=$_.Hash;ValidatorHash=$_.ValidatorHash}})
$plan82=Get-DatabaseMigrationPlan -Migrations $catalog -BaselineVersion 40 -History $appliedThrough81
Assert-True ((@($plan82|Where-Object Status -eq 'PENDING'|ForEach-Object Version) -join ',') -eq '82,83') 'a verified V41-V81 ledger plans only V82 and V83 as pending'
$historyThrough83=@($catalog|Where-Object Version -le 83|ForEach-Object{[pscustomobject]@{MigrationVersion=$_.Version;MigrationName=$_.FileName;ScriptHash=$_.Hash;ValidatorHash=$_.ValidatorHash}})
$future=[pscustomobject]@{Version=84;Name='Future';FileName='V84-Future.sql';Path='V84-Future.sql';Hash='synthetic-hash';ValidatorPath='V84-Future.validate.sql';ValidatorHash='synthetic-validator-hash'}
$plan84=Get-DatabaseMigrationPlan -Migrations (@($catalog)+@($future)) -BaselineVersion 40 -History $historyThrough83
Assert-True ((@($plan84|Where-Object Status -eq 'PENDING'|ForEach-Object Version) -join ',') -eq '84') 'catalog-driven sequencing supports V84 and later migrations'
Write-Host "DEV migration runner tests: PASS ($script:passed assertions)"
