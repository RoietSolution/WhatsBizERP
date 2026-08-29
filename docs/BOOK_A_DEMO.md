# Book a Demo deployment

## Database

Publish the SQL database project or run `database/WhatsBiz.Database/Scripts/V20-DemoRequests.sql`. The script is transactional and safe to run repeatedly.

## API configuration

On Linux, systemd loads `/etc/whatsbiz/qa.env` with its `EnvironmentFile` directive. ASP.NET Core reads the resulting process environment directly. Use double underscores for nested keys and keep the real file outside source control.

Configure production values through environment variables, IIS configuration, or the existing secret manager. Do not commit credentials.

```text
DemoRequests__Email__Enabled=true
DemoRequests__Email__Host=smtp.hostinger.com
DemoRequests__Email__Port=587
DemoRequests__Email__EnableSsl=true
DemoRequests__Email__Username=...
DemoRequests__Email__Password=...
DemoRequests__Email__FromAddress=website@khatadhari.com
DemoRequests__Email__FromName=KhataDhari Website
DemoRequests__Email__SupportAddress=support@khatadhari.com
DemoRequests__WhatsAppContactNumber=919876543210
```

The current `System.Net.Mail.SmtpClient` implementation uses STARTTLS when
`EnableSsl=true`. For Hostinger, use port `587` with TLS/STARTTLS. Do not use port `465`
with this implementation because `SmtpClient` does not support implicit SMTP-over-SSL.
Use the complete Hostinger mailbox address as `Username`; `FromAddress` should normally
be that same authenticated mailbox. `SupportAddress` is the internal KhataDhari recipient.

For each new, non-duplicate lead, the API sends the internal notification first and then,
when the requester supplied an email address, sends a separate acknowledgement titled
`Your KhataDhari Demo Request Has Been Received`. Either send may fail independently;
failures are logged without configuration values and do not roll back the saved lead.

`WhatsAppContactNumber` is optional. When blank, the public form does not show a WhatsApp button.

Cloudflare Turnstile support is built in but disabled by default:

```text
DemoRequests__Captcha__Enabled=true
DemoRequests__Captcha__Provider=Turnstile
DemoRequests__Captcha__SiteKey=...
DemoRequests__Captcha__SecretKey=...
```

The public site origins must be exact entries in `Cors__AllowedOrigins`; the defaults include `https://khatadhari.com` and `https://www.khatadhari.com`.

When the website and API are published behind the same domain, proxy `/api` to WhatsBiz.Api; the website defaults to `/api/demo-requests`. Netlify deployments can instead use the included function and `KHATADHARI_API_BASE_URL`.

## Public website

The marketing website is deployed separately and is not in this checkout. Merge the production-ready assets under `deployment/khatadhari-website` into that site's existing Book a Demo section. If its API is on a different origin, set the form's `data-api-base` to that API origin.

## Endpoints

- `POST /api/demo-requests` — anonymous, validation and demo-request rate policy applied.
- `GET /api/demo-requests/configuration` — public WhatsApp/CAPTCHA presentation configuration only.
- `GET /api/demo-requests` — requires `admin.view`.
- `GET /api/demo-requests/{id}` — requires `admin.view`.
- `PATCH /api/demo-requests/{id}/status` — requires `admin.settings`.
