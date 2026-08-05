You are a Senior Enterprise Software Architect and Lead Full Stack Developer.

Continue development of the existing WhatsBiz ERP repository.

IMPORTANT:
- The repository foundation (Sprint-0001A) is already complete.
- Do NOT recreate or modify the solution structure.
- Extend the existing solution only.
- Produce production-ready code.
- No placeholder implementations.
- No TODO comments.
- The solution must build successfully.

====================================================
SPRINT
====================================================

Sprint-0001B

Title:
Identity & Authentication Foundation

Objective:
Implement enterprise-grade authentication and authorization that will be used by every module in the ERP.

====================================================
TECHNOLOGY
====================================================

Backend
- .NET 9
- ASP.NET Core Web API
- Clean Architecture
- Vertical Slice Architecture
- CQRS
- MediatR
- Entity Framework Core
- SQL Server
- FluentValidation
- AutoMapper
- Serilog
- ASP.NET Core Identity
- JWT Authentication
- Refresh Tokens

Frontend
- Angular 20
- Standalone Components
- Angular Material
- Signals
- RxJS

Database
- SQL Server Database Project

====================================================
BACKEND REQUIREMENTS
====================================================

Implement ASP.NET Core Identity.

Implement JWT Authentication.

Implement Refresh Token authentication.

Implement Current User service.

Implement Role-Based Authorization.

Implement Permission-Based Authorization.

Register everything through Dependency Injection.

Use strongly typed configuration.

====================================================
DATABASE
====================================================

Create all required Identity tables.

Create additional tables:

RefreshTokens

Use audit columns on application tables:

CreatedOn
CreatedBy
ModifiedOn
ModifiedBy
IsActive
IsDeleted
RowVersion

Use RowVersion for optimistic concurrency.

====================================================
PERMISSIONS
====================================================

Create a centralized permission catalog.

Location:

WhatsBiz.SharedKernel

Create

Permissions.cs

Implement the following permissions.

Product

product.view
product.create
product.edit
product.delete

Supplier

supplier.view
supplier.create
supplier.edit
supplier.delete

Customer

customer.view
customer.create
customer.edit
customer.delete

Purchase

purchase.view
purchase.create
purchase.approve

Inventory

inventory.view
inventory.adjust

Sales

sales.view
sales.create
sales.approve

Reports

reports.view

Settings

settings.manage

Users

user.manage

Roles

role.manage

Permissions

permission.manage

Controllers and services must use these constants.

Never use hardcoded permission strings.

====================================================
SEED DATA
====================================================

Seed the following.

Administrator Role

Default administrator

Username

admin

Email

admin@whatsbiz.local

Password

Admin@123456

Assign Administrator role.

Assign every permission to Administrator.

====================================================
JWT
====================================================

Configure

JWT Access Token

Refresh Token

Token Expiration

Refresh Token Rotation

Role Claims

Permission Claims

JWT configuration must come from appsettings.json.

====================================================
CURRENT USER
====================================================

Create

ICurrentUserService

CurrentUserService

Expose

UserId

Username

Email

Roles

Permissions

====================================================
AUTHORIZATION
====================================================

Implement

PermissionRequirement

PermissionAuthorizationHandler

HasPermissionAttribute

Example usage

[HasPermission(Permissions.Product.View)]

====================================================
APPLICATION LAYER
====================================================

Create the following Vertical Slice features.

Authentication

Login

Refresh Token

Logout

Current User

Each feature must contain

Command or Query

Handler

Validator

DTO

Mapping

====================================================
API
====================================================

Create endpoints

POST /api/auth/login

POST /api/auth/refresh

POST /api/auth/logout

GET /api/auth/me

Responses must be strongly typed.

Configure Swagger JWT authentication.

====================================================
INFRASTRUCTURE
====================================================

Configure

Identity

Authentication

Authorization

Persistence

JWT

Dependency Injection

====================================================
FRONTEND
====================================================

Create authentication infrastructure.

Pages

Login

Forbidden

Unauthorized

Services

AuthenticationService

CurrentUserService

PermissionService

JWT Storage Service

Create

HTTP Interceptor

Authentication Guard

Permission Guard

Configure routing

/login

/dashboard

/403

/404

Automatically

Store JWT

Attach JWT

Refresh expired token

Redirect to Login after logout

====================================================
TESTING
====================================================

Create unit tests for

Login

Refresh Token

JWT generation

Permission authorization

Authentication handlers

====================================================
QUALITY
====================================================

Requirements

Solution builds successfully.

Angular builds successfully.

No warnings.

No duplicate code.

SOLID principles.

Clean Architecture.

Async APIs.

File-scoped namespaces.

Production-ready implementation.

====================================================
DEFINITION OF DONE
====================================================

Sprint is complete only when

- Solution builds successfully.
- Angular builds successfully.
- Database project builds successfully.
- Authentication endpoints are implemented.
- JWT authentication works.
- Refresh Token flow works.
- Permission authorization works.
- Swagger JWT authentication works.
- Default administrator is seeded.
- Administrator has all permissions.
- Unit tests pass.

====================================================
OUTPUT
====================================================

After implementation provide:

1. Files Added
2. Files Modified
3. Database Tables Created
4. API Endpoints Created
5. Angular Pages Created
6. Unit Tests Added
7. Build Result
8. Test Result

Do not stop until the sprint is fully completed and all projects build successfully.