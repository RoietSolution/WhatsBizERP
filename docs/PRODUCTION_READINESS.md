# V1 Production Readiness Checklist

## Application

- [x] Backend Release build has zero warnings/errors.
- [x] Angular production build is within configured bundle budgets.
- [x] Unit, validation, permission and API integration tests pass.
- [x] SQL-backed health endpoint returns healthy.
- [x] Production Swagger is disabled.
- [x] Rate limiting, response compression, forwarded headers and security headers are enabled.
- [x] Production secrets are external configuration.

## Database

- [x] `DBCC CHECKDB` and `DBCC CHECKCONSTRAINTS` pass.
- [x] Foreign keys, checks, defaults and programmable-object inventories reviewed.
- [x] Every foreign-key column has a leading support index.
- [x] Verified checksummed backup and restore validation exist.
- [ ] Reconcile later-sprint live objects into source-controlled SQL project before a greenfield install is supported solely from the DACPAC.

## Deployment

- [x] IIS/reverse-proxy deployment and rollback documented.
- [x] Docker Angular output path and production secret injection corrected.
- [x] Production startup, SQL health, CSP and Swagger isolation verified.
- [ ] Perform staging load/security/accessibility testing with production-scale data and the final public hostname/certificate.
- [ ] Rotate seeded administrator credentials and all deployment secrets.

## Release decision

The application binaries and current upgraded database are release-candidate ready. A greenfield release must use a schema-complete approved database package; the historical SQL project does not yet represent every object deployed during all later sprints.
