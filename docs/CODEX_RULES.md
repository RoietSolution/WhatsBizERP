## Database Development Standard

- Use the local SQL Server configured in `appsettings.Development.json`.
- Apply all schema changes directly to the development database.
- Create or alter tables, stored procedures, views, functions, indexes, and constraints as required.
- Preserve existing data where possible.
- Verify the schema after every sprint.
- Build and run the application successfully.

Do NOT generate separate SQL files for every database object during normal development.

Generate deployment or upgrade SQL scripts only when explicitly requested for a release.