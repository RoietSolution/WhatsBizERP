# Database and EF compatibility checks

The fast `EfTriggerCompatibilityTests` scan repository trigger definitions under
`database/WhatsBiz.Database`, match trigger targets to
the SQL Server `ApplicationDbContext` model, and require
`IsSqlOutputClauseUsed() == false` for every mapped entity on a triggered
table. The rule applies to INSERT, UPDATE, and DELETE triggers. Keep
`RowVersion` properties configured as generated concurrency tokens.

Run the focused checks after changing a trigger or EF table mapping:

```powershell
dotnet test backend/tests/WhatsBiz.Tests/WhatsBiz.Tests.csproj --filter "FullyQualifiedName~EfTriggerCompatibilityTests|FullyQualifiedName~UserTriggerMappingTests"
```

The SQL checks use the existing `SqlIntegrationDatabase` gate. Set
`ConnectionStrings__IntegrationTests` to an explicitly disposable, current-schema
Test/Integration database. Never point it at QA or production. The new
`SqlEfWriteCompatibilityTests` roll back their user, role, product, customer,
and storefront configuration writes. Run them alongside these existing SQL
checks for the remaining service paths:

| Write path | SQL-backed check |
| --- | --- |
| Password update and user RowVersion; role assignment and removal | `SqlEfWriteCompatibilityTests.IdentityProductCustomerAndStorefrontWritesWorkWithPublishedTriggers` |
| Product and customer save | Same test; `SqlCommerceIntegrationTests.SqlBackedCommerceAndSecurityRegressionPasses` |
| Storefront configuration insert and update | Same test |
| POS sale, payment, and inventory sale/restoration writes | `ErpSaleReversalRuntimeIntegrationTests.PaidStorefrontApprovalReversesAndPreparesRefundWithoutSettlingMoney` |

These SQL tests are a database/application compatibility gate after publishing
schema changes to the disposable test database. The fast trigger audit does not
replace them: it cannot detect SQL changes made outside this repository.