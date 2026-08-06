# WhatsBiz ERP V1

Production-oriented ERP for Indian retailers, built with .NET 9, Angular 20 and SQL Server 2022. V1 includes product, supplier, customer, warehouse, inventory, POS, purchase, finance, receivables/payables, analytics, GST, printing and administration modules.

## Release candidate baseline

- Backend: Clean Architecture with vertical application slices, MediatR, FluentValidation and permission policies.
- Frontend: standalone Angular components, lazy routes, guards and centralized authentication/error interception.
- Database: SQL Server schemas, constraints, transactional posting procedures and release index hardening.
- Operations: SQL health checks, Serilog, audit/login history, rate limiting, security headers and verified backups.

Start with [Installation](docs/INSTALLATION.md), then read [Deployment](docs/DEPLOYMENT.md), [Architecture](docs/ARCHITECTURE.md) and [V1 release notes](docs/RELEASE_NOTES_V1.md).

Never commit production connection strings, JWT signing keys, SMTP passwords or SMS credentials. Provide them through IIS/environment configuration or a secret manager.
