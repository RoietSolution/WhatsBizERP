[CmdletBinding()]
param(
    [string] $Server = 'DESKTOP-DQ0868S',
    [string] $Database = 'WhatsBizERP_PosGstIntegrationTest',
    [string] $Configuration = 'Release',
    [ValidateSet('BASELINE','CORRECTED')][string] $Mode = 'BASELINE'
)

$ErrorActionPreference = 'Stop'
$expectedDatabase = 'WhatsBizERP_PosGstIntegrationTest'
$expectedServer = 'DESKTOP-DQ0868S'
if ($Database -cne $expectedDatabase) { throw "This runner is restricted to $expectedDatabase." }
if ($Database -in @('WhatsBizERP', 'WhatsBizERP_QA', 'WhatsBizERP_PROD')) { throw 'Refusing to run against a protected database.' }

$sqlCmd = (Get-Command sqlcmd -ErrorAction Stop).Source
$rawIdentity = @(& $sqlCmd -S $Server -d $Database -E -C -b -h -1 -W -Q "SET NOCOUNT ON; SELECT CONCAT(@@SERVERNAME, N'|', DB_NAME());" 2>&1)
$identityExitCode = $LASTEXITCODE
if ($identityExitCode -ne 0) { throw "Integration database identity query failed with exit code $identityExitCode." }
$identityLine = @($rawIdentity | ForEach-Object { $_.ToString().Trim() } | Where-Object { $_ } | Select-Object -Last 1)
if ($identityLine.Count -ne 1) { throw 'Integration database identity query returned no usable identity row.' }
$identityParts = $identityLine[0].Split('|', 2)
if ($identityParts.Count -ne 2 -or $identityParts[0].Trim() -cne $expectedServer -or $identityParts[1].Trim() -cne $expectedDatabase) {
    $observed = $identityLine[0]
    throw "Unexpected SQL identity. Observed '$observed'; expected '$expectedServer|$expectedDatabase'."
}

$connection = "Server=$Server;Database=$Database;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
$env:ConnectionStrings__IntegrationTests = $connection
$testProject = Join-Path (Split-Path -Parent $PSScriptRoot) 'backend/tests/WhatsBiz.Tests/WhatsBiz.Tests.csproj'

if ($Mode -eq 'BASELINE') {
    dotnet test $testProject --configuration $Configuration --filter 'FullyQualifiedName~PosGstBaselineKnownInclusiveModeIsCurrentlyDoubleTaxed'
} else {
    dotnet test $testProject --configuration $Configuration --filter 'FullyQualifiedName~PosGstRuntimeIntegrationTests&FullyQualifiedName!~PosGstBaselineKnownInclusiveModeIsCurrentlyDoubleTaxed'
}
if ($LASTEXITCODE -ne 0) { throw 'Focused POS GST integration tests failed.' }
Write-Host "Focused POS GST integration tests passed against $expectedDatabase."
