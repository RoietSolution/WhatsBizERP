$script:WhatsBizApplicationSchemas = @(
    # Schemas in the SQL project model (Schemas/Schemas.sql).
    'audit', 'core', 'finance', 'integration', 'inventory',
    'marketing', 'master', 'purchase', 'reporting', 'sales',
    # Schemas introduced by canonical commerce migrations and runtime objects.
    'admin', 'commerce', 'dashboard', 'gst', 'loyalty', 'printing'
)

function Get-WhatsBizApplicationSchemaSqlList {
    [OutputType([string])]
    param()

    return (($script:WhatsBizApplicationSchemas | ForEach-Object { "N'$_'" }) -join ',')
}

function Get-WhatsBizApplicationSchemaCount {
    [OutputType([int])]
    param([AllowEmptyCollection()][string[]] $SchemaNames = @())

    $knownSchemas = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($name in $script:WhatsBizApplicationSchemas) { [void]$knownSchemas.Add($name) }

    $count = 0
    foreach ($name in $SchemaNames) {
        if ($knownSchemas.Contains($name)) { $count++ }
    }
    return $count
}

function Test-WhatsBizFreshDatabase {
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)][long] $UserTableCount,
        [Parameter(Mandatory)][long] $UserObjectCount,
        [AllowEmptyCollection()][string[]] $SchemaNames = @()
    )

    if ($UserTableCount -lt 0 -or $UserObjectCount -lt 0) { throw 'Database object counts cannot be negative.' }
    return $UserTableCount -eq 0 -and $UserObjectCount -eq 0 -and (Get-WhatsBizApplicationSchemaCount -SchemaNames $SchemaNames) -eq 0
}

Export-ModuleMember -Function Get-WhatsBizApplicationSchemaSqlList, Get-WhatsBizApplicationSchemaCount, Test-WhatsBizFreshDatabase
