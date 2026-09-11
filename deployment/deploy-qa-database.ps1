[CmdletBinding()]
param(
    [string] $SshHost = '93.127.198.50',
    [string] $SshUser = 'root',
    [string] $SshKey = "$env:USERPROFILE\.ssh\khatadhari_qa_deploy",
    [string] $Configuration = 'Release',
    [switch] $PlanOnly,
    [switch] $ApproveTableRebuild,
    [switch] $ResumeHardening,
    [switch] $SkipBuild
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$expectedDatabase = 'WhatsBizERP_QA'
$expectedEnvironmentFile = '/etc/whatsbiz/qa.env'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'database\WhatsBiz.Database\WhatsBiz.Database.sqlproj'
$dacpac = Join-Path $repositoryRoot "database\WhatsBiz.Database\bin\$Configuration\WhatsBiz.Database.dacpac"
$scriptRoot = Join-Path $repositoryRoot 'database\WhatsBiz.Database\Scripts'
$sqlPackage = (Get-Command SqlPackage -ErrorAction Stop).Source
$sqlCmd = (Get-Command sqlcmd -ErrorAction Stop).Source
$ssh = (Get-Command ssh -ErrorAction Stop).Source
$sshTarget = "$SshUser@$SshHost"
$sshOptions = @('-i',$SshKey,'-o','IdentitiesOnly=yes','-o','BatchMode=yes','-o','ConnectTimeout=15')
$tunnel = $null
$oldSqlCmdPassword = $env:SQLCMDPASSWORD
$serviceStopped = $false

function Invoke-SshCapture([string] $Command) {
    $result = & $ssh @sshOptions $sshTarget $Command
    if ($LASTEXITCODE -ne 0) { throw "QA SSH command failed with exit code $LASTEXITCODE." }
    return ($result -join "`n")
}

function Invoke-SqlQuery([string] $Database,[string] $Query,[switch] $Quiet) {
    $arguments = @('-S',$script:targetBuilder.DataSource,'-d',$Database,'-U',$script:targetBuilder.UserID,'-C','-b','-Q',$Query)
    if ($Quiet) { $arguments += @('-h','-1','-W') }
    & $sqlCmd @arguments
    if ($LASTEXITCODE -ne 0) { throw "SQL query failed against verified QA database '$Database'." }
}

function Invoke-SqlFile([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) { throw "Required SQL deployment file is missing: $Path" }
    Write-Host "Applying $(Split-Path -Leaf $Path)"
    & $sqlCmd -S $script:targetBuilder.DataSource -d $expectedDatabase -U $script:targetBuilder.UserID -C -b -i $Path
    if ($LASTEXITCODE -ne 0) { throw "SQL deployment file failed: $(Split-Path -Leaf $Path)" }
}

function Invoke-IdentityGate {
    Invoke-SqlQuery $expectedDatabase "SET NOCOUNT ON; IF DB_NAME()<>N'WhatsBizERP_QA' THROW 51690,N'Wrong database target.',1; SELECT N'QA_TARGET_VERIFIED' Result;" -Quiet
}

try {
    if (-not (Test-Path -LiteralPath $SshKey)) { throw "Established QA SSH key not found: $SshKey" }
    if (-not (Test-Path -LiteralPath $project)) { throw "Database project not found: $project" }

    $environmentFiles = Invoke-SshCapture "systemctl show whatsbiz-qa --property=EnvironmentFiles --value"
    if ($environmentFiles -notlike "*$expectedEnvironmentFile*") {
        throw "whatsbiz-qa does not resolve $expectedEnvironmentFile."
    }

    # Capture only the one required setting and never write it to the console or disk.
    $connectionString = Invoke-SshCapture "sed -n 's/^ConnectionStrings__DefaultConnection=//p' $expectedEnvironmentFile | tail -n 1"
    if ([string]::IsNullOrWhiteSpace($connectionString)) { throw 'QA database connection is not configured.' }
    $configuredBuilder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($connectionString.Trim().Trim('"').Trim("'"))
    if ($configuredBuilder.InitialCatalog -cne $expectedDatabase) {
        throw "QA database target must be exactly $expectedDatabase."
    }
    if ($configuredBuilder.DataSource -match '(?i)DESKTOP-DQ0868S' -or $configuredBuilder.InitialCatalog -ceq 'WhatsBizERP') {
        throw 'Development SQL Server/database target is forbidden.'
    }
    if ([string]::IsNullOrWhiteSpace($configuredBuilder.UserID) -or [string]::IsNullOrWhiteSpace($configuredBuilder.Password)) {
        throw 'QA SQL authentication credentials are unavailable to the deployment workflow.'
    }

    $configuredServer = $configuredBuilder.DataSource -replace '^(?i)tcp:',''
    $configuredHost = $configuredServer
    $configuredPort = 1433
    if ($configuredServer -match '^(.*),(\d+)$') {
        $configuredHost = $Matches[1]
        $configuredPort = [int]$Matches[2]
    }
    if ($configuredHost -match '^(?i)(localhost|127\.0\.0\.1|\.)$') { $configuredHost = '127.0.0.1' }
    Write-Host "Verified QA SQL target: $($configuredBuilder.DataSource) / $expectedDatabase"

    if (-not $SkipBuild) {
        & dotnet build $project --configuration $Configuration --verbosity minimal
        if ($LASTEXITCODE -ne 0) { throw 'Database project build failed.' }
    }
    if (-not (Test-Path -LiteralPath $dacpac)) { throw "Current DACPAC not found: $dacpac" }

    $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback,0)
    $listener.Start()
    $localPort = ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port
    $listener.Stop()
    $tunnelArguments = @($sshOptions + @('-N','-L',"$localPort`:$configuredHost`:$configuredPort",$sshTarget))
    $tunnel = Start-Process -FilePath $ssh -ArgumentList $tunnelArguments -PassThru -WindowStyle Hidden
    $connected = $false
    for ($attempt=0; $attempt -lt 80 -and -not $connected; $attempt++) {
        Start-Sleep -Milliseconds 250
        try { $client=[System.Net.Sockets.TcpClient]::new('127.0.0.1',$localPort); $client.Dispose(); $connected=$true } catch { }
    }
    if (-not $connected -or $tunnel.HasExited) { throw 'Unable to establish the QA SQL SSH tunnel.' }

    $targetBuilder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new($connectionString.Trim().Trim('"').Trim("'"))
    $targetBuilder['Data Source'] = "127.0.0.1,$localPort"
    $targetBuilder['Initial Catalog'] = $expectedDatabase
    $targetBuilder['Encrypt'] = $false
    $targetBuilder['TrustServerCertificate'] = $true
    $env:SQLCMDPASSWORD = $targetBuilder.Password

    $exists = ((& $sqlCmd -S $targetBuilder.DataSource -d master -U $targetBuilder.UserID -C -b -h -1 -W -Q "SET NOCOUNT ON; SELECT CASE WHEN DB_ID(N'WhatsBizERP_QA') IS NULL THEN 0 ELSE 1 END;" | Out-String).Trim())
    if ($LASTEXITCODE -ne 0 -or $exists -notin @('0','1')) { throw 'Could not verify the QA database safely.' }

    if ($ResumeHardening -and $PlanOnly) { throw 'ResumeHardening cannot be combined with PlanOnly.' }
    if (-not $ResumeHardening) {
    $planDirectory = Join-Path $env:TEMP "whatsbiz-qa-db-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))"
    New-Item -ItemType Directory -Path $planDirectory -Force | Out-Null
    $report = Join-Path $planDirectory 'deploy-report.xml'
    $deployScript = Join-Path $planDirectory 'deploy.sql'
    $packageArguments = @(
        '/Action:DeployReport',"/SourceFile:$dacpac","/TargetConnectionString:$($targetBuilder.ConnectionString)",
        "/OutputPath:$report",'/p:BlockOnPossibleDataLoss=True','/p:DropObjectsNotInSource=False','/p:IgnoreColumnOrder=True'
    )
    & $sqlPackage @packageArguments
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $report)) { throw 'DACPAC deployment report generation failed.' }
    & $sqlPackage '/Action:Script' "/SourceFile:$dacpac" "/TargetConnectionString:$($targetBuilder.ConnectionString)" "/OutputPath:$deployScript" '/p:BlockOnPossibleDataLoss=True' '/p:DropObjectsNotInSource=False' '/p:IgnoreColumnOrder=True'
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $deployScript)) { throw 'DACPAC deployment script generation failed.' }
    $reportText = Get-Content -LiteralPath $report -Raw
    $deployText = Get-Content -LiteralPath $deployScript -Raw
    $obsoleteIdentityStatements = @(
        "N'UPDATE core.Users SET TenantId=@id WHERE TenantId IS NULL'",
        "IF EXISTS(SELECT 1 FROM sys.columns WHERE object_id=OBJECT_ID(N'core.Users') AND name='TenantId' AND is_nullable=1) EXEC(N'ALTER TABLE core.Users ALTER COLUMN TenantId uniqueidentifier NOT NULL')"
    )
    $unsafeIdentityStatements = @($obsoleteIdentityStatements | Where-Object { $deployText.Contains($_) })
    if ($unsafeIdentityStatements.Count -gt 0) {
        throw "DACPAC script contains an obsolete unconditional core.Users tenant migration; publish was blocked. Review $deployScript"
    }
    # Plan generation does not compile or execute post-deployment batches. Detect the
    # SQL Server invalid-column pattern where a static ADD is followed by a reference
    # to that new column before the next GO. Dynamic DDL strings are masked first.
    $unsafeTenantAddBatches = @([regex]::Split($deployText,'(?im)^\s*GO\s*$') | Where-Object {
        $withoutStrings = [regex]::Replace($_,"N?'(?:''|[^'])*'","''")
        $add = [regex]::Match($withoutStrings,'(?is)\bALTER\s+TABLE\s+(?:admin\.Companies|gst\.GSTSettings|printing\.PrinterConfigurations)\s+ADD\s+TenantId\b(?<after>.*)$')
        $add.Success -and $add.Groups['after'].Value -match '(?i)\bTenantId\b'
    })
    if ($unsafeTenantAddBatches.Count -gt 0) {
        throw "DACPAC script contains ADD TenantId followed by a same-batch static TenantId reference; publish was blocked. Review $deployScript"
    }
    $unsupportedDrops = [regex]::Matches($reportText,'(?i)<Operation Name="Drop">(?<body>.*?)</Operation>') | ForEach-Object {
        [regex]::Matches($_.Groups['body'].Value,'<Item Value="(?<name>[^"]+)" Type="(?<type>[^"]+)"')
    } | ForEach-Object { $_ } | Where-Object {
        $name = $_.Groups['name'].Value
        $type = $_.Groups['type'].Value
        $isRecreatedIndex = $type -eq 'SqlIndex' -and ([regex]::Matches($reportText,[regex]::Escape($name))).Count -gt 1
        $isManagedTenantGuard = $type -eq 'SqlDmlTrigger' -and $name -match '\[TR_[^\]]+_TenantGuard\]$'
        $type -notmatch 'Constraint$' -and -not $isRecreatedIndex -and -not $isManagedTenantGuard
    }
    if (@($unsupportedDrops).Count -gt 0) {
        $dropNames = @($unsupportedDrops | ForEach-Object { $_.Groups['name'].Value }) -join ', '
        throw "DACPAC plan contains unsupported object drops ($dropNames); publish was blocked. Review $report"
    }
    $tableRebuilds = [regex]::Matches($reportText,'(?i)<Operation Name="TableRebuild">(?<body>.*?)</Operation>') | ForEach-Object {
        [regex]::Matches($_.Groups['body'].Value,'<Item Value="(?<name>[^"]+)" Type="SqlTable"')
    } | ForEach-Object { $_.Groups['name'].Value }
    if (@($tableRebuilds).Count -gt 0 -and -not $ApproveTableRebuild) {
        throw "DACPAC plan requires reviewed table rebuild(s): $(@($tableRebuilds) -join ', '). Publish was blocked. Review $deployScript"
    }
    Write-Host "Deployment report: $report"
    Write-Host "Deployment script: $deployScript"
    Write-Host "Reviewed table rebuilds: $(@($tableRebuilds) -join ', ')"
    if ($PlanOnly) { return }

    if ($exists -eq '1') {
        Invoke-SqlFile (Join-Path $scriptRoot 'QA-TenantHardening-Preflight.sql')
        $backupStamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
        $backupPath = "/var/opt/mssql/backups/WhatsBizERP_QA-pre-$backupStamp.bak"
        Invoke-SshCapture "install -d -o mssql -g mssql -m 750 /var/opt/mssql/backups" | Out-Null
        Invoke-SqlQuery master "BACKUP DATABASE [WhatsBizERP_QA] TO DISK=N'$backupPath' WITH COPY_ONLY,CHECKSUM,INIT,NAME=N'WhatsBizERP_QA pre-deployment $backupStamp';"
        try {
            Invoke-SqlQuery master "RESTORE VERIFYONLY FROM DISK=N'$backupPath' WITH CHECKSUM;"
        }
        catch {
            Write-Warning 'RESTORE VERIFYONLY was not permitted for the configured QA deployment login; backup checksum, size, and SHA-256 evidence are still required.'
        }
        $backupEvidence = Invoke-SshCapture "test -s '$backupPath' && stat -c 'BACKUP_BYTES=%s' '$backupPath' && sha256sum '$backupPath' | cut -d' ' -f1 | sed 's/^/BACKUP_SHA256=/'"
        Write-Host $backupEvidence
    }

    Invoke-SshCapture 'systemctl stop whatsbiz-qa' | Out-Null
    $serviceStopped = $true
    & $sqlPackage '/Action:Publish' "/SourceFile:$dacpac" "/TargetConnectionString:$($targetBuilder.ConnectionString)" '/p:BlockOnPossibleDataLoss=True' '/p:DropObjectsNotInSource=False' '/p:IgnoreColumnOrder=True'
    if ($LASTEXITCODE -ne 0) { throw 'DACPAC publish failed.' }
    }
    else {
        if ($exists -ne '1') { throw 'Cannot resume hardening because WhatsBizERP_QA does not exist.' }
        Invoke-SshCapture 'systemctl stop whatsbiz-qa' | Out-Null
        $serviceStopped = $true
        Invoke-IdentityGate
        Write-Host 'Resuming the manual hardening chain after the previously successful DACPAC publish.'
    }

    foreach ($file in @('V7-SafeInventoryTenantColumns.sql','V8-TenantOwnershipPhase1.sql')) {
        Invoke-IdentityGate
        Invoke-SqlFile (Join-Path $scriptRoot $file)
    }
    Invoke-IdentityGate
    Invoke-SqlFile (Join-Path $scriptRoot 'Bootstrap_QA.sql')
    Invoke-SqlFile (Join-Path $scriptRoot 'Bootstrap_QA.sql')
    foreach ($file in @('V7-SafeInventoryTenantColumns.sql','V8-TenantOwnershipPhase1.sql')) {
        Invoke-IdentityGate
        Invoke-SqlFile (Join-Path $scriptRoot $file)
    }
    Invoke-SqlFile (Join-Path $scriptRoot 'V9-TenantOwnershipResolutionReport.sql')
    foreach ($file in @('V18-POS-PostInvoice-TenantHardening.sql','V24-RecreateOperationalTenantGuards-WithRequiredSetOptions.sql')) {
        Invoke-IdentityGate
        Invoke-SqlFile (Join-Path $scriptRoot $file)
    }
    Invoke-SqlFile (Join-Path $scriptRoot 'V25-FinanceTenantOwnershipAudit.sql')
    Invoke-IdentityGate
    Invoke-SqlFile (Join-Path $scriptRoot 'V26-FinanceTenantIsolationAndPostingRepair.sql')
    Invoke-SqlFile (Join-Path $scriptRoot 'QA-TenantHardening-PostValidation.sql')
    Invoke-SqlQuery master "IF DB_ID(N'WhatsBizERP_QA') IS NULL THROW 51691,N'QA database is absent after deployment.',1; SELECT N'QA_SCHEMA_DEPLOYED' Result;" -Quiet

    Invoke-SshCapture 'systemctl restart whatsbiz-qa; systemctl is-active whatsbiz-qa' | Write-Host
    $serviceStopped = $false
    $health = $null
    for ($attempt=0; $attempt -lt 12 -and $null -eq $health; $attempt++) {
        try {
            $response = Invoke-WebRequest -Uri 'https://qa-api.khatadhari.com/health' -UseBasicParsing -TimeoutSec 20
            if ($response.StatusCode -eq 200) { $health = $response }
        }
        catch { Start-Sleep -Seconds 5 }
    }
    if ($null -eq $health) { throw 'QA API health check did not return HTTP 200.' }
    Write-Host 'QA database deployment and API health validation completed.'
}
finally {
    $env:SQLCMDPASSWORD = $oldSqlCmdPassword
    if ($tunnel -and -not $tunnel.HasExited) { Stop-Process -Id $tunnel.Id -Force -ErrorAction SilentlyContinue }
    if ($serviceStopped) { Write-Warning 'QA API remains stopped because the database deployment did not complete safely.' }
}
