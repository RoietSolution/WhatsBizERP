# Architecture

```text
Angular Web → ASP.NET Controllers → MediatR Requests/Handlers
                                      ↓
                         Application interfaces/validation
                                      ↓
                   Infrastructure repositories and services
                                      ↓
                          SQL Server / stored procedures
```

The backend separates Domain, Application, Infrastructure, API, Contracts and SharedKernel projects. Business use cases are vertical slices containing commands, queries, handlers, DTOs and validators. Infrastructure implements persistence, identity, spreadsheets, printing and maintenance. Controllers handle HTTP concerns and permission declarations only.

Transactional inventory, sales, purchase and finance postings are implemented in database procedures/services so cross-module balances remain atomic. Read-heavy dashboard and statutory reports use dedicated aggregation queries and indexes. Angular uses standalone lazy-loaded pages, route guards and a centralized bearer/refresh interceptor.

Cross-cutting controls include FluentValidation, sanitized exception middleware, Serilog request logging, audit logging, SQL-backed health checks, rate limiting, CORS allowlists, security headers, JWT validation and refresh-token rotation.
