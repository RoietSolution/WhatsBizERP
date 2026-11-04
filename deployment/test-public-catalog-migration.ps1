[CmdletBinding()]
param([string]$ScriptDomPath,[ValidateSet(72,73)][int]$Version=72)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'DatabaseMigration.Core.psm1') -Force
$catalog = @(Get-DatabaseMigrationCatalog -Directory (Join-Path $PSScriptRoot '../database/migrations'))
$pending = @($catalog | Where-Object Version -eq $Version)
if ($pending.Count -ne 1) { throw "Exactly one V$Version migration is required." }
Assert-DatabaseMigrationPreflight -Migrations $catalog -Pending $pending -BaselineVersion 40
if (-not $ScriptDomPath) {
    $packages = Join-Path $env:USERPROFILE '.nuget/packages/microsoft.sqlserver.transactsql.scriptdom'
    $assembly = Get-ChildItem -LiteralPath $packages -Recurse -Filter 'Microsoft.SqlServer.TransactSql.ScriptDom.dll' |
        Where-Object FullName -Match '[\\/]net472[\\/]' | Select-Object -First 1
    if (-not $assembly) { throw 'Supply -ScriptDomPath for a compatible installed ScriptDom assembly.' }
    $ScriptDomPath = $assembly.FullName
}
Add-Type -Path $ScriptDomPath
$parser = [Microsoft.SqlServer.TransactSql.ScriptDom.TSql160Parser]::new($true)
$files = @($pending[0].Path, $pending[0].ValidatorPath, (Join-Path $PSScriptRoot 'sql/V72-public-catalog-preflight.sql'))
foreach ($file in $files) {
    $reader = [IO.StringReader]::new([IO.File]::ReadAllText($file))
    try {
        $errors = $null
        $null = $parser.Parse($reader, [ref]$errors)
        if ($errors.Count) {
            $details = ($errors | ForEach-Object { "line $($_.Line), column $($_.Column): $($_.Message)" }) -join [Environment]::NewLine
            throw "SQL parse failed for $file`n$details"
        }
        Write-Host "SQL Server 2022 parse PASS: $([IO.Path]::GetFileName($file))"
    } finally { $reader.Dispose() }
}
Write-Host 'Canonical migration preflight PASS. Static checks only; no SQL connection or deployment performed.'
