# API Documentation

The API uses JSON over HTTPS under `/api`. Authenticate with `POST /api/auth/login`, then send `Authorization: Bearer <token>`. Refresh tokens are one-time rotated through `/api/auth/refresh`; logout revokes the active refresh token.

Endpoints are grouped by module controllers: authentication, products/master data, suppliers, customers, warehouses, inventory, POS, purchases, finance, receivables, dashboard, GST, printing and administration. Each action declares a permission policy such as `product.view`, `pos.create`, `gst.export` or `admin.backup`.

Validation failures return HTTP 400, unauthenticated requests 401, forbidden requests 403, missing resources 404, business/concurrency conflicts 409 and rate-limit rejection 429. Unexpected failures return sanitized problem details with server-side logs.

Interactive OpenAPI/Swagger is enabled only in Development. Generate a release OpenAPI document from a controlled Development configuration when integration partners need a frozen contract.
