# Database Rules

## Connection

Always use the connection string configured in:

- backend/WhatsBiz.Api/appsettings.Development.json

Never create a new connection string.

Never assume Windows Authentication.

Never assume SQL Authentication.

Use exactly the configured connection string.

## Database

Use the existing SQL Server instance.

Database Name:

WhatsBizERP

## Development

For every sprint:

- Connect to SQL Server.
- Create missing tables.
- Create or alter stored procedures.
- Create or alter views.
- Create or alter functions.
- Create indexes.
- Create constraints.
- Seed required data.
- Preserve existing data.
- Apply schema changes directly to the database.

## Repository

Every database object must have its corresponding SQL file inside:

database/

Tables/

StoredProcedures/

Views/

Functions/

SeedData/

Scripts/

## Validation

At the end of every sprint:

- Verify SQL Server schema.
- Verify application startup.
- Verify API.
- Verify Angular application.
- Build solution.
- Fix errors before completing the sprint.

No sprint is complete until the application runs successfully.