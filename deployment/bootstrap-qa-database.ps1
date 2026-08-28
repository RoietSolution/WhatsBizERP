[CmdletBinding()]
param(
    [string] $Server = 'localhost,1433',
    [string] $Database = 'WhatsBizERP_QA',
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
if ($Database -cne 'WhatsBizERP_QA') {
    throw 'This deployment helper is restricted to WhatsBizERP_QA.'
}

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'database/WhatsBiz.Database/WhatsBiz.Database.sqlproj'
$dacpac = Join-Path $repositoryRoot "database/WhatsBiz.Database/bin/$Configuration/WhatsBiz.Database.dacpac"
$bootstrap = Join-Path $repositoryRoot 'database/WhatsBiz.Database/Scripts/Bootstrap_QA.sql'
$validation = Join-Path $repositoryRoot 'database/WhatsBiz.Database/Scripts/Validate_QA_Bootstrap.sql'

$sqlPackage = Get-Command SqlPackage -ErrorAction Stop
$sqlCmd = Get-Command sqlcmd -ErrorAction Stop

dotnet build $project --configuration $Configuration
if ($LASTEXITCODE -ne 0) { throw 'Database project build failed.' }

& $sqlPackage.Source /Action:Publish /SourceFile:$dacpac /TargetServerName:$Server /TargetDatabaseName:$Database /TargetIntegratedSecurity:True /TargetEncryptConnection:True /TargetTrustServerCertificate:True /p:BlockOnPossibleDataLoss=True
if ($LASTEXITCODE -ne 0) { throw 'DACPAC publish failed.' }

# Run twice deliberately: the second pass is the idempotency smoke test.
& $sqlCmd.Source -S $Server -d $Database -E -C -b -i $bootstrap
if ($LASTEXITCODE -ne 0) { throw 'First QA bootstrap pass failed.' }
& $sqlCmd.Source -S $Server -d $Database -E -C -b -i $bootstrap
if ($LASTEXITCODE -ne 0) { throw 'Second QA bootstrap pass failed.' }
& $sqlCmd.Source -S $Server -d $Database -E -C -b -i $validation
if ($LASTEXITCODE -ne 0) { throw 'QA bootstrap validation failed.' }

Write-Host 'WhatsBizERP_QA publish, repeat bootstrap, and SQL validation completed.'
