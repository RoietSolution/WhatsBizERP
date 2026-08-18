# RC-QA-002 — Role Matrix Authorization Qualification

## Users used

| Role | QA user | Provisioning |
|---|---|---|
| Admin | `admin` | Existing application seed |
| Manager | `qa-manager` | Development-only QA seeder through ASP.NET Identity services |
| Cashier | `qa-cashier` | Development-only QA seeder through ASP.NET Identity services |
| Accountant | `qa-accountant` | Development-only QA seeder through ASP.NET Identity services |

The QA seeder is disabled by default, requires Development, refuses database names without `QA`, and requires the password through `Qa__RoleMatrix_y_Password`. Testing used only `WhatsBizERP_QA_RCQA001`; production Identity tables were not accessed.

## Qualification matrix

| Role | Function | Expected | Actual | Result |
|---|---|---:|---:|---|
| Admin | POS Sale | ALLOW | UI route/menu ALLOW; API ALLOW | PASS |
| Admin | Discount | ALLOW | POS permission present; configured-limit restriction not applied to Admin | PASS |
| Admin | Product | ALLOW | UI route/menu ALLOW; API ALLOW | PASS |
| Admin | Purchase | ALLOW | UI route/menu ALLOW; API ALLOW | PASS |
| Admin | Inventory Adjustment | ALLOW | UI route ALLOW; API reached validation rather than 403 | PASS |
| Admin | Finance | ALLOW | UI view/mutation routes ALLOW; API view/mutation ALLOW | PASS |
| Admin | GST Reports | ALLOW | UI route/menu ALLOW; API ALLOW | PASS |
| Admin | Users/Roles | ALLOW | Users and Roles routes/actions visible; both APIs 200 | PASS |
| Admin | Backup | ALLOW | UI route/action visible; API ALLOW | PASS |
| Manager | POS Sale | ALLOW | UI route/menu ALLOW; API ALLOW | PASS |
| Manager | Discount | ALLOW | `pos.discount` granted; no Cashier-only cap applied | PASS |
| Manager | Product | ALLOW | UI route/menu ALLOW; API ALLOW | PASS |
| Manager | Purchase | ALLOW | UI route/menu ALLOW; API ALLOW | PASS |
| Manager | Inventory Adjustment | ALLOW | UI route/menu ALLOW; API reached validation rather than 403 | PASS |
| Manager | Finance | VIEW ONLY | Book UI/API ALLOW; receipt mutation UI/API 403 | PASS |
| Manager | GST Reports | ALLOW | UI route/menu ALLOW; API ALLOW | PASS |
| Manager | Users/Roles | DENY | Menu/action unavailable; direct routes and APIs 403 | PASS |
| Manager | Backup | DENY | Menu unavailable; direct route and API 403 | PASS |
| Cashier | POS Sale | ALLOW | UI route/menu ALLOW; API ALLOW | PASS |
| Cashier | Discount | LIMITED | 4.99% ALLOW, 5.00% ALLOW, 5.01% rejected 409 | PASS |
| Cashier | Product | DENY | Menu absent; direct route and API 403 | PASS |
| Cashier | Purchase | DENY | Menu absent; direct route and API 403 | PASS |
| Cashier | Inventory Adjustment | DENY | Menu absent; direct route and API 403 | PASS |
| Cashier | Finance | DENY | Menu absent; view/mutation direct routes and APIs 403 | PASS |
| Cashier | GST Reports | DENY | Menu absent; direct route and API 403 | PASS |
| Cashier | Users/Roles | DENY | Menu/action unavailable; direct routes and APIs 403 | PASS |
| Cashier | Backup | DENY | Menu unavailable; direct route and API 403 | PASS |
| Accountant | POS Sale | DENY | Menu absent; direct route and API 403 | PASS |
| Accountant | Discount | DENY | No POS access/discount permission | PASS |
| Accountant | Product | DENY | Menu absent; direct route and API 403 | PASS |
| Accountant | Purchase | ALLOW | UI route/menu ALLOW; API ALLOW | PASS |
| Accountant | Inventory Adjustment | DENY | Menu absent; direct route and API 403 | PASS |
| Accountant | Finance | ALLOW | UI view/mutation routes and APIs ALLOW | PASS |
| Accountant | GST Reports | ALLOW | UI route/menu ALLOW; API ALLOW | PASS |
| Accountant | Users/Roles | DENY | Menu/action unavailable; direct routes and APIs 403 | PASS |
| Accountant | Backup | DENY | Menu unavailable; direct route and API 403 | PASS |

## Tests executed

- Four Chromium UI authorization cases: login, permission-filtered primary menu, protected direct URLs, `/403` redirects, Manager finance mutation denial, Users and Roles routes, and Admin Users/Roles/Backup actions.
- Four API authorization cases covering POS, product, purchase, inventory adjustment, finance view, finance mutation, GST, users, roles, and backup.
- One Cashier discount boundary case using `Retail:CashierMaxDiscountPercent` from existing configuration (5%): below, exact, and above.
- Command: `npm run test:e2e:role-matrix` with `QA_ROLE_MATRIX_PASSWORD` supplied only to the QA process.
- Focused retests: API matrix 5/5 passed cumulatively; UI matrix 4/4 passed after harness corrections; final Admin action retest passed.

No backend suite, SQL/DACPAC suite, browser smoke test, report/export test, idempotency test, rollback test, or final regression was run.

## Defects found and fixed

No application authorization defect was found.

Two QA-harness defects were corrected:

1. The test proxy used `127.0.0.1` upstream while `AllowedHosts` accepts `localhost`, producing HTTP 400 before authentication. The QA proxy/API target now uses `localhost`.
2. Initial menu assertions used exact accessible button names, but Material icon text is included in those names. Assertions now target exact visible labels inside `Primary navigation`; Admin action checks navigate the Security and Backup categories before asserting their links.

The absence of a supported create/assign API was handled with a guarded Development-only ASP.NET Identity seeder. It is inactive unless explicitly enabled in a QA environment.

## Retest result

- UI authorization: PASS for Admin, Manager, Cashier, Accountant.
- API authorization: PASS for Admin, Manager, Cashier, Accountant.
- Cashier discount limit: PASS at 4.99%, 5.00%, and 5.01% rejection.
- No authorization defect remains in the targeted matrix.

## Final status

# RC-QA-002 PASS
