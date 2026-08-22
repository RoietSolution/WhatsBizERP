# Razorpay live payment setup

The local configuration defaults to `MOCK`, so it cannot collect money.

For a live deployment, set the following values as environment variables or deployment secrets; do not commit them to source control.

```text
Razorpay__Mode=LIVE
Razorpay__PublicBaseUrl=https://erp.example.com
Razorpay__KeyId=rzp_live_xxx
Razorpay__KeySecret=your-key-secret
Razorpay__WebhookSecret=your-webhook-secret
Razorpay__PaymentLinkExpiryMinutes=1440
```

Configure Razorpay to deliver payment-link webhooks to:

```text
https://erp.example.com/api/payments/razorpay/webhook
```

The host must have public DNS and a valid HTTPS certificate. `localhost` is only suitable for a mock or a tunnel during development.
