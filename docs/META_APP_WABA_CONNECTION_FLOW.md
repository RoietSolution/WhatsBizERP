# Meta App and Retailer WABA Connection Flow

## 1. Purpose

This runbook describes the WhatsBiz Option 3 architecture:

> One KhataDhari Meta Developer App with a separate WhatsApp Business Account (WABA), phone number, business identity, access token, and WhatsApp configuration for every retailer tenant.

It covers the current manual setup used for `META_TEST` and `LIVE`, webhook configuration, validation, security boundaries, and the future Meta Embedded Signup path.

Meta changes its dashboard periodically. Use the current official documentation alongside this runbook:

- [WhatsApp Cloud API overview](https://developers.facebook.com/docs/whatsapp/cloud-api/)
- [Cloud API get started](https://developers.facebook.com/docs/whatsapp/cloud-api/get-started/)
- [Cloud API webhooks](https://developers.facebook.com/docs/whatsapp/cloud-api/webhooks/)
- [Meta Embedded Signup](https://developers.facebook.com/docs/whatsapp/embedded-signup/)

## 2. Configuration ownership

| Scope | Owner | Stored configuration |
| --- | --- | --- |
| Platform | KhataDhari SystemAdministrator | Meta App ID, encrypted App Secret, encrypted webhook verify token, enabled state |
| Retailer tenant | One retailer only | Provider mode, WABA ID, Phone Number ID, encrypted access token, API version, test recipient, connection status and validated business identity |
| Webhook event | Resolved retailer | Tenant ID derived from Phone Number ID, WABA ID, event key, message/status metadata and processing state |

Platform configuration is stored once in `integration.WhatsAppPlatformConfiguration`. Retailer connections remain in `integration.WhatsAppConfigurations`, with one row per `TenantId`.

`PhoneNumberId` and `WhatsAppBusinessAccountId` are unique across tenant configurations. The same Meta identifier cannot be assigned to two retailers.

## 3. Architecture flow

```text
KhataDhari Meta App
   |
   +-- Retailer A WABA -- Retailer A Phone Number ID -- Tenant A configuration
   |
   +-- Retailer B WABA -- Retailer B Phone Number ID -- Tenant B configuration
   |
   +-- Retailer N WABA -- Retailer N Phone Number ID -- Tenant N configuration

Meta webhook
   -> validate App Secret HMAC
   -> read entry.id (WABA) and metadata.phone_number_id
   -> resolve the unique active tenant by Phone Number ID
   -> require exact WABA match
   -> process only that tenant's commerce data
```

The public webhook does not accept a trusted tenant ID. Any `tenantId` property placed in the JSON body is ignored.

## 4. Prerequisites

Before creating the production connection, confirm:

- KhataDhari has an appropriate Meta Business Portfolio and a Meta Developer account.
- The business verification and app review requirements applicable to the intended production use are complete.
- Each retailer has its own WABA and a WhatsApp-capable number that is not actively registered in an incompatible WhatsApp product.
- The public WhatsBiz API is available through HTTPS.
- The retailer has the required WhatsApp Commerce feature/subscription assignments in WhatsBiz.
- Production secrets can be protected by the same persistent ASP.NET Data Protection key ring on every API instance.
- The database migration `V14-WhatsAppOption3TenantConnections.sql` has been deployed.

## 5. Create the shared KhataDhari Meta App

Perform these steps once for the platform:

1. In Meta for Developers, create or select the KhataDhari business app.
2. Add the WhatsApp product to the app.
3. Associate the app with the KhataDhari business assets required to manage the retailer onboarding model.
4. Record the numeric Meta App ID.
5. Obtain the App Secret. Do not place it in source control, environment logs, tickets, or browser storage.
6. Generate a strong webhook verify token. It is a KhataDhari-selected secret, not a value supplied by the retailer.
7. Configure the public callback URL:

   ```text
   https://<public-api-host>/api/whatsapp/webhook
   ```

8. Configure the same verify token in Meta and subscribe to the webhook fields required by WhatsBiz, including message and message-status notifications.
9. Open WhatsBiz as a `SystemAdministrator` and navigate to:

   ```text
   /admin/whatsapp-platform
   ```

10. Save the Meta App ID, App Secret, webhook verify token, and enable the platform.

The API encrypts both secrets before storing them. Subsequent API responses return only `hasAppSecret` and `hasWebhookVerifyToken` flags.

## 6. Create or attach a retailer WABA

Repeat this section for every retailer:

1. Create or select the retailer's WABA in the appropriate Meta business portfolio.
2. Complete the retailer's legal/business identity and WhatsApp display-name review requirements.
3. Add and verify the retailer's WhatsApp business number.
4. Record:

   - WABA ID
   - Phone Number ID
   - Display phone number
   - Approved business/display name

5. Ensure the KhataDhari Meta App is subscribed to this WABA so events are sent to the shared callback URL.
6. Create a long-lived production access token through the supported Meta system-user/business integration process.
7. Grant only the permissions/assets needed for this retailer connection. Common Cloud API permissions include `whatsapp_business_messaging` and `whatsapp_business_management`; confirm the current requirements in Meta's official documentation.
8. Prefer a separate token or separately restricted system-user asset assignment for each retailer. A Retailer A credential must not be capable of sending from Retailer B's phone number.
9. Never send the long-lived token to a retailer's browser after it has been submitted to the WhatsBiz API.

## 7. Configure the retailer in WhatsBiz

Sign in under the retailer tenant and navigate to:

```text
/admin/whatsapp
```

For production, configure:

| Field | Value |
| --- | --- |
| Provider mode | `LIVE` |
| Meta App ID | Displayed from the shared platform configuration; not duplicated in the tenant row |
| WABA ID | Retailer's numeric WABA ID |
| Phone Number ID | Retailer's numeric Phone Number ID |
| API version | Supported Meta Graph API version, for example `vNN.N` |
| Access token | Retailer-specific token; encrypted on save |
| Enabled | On |

Save the configuration and select **Validate connection**. A successful validation records the safe business identity, display phone number, `CONNECTED` status, and validation timestamp. It never returns the access token.

The save is rejected when:

- The current tenant is inactive.
- The WABA ID or Phone Number ID is malformed.
- The WABA ID or Phone Number ID is already assigned to another tenant.
- `LIVE` is selected while the shared KhataDhari platform configuration is disabled.
- Required tenant credentials are missing.

## 8. META_TEST development flow

`META_TEST` continues to use the existing Meta Cloud API provider and manual configuration page.

1. Select `META_TEST` for the development tenant.
2. Enter the test WABA ID, test Phone Number ID, Graph API version, temporary/development access token, and an approved test recipient.
3. When the shared platform configuration is enabled, webhook verification and HMAC validation use the shared platform verify token and App Secret.
4. When the shared platform configuration is not enabled, the legacy tenant-level App ID, App Secret, and verify token remain supported for manual META_TEST development.
5. Save, validate, and send a test message.

`MOCK` remains fully local and requires no Meta credentials or external message send.

## 9. Incoming webhook processing

The POST callback processing order is:

1. Enforce the request-size limit.
2. Parse only a `whatsapp_business_account` payload.
3. Preserve the WABA ID and Phone Number ID belonging to every individual `entry/change`; multi-WABA payloads are not collapsed into one tenant.
4. Validate `X-Hub-Signature-256` using HMAC-SHA256 and the shared encrypted App Secret.
5. Resolve exactly one active tenant configuration using `metadata.phone_number_id`.
6. Require the resolved configuration's WABA ID to exactly match `entry.id`.
7. Require an enabled, non-MOCK configuration, active tenant, and enabled WhatsApp Commerce feature.
8. Insert the event into `integration.WhatsAppWebhookEvents` using the resolved tenant.
9. Treat the unique `(TenantId, EventKey)` conflict as a safe duplicate and update diagnostics without processing the event twice.

Unknown, ambiguous, inactive, disabled, or mismatched identifiers receive a safe rejection before tenant commerce data is accessed.

## 10. Outbound message processing

Outbound operations follow this path:

1. Authentication establishes `TenantId` from the JWT/current-user context.
2. The service queries `integration.WhatsAppConfigurations` using that exact tenant ID and joins an active tenant.
3. The configuration must be enabled. Meta sends additionally require `CONNECTED`, a Phone Number ID, and an encrypted access token.
4. `LIVE` additionally requires the shared KhataDhari platform configuration to be enabled.
5. The access token is decrypted only inside the backend.
6. The Meta provider sends through:

   ```text
   /<graph-api-version>/<current-tenant-phone-number-id>/messages
   ```

7. Products, customers, collections, carts, orders, deliveries, and analytics continue to use the same resolved/authenticated tenant ID.

There is no API input that allows an authenticated retailer to substitute another tenant's WhatsApp configuration.

## 11. SystemAdministrator monitoring

Open `/admin/whatsapp-platform` to view:

- Shared platform enabled/configured state.
- Whether platform secrets exist, without their values.
- Every retailer's tenant state, provider mode, WABA ID, Phone Number ID, display identity, connection status, and last validation time.

This page is protected by the existing `features.manage` permission assigned to the `SystemAdministrator` role. It must not display access tokens, App Secrets, verify tokens, or encrypted ciphertext.

## 12. API endpoints

Platform administration:

```text
GET  /api/whatsapp/administration/platform
PUT  /api/whatsapp/administration/platform
GET  /api/whatsapp/administration/retailer-connections
```

Authenticated retailer configuration:

```text
GET  /api/whatsapp/configuration
PUT  /api/whatsapp/configuration
POST /api/whatsapp/configuration/validate
POST /api/whatsapp/configuration/test-message
GET  /api/whatsapp/configuration/diagnostics
```

Public Meta callback:

```text
GET  /api/whatsapp/webhook
POST /api/whatsapp/webhook
```

The public endpoints do not accept a tenant ID.

## 13. Database verification

Run this read-only check after deployment:

```sql
SELECT PlatformConfigurationId, MetaAppId, IsEnabled, ModifiedOn
FROM integration.WhatsAppPlatformConfiguration;

SELECT t.TenantKey, t.Name, t.IsActive,
       c.ProviderMode, c.WhatsAppBusinessAccountId, c.PhoneNumberId,
       c.IsEnabled, c.ConnectionStatus, c.LastValidatedOn
FROM core.Tenants t
LEFT JOIN integration.WhatsAppConfigurations c ON c.TenantId=t.TenantId
ORDER BY t.Name;

SELECT name, is_unique, has_filter
FROM sys.indexes
WHERE object_id=OBJECT_ID(N'integration.WhatsAppConfigurations')
  AND name IN
  (
      N'UX_WhatsAppConfigurations_PhoneNumberId',
      N'UX_WhatsAppConfigurations_WabaId',
      N'IX_WhatsAppConfigurations_WebhookResolution'
  );
```

Do not select protected secret columns for screenshots, support tickets, or routine diagnostics.

## 14. Retailer connection checklist

- [ ] Retailer WABA exists and belongs to the intended retailer/business portfolio.
- [ ] WhatsApp number is verified and its display identity is approved.
- [ ] WABA ID and Phone Number ID were copied from the correct Meta asset.
- [ ] KhataDhari Meta App is subscribed to the retailer WABA.
- [ ] Retailer-specific/restricted access token was created with required permissions.
- [ ] WhatsBiz tenant features/subscription are enabled.
- [ ] Retailer configuration was saved under the correct authenticated tenant.
- [ ] Connection validation reports `CONNECTED` and the expected business identity.
- [ ] Test outbound message uses the expected retailer number.
- [ ] Test inbound webhook appears only in the expected retailer diagnostics/data.
- [ ] No credentials appear in browser responses or application logs.

## 15. Disconnecting or deboarding a retailer

1. Disable the retailer's WhatsApp configuration in WhatsBiz.
2. Revoke the retailer-specific access token or remove its WABA/phone asset assignments in Meta.
3. Remove the KhataDhari app subscription from that WABA if the retailer is fully disconnected.
4. Preserve webhook and commerce audit history according to the retention policy.
5. Use the existing retailer deboarding process; do not reassign the old Phone Number ID or WABA ID until Meta ownership and historical-data implications have been reviewed.

## 16. Future Meta Embedded Signup flow

Embedded Signup should populate the same platform and tenant model rather than creating another subsystem:

1. Angular starts Meta Embedded Signup using the shared KhataDhari App ID and a server-issued correlation/state value.
2. Meta returns the authorization result and selected WABA/phone identifiers.
3. The backend validates state and exchanges the authorization code server-to-server.
4. The backend obtains and encrypts the retailer-specific token; the long-lived token is never returned to Angular.
5. The backend upserts the current authenticated tenant's existing `integration.WhatsAppConfigurations` row.
6. Existing unique WABA/Phone Number ID constraints prevent cross-tenant assignment.
7. Existing validation, feature management, outbound provider, webhook resolution, diagnostics, and deboarding flows remain unchanged.

This is why Embedded Signup can be added as a new onboarding adapter without redesigning the Option 3 tenant model.
