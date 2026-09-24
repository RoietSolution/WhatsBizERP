# KhataDhari.Customer deployment preparation

The separate Angular customer PWA serves `shop.khatadhari.com` from
`/var/www/khatadhari-customer/browser`; it calls the production Store API at
`https://api.khatadhari.com`. The shop-only Nginx vhost does not alter the
existing website, admin, API, or QA virtual hosts.

## Build and copy

On the approved production deployment host/release checkout, run:

```bash
bash KhataDhari.Customer-Deployment/deploy.sh
```

The script runs `npm ci` and the production Angular build, checks for
`index.html` and `ngsw.json`, then synchronizes only the customer browser output
to the dedicated destination. Repeats remove stale files only within that
dedicated `browser` directory. It validates Nginx configuration but does not
install/reload Nginx. Review the artifact and script before running it.

## Nginx and TLS preparation

Install the temporary shop-only HTTP ACME site and issue the certificate:

```bash
sudo install -d -o root -g www-data -m 0755 /var/www/letsencrypt
sudo install -o root -g root -m 0644 KhataDhari.Customer-Deployment/nginx/shop.khatadhari.com-acme.conf /etc/nginx/sites-available/shop.khatadhari.com-acme
sudo ln -s /etc/nginx/sites-available/shop.khatadhari.com-acme /etc/nginx/sites-enabled/shop.khatadhari.com-acme
sudo nginx -t
sudo systemctl reload nginx
sudo certbot certonly --webroot -w /var/www/letsencrypt -d shop.khatadhari.com
```

After certificate issuance, disable the temporary ACME site and install
`KhataDhari.Customer-Deployment/nginx/shop.khatadhari.com.conf` as the final
shop site. Run `sudo nginx -t`; reload only after explicit deployment approval.
The shop vhost uses the Angular SPA fallback `/index.html` for deep links and
avoids long-lived caching for the app shell, manifest, and service worker.

## CORS prerequisite

`backend/src/WhatsBiz.Api/appsettings.Production.json` lists the exact origin
`https://shop.khatadhari.com` alongside `https://app.khatadhari.com`. However,
the production systemd service loads `/etc/whatsbiz/prod.env`, and the checked-in
`deployment/prod.env.example` sets `Cors__AllowedOriginsCsv` to only
`https://app.khatadhari.com`. Environment configuration overrides the JSON
value, so the effective allow-list may omit the shop origin. The live
`/etc/whatsbiz/prod.env` was not inspected and this task makes no API/config
change. Before browser testing, verify that deployed file includes the exact
shop origin while preserving the app origin if still needed.
