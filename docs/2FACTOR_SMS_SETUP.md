# 2Factor SMS setup

The API supports separate 2Factor settings for storefront customer OTP and customer transaction notifications. No messages are sent until the SMS feature and tenant notification settings are enabled.

Keep API keys in the deployment secret store or environment variables. Do not put keys in source control or application logs.

## Storefront OTP

Set these API configuration values:

```text
StorefrontOtp:Provider=2Factor
StorefrontOtp:Endpoint=https://2factor.in/API/V1/OTP/SEND
StorefrontOtp:ApiKey=<secret>
StorefrontOtp:TemplateName=<approved 2Factor OTP template name>
```

The request uses the documented 2Factor `X-API-Key` header by default and sends `to`, `channel=SMS`, `template_name`, and `var1` (the ERP-generated OTP). Configure `ApiKeyHeader=Authorization` and `ApiKeyScheme=Bearer` only if the 2Factor account explicitly supplies that API contract. The OTP still expires and is verified by KhataDhari; the provider does not become the verification authority.

If 2Factor supplies a different HTTPS endpoint on one of its official API hosts, set `StorefrontOtp:Endpoint` accordingly. A fixed OTP/test mode must be disabled in the target environment for live SMS delivery.

## Transactional customer notifications

```text
Features:Sms:Enabled=true
CustomerNotifications:Sms:Provider=2Factor
CustomerNotifications:Sms:Endpoint=<HTTPS endpoint from the 2Factor account API guide>
CustomerNotifications:Sms:ApiKey=<secret>
CustomerNotifications:Sms:ApiKeyHeader=Authorization
CustomerNotifications:Sms:ApiKeyScheme=Bearer
CustomerNotifications:Sms:SenderId=<approved DLT sender ID>
CustomerNotifications:Sms:MessageType=transactional
CustomerNotifications:Sms:TemplateId=<approved DLT template ID, when required by the account API>
```

The transaction request sends the existing rendered ERP message, recipient, sender ID, message type, and notification reference. If configured, the DLT template ID is included. Confirm the endpoint and JSON field contract with the API guide for the purchased 2Factor account before enabling it; 2Factor has multiple API generations. The configured sender ID and message template must be approved for the message content and applicable DLT route.

Tenant-level SMS notification settings and event preferences remain controlled in **Administration → Customer Notifications**. The global `Features:Sms:Enabled` setting is also required.

Delivery proof OTP currently uses the tenant's configured WhatsApp delivery-notification provider; this 2Factor setup does not change that workflow.
