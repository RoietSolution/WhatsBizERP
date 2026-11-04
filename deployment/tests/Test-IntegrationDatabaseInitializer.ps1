$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Import-Module (Join-Path $PSScriptRoot '..\IntegrationDatabase.Initializer.Core.psm1') -Force
Import-Module (Join-Path $PSScriptRoot '..\DatabaseMigration.Core.psm1') -Force
$script:passed = 0
$integrationTarget = Get-IntegrationDatabaseConfig
$expectedDatabase = $integrationTarget.Database

function Assert-True([bool]$Condition,[string]$Name) {
    if (-not $Condition) { throw "FAILED: $Name" }
    $script:passed++
    Write-Host "PASS $Name"
}
function Assert-Throws([scriptblock]$Action,[string]$Name) {
    $threw = $false
    try { & $Action } catch { $threw = $true }
    Assert-True $threw $Name
}

$initializer = Join-Path $PSScriptRoot '..\initialize-integration-database.ps1'
$tokens = $null; $parseErrors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $initializer),[ref]$tokens,[ref]$parseErrors)
Assert-True ($parseErrors.Count -eq 0) 'initializer PowerShell syntax'
$initializerAst = [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $initializer),[ref]$tokens,[ref]$parseErrors)
$invalidTransactionAnnotations = @($initializerAst.FindAll({ param($node) $node -is [System.Management.Automation.Language.TypeConstraintAst] -and $node.TypeName.FullName -eq 'System.Data.SqlTransaction' },$true))
Assert-True ($invalidTransactionAnnotations.Count -eq 0) 'initializer has no invalid System.Data.SqlTransaction annotations'
Assert-True ($null -ne [System.Data.SqlClient.SqlTransaction]) 'SqlConnection transaction type resolves from SqlClient namespace'

# Exercise the real SQL helper function bodies with an in-memory command double.
# This validates parameter binding and transaction assignment without opening SQL.
foreach ($functionName in @('Invoke-Scalar','Invoke-SqlText','Get-ValidatorSql','Invoke-Validator')) {
    $functionAst = $initializerAst.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $functionName },$true)
    if (-not $functionAst) { throw "Focused SQL helper test could not find '$functionName'." }
    Invoke-Expression $functionAst.Extent.Text
}
$mockCommand = [pscustomobject]@{ CommandText=$null; Transaction=$null; CommandTimeout=0; NonQueryCallCount=0 }
$mockCommand | Add-Member ScriptMethod ExecuteScalar { return 73 }
$mockCommand | Add-Member ScriptMethod ExecuteNonQuery { $this.NonQueryCallCount++; return 1 }
$mockCommand | Add-Member ScriptMethod Dispose { }
$mockConnection = [pscustomobject]@{ State=[System.Data.ConnectionState]::Open; Command=$mockCommand }
$mockConnection | Add-Member ScriptMethod CreateCommand { return $this.Command }
$script:Connection = $mockConnection
$scalarResult = Invoke-Scalar -Sql 'SELECT 73;' -Transaction $null -Operation 'Focused transaction binding test'
Assert-True ($scalarResult -eq 73 -and $mockCommand.CommandText -eq 'SELECT 73;' -and $null -eq $mockCommand.Transaction) 'Invoke-Scalar binds SqlClient transaction parameter and executes with null transaction'
$validatorPath = Join-Path $env:TEMP ('initializer-transaction-test-' + [guid]::NewGuid().ToString('N') + '.validate.sql')
try {
    [IO.File]::WriteAllText($validatorPath,'SELECT 1;')
    Invoke-Validator -Path $validatorPath -Transaction $null
    Assert-True ($mockCommand.NonQueryCallCount -eq 1 -and $null -eq $mockCommand.Transaction -and $mockCommand.CommandText -eq 'SELECT 1;') 'Invoke-Validator passes a compatible null SqlClient transaction to Invoke-SqlText'
} finally { Remove-Item -LiteralPath $validatorPath -Force -ErrorAction SilentlyContinue }
$script:Connection = $null
$modulePath = Join-Path $PSScriptRoot '..\IntegrationDatabase.Initializer.Core.psm1'
$tokens = $null; $parseErrors = $null
[void][System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $modulePath),[ref]$tokens,[ref]$parseErrors)
Assert-True ($parseErrors.Count -eq 0) 'initializer safety module PowerShell syntax'

$connection = "Data Source=$($integrationTarget.Server);Initial Catalog=$expectedDatabase;Integrated Security=True;Encrypt=True;TrustServerCertificate=True"
$builder = New-IntegrationSqlConnectionStringBuilder $connection
Assert-True ($builder.DataSource -ceq $integrationTarget.Server -and $builder.InitialCatalog -ceq $expectedDatabase) 'central exact disposable server/database accepted'
$databaseKeywordConnection = "Server=$($integrationTarget.Server);Database=$expectedDatabase;Integrated Security=True;Encrypt=False;TrustServerCertificate=True"
$databaseKeywordMaster = New-IntegrationMasterConnectionStringBuilder -ConnectionString $databaseKeywordConnection
Assert-True ($databaseKeywordMaster.InitialCatalog -ceq 'master' -and $databaseKeywordMaster.IntegratedSecurity -and -not $databaseKeywordMaster.Encrypt -and $databaseKeywordMaster.TrustServerCertificate) 'Status master connection preparation accepts Database keyword and preserves integrated-auth options'
$initialCatalogConnection = "Server=$($integrationTarget.Server);Initial Catalog=$expectedDatabase;Integrated Security=True;Encrypt=False;TrustServerCertificate=True"
$initialCatalogMaster = New-IntegrationMasterConnectionStringBuilder -ConnectionString $initialCatalogConnection
Assert-True ($initialCatalogMaster.InitialCatalog -ceq 'master' -and $initialCatalogMaster.IntegratedSecurity) 'Status master connection preparation accepts Initial Catalog keyword'
Assert-Throws { New-IntegrationMasterConnectionStringBuilder -ConnectionString "Server=$($integrationTarget.Server);InitialCatalog=$expectedDatabase;Integrated Security=True" } 'unsupported InitialCatalog keyword is rejected by the real SqlClient parser'
foreach ($protectedDatabase in $integrationTarget.ProtectedDatabases) {
    Assert-Throws { New-IntegrationMasterConnectionStringBuilder -ConnectionString "Server=$($integrationTarget.Server);Database=$protectedDatabase;Integrated Security=True" } "protected database '$protectedDatabase' rejected by Status connection preparation"
}
Assert-True ($databaseKeywordMaster.DataSource -ceq $integrationTarget.Server) 'Status connection preparation retains the configured shared server'
Assert-Throws { New-IntegrationSqlConnectionStringBuilder 'Data Source=DESKTOP-DQ0868S;Initial Catalog=WhatsBizERP_IntegrationTest;Integrated Security=True' } 'legacy ambiguous integration database rejected for use'
Assert-Throws { New-IntegrationSqlConnectionStringBuilder 'Data Source=DESKTOP-DQ0868S;Initial Catalog=WhatsBizERP_IntegrationTest_V83;Integrated Security=True' } 'version-specific neighboring database rejected'
Assert-True (-not $builder.PersistSecurityInfo) 'connection string does not persist security information'
Assert-Throws { New-IntegrationSqlConnectionStringBuilder '$env:ConnectionStrings__IntegrationTests="Server=..."' } 'PowerShell assignment rejected'
Assert-Throws { New-IntegrationSqlConnectionStringBuilder 'Data Source=DESKTOP-DQ0868S;Initial Catalog=WhatsBizERP_QA;Integrated Security=True' } 'QA database rejected'
Assert-Throws { New-IntegrationSqlConnectionStringBuilder 'Data Source=DESKTOP-DQ0868S;Initial Catalog=WhatsBizERP_PROD;Integrated Security=True' } 'PROD database rejected'
Assert-Throws { New-IntegrationSqlConnectionStringBuilder 'Data Source=DESKTOP-DQ0868S;Initial Catalog=WhatsBizERP;Integrated Security=True' } 'application database rejected'
Assert-Throws { New-IntegrationSqlConnectionStringBuilder 'Data Source=other;Initial Catalog=WhatsBizERP_IntegrationTest;Integrated Security=True' } 'unexpected SQL server rejected'
Assert-Throws { New-IntegrationSqlConnectionStringBuilder 'Data Source=DESKTOP-DQ0868S;Initial Catalog=WhatsBizERP_QA;Integrated Security=True' } 'Apply cannot target protected QA database'
Assert-Throws { New-IntegrationSqlConnectionStringBuilder 'Data Source=DESKTOP-DQ0868S;Initial Catalog=WhatsBizERP_PROD;Integrated Security=True' } 'Apply cannot target protected PROD database'
Assert-Throws { Assert-IntegrationInitializationConfirmation -Mode Apply -ConfirmTarget 'DESKTOP-DQ0868S/WhatsBizERP_QA' } 'wrong typed apply confirmation rejected'
Assert-Throws { Assert-IntegrationInitializationConfirmation -Mode Apply } 'Apply without explicit confirmation rejected'
Assert-IntegrationInitializationConfirmation -Mode Status
Assert-IntegrationInitializationConfirmation -Mode DryRun
Assert-IntegrationInitializationConfirmation -Mode Apply -ConfirmTarget "$($integrationTarget.Server)/$expectedDatabase"
Assert-True $true 'exact typed shared-target Apply confirmation accepted'
Assert-Throws { Assert-IntegrationInitializationConfirmation -Mode RecoverBaseline } 'RecoverBaseline without exact target confirmation rejected'
Assert-IntegrationInitializationConfirmation -Mode RecoverBaseline -ConfirmTarget "$($integrationTarget.Server)/$expectedDatabase"
Assert-True $true 'RecoverBaseline exact target confirmation accepted'
Assert-Throws { Assert-IntegrationInitializationConfirmation -Mode Apply -ConfirmTarget 'DESKTOP-DQ0868S/WhatsBizERP_IntegrationTest_V83' } 'unapproved V83 target confirmation rejected'
Assert-True ((Get-IntegrationDatabaseCreationDecision -Mode Status -DatabaseExists $false -CreateIfAbsent $false -HasCreatePermission $false) -eq 'ABSENT_READ_ONLY') 'Status leaves absent target untouched'
Assert-True ((Get-IntegrationDatabaseCreationDecision -Mode DryRun -DatabaseExists $false -CreateIfAbsent $false -HasCreatePermission $true) -eq 'ABSENT_READ_ONLY') 'DryRun leaves absent target untouched'
Assert-True ((Get-IntegrationDatabaseCreationDecision -Mode Apply -DatabaseExists $true -CreateIfAbsent $false -HasCreatePermission $false) -eq 'USE_EXISTING') 'existing database is never recreated'
Assert-True ((Get-IntegrationDatabaseCreationDecision -Mode Apply -DatabaseExists $true -CreateIfAbsent $true -HasCreatePermission $true) -eq 'USE_EXISTING') 'create-if-absent never resets an existing database'
Assert-Throws { Get-IntegrationDatabaseCreationDecision -Mode Apply -DatabaseExists $false -CreateIfAbsent $true -HasCreatePermission $false } 'first-time creation fails without server permission'
Assert-True ((Get-IntegrationDatabaseCreationDecision -Mode Apply -DatabaseExists $false -CreateIfAbsent $true -HasCreatePermission $true) -eq 'CREATE_NEW') 'first-time create requires Apply authorization and explicit switch'
Assert-True ((Get-IntegrationProvisioningDiagnostic -DatabaseExists $false -HasCreatePermission $false -Principal 'DESKTOP-DQ0868S\pc') -match "DESKTOP-DQ0868S\\pc.*lacks CREATE ANY DATABASE.*database-level CONTROL") 'read-only diagnostic distinguishes one-time provisioning from migration permission'
Assert-True ((Get-IntegrationProvisioningDiagnostic -DatabaseExists $true -HasCreatePermission $false -Principal 'DESKTOP-DQ0868S\pc') -match 'CREATE ANY DATABASE is not required.*database-level CONTROL') 'existing target does not require server-level create permission'
Assert-True ((Get-IntegrationProvisioningDiagnostic -DatabaseExists $false -HasCreatePermission $true -Principal 'test') -match 'explicit Apply and exact target confirmation') 'create permission alone does not authorize provisioning'
Assert-True ($integrationTarget.ProtectedDatabases -contains 'WhatsBizERP' -and $integrationTarget.ProtectedDatabases -contains 'WhatsBizERP_QA' -and $integrationTarget.ProtectedDatabases -contains 'WhatsBizERP_PROD' -and $integrationTarget.ProtectedDatabases -contains 'WhatsBizERP_IntegrationTest') 'shared configuration explicitly protects operational and legacy databases'
Assert-IntegrationConnectionUsable -State 'Open' -Operation 'test read'
Assert-Throws { Assert-IntegrationConnectionUsable -State 'Broken' -Operation 'test read' } 'broken SQL connection fails closed without retry'
Assert-Throws { Assert-IntegrationConnectionUsable -State 'Closed' -Operation 'test read' } 'closed SQL connection fails closed without retry'
Assert-Throws { Assert-IntegrationTargetClassified -Mode Apply -UserObjectCount 1 -MarkerPresent $false } 'unmarked existing schema cannot be adopted'
Assert-IntegrationTargetClassified -Mode Apply -UserObjectCount 0 -MarkerPresent $false
Assert-True $true 'empty target may be initialized after explicit confirmation'
Assert-IntegrationTargetClassified -Mode Apply -UserObjectCount 12 -MarkerPresent $true
Assert-True $true 'marked target supports safe repeat execution'
Assert-True ((Convert-IntegrationSqlIntegerToInt32 -Value ([int]81) -SqlType 'int' -ColumnName 'test.int') -eq 81) 'SQL INT maps from CLR Int32'
Assert-True ((Convert-IntegrationSqlIntegerToInt32 -Value ([long]81) -SqlType 'bigint' -ColumnName 'test.bigint') -eq 81) 'SQL BIGINT maps with range check'
Assert-True ((Convert-IntegrationSqlIntegerToInt32 -Value ([System.Int16]81) -SqlType 'smallint' -ColumnName 'test.smallint') -eq 81) 'SQL SMALLINT maps from CLR Int16'
Assert-True ((Convert-IntegrationSqlIntegerToInt32 -Value ([byte]81) -SqlType 'tinyint' -ColumnName 'test.tinyint') -eq 81) 'SQL TINYINT maps from CLR Byte'
Assert-Throws { Convert-IntegrationSqlIntegerToInt32 -Value ([long]([int]::MaxValue + 1L)) -SqlType 'bigint' -ColumnName 'test.overflow' } 'out-of-range BIGINT rejected'
Assert-Throws { Convert-IntegrationSqlIntegerToInt32 -Value ([DBNull]::Value) -SqlType 'int' -ColumnName 'test.null' } 'NULL integer rejected'
Assert-Throws { Convert-IntegrationSqlIntegerToInt32 -Value ([decimal]81) -SqlType 'decimal' -ColumnName 'test.unexpected' } 'unexpected SQL numeric type rejected'
Assert-True ((ConvertTo-IntegrationVersionScalar -Value ([int]40) -Name 'BaselineVersion' -Minimum 1 -Maximum 2147483647) -eq 40) 'scalar baseline version accepted'
Assert-Throws { ConvertTo-IntegrationVersionScalar -Value ([object[]]@(40)) -Name 'BaselineVersion' -Minimum 1 -Maximum 2147483647 } 'array-valued baseline version rejected'
Assert-True ((ConvertTo-IntegrationVersionScalar -Value ([int]81) -Name 'ThroughVersion' -Minimum 1 -Maximum 2147483647) -eq 81) 'scalar through version accepted'
Assert-Throws { ConvertTo-IntegrationVersionScalar -Value ([object[]]@(81)) -Name 'ThroughVersion' -Minimum 1 -Maximum 2147483647 } 'array-valued through version rejected'
$testHash = 'A' * 64
$recordOne = [pscustomobject]@{ MigrationVersion=41; MigrationName='V41-test.sql'; ScriptHash=$testHash; ValidatorHash=$testHash }
$recordTwo = [pscustomobject]@{ MigrationVersion=42; MigrationName='V42-test.sql'; ScriptHash=$testHash; ValidatorHash=$testHash }
$emptyHistory = ConvertTo-IntegrationMigrationHistoryCollection -HistoryTablePresent $true -Records @()
Assert-True ($emptyHistory -is [object[]] -and $emptyHistory.Count -eq 0) 'empty migration history remains an empty object array'
function Return-TestHistory([object[]]$Rows) { $records = ConvertTo-IntegrationMigrationHistoryCollection -HistoryTablePresent $true -Records $Rows; return ,$records }
$returnedEmptyHistory = Return-TestHistory -Rows @()
Assert-True ($returnedEmptyHistory -is [object[]] -and $returnedEmptyHistory.Count -eq 0) 'history getter contract preserves truly empty collection'
$singleHistory = ConvertTo-IntegrationMigrationHistoryCollection -HistoryTablePresent $true -Records @($recordOne)
Assert-True ($singleHistory -is [object[]] -and $singleHistory.Count -eq 1 -and $singleHistory[0].MigrationVersion -eq 41) 'single migration history record has collection shape'
$returnedSingleHistory = Return-TestHistory -Rows @($recordOne)
Assert-True ($returnedSingleHistory.Count -eq 1 -and $returnedSingleHistory[0] -isnot [System.Array]) 'history getter does not nest one record'
$multipleHistory = ConvertTo-IntegrationMigrationHistoryCollection -HistoryTablePresent $true -Records @($recordOne,$recordTwo)
Assert-True ($multipleHistory -is [object[]] -and $multipleHistory.Count -eq 2 -and $multipleHistory[1].MigrationVersion -eq 42) 'multiple migration history records retain collection shape'
Assert-Throws { ConvertTo-IntegrationMigrationHistoryCollection -HistoryTablePresent $true -Records @([pscustomobject]@{ MigrationVersion=41; MigrationName='V41-test.sql'; ScriptHash='invalid'; ValidatorHash=$testHash }) } 'invalid migration hash rejected'
Assert-Throws { ConvertTo-IntegrationMigrationHistoryCollection -HistoryTablePresent $true -Records @([pscustomobject]@{ MigrationVersion=41; MigrationName='V41-test.sql'; ScriptHash=$testHash }) } 'malformed record missing ValidatorHash rejected'
Assert-Throws { ConvertTo-IntegrationMigrationHistoryCollection -HistoryTablePresent $false -Records @() } 'missing history table rejected'
Assert-Throws { ConvertTo-IntegrationMigrationHistoryCollection -HistoryTablePresent $true -Records @('unexpected scalar') } 'unexpected query result rejected'

$batches = @(Split-IntegrationSqlBatches "SELECT 1;`nGO`nSELECT 2;")
Assert-True ($batches.Count -eq 2) 'GO batch splitting'
Assert-Throws { Split-IntegrationSqlBatches ':r another.sql' } 'SQLCMD include directive rejected'

$guarded = "IF DB_NAME() NOT IN(N'WhatsBizERP_QA',N'WhatsBizERP_PROD') THROW 52110,N'wrong environment',1;`nSELECT 1;"
$rewritten = ConvertTo-IntegrationValidatorText $guarded 'test.validate.sql'
Assert-True ($rewritten.Contains("IF DB_NAME() <> N'$expectedDatabase'") -and $rewritten.Contains('THROW 52110')) 'approved environment guard is narrowed to centralized shared database'
Assert-Throws { ConvertTo-IntegrationValidatorText -Text $guarded -FileName 'wrong-target.validate.sql' -DatabaseName 'WhatsBizERP_IntegrationTest' } 'validator cannot override centralized target'
Assert-Throws { ConvertTo-IntegrationValidatorText "IF DB_NAME() NOT IN(N'other') THROW 1,N'x',1;" 'unknown.validate.sql' } 'unrecognized database allowlist rejected'
$nullGuard = ConvertTo-IntegrationValidatorText "IF DB_NAME() IS NULL THROW 1,N'no database',1;" 'null-guard.validate.sql'
Assert-True ($nullGuard.Contains('DB_NAME() IS NULL')) 'non-environment database validation preserved'

$report = Join-Path $env:TEMP ('integration-dacpac-report-' + [guid]::NewGuid().ToString('N') + '.xml')
try {
    '<DeploymentReport><Operations><Operation Name="Create Table" /></Operations></DeploymentReport>' | Set-Content -LiteralPath $report
    Assert-IntegrationDacpacReportSafe $report
    Assert-True $true 'non-destructive DACPAC plan accepted'
    '<DeploymentReport><Operations><Operation Name="Drop"><Item Value="[sales].[UnexpectedTable]" Type="SqlTable" /></Operation></Operations></DeploymentReport>' | Set-Content -LiteralPath $report
    $destructiveMessage = $null
    try { Assert-IntegrationDacpacReportSafe $report } catch { $destructiveMessage = $_.Exception.Message }
    Assert-True ($destructiveMessage -match "Drop.*\[sales\]\.\[UnexpectedTable\].*SqlTable") 'destructive DACPAC plan reports exact operation and affected object'
} finally { Remove-Item -LiteralPath $report -Force -ErrorAction SilentlyContinue }

$catalog = @(Get-DatabaseMigrationCatalog -Directory (Join-Path $root 'database/migrations') | Where-Object { $_.Version -ge 41 } | Sort-Object Version)
$catalog = ConvertTo-IntegrationMigrationCatalogCollection -Migrations $catalog
Assert-DatabaseMigrationSequence -Migrations $catalog -BaselineVersion 40
Assert-True (($catalog.Version -join ',') -ceq ((41..$catalog[-1].Version) -join ',')) 'canonical migration catalog discovers complete V41-to-latest sequence'
Assert-Throws { Assert-DatabaseMigrationSequence -Migrations @($catalog[0],$catalog[2]) -BaselineVersion 40 } 'missing canonical migration predecessor fails closed'
Assert-Throws { ConvertTo-IntegrationMigrationCatalogCollection -Migrations (,($catalog[0..1])) } 'nested migration catalog rejected'
function Return-TestCatalog([object[]]$Items) { $items = ConvertTo-IntegrationMigrationCatalogCollection -Migrations $Items; return $items }
$returnedCatalog = @(Return-TestCatalog -Items @($catalog[0],$catalog[1]))
Assert-True ($returnedCatalog.Count -eq 2 -and $returnedCatalog[0] -isnot [System.Array]) 'migration catalog getter returns a flat collection'
$singleMigrations = ConvertTo-IntegrationMigrationCatalogCollection -Migrations @($catalog[0],$catalog[1])
$scalarBaseline = ConvertTo-IntegrationVersionScalar -Value ([int]40) -Name 'BaselineVersion' -Minimum 1 -Maximum 2147483647
$emptyHistory = ConvertTo-IntegrationMigrationHistoryCollection -HistoryTablePresent $true -Records @()
$uninitializedPlan = @(Get-DatabaseMigrationPlan -Migrations $singleMigrations -BaselineVersion $scalarBaseline -History $emptyHistory)
Assert-True ($uninitializedPlan.Count -eq 2 -and @($uninitializedPlan | Where-Object Status -eq 'PENDING').Count -eq 2) 'uninitialized V40 plan marks the first migrations pending'
$fullPendingPlan = @(Get-DatabaseMigrationPlan -Migrations $catalog -BaselineVersion $scalarBaseline -History $emptyHistory)
Assert-True ($fullPendingPlan.Count -eq $catalog.Count -and @($fullPendingPlan | Where-Object Status -eq 'PENDING').Count -eq $catalog.Count) 'uninitialized V40 plan marks every catalogued migration pending'
Assert-Throws { Get-DatabaseMigrationPlan -Migrations $singleMigrations -BaselineVersion ([object[]]@(40)) -History $emptyHistory } 'migration plan rejects array-valued baseline version'
$appliedRecord = [pscustomobject]@{ MigrationVersion=$singleMigrations[0].Version; MigrationName=$singleMigrations[0].FileName; ScriptHash=$singleMigrations[0].Hash; ValidatorHash=$singleMigrations[0].ValidatorHash }
$appliedHistory = ConvertTo-IntegrationMigrationHistoryCollection -HistoryTablePresent $true -Records @($appliedRecord)
$appliedPlan = @(Get-DatabaseMigrationPlan -Migrations $singleMigrations -BaselineVersion $scalarBaseline -History $appliedHistory)
Assert-True (@($appliedPlan | Where-Object Status -eq 'APPLIED').Count -eq 1 -and @($appliedPlan | Where-Object Status -eq 'PENDING').Count -eq 1) 'applied migration remains applied and subsequent migration remains pending'
$mismatchedHistory = ConvertTo-IntegrationMigrationHistoryCollection -HistoryTablePresent $true -Records @([pscustomobject]@{ MigrationVersion=$singleMigrations[0].Version; MigrationName=$singleMigrations[0].FileName; ScriptHash=('0' * 64); ValidatorHash=$singleMigrations[0].ValidatorHash })
Assert-Throws { Get-DatabaseMigrationPlan -Migrations $singleMigrations -BaselineVersion $scalarBaseline -History $mismatchedHistory } 'migration checksum mismatch blocks repeat initialization'
$statusLines = Get-IntegrationStatusLines -Mode Status -BaselineLabel 'not installed; empty database' -MarkerLabel 'not yet registered' -SchemaState 'EMPTY_DATABASE' -TenantDataSummary 'TENANT_TABLE_ABSENT' -ThroughVersion 42 -Plan $uninitializedPlan
Assert-True ($statusLines -contains 'Canonical migrations V41 to V42 pending: 2' -and $statusLines -contains 'Pending versions: 41, 42') 'Status formatting reports catalog-driven pending versions'
$dryRunLines = Get-IntegrationStatusLines -Mode DryRun -BaselineLabel 'validated' -MarkerLabel 'present' -SchemaState 'VALIDATED_V40_BASELINE' -TenantDataSummary 'TEST_ONLY_TENANT_ROWS' -ThroughVersion 42 -Plan $appliedPlan
Assert-True ($dryRunLines[0] -eq 'Mode: DryRun' -and $dryRunLines -contains 'Canonical migrations V41 to V42 applied: 1' -and $dryRunLines -contains 'Canonical migrations V41 to V42 pending: 1') 'DryRun formatting reports applied and pending progress'
Assert-True ($statusLines -contains 'Schema state: EMPTY_DATABASE') 'Status distinguishes an empty database'
Assert-True ($statusLines -contains 'Canonical migrations V41 to V42 applied: 0') 'Status reports zero applied migrations for an empty target'
Assert-True ((Get-IntegrationSchemaStateLabel -UserObjectCount 8 -HasBaseline $false -HasHistory $false -MarkerPresent $false -CanonicalBaselineFootprint $true -BaselineValidated $false) -eq 'PARTIAL_V40_BASELINE') 'Status classifies partial V40 baseline footprint'
Assert-True ((Get-IntegrationSchemaStateLabel -UserObjectCount 30 -HasBaseline $false -HasHistory $false -MarkerPresent $false -CanonicalBaselineFootprint $true -BaselineValidated $true) -eq 'V40_SCHEMA_PUBLISHED_LEDGER_MISSING') 'Status distinguishes published V40 schema missing its ledger'
Assert-True ((Get-IntegrationSchemaStateLabel -UserObjectCount 1 -HasBaseline $false -HasHistory $true -MarkerPresent $false -CanonicalBaselineFootprint $false -BaselineValidated $false) -eq 'PARTIAL_MIGRATION_LEDGER') 'Status classifies partial migration ledger'
Assert-True ((Get-IntegrationSchemaStateLabel -UserObjectCount 20 -HasBaseline $false -HasHistory $false -MarkerPresent $false -CanonicalBaselineFootprint $false -BaselineValidated $false) -eq 'UNEXPECTED_UNMARKED_SCHEMA') 'Status distinguishes unexpected unmarked schema'
Assert-True ((Get-IntegrationSchemaStateLabel -UserObjectCount 40 -HasBaseline $true -HasHistory $true -MarkerPresent $true -CanonicalBaselineFootprint $true -BaselineValidated $true) -eq 'VALIDATED_V40_BASELINE') 'Status recognizes validated V40 baseline'
Assert-True ((Get-IntegrationSchemaStateLabel -UserObjectCount 40 -HasBaseline $true -HasHistory $true -MarkerPresent $true -CanonicalBaselineFootprint $true -BaselineValidated $true) -eq 'VALIDATED_V40_BASELINE') 'schema classification is independent of tenant row classification'
$tenantAbsent = Get-IntegrationTenantDataClassification -TenantObjectPresent $false -TenantKeyColumnPresent $false
Assert-True ($tenantAbsent.State -eq 'TENANT_TABLE_ABSENT' -and $tenantAbsent.TenantCount -eq 0) 'absent tenant schema is distinct from tenant data'
$tenantSchemaIncomplete = Get-IntegrationTenantDataClassification -TenantObjectPresent $true -TenantKeyColumnPresent $false
Assert-True ($tenantSchemaIncomplete.State -eq 'TENANT_SCHEMA_INCOMPLETE' -and $null -eq $tenantSchemaIncomplete.TenantCount) 'tenant schema without key column is reported separately'
$noTenantRows = Get-IntegrationTenantDataClassification -TenantObjectPresent $true -TenantKeyColumnPresent $true -TenantCount 0 -UnknownTenantKeyCount 0 -NonTestTenantCount 0
Assert-True ($noTenantRows.State -eq 'NO_TENANT_ROWS' -and $noTenantRows.NonTestKeyCount -eq 0) 'schema existence without tenant rows is not classified as non-disposable tenant data'
$testTenants = Get-IntegrationTenantDataClassification -TenantObjectPresent $true -TenantKeyColumnPresent $true -TenantCount 3 -UnknownTenantKeyCount 0 -NonTestTenantCount 0
Assert-True ($testTenants.State -eq 'TEST_ONLY_TENANT_ROWS' -and $testTenants.TestKeyCount -eq 3) 'only test/integration-pattern tenant rows are identified without exposing keys'
$realTenant = Get-IntegrationTenantDataClassification -TenantObjectPresent $true -TenantKeyColumnPresent $true -TenantCount 2 -UnknownTenantKeyCount 0 -NonTestTenantCount 1
Assert-True ($realTenant.State -eq 'NON_DISPOSABLE_TENANT_DATA' -and $realTenant.NonTestKeyCount -eq 1) 'non-test tenant rows produce the precise fail-closed classification'
$canonicalSeed = Get-IntegrationTenantDataClassification -TenantObjectPresent $true -TenantKeyColumnPresent $true -TenantCount 1 -UnknownTenantKeyCount 0 -NonTestTenantCount 1 -CanonicalDefaultSeedCount 1
Assert-True ($canonicalSeed.State -eq 'CANONICAL_DEFAULT_SEED_ONLY' -and $canonicalSeed.NonSeedNonTestCount -eq 0) 'the exact V2 DEFAULT seed is identified independently of key-name heuristics'
$seedAndUnexpectedTenant = Get-IntegrationTenantDataClassification -TenantObjectPresent $true -TenantKeyColumnPresent $true -TenantCount 2 -UnknownTenantKeyCount 0 -NonTestTenantCount 2 -CanonicalDefaultSeedCount 1
Assert-True ($seedAndUnexpectedTenant.State -eq 'NON_DISPOSABLE_TENANT_DATA' -and $seedAndUnexpectedTenant.NonSeedNonTestCount -eq 1) 'an additional non-seed tenant remains non-disposable'
$unknownTenant = Get-IntegrationTenantDataClassification -TenantObjectPresent $true -TenantKeyColumnPresent $true -TenantCount 2 -UnknownTenantKeyCount 1 -NonTestTenantCount 0
Assert-True ($unknownTenant.State -eq 'UNKNOWN_TENANT_KEYS') 'blank tenant keys are distinct from confirmed non-test tenant keys'
Assert-Throws { Get-IntegrationTenantDataClassification -TenantObjectPresent $true -TenantKeyColumnPresent $true -TenantCount ([long]1) -UnknownTenantKeyCount 0 -NonTestTenantCount 0 } 'unexpected SQL aggregate CLR type is rejected'
Assert-Throws { Get-IntegrationTenantDataClassification -TenantObjectPresent $true -TenantKeyColumnPresent $true -TenantCount 1 -UnknownTenantKeyCount 2 -NonTestTenantCount 0 } 'inconsistent tenant aggregates fail closed'
Assert-True ((Get-IntegrationModeDisposition -Mode Status) -eq 'ReadOnly') 'Status dispatches to read-only path'
Assert-True ((Get-IntegrationModeDisposition -Mode DryRun) -eq 'ReadOnly') 'DryRun dispatches to read-only path'
Assert-True ((Get-IntegrationModeDisposition -Mode AuditBaseline) -eq 'ReadOnlyAudit') 'V40 baseline audit dispatches to read-only audit path'
Assert-True ((Get-IntegrationModeDisposition -Mode RecoverBaseline) -eq 'Recovery') 'V40 baseline recovery has a separate mode from Apply'
Assert-True ((Get-IntegrationModeDisposition -Mode Apply) -eq 'Apply') 'authorized Apply dispatches outside the read-only formatter'
Assert-Throws { Get-IntegrationStatusLines -Mode Apply -BaselineLabel 'validated' -MarkerLabel 'present' -SchemaState 'VALIDATED_V40_BASELINE' -TenantDataSummary 'TEST_ONLY_TENANT_ROWS' -ThroughVersion 42 -Plan $appliedPlan } 'read-only formatter rejects Apply mode'
foreach ($migration in $catalog) {
    [void](Split-IntegrationSqlBatches ([IO.File]::ReadAllText($migration.Path)))
    $validator = [IO.File]::ReadAllText($migration.ValidatorPath)
    Assert-DatabaseMigrationValidatorReadOnly -Text $validator -FileName (Split-Path -Leaf $migration.ValidatorPath)
    [void](Split-IntegrationSqlBatches (ConvertTo-IntegrationValidatorText $validator (Split-Path -Leaf $migration.ValidatorPath)))
}
$v82 = Join-Path $root 'database/migrations/V82-GuestVisitorAnalytics.sql'
$v82Validator = Join-Path $root 'database/migrations/validators/V82-GuestVisitorAnalytics.validate.sql'
[void](Split-IntegrationSqlBatches ([IO.File]::ReadAllText($v82)))
Assert-DatabaseMigrationValidatorReadOnly -Text ([IO.File]::ReadAllText($v82Validator)) -FileName 'V82-GuestVisitorAnalytics.validate.sql'
[void](Split-IntegrationSqlBatches ([IO.File]::ReadAllText($v82Validator)))
Assert-True $true 'V82 migration and validator batch parsing without execution'
$initializerSource = Get-Content -LiteralPath $initializer -Raw
Assert-True ($integrationTarget.HistoricalBaselineCommit -eq '36a9a90505c797ba206d93152e74668d9a304472' -and $initializerSource.Contains('archive --format=zip') -and $initializerSource.Contains('V40-StorefrontAllCategoryImage')) 'first-time baseline source is centrally pinned to the verified V40 commit and its V40 post-deploy chain'
Assert-True ($initializerSource -match 'Build-HistoricalV40Dacpac' -and $initializerSource -notmatch 'dotnet build \$sqlProject --configuration') 'initializer does not build baseline DACPAC from the mutable current SQL project'
$coreSource = Get-Content -LiteralPath $modulePath -Raw
Assert-True ($coreSource -match 'integration-database\.config\.json' -and $initializerSource -match 'expectedDatabase = \$targetBuilder.InitialCatalog' -and $expectedDatabase -eq 'WhatsBizERP_IntegrationTest_V82') 'initializer and SQL tests share the configured exact reusable database target'
foreach ($functionName in @('ConvertTo-DeterministicDacpac','Get-DacpacModelSha256','Build-HistoricalV40Dacpac','Remove-HistoricalV40Build')) {
    $functionAst = $initializerAst.Find({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $functionName },$true)
    if (-not $functionAst) { throw "Historical V40 artifact helper '$functionName' was not found." }
    Invoke-Expression $functionAst.Extent.Text
}
$script:HistoricalV40SourceCommit = '36a9a90505c797ba206d93152e74668d9a304472'
$script:HistoricalV40DacpacSha256 = $integrationTarget.HistoricalBaselineDacpacSha256
$Configuration = 'Release'
$historicalArtifact = Build-HistoricalV40Dacpac
try {
    Assert-True ($historicalArtifact.SourceCommit -ceq $script:HistoricalV40SourceCommit -and $historicalArtifact.Sha256 -ceq $integrationTarget.HistoricalBaselineDacpacSha256 -and (Test-Path -LiteralPath $historicalArtifact.Path -PathType Leaf)) 'historical V40 DACPAC is built from the pinned source commit and matches the pinned schema-payload SHA-256'
    $historicalProductSpecifications = Join-Path $historicalArtifact.Root 'database/WhatsBiz.Database/Tables/ProductSpecifications.sql'
    $currentProductSpecifications = Join-Path $root 'database/WhatsBiz.Database/Tables/ProductSpecifications.sql'
    Assert-True (-not (Test-Path -LiteralPath $historicalProductSpecifications) -and (Test-Path -LiteralPath $currentProductSpecifications)) 'historical V40 build excludes the later V49 ProductSpecifications object present in current source'
    $historicalWarehouse = Get-Content -LiteralPath (Join-Path $historicalArtifact.Root 'database/WhatsBiz.Database/Tables/Warehouses.sql') -Raw
    Assert-True ($historicalWarehouse -match 'WarehouseCode\].*WHERE \[IsDeleted\]' -and $historicalWarehouse -notmatch '\[TenantId\],\[WarehouseCode\]') 'historical V40 warehouse definition predates the V48 tenant-scoped index'
    $historicalPos = Get-Content -LiteralPath (Join-Path $historicalArtifact.Root 'database/WhatsBiz.Database/StoredProcedures/POS_PostInvoice.sql') -Raw
    Assert-True ($historicalPos -match '@ServicePincode' -and $historicalPos -match '@FreeDeliveryThresholdSnapshot') 'V44 contract validator can match a contract already present in the historical V40 source'
    $historicalPostDeploy = Get-Content -LiteralPath (Join-Path $historicalArtifact.Root 'database/WhatsBiz.Database/Scripts/PostDeployment.sql') -Raw
    Assert-True ($historicalPostDeploy -match 'V12-HierarchicalFeatureManagement\.sql' -and $historicalPostDeploy -match 'V17-DeliveryManagement\.sql') 'historical V40 post-deployment chain executes V12 and V17 seed scripts'
    $historicalRuntime = Get-Content -LiteralPath (Join-Path $historicalArtifact.Root 'database/WhatsBiz.Database/Scripts/RCDEV008-RuntimeObjects.sql') -Raw
    Assert-True ($historicalRuntime -match '\[StateCode\]\s+CHAR\s*\(2\)') 'V62 state-code validator requirement is present in historical V40 runtime seed source'
} finally { Remove-HistoricalV40Build $historicalArtifact }
$tenantClassificationSql = Get-IntegrationTenantClassificationSql
Assert-True ($tenantClassificationSql -match '(?im)^FROM\s+core\.Tenants\s+AS\s+T;\s*$') 'actual tenant classification query reads from canonical core.Tenants table'
Assert-True ($tenantClassificationSql -match 'COUNT\(\*\) AS TenantCount' -and $tenantClassificationSql -match 'UnknownTenantKeyCount' -and $tenantClassificationSql -match 'NonTestTenantCount') 'actual tenant classification query returns required safe aggregates'
Assert-True ($tenantClassificationSql -match 'T\.TenantKey IS NULL' -and $tenantClassificationSql -match 'T\.TenantKey NOT LIKE' -and $tenantClassificationSql -notmatch '(?<![.\w])TenantKey\b') 'every actual TenantKey reference is qualified through core.Tenants alias'
Assert-True ($tenantClassificationSql -match "T\.TenantId='11111111-1111-1111-1111-111111111111'" -and $tenantClassificationSql -match "T\.TenantKey=N'DEFAULT'" -and $tenantClassificationSql -match "T\.Name=N'Default V1 Tenant'" -and $tenantClassificationSql -match 'T\.IsActive=1') 'actual SQL identifies the canonical DEFAULT seed by stable ID and source-defined attributes'
$inventorySql = Get-IntegrationTenantScopedInventorySql
Assert-True ($inventorySql -match 'sys\.tables' -and $inventorySql -match "c\.name=N'TenantId'" -and $inventorySql -match 'FOR JSON PATH') 'tenant inventory SQL discovers tenant-scoped tables and emits aggregate JSON only'
Assert-True ($inventorySql -match "NOT \(s\.name=N'core' AND t\.name=N'Tenants'\)") 'tenant master is excluded from table inventory because it is separately seed-classified'
Assert-True ($inventorySql -match "s\.name=N'core'.*t\.name=N'Subscriptions'" -and $inventorySql -match "s\.name=N'core'.*t\.name=N'TenantFeatures'" -and $inventorySql -match "s\.name=N'commerce'.*t\.name=N'TenantDeliverySettings'" -and $inventorySql -match "s\.name=N'admin'.*t\.name=N'Companies'" -and $inventorySql -match 'Default V1 Tenant' -and $inventorySql -match 'ExpectedSeedRows') 'tenant inventory SQL encodes the V2, V12, V15, V17 and V29 baseline seed expectations'
Assert-True ($inventorySql -match 'SUM\(R\.ExpectedSeedRow\) AS ExpectedSeedRows' -and $inventorySql -match 'FROM \(SELECT T\.TenantId,CASE WHEN' -and $inventorySql -match 'END AS ExpectedSeedRow' -and $inventorySql -match 'AS T\) AS R HAVING COUNT_BIG') 'subquery-based seed eligibility is projected per row in a derived table before aggregation'
Assert-True ($inventorySql -notmatch 'SUM\(CASE WHEN ''\+CASE' -and $inventorySql -notmatch 'SUM\([^\r\n]*EXISTS\s*\(') 'generated inventory SQL contains no aggregate expression with a subquery'
$emptyInventory = ConvertFrom-IntegrationTenantScopedInventoryJson -Json '[]'
Assert-True ($emptyInventory -is [System.Array] -and $emptyInventory.Count -eq 0) 'actual inventory JSON collector returns a flat empty collection for zero rows'
Assert-True (@(Test-IntegrationTenantScopedInventory -Inventory $emptyInventory).Count -eq 4) 'empty tenant tables fail closed because all four canonical V40 seed tables are missing'
$pipelineWrappedInventory = @('[{"TableName":"core.Subscriptions","TotalRows":1,"UnownedRows":0,"CanonicalTenantRows":1,"ExpectedSeedRows":1,"OtherTenantRows":0}]' | ConvertFrom-Json -ErrorAction Stop)
Assert-True ($pipelineWrappedInventory.Count -eq 1 -and $pipelineWrappedInventory[0] -is [System.Array]) 'legacy ConvertFrom-Json pipeline form reproduces the nested-array inventory defect'
Assert-Throws { Test-IntegrationTenantScopedInventory -Inventory $pipelineWrappedInventory } 'inventory policy rejects the legacy nested-array result rather than silently flattening it'
$singleInventory = ConvertFrom-IntegrationTenantScopedInventoryJson -Json '[{"TableName":"core.Subscriptions","TotalRows":1,"UnownedRows":0,"CanonicalTenantRows":1,"ExpectedSeedRows":1,"OtherTenantRows":0}]'
Assert-True ($singleInventory -is [System.Array] -and $singleInventory.Count -eq 1 -and $singleInventory[0] -isnot [System.Array]) 'actual inventory JSON collector returns one flat record'
$multipleInventory = ConvertFrom-IntegrationTenantScopedInventoryJson -Json '[{"TableName":"admin.Companies","TotalRows":1,"UnownedRows":0,"CanonicalTenantRows":1,"ExpectedSeedRows":1,"OtherTenantRows":0},{"TableName":"core.Subscriptions","TotalRows":1,"UnownedRows":0,"CanonicalTenantRows":1,"ExpectedSeedRows":1,"OtherTenantRows":0}]'
Assert-True ($multipleInventory -is [System.Array] -and $multipleInventory.Count -eq 2 -and $multipleInventory[0] -isnot [System.Array]) 'actual inventory JSON collector returns multiple flat records'
Assert-Throws { ConvertFrom-IntegrationTenantScopedInventoryJson -Json '[null]' } 'actual inventory JSON collector rejects null rows'
Assert-Throws { ConvertFrom-IntegrationTenantScopedInventoryJson -Json '[[{"TableName":"x"}]]' } 'actual inventory JSON collector rejects nested arrays'
Assert-Throws { ConvertFrom-IntegrationTenantScopedInventoryJson -Json '{"TableName":"x"}' } 'actual inventory JSON collector rejects unexpected non-array SQL result shape'
Assert-Throws { ConvertFrom-IntegrationTenantScopedInventoryJson -Json '[{"TableName":"x"}]' } 'actual inventory JSON collector rejects malformed row fields'
$baselinePostDeploy = Get-Content -LiteralPath (Join-Path $root 'database/WhatsBiz.Database/Scripts/PostDeployment.sql') -Raw
$v2SeedScript = Get-Content -LiteralPath (Join-Path $root 'database/WhatsBiz.Database/Scripts/V2-FeatureEntitlements.sql') -Raw
$v29SeedScript = Get-Content -LiteralPath (Join-Path $root 'database/WhatsBiz.Database/Scripts/V29-NewTenantIsolationAndOnboarding.sql') -Raw
Assert-True ($baselinePostDeploy -match ':r \.\\V2-FeatureEntitlements\.sql' -and $v2SeedScript -match "'11111111-1111-1111-1111-111111111111'.*'22222222-2222-2222-2222-222222222222'" -and $v2SeedScript -match "'DEFAULT','Default V1 Tenant',1" -and $v29SeedScript -match 'INSERT admin\.Companies') 'canonical seed expectations are backed by V40 post-deployment chain source'
$cleanAuditBlockers = @(Get-IntegrationBaselineAuditBlockers -SchemaState 'V40_SCHEMA_PUBLISHED_LEDGER_MISSING' -BaselineValidatorPassed $true -BaselineLedgerState ABSENT -MarkerPresent $false -MigrationLedgerState ABSENT -AppliedMigrationCount 0 -DacpacOperationCount 0 -TenantState CANONICAL_DEFAULT_SEED_ONLY -TenantCount 1 -CanonicalSeedCount 1 -UnknownTenantKeyCount 0 -NonTenantSeedDataVerified $true)
Assert-True ($cleanAuditBlockers.Count -eq 0) 'clean V40 baseline audit policy has no blockers'
$seedInventory = @(
    [pscustomobject]@{TableName='admin.Companies';TotalRows=[long]1;UnownedRows=[long]0;CanonicalTenantRows=[long]1;ExpectedSeedRows=[long]1;OtherTenantRows=[long]0},
    [pscustomobject]@{TableName='core.Subscriptions';TotalRows=[long]1;UnownedRows=[long]0;CanonicalTenantRows=[long]1;ExpectedSeedRows=[long]1;OtherTenantRows=[long]0},
    [pscustomobject]@{TableName='core.TenantFeatures';TotalRows=[long]31;UnownedRows=[long]0;CanonicalTenantRows=[long]31;ExpectedSeedRows=[long]31;OtherTenantRows=[long]0},
    [pscustomobject]@{TableName='commerce.TenantDeliverySettings';TotalRows=[long]1;UnownedRows=[long]0;CanonicalTenantRows=[long]1;ExpectedSeedRows=[long]1;OtherTenantRows=[long]0}
)
Assert-True (@(Test-IntegrationTenantScopedInventory -Inventory $seedInventory).Count -eq 0) 'expected V40 tenant-scoped seed inventory is accepted'
$unexpectedTenantInventory = @($seedInventory | ForEach-Object { if ($_.TableName -eq 'core.Subscriptions') { [pscustomobject]@{TableName=$_.TableName;TotalRows=[long]2;UnownedRows=[long]0;CanonicalTenantRows=[long]1;ExpectedSeedRows=[long]1;OtherTenantRows=[long]1} } else { $_ } })
Assert-True (@(Test-IntegrationTenantScopedInventory -Inventory $unexpectedTenantInventory).Count -eq 1) 'an unexpected tenant row remains a recovery blocker after the SQL aggregation fix'
$unexpectedFeatureSeed = @($seedInventory | ForEach-Object { if ($_.TableName -eq 'core.TenantFeatures') { [pscustomobject]@{TableName=$_.TableName;TotalRows=[long]32;UnownedRows=[long]0;CanonicalTenantRows=[long]32;ExpectedSeedRows=[long]31;OtherTenantRows=[long]0} } else { $_ } })
Assert-True (@(Test-IntegrationTenantScopedInventory -Inventory $unexpectedFeatureSeed).Count -gt 0) 'tenant-feature rows beyond source-defined seed rows block baseline audit'
$seedInventoryJson = ConvertTo-Json -InputObject $seedInventory -Compress
$collectedSeedInventory = ConvertFrom-IntegrationTenantScopedInventoryJson -Json $seedInventoryJson
Assert-True (@(Test-IntegrationTenantScopedInventory -Inventory $collectedSeedInventory).Count -eq 0) 'flat collected V40 inventory including canonical V12/V15/V17 seed rows passes inventory policy'
$unexpectedInventory = @($seedInventory + [pscustomobject]@{TableName='sales.Customers';TotalRows=[long]1;UnownedRows=[long]0;CanonicalTenantRows=[long]1;ExpectedSeedRows=[long]0;OtherTenantRows=[long]0})
$unexpectedInventoryBlockers = @(Test-IntegrationTenantScopedInventory -Inventory $unexpectedInventory)
Assert-True ($unexpectedInventoryBlockers.Count -eq 1 -and $unexpectedInventoryBlockers[0] -match 'sales.Customers') 'unexpected tenant-scoped business records block reconciliation eligibility'
$unexpectedBusinessAudit = @(Get-IntegrationBaselineAuditBlockers -SchemaState 'V40_SCHEMA_PUBLISHED_LEDGER_MISSING' -BaselineValidatorPassed $true -BaselineLedgerState ABSENT -MarkerPresent $false -MigrationLedgerState ABSENT -AppliedMigrationCount 0 -DacpacOperationCount 0 -TenantState CANONICAL_DEFAULT_SEED_ONLY -TenantCount 1 -CanonicalSeedCount 1 -UnknownTenantKeyCount 0 -NonTenantSeedDataVerified $true -InventoryBlockers @('Unexpected business/seed records exist in sales.Customers.'))
Assert-True ($unexpectedBusinessAudit.Count -eq 1 -and $unexpectedBusinessAudit[0] -match 'sales\.Customers') 'unexpected business-table records propagate to a blocked recovery audit result'
$unexpectedExtraBlockers = @(Get-IntegrationBaselineAuditBlockers -SchemaState 'V40_SCHEMA_PUBLISHED_LEDGER_MISSING' -BaselineValidatorPassed $true -BaselineLedgerState ABSENT -MarkerPresent $false -MigrationLedgerState ABSENT -AppliedMigrationCount 0 -UntrackedMigrationVersions @(41) -DacpacOperationCount 0 -TenantState CANONICAL_DEFAULT_SEED_ONLY -TenantCount 1 -CanonicalSeedCount 1 -UnknownTenantKeyCount 0 -NonTenantSeedDataVerified $true)
Assert-True ($unexpectedExtraBlockers.Count -gt 0 -and $unexpectedExtraBlockers[0] -match 'Unledgered later migration') 'unledgered post-V40 migration validator match blocks audit eligibility'
$partialAuditBlockers = @(Get-IntegrationBaselineAuditBlockers -SchemaState 'PARTIAL_MIGRATION_LEDGER' -BaselineValidatorPassed $false -BaselineLedgerState PARTIAL -MarkerPresent $false -MigrationLedgerState PARTIAL -AppliedMigrationCount 0 -DacpacOperationCount 1 -TenantState CANONICAL_DEFAULT_SEED_ONLY -TenantCount 1 -CanonicalSeedCount 1 -UnknownTenantKeyCount 0 -NonTenantSeedDataVerified $true -InventoryBlockers @('Unexpected data'))
Assert-True ($partialAuditBlockers.Count -ge 5) 'partial schema, failed validator, ledger, DACPAC and inventory states all block audit eligibility'
$repeatAuditBlockers = @(Get-IntegrationBaselineAuditBlockers -SchemaState 'VALIDATED_V40_BASELINE' -BaselineValidatorPassed $true -BaselineLedgerState CONSISTENT -MarkerPresent $true -MigrationLedgerState EMPTY -AppliedMigrationCount 0 -DacpacOperationCount 0 -TenantState CANONICAL_DEFAULT_SEED_ONLY -TenantCount 1 -CanonicalSeedCount 1 -UnknownTenantKeyCount 0 -NonTenantSeedDataVerified $true)
Assert-True ($repeatAuditBlockers.Count -gt 0) 'already registered state is not treated as a missing-ledger reconciliation candidate'
$incompleteGlobalDataAudit = @(Get-IntegrationBaselineAuditBlockers -SchemaState 'V40_SCHEMA_PUBLISHED_LEDGER_MISSING' -BaselineValidatorPassed $true -BaselineLedgerState ABSENT -MarkerPresent $false -MigrationLedgerState ABSENT -AppliedMigrationCount 0 -DacpacOperationCount 0 -TenantState CANONICAL_DEFAULT_SEED_ONLY -TenantCount 1 -CanonicalSeedCount 1 -UnknownTenantKeyCount 0 -NonTenantSeedDataVerified $false)
Assert-True ($incompleteGlobalDataAudit.Count -eq 1 -and $incompleteGlobalDataAudit[0] -match 'Global/non-tenant') 'incomplete non-tenant seed inventory explicitly blocks reconciliation eligibility'
$mismatchedLedgerAudit = @(Get-IntegrationBaselineAuditBlockers -SchemaState 'V40_SCHEMA_PUBLISHED_LEDGER_MISSING' -BaselineValidatorPassed $true -BaselineLedgerState MISMATCHED -MarkerPresent $false -MigrationLedgerState ABSENT -AppliedMigrationCount 0 -DacpacOperationCount 0 -TenantState CANONICAL_DEFAULT_SEED_ONLY -TenantCount 1 -CanonicalSeedCount 1 -UnknownTenantKeyCount 0 -NonTenantSeedDataVerified $true)
Assert-True ($mismatchedLedgerAudit.Count -eq 1 -and $mismatchedLedgerAudit[0] -match 'MISMATCHED') 'mismatched baseline ledger blocks audit eligibility'
Assert-True ($initializerSource -match 'Get-IntegrationTenantClassificationSql' -and $initializerSource -notmatch 'SELECT\s+TenantKey\b') 'initializer executes the tested aggregate query and never selects tenant key values'
Assert-True ($initializerSource -match 'ConvertFrom-IntegrationTenantScopedInventoryJson\s+-Json\s+\$inventoryJson' -and $initializerSource -notmatch '\$inventoryJson\s*\|\s*ConvertFrom-Json') 'AuditBaseline uses the tested direct-input inventory parser rather than pipeline-wrapping the JSON array'
Assert-True ($initializerSource -match 'UnknownTenantKeyCount\s+-gt\s+0' -and $initializerSource -match 'NonSeedNonTestTenantCount\s+-gt\s+0') 'Apply permits only the exact canonical DEFAULT seed alongside classified test tenants'
Assert-True ($initializerSource -match "ValidateSet\('Status','DryRun','Apply','AuditBaseline','RecoverBaseline'\)" -and $initializerSource -match 'if \(\$Mode -eq ''AuditBaseline''\) \{') 'audit and recovery modes are explicit and separate from Apply'
$auditFunctionSource = [regex]::Match($initializerSource,'(?s)function Invoke-ReadOnlyV40BaselineAudit\(.*?\n\}').Value
Assert-True ($auditFunctionSource -and $auditFunctionSource -notmatch '(?i)ExecuteNonQuery|\b(INSERT|UPDATE|DELETE|ALTER|DROP|PUBLISH)\b') 'baseline audit function contains no database or DACPAC write operation'
Assert-True ($initializerSource -notmatch '(?i)DROP\s+DATABASE' -and $initializerSource -match 'Where-Object \{ \$_\.Version -gt \$baselineVersion \}' -and $initializerSource -match 'Get-IntegrationDatabaseCreationDecision') 'initializer discovers every canonical migration after V40 and creates only by explicit decision without reset'
Assert-True ($initializerSource -match '\[switch\]\$CreateIfAbsent' -and $initializerSource -match 'Assert-IntegrationInitializationConfirmation -Mode \$Mode -ConfirmTarget \$ConfirmTarget') 'creation remains behind exact typed Apply confirmation'
Assert-True ($coreSource -match 'function Get-IntegrationDatabaseCreationDecision' -and $coreSource -match 'if \(\$DatabaseExists\) \{ return ''USE_EXISTING'' \}') 'existing shared database is preserved on repeated initialization'
$provisioningSql = Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\provision-integration-database.sql') -Raw
Assert-True ($provisioningSql -match "SERVERPROPERTY\('ServerName'\).*DESKTOP-DQ0868S" -and $provisioningSql -match "DB_ID\(N'WhatsBizERP_IntegrationTest_V82'\) IS NOT NULL" -and $provisioningSql -match 'CREATE DATABASE \[WhatsBizERP_IntegrationTest_V82\]' -and $provisioningSql -notmatch '(?i)\b(DROP|ALTER LOGIN|GRANT)\b') 'one-time DBA provisioning script is target-scoped, refuses existing databases and grants no permissions'
$permissionCheckSource = [regex]::Match($initializerSource,'(?s)function Test-DatabaseControlPermission \{.*?\n\}').Value
Assert-True ($permissionCheckSource -match "HAS_PERMS_BY_NAME\(DB_NAME\(\),N'DATABASE',N'CONTROL'\)" -and $initializerSource -match 'if \(-not \(Test-DatabaseControlPermission\)\)') 'ongoing schema authorization is checked at database scope before Apply writes'
Assert-True ($initializerSource -match '\$history = Get-MigrationHistory' -and $initializerSource -notmatch '\$history\s*=\s*@\(Get-MigrationHistory\)') 'history collection is not wrapped into a nested array'
Assert-True ($initializerSource -match "if \(-not \(Get-TargetState -Operation 'Verify migration history table before reading ledger'\)\.HasHistory\) \{ throw 'Migration history table is missing") 'missing history table is not accepted as an initialized ledger'
$preflightPosition = $initializerSource.IndexOf('$null = Assert-DatabaseMigrationPreflight')
$workflowPosition = $initializerSource.IndexOf('Invoke-DatabaseMigrationWorkflow -Pending $pending -Mode Deploy')
Assert-True ($preflightPosition -ge 0 -and $workflowPosition -gt $preflightPosition) 'Apply runs integrity preflight before migration workflow'
$statusReturnPosition = $initializerSource.IndexOf("if (`$Mode -eq 'Status') { return }")
$dryRunReturnPosition = $initializerSource.IndexOf("if (`$Mode -eq 'DryRun') {")
$applyReconnectPosition = $initializerSource.IndexOf("Refresh-WorkConnection -Operation 'Revalidate approved SQL target before Apply preflight'")
Assert-True ($statusReturnPosition -ge 0 -and $dryRunReturnPosition -gt $statusReturnPosition -and $applyReconnectPosition -gt $dryRunReturnPosition) 'Status and DryRun return before Apply connection refresh or write gates'
$statusHelperSource = [regex]::Match((Get-Content -LiteralPath $modulePath -Raw),'(?s)function Get-IntegrationStatusLines \{.*?\n\}').Value
Assert-True ($statusHelperSource -and $statusHelperSource -notmatch '(?i)CreateCommand|ExecuteNonQuery|\b(INSERT|UPDATE|DELETE|ALTER|DROP)\b') 'Status and DryRun formatter has no database write operations'
$publishIndex = $initializerSource.IndexOf('$publishOutput = @(& $sqlPackage @arguments')
$closeBeforePublishIndex = $initializerSource.LastIndexOf('Close-WorkConnection', $publishIndex)
$refreshAfterPublishIndex = $initializerSource.IndexOf("Refresh-WorkConnection -Operation 'Revalidate approved SQL target after V40 DACPAC Publish'", $publishIndex)
Assert-True ($closeBeforePublishIndex -ge 0 -and $refreshAfterPublishIndex -gt $publishIndex) 'work connection is disposed before DACPAC publish and reopened/revalidated afterward'
Assert-True ($initializerSource.Contains('$script:LockConnection = Open-LockConnection') -and $initializerSource.Contains('Assert-MigrationLockHeld')) 'migration lock uses a separate master session across DACPAC connection replacement'
Assert-True ($initializerSource.Contains('the baseline may be partially published. No retry was attempted. Run Status to reconcile') -and $initializerSource.Contains('$script:MigrationTransaction.Rollback()')) 'baseline and migration failures stop for status reconciliation without automatic retry'
Assert-True ($initializerSource.Contains('Commit outcome may be unknown; no retry was attempted. Run Status') -and $initializerSource.Contains('Commit migration V$($Migration.Version)')) 'ledger commit failures identify migration and require reconciliation'

$lockFunctionSource = [regex]::Match($initializerSource,'(?s)function Assert-MigrationLockHeld \{.*?\n\}').Value
Assert-True ($initializerSource -match 'Open-LockConnection' -and $initializerSource -match '\$builder\.InitialCatalog|New-IntegrationMasterConnectionStringBuilder' -and $initializerSource -match 'ConnectRetryCount = 0' -and $initializerSource -match 'Pooling = \$false') 'lock session is dedicated, non-reconnecting and isolated in master from target DACPAC publication'
Assert-True ($lockFunctionSource -match '@@SPID' -and $lockFunctionSource -match 'APPLOCK_MODE' -and $lockFunctionSource -match 'LockSessionId' -and $lockFunctionSource -match 'all further database writes are refused') 'lost or replaced SQL application-lock session fails closed'
Assert-True ($initializerSource -match 'RecoverBaseline phase 1: running read-only diagnostics' -and $initializerSource -match 'RecoverBaseline phase 2: repeating every recovery prerequisite' -and $initializerSource -match 'if \(-not \$lockedAudit\.Eligible\)') 'clean interrupted V40 recovery requires read-only audit followed by a locked re-audit'
Assert-True ($initializerSource -match 'DACPAC SHA-256 mismatch' -and $integrationTarget.HistoricalBaselineDacpacSha256 -match '^[A-F0-9]{64}$') 'historical V40 DACPAC schema payload is hash pinned'
Assert-True ($initializerSource -match 'Get-V40GlobalDataAudit' -and $initializerSource -match 'Unexpected business/seed records exist' -and $initializerSource -match 'NonTenantSeedDataVerified \(\$globalDataBlockers.Count -eq 0\)') 'unexpected global seed or business data blocks recovery'
Assert-True ($initializerSource -match 'SchemaOperationCount' -and $initializerSource -match 'UntrackedMigrationVersions' -and $initializerSource -match 'MigrationLedgerState') 'unexpected post-V40 schema and partial migration artifacts are recovery blockers'
Assert-True ($initializerSource -match 'Initialize-BaselineLedger' -and $initializerSource -match 'transaction\.Rollback' -and $initializerSource -match 'registration verification failed') 'failed baseline registration rolls back or stops for outcome reconciliation'
Assert-True ($initializerSource -match 'already complete and canonical; idempotent verification performed no writes') 'successful recovery is idempotent and does not apply later migrations'
Assert-True ($initializerSource -match 'V41\+ migrations were not applied' -and $initializerSource -match 'Get-DatabaseMigrationPlan -Migrations \$allMigrations' -and $initializerSource -match 'Invoke-DatabaseMigrationWorkflow -Pending \$pending') 'recovery leaves V41+ pending for normal subsequent Apply sequencing'

Write-Host "Integration initializer focused tests passed: $script:passed assertions. No SQL connection was opened."
