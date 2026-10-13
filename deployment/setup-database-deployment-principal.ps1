[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('QA','PROD')][string]$Environment,
    [string]$SqlServer = '93.127.198.50,1433',
    [string]$AdminUser,
    [string]$SqlcmdPath = 'sqlcmd'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$database = if ($Environment -eq 'QA') { 'WhatsBizERP_QA' } else { 'WhatsBizERP_PROD' }
$login = 'WhatsBizDatabaseDeployment'
$localAppData = [Environment]::GetEnvironmentVariable('LOCALAPPDATA')
if ([string]::IsNullOrWhiteSpace($localAppData)) { throw 'LOCALAPPDATA is unavailable; cannot persist the encrypted deployment connection.' }
if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) { throw 'The setup script location could not be resolved; invoke the script from its full path or repository checkout.' }
$configRoot = Join-Path $localAppData 'WhatsBiz\DatabaseDeployment'
$configPath = Join-Path $configRoot "$($Environment.ToLowerInvariant()).connection.dpapi"
$templatePath = Join-Path $PSScriptRoot 'sql\setup-database-deployment-principal.sql'
$tempRoot = [IO.Path]::GetTempPath()
if ([string]::IsNullOrWhiteSpace($tempRoot)) { throw 'The system temporary directory could not be resolved.' }
$tempPath = Join-Path $tempRoot "whatsbiz-database-bootstrap-$([guid]::NewGuid().ToString('N')).sql"
$stdoutPath = Join-Path $tempRoot "whatsbiz-database-bootstrap-$([guid]::NewGuid().ToString('N')).out"
$stderrPath = Join-Path $tempRoot "whatsbiz-database-bootstrap-$([guid]::NewGuid().ToString('N')).err"

function ConvertFrom-SecureStringText([Security.SecureString]$Value) {
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Value)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}
function Replace-SqlToken([string]$Text,[string]$Token,[string]$Value) {
    $escaped = [regex]::Escape("`$($Token)")
    return [regex]::Replace($Text,$escaped,{ param($match) $Value })
}
function Sanitize-SqlcmdDiagnostic([string]$Text,[string[]]$Secrets) {
    $safe = if ($null -eq $Text) { '' } else { $Text }
    foreach ($secret in @($Secrets)) {
        if (-not [string]::IsNullOrEmpty($secret)) { $safe = $safe -replace [regex]::Escape($secret), '[REDACTED]' }
    }
    $safe = $safe -replace '(?i)(SQLCMDPASSWORD\s*[=:]\s*)[^\s;\r\n]+', '$1[REDACTED]'
    $safe = $safe -replace '(?i)(-P\s+)[^\s;\r\n]+', '$1[REDACTED]'
    $safe = $safe -replace '(?i)((?:Server|Data Source)\s*=\s*[^;\r\n]+(?:;[^;\r\n]+)*)', '[CONNECTION_STRING_REDACTED]'
    $safe = $safe -replace '(?i)((?:password|pwd)\s*=\s*)[^;\r\n]+', '$1[REDACTED]'
    return $safe.Trim()
}
function Quote-SqlIdentifier([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { throw 'SQL database identifier is empty.' }
    return '[' + $Value.Replace(']',']]') + ']'
}
$databaseIdentifier = Quote-SqlIdentifier $database
function Protect-TemporaryFile([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'Temporary bootstrap path is empty.' }
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Temporary bootstrap file was not created: $Path" }
    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true,$false)
    $rule = [Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.WindowsIdentity]::GetCurrent().Name,'FullControl','Allow')
    $acl.SetAccessRule($rule)
    Set-Acl -LiteralPath $Path -AclObject $acl
}
function Get-DpapiTypes {
    $protectedType = $null
    foreach ($assemblyName in @('System.Security.Cryptography.ProtectedData','System.Security')) {
        try { Add-Type -AssemblyName $assemblyName -ErrorAction Stop } catch { }
    }
    foreach ($assembly in [AppDomain]::CurrentDomain.GetAssemblies()) {
        $candidate = $assembly.GetType('System.Security.Cryptography.ProtectedData',$false)
        if ($null -ne $candidate) { $protectedType = $candidate; break }
    }
    if ($null -eq $protectedType) { throw 'DPAPI support is unavailable. Load System.Security.Cryptography.ProtectedData or System.Security before running database deployment setup.' }
    $scopeType = $protectedType.Assembly.GetType('System.Security.Cryptography.DataProtectionScope',$false)
    if ($null -eq $scopeType) { $scopeType = [Type]::GetType('System.Security.Cryptography.DataProtectionScope, System.Security',$false) }
    if ($null -eq $scopeType) { throw 'DPAPI protection scope is unavailable in the current PowerShell runtime.' }
    [pscustomobject]@{ ProtectedDataType=$protectedType; ScopeType=$scopeType }
}
function Protect-DeploymentConnection([string]$ConnectionString,[string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw 'DPAPI configuration path is empty.' }
    $bytes = [Text.Encoding]::UTF8.GetBytes($ConnectionString)
    try {
        $dpapi = Get-DpapiTypes
        $scope = [Enum]::Parse($dpapi.ScopeType,'CurrentUser')
        $protectMethod = @($dpapi.ProtectedDataType.GetMethods([Reflection.BindingFlags]::Public -bor [Reflection.BindingFlags]::Static) | Where-Object { $_.Name -eq 'Protect' -and $_.GetParameters().Count -eq 3 })[0]
        $protected = $protectMethod.Invoke($null,@($bytes,$null,$scope))
        New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
        [IO.File]::WriteAllText($Path,[Convert]::ToBase64String($protected),[Text.UTF8Encoding]::new($false))
    }
    finally { [Array]::Clear($bytes,0,$bytes.Length) }
}

function Resolve-Sqlcmd([string]$RequestedPath) {
    if ([string]::IsNullOrWhiteSpace($RequestedPath)) { throw 'SQLCMD was not found. Install/configure SQL Server command-line tools.' }
    if ([IO.Path]::IsPathRooted($RequestedPath) -or $RequestedPath.Contains([IO.Path]::DirectorySeparatorChar) -or $RequestedPath.Contains([IO.Path]::AltDirectorySeparatorChar)) {
        if (-not (Test-Path -LiteralPath $RequestedPath -PathType Leaf)) { throw "SQLCMD was not found. Install/configure SQL Server command-line tools. Checked: $RequestedPath" }
        return (Resolve-Path -LiteralPath $RequestedPath).Path
    }
    $command = Get-Command -Name $RequestedPath -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $command -or [string]::IsNullOrWhiteSpace($command.Source)) { throw 'SQLCMD was not found. Install/configure SQL Server command-line tools.' }
    return $command.Source
}

$resolvedSqlcmd = Resolve-Sqlcmd $SqlcmdPath
if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) { throw "Bootstrap SQL script was not found: $templatePath" }
New-Item -ItemType Directory -Path $configRoot -Force | Out-Null
if (-not (Test-Path -LiteralPath $configRoot -PathType Container)) { throw "DPAPI configuration directory could not be created: $configRoot" }
$previousSqlcmdPassword = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD','Process')
$sqlcmdPasswordChanged = $false
$deploymentSecure = Read-Host "Enter the new password for $login" -AsSecureString
$deploymentConfirmSecure = Read-Host 'Re-enter the deployment-login password' -AsSecureString
$deploymentPassword = ConvertFrom-SecureStringText $deploymentSecure
$deploymentConfirm = ConvertFrom-SecureStringText $deploymentConfirmSecure
try {
    if ([string]::IsNullOrWhiteSpace($deploymentPassword) -or $deploymentPassword -cne $deploymentConfirm) { throw 'Deployment-login passwords are empty or do not match.' }
    $adminPassword = $null
    if ([string]::IsNullOrWhiteSpace($AdminUser)) {
        $sqlAuthArgs = @('-E')
    }
    else {
        $adminSecure = Read-Host "Enter the SQL administrator password for $AdminUser" -AsSecureString
        $adminPassword = ConvertFrom-SecureStringText $adminSecure
        if ([string]::IsNullOrWhiteSpace($adminPassword)) { throw 'Administrator password cannot be empty.' }
        $sqlAuthArgs = @('-U',$AdminUser)
    }

    $sql = [IO.File]::ReadAllText($templatePath)
    $sql = Replace-SqlToken $sql 'WHATSBIZ_DEPLOYMENT_DATABASE' $database
    $sql = Replace-SqlToken $sql 'WHATSBIZ_DEPLOYMENT_DATABASE_IDENTIFIER' $databaseIdentifier
    $sql = Replace-SqlToken $sql 'WHATSBIZ_DATABASE_DEPLOYMENT_PASSWORD' ($deploymentPassword.Replace("'","''"))
    [IO.File]::WriteAllText($tempPath,$sql,[Text.UTF8Encoding]::new($false))
    Protect-TemporaryFile $tempPath
    New-Item -ItemType File -Path $stdoutPath -Force | Out-Null
    New-Item -ItemType File -Path $stderrPath -Force | Out-Null
    Protect-TemporaryFile $stdoutPath
    Protect-TemporaryFile $stderrPath

    $sqlcmdArgs = @('-S',$SqlServer,'-d','master','-C','-b','-x') + $sqlAuthArgs + @('-i',$tempPath)
    if (-not [string]::IsNullOrWhiteSpace($AdminUser)) {
        [Environment]::SetEnvironmentVariable('SQLCMDPASSWORD',$adminPassword,'Process')
        $sqlcmdPasswordChanged = $true
    }
    else {
        [Environment]::SetEnvironmentVariable('SQLCMDPASSWORD',$null,'Process')
        $sqlcmdPasswordChanged = $true
    }
    $exitCode = -1
    $stdoutText = ''
    $stderrText = ''
    try {
        & $resolvedSqlcmd @sqlcmdArgs 1> $stdoutPath 2> $stderrPath
        $exitCode = $LASTEXITCODE
    }
    catch {
        $stderrText = $_.Exception.Message
    }
    if (Test-Path -LiteralPath $stdoutPath -PathType Leaf) { $stdoutText = [IO.File]::ReadAllText($stdoutPath) }
    if ([string]::IsNullOrWhiteSpace($stderrText) -and (Test-Path -LiteralPath $stderrPath -PathType Leaf)) { $stderrText = [IO.File]::ReadAllText($stderrPath) }
    if ($exitCode -ne 0) {
        $diagnostic = Sanitize-SqlcmdDiagnostic (($stdoutText + [Environment]::NewLine + $stderrText).Trim()) @($adminPassword,$deploymentPassword)
        if ([string]::IsNullOrWhiteSpace($diagnostic)) { $diagnostic = 'sqlcmd returned no diagnostic output.' }
        $administratorLabel = if ([string]::IsNullOrWhiteSpace($AdminUser)) { 'Windows Integrated Authentication' } else { $AdminUser }
        throw "DATABASE DEPLOYMENT PRINCIPAL BOOTSTRAP FAILED`nServer: $SqlServer`nDatabase: $database`nAdministrator: $administratorLabel`nSQLCMD exit code: $exitCode`n$diagnostic"
    }

    $builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new()
    $builder['Data Source'] = $SqlServer
    $builder['Initial Catalog'] = $database
    $builder['User ID'] = $login
    $builder['Password'] = $deploymentPassword
    $builder['Encrypt'] = $true
    $builder['TrustServerCertificate'] = $true
    try {
        Protect-DeploymentConnection $builder.ConnectionString $configPath
    }
    catch { throw "SQL bootstrap succeeded, but encrypted deployment configuration could not be persisted. The SQL login/user may already exist and it is safe to retry. $($_.Exception.Message)" }
    Write-Host "Configured $login for $Environment."
    Write-Host "Encrypted deployment connection saved outside the repository: $configPath"
}
finally {
    if ($sqlcmdPasswordChanged) { [Environment]::SetEnvironmentVariable('SQLCMDPASSWORD',$previousSqlcmdPassword,'Process') }
    if ($null -ne $deploymentPassword) { $deploymentPassword = $null }
    if ($null -ne $deploymentConfirm) { $deploymentConfirm = $null }
    if ($null -ne $adminPassword) { $adminPassword = $null }
    if (-not [string]::IsNullOrWhiteSpace($tempPath) -and (Test-Path -LiteralPath $tempPath)) { Remove-Item -LiteralPath $tempPath -Force -ErrorAction SilentlyContinue }
    if (-not [string]::IsNullOrWhiteSpace($stdoutPath) -and (Test-Path -LiteralPath $stdoutPath)) { Remove-Item -LiteralPath $stdoutPath -Force -ErrorAction SilentlyContinue }
    if (-not [string]::IsNullOrWhiteSpace($stderrPath) -and (Test-Path -LiteralPath $stderrPath)) { Remove-Item -LiteralPath $stderrPath -Force -ErrorAction SilentlyContinue }
}
