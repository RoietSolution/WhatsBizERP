[CmdletBinding()]
param(
    [string] $Server = 'DESKTOP-DQ0868S',
    [string] $Database = 'WhatsBizERP_PosGstIntegrationTest',
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$expectedServer = 'DESKTOP-DQ0868S'
$expectedDatabase = 'WhatsBizERP_PosGstIntegrationTest'
$forbidden = @('WhatsBizERP', 'WhatsBizERP_QA', 'WhatsBizERP_PROD')
if ($Server -cne $expectedServer) { throw "This diagnostic is restricted to $expectedServer." }
if ($Database -cne $expectedDatabase -or $forbidden -contains $Database) { throw "This diagnostic is restricted to $expectedDatabase." }

$sqlCmd = (Get-Command sqlcmd -ErrorAction Stop).Source
$connection = $null

function Get-QueryRows([System.Data.SqlClient.SqlConnection] $Connection, [string] $Query) {
    $command = $Connection.CreateCommand()
    $command.CommandTimeout = 0
    $command.CommandText = $Query
    $rows = [System.Collections.Generic.List[object]]::new()
    try {
        $reader = $command.ExecuteReader()
        try {
            while ($reader.Read()) {
                $row = [ordered]@{}
                for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                    $row[$reader.GetName($i)] = if ($reader.IsDBNull($i)) { $null } else { $reader.GetValue($i) }
                }
                $rows.Add([pscustomobject]$row)
            }
        } finally { $reader.Dispose() }
    } finally { $command.Dispose() }
    return @($rows.ToArray())
}

try {
    $master = [System.Data.SqlClient.SqlConnection]::new("Server=$Server;Database=master;Integrated Security=True;Encrypt=True;TrustServerCertificate=True")
    $master.Open()
    $identity = @(Get-QueryRows $master "SET NOCOUNT ON; SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName;")[0]
    if ([string]$identity.ServerName -cne $expectedServer -or [string]$identity.DatabaseName -cne 'master') {
        throw "Unexpected SQL identity. Observed server '$($identity.ServerName)' and database '$($identity.DatabaseName)'."
    }
    $exists = [int](@(Get-QueryRows $master "SELECT CASE WHEN DB_ID(N'$expectedDatabase') IS NULL THEN 0 ELSE 1 END AS DatabaseExists;")[0].DatabaseExists)
    $master.Dispose()
    if ($exists -ne 1) { throw "The isolated database '$expectedDatabase' does not exist." }

    $connection = [System.Data.SqlClient.SqlConnection]::new("Server=$Server;Database=$Database;Integrated Security=True;Encrypt=True;TrustServerCertificate=True")
    $connection.Open()
    $targetIdentity = @(Get-QueryRows $connection "SET NOCOUNT ON; SELECT @@SERVERNAME AS ServerName, DB_NAME() AS DatabaseName;")[0]
    if ([string]$targetIdentity.ServerName -cne $expectedServer -or [string]$targetIdentity.DatabaseName -cne $expectedDatabase) {
        throw "Unexpected target identity. Observed server '$($targetIdentity.ServerName)' and database '$($targetIdentity.DatabaseName)'."
    }

    $baselineColumns = @(Get-QueryRows $connection @"
SELECT v.SchemaName,v.ObjectName,v.ColumnName,
       CASE WHEN c.object_id IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS Present,
       t.name AS SqlType,c.max_length,c.is_nullable
FROM (VALUES
 (N'sales',N'Customers',N'MobileNormalized'),
 (N'master',N'Products',N'PackSize'),
 (N'commerce',N'StorefrontConfigurations',N'AllCategoryMediaId'),
 (N'commerce',N'StorefrontConfigurations',N'DeliveryEnabled'),
 (N'commerce',N'StorefrontConfigurations',N'ShowProductReviews')
) v(SchemaName,ObjectName,ColumnName)
LEFT JOIN sys.objects o ON o.schema_id=SCHEMA_ID(v.SchemaName) AND o.name=v.ObjectName AND o.type='U'
LEFT JOIN sys.columns c ON c.object_id=o.object_id AND c.name=v.ColumnName
LEFT JOIN sys.types t ON t.user_type_id=c.user_type_id;
"@)

    $baselineObjects = @(Get-QueryRows $connection @"
SELECT v.SchemaName,v.ObjectName,v.ObjectType,
       CASE WHEN o.object_id IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS Present,
       o.is_ms_shipped AS IsMsShipped
FROM (VALUES
 (N'core',N'Tenants',N'U'),(N'master',N'Products',N'U'),(N'sales',N'Customers',N'U'),
 (N'commerce',N'StorefrontMedia',N'U'),(N'commerce',N'StorefrontConfigurations',N'U'),
 (N'commerce',N'StorefrontBanners',N'U'),(N'commerce',N'StorefrontOtpChallenges',N'U'),
 (N'commerce',N'StorefrontWishlistItems',N'U'),(N'commerce',N'StorefrontProductReviews',N'U'),
 (N'commerce',N'StorefrontPromotions',N'U'),(N'commerce',N'StorefrontRefunds',N'U'),
 (N'sales',N'FullSaleReversals',N'U'),(N'inventory',N'InventoryTransactions',N'U'),
 (N'finance',N'PostSource',N'P'),(N'sales',N'POS_AddPayment',N'P'),
 (N'sales',N'POS_PostInvoice',N'P'),(N'sales',N'POS_ReverseFullSale',N'P'),
 (N'finance',N'SettleStorefrontRefund',N'P'),(N'core',N'TR_Users_ResourceCapacity',N'TR')
) v(SchemaName,ObjectName,ObjectType)
LEFT JOIN sys.objects o ON o.schema_id=SCHEMA_ID(v.SchemaName) AND o.name=v.ObjectName AND o.type=v.ObjectType;
"@)

    $constraints = @(Get-QueryRows $connection @"
SELECT 'INDEX' AS Kind,OBJECT_SCHEMA_NAME(i.object_id) AS SchemaName,OBJECT_NAME(i.object_id) AS ObjectName,
       i.name AS ConstraintName,CAST(i.is_unique AS int) AS IsUnique,CAST(i.has_filter AS int) AS HasFilter,
       i.filter_definition AS Definition
FROM sys.indexes i
WHERE i.name=N'UX_Customers_TenantMobileNormalized'
UNION ALL
SELECT 'FOREIGN_KEY',OBJECT_SCHEMA_NAME(fk.parent_object_id),OBJECT_NAME(fk.parent_object_id),fk.name,
       NULL,NULL,CONVERT(nvarchar(max),fk.is_disabled)
FROM sys.foreign_keys fk
WHERE fk.name=N'FK_StorefrontConfigurations_AllCategoryMedia'
UNION ALL
SELECT 'CHECK',OBJECT_SCHEMA_NAME(cc.parent_object_id),OBJECT_NAME(cc.parent_object_id),cc.name,
       NULL,NULL,cc.definition
FROM sys.check_constraints cc
WHERE cc.name IN (N'CK_StorefrontMedia_Resource',N'CK_InventoryTransactions_Type')
UNION ALL
SELECT 'TRIGGER',OBJECT_SCHEMA_NAME(tr.parent_id),OBJECT_NAME(tr.parent_id),tr.name,
       NULL,NULL,CONVERT(nvarchar(max),tr.is_disabled)
FROM sys.triggers tr
WHERE tr.name=N'TR_Users_ResourceCapacity';
"@)

    $ledger = @(Get-QueryRows $connection @"
SELECT
 CASE WHEN OBJECT_ID(N'deployment.DatabaseMigrationBaseline',N'U') IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS BaselineTableExists,
 CASE WHEN OBJECT_ID(N'deployment.DatabaseMigrationHistory',N'U') IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS HistoryTableExists;
"@)[0]
    $baselineRow = @()
    $history = @()
    if ($ledger.BaselineTableExists) {
        $baselineRow = @(Get-QueryRows $connection 'SELECT SingletonId,BaselineVersion,BaselineName,BaselineValidatorFile,BaselineValidatorHash FROM deployment.DatabaseMigrationBaseline ORDER BY SingletonId;')
    }
    if ($ledger.HistoryTableExists) {
        $history = @(Get-QueryRows $connection 'SELECT MigrationVersion,MigrationName,ScriptHash,ValidatorHash,IsAdopted FROM deployment.DatabaseMigrationHistory ORDER BY MigrationVersion;')
    }

    $tableCounts = @(Get-QueryRows $connection @"
SELECT s.name AS SchemaName,o.name AS TableName,
       SUM(p.rows) AS [TableRowCount]
FROM sys.objects o
JOIN sys.schemas s ON s.schema_id=o.schema_id
JOIN sys.partitions p ON p.object_id=o.object_id AND p.index_id IN (0,1)
WHERE o.type='U'
GROUP BY s.name,o.name
ORDER BY s.name,o.name;
"@)

    $result = [ordered]@{
        ReadOnly = $true
        Server = [string]$targetIdentity.ServerName
        Database = [string]$targetIdentity.DatabaseName
        BaselineColumns = $baselineColumns
        BaselineObjectsAndProcedures = $baselineObjects
        BaselineConstraintsAndIndexes = $constraints
        MigrationLedger = [ordered]@{Tables = $ledger; BaselineRows = $baselineRow; HistoryRows = $history}
        KnownV64Artifact = @(Get-QueryRows $connection "SELECT CASE WHEN COL_LENGTH(N'master.Products',N'GstPricingType') IS NULL THEN CAST(0 AS bit) ELSE CAST(1 AS bit) END AS GstPricingTypeExists;")[0]
        TableRowCounts = $tableCounts
    }
    $json = $result | ConvertTo-Json -Depth 8
    if ([string]::IsNullOrWhiteSpace($OutputPath)) { $json | Write-Output }
    else { $json | Set-Content -LiteralPath $OutputPath -Encoding UTF8; Write-Host "Read-only diagnostic written to $OutputPath" }
} finally {
    if ($connection) { $connection.Dispose() }
}
