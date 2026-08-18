# RC-DEV-010 Customer Notifications — Development Completion

## 1. Architecture

Customer notifications use a post-commit durable queue. POS sale/payment processing completes through the existing idempotent financial transaction first. Only after the command returns successfully does the API attempt to create duplicate-safe notification records. Queue creation catches and logs its own failures, so it cannot change the completed financial result. A bounded background worker claims persisted messages and updates delivery status.

## 2. Angular changes

Administration now contains **Communication → Customer Notifications**. The screen supports master enablement, WhatsApp/SMS enablement, successful-sale and successful-payment triggers, message template editing, reset/save, server-side provider readiness, notification history, and explicit failed-message retry.

## 3. .NET changes

- Added customer-notification application contracts and DTOs.
- Added protected administration endpoints for settings, history, configuration readiness, and retry.
- Added post-commit POS sale/payment queue hooks.
- Added SQL-backed notification service, template renderer, phone normalizer, provider abstractions, HTTP provider adapters, and background worker.
- Added structured logging that does not include credentials, message bodies, provider bodies, or access tokens.

## 4. Database changes

`integration.CustomerNotifications` stores customer/document/event/channel identity, recipient, rendered/template text, lifecycle status, provider reference, sanitized error, attempts, and timestamps. Foreign keys preserve customer/invoice traceability. Work/history indexes support processing and administration views.

## 5. SQL deployment

`docs/deployment/RC-DEV-010-Customer-Notifications.sql` is rerunnable, database-target guarded, non-destructive, and defaults all external delivery to OFF. The SQL project includes the table definition and the post-deployment settings script.

## 6. Configuration

Non-secret channel endpoint placeholders are under `CustomerNotifications:WhatsApp` and `CustomerNotifications:Sms`. `AccessToken` values must be supplied through environment variables, development user-secrets, or deployment secret storage. No provider secret is committed or returned to Angular.

## 7. WhatsApp provider abstraction

`ICustomerMessageProvider` isolates delivery from the queue worker. `WhatsAppProvider` is an HTTP adapter whose approved provider endpoint and bearer token are server-side configuration. Missing configuration returns an accurate `NOT_CONFIGURED` failure; it never reports fake success.

## 8. SMS provider abstraction

`SmsProvider` implements the same server-side contract independently. SMS is optional and disabled by default. Missing configuration is persisted as failure rather than simulated success.

## 9. Notification lifecycle

Statuses are `PENDING`, internal claim state `PROCESSING`, `SENT`, and `FAILED`. A record becomes `SENT` only after an HTTP success response. Invalid/missing customer mobile numbers are recorded as `FAILED` without affecting the transaction. Stale worker claims are recoverable.

## 10. Retry behavior

The worker performs at most three attempts with bounded 1-minute then 5-minute delays. After exhaustion the record is `FAILED`. Explicit administration retry refreshes and revalidates the current customer number, clears provider state, and starts a new bounded attempt cycle.

## 11. Duplicate protection

A unique business key covers `DocumentId + DocumentType + CustomerId + Channel + EventType`. POS/browser/API idempotent replays therefore do not create a second notification event. Provider requests receive the notification ID as a stable reference for provider-side idempotency where supported.

## 12. Security

Angular never calls messaging providers. Credentials remain server-side and are neither logged nor returned. Provider response bodies are not logged. Provider errors are reduced to safe status/type information. Phone numbers are exposed only in permission-protected administration history. Ten-digit local numbers receive `+91` only when the configured company country is India; explicit international formats are preserved.

## 13. Build results

- .NET API and dependencies: **PASS**, 0 warnings, 0 errors (isolated output used because the running development API/Visual Studio locked normal debug output).
- Angular production build: **PASS**.
- SQL/DACPAC build: **PASS**, 0 warnings, 0 errors.

No full regression, RC-QA-001, A–J scenarios, Playwright suite, final QA, V1 qualification, or production deployment was performed.

## 14. How the feature should be tested later

Separate QA should deploy the RC-DEV-010 SQL to an isolated QA database; configure approved sandbox provider endpoints and non-customer test recipients; verify committed sale/payment ordering, unavailable-provider isolation, missing/invalid/international numbers, placeholder rendering, duplicate API/idempotency replays, automatic attempt limits, application-restart recovery, history permissions, and explicit retry after correcting a customer mobile number. Financial, inventory, GST, ledger, and outstanding reconciliation belongs to the later QA prompt, not this development completion.
