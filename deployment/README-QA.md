# QA deployment

The repository contains templates only. Keep the real environment file outside the application directory and source control.

## API setup

```bash
sudo useradd --system --home /var/lib/whatsbiz-qa --create-home --shell /usr/sbin/nologin whatsbiz
sudo install -d -o whatsbiz -g whatsbiz -m 0750 /var/www/whatsbiz-qa /var/www/whatsbiz-qa/Logs
sudo install -d -o root -g whatsbiz -m 0750 /etc/whatsbiz
sudo install -d -o whatsbiz -g whatsbiz -m 0700 /var/lib/whatsbiz-qa/data-protection-keys
sudo install -o root -g whatsbiz -m 0640 deployment/qa.env.example /etc/whatsbiz/qa.env
sudo editor /etc/whatsbiz/qa.env
sudo install -o root -g root -m 0644 deployment/whatsbiz-qa.service /etc/systemd/system/whatsbiz-qa.service
sudo systemctl daemon-reload
sudo systemctl enable --now whatsbiz-qa
sudo systemctl status whatsbiz-qa --no-pager
curl --fail http://127.0.0.1:5001/health
```

Publish into a release directory and copy/symlink it to `/var/www/whatsbiz-qa` using the existing release process. Ensure the `whatsbiz` account owns the deployed application and `Logs` directory. Do not replace `/var/lib/whatsbiz-qa/data-protection-keys` during deployment.

After a new API release or environment-file change:

```bash
sudo systemctl restart whatsbiz-qa
sudo systemctl status whatsbiz-qa --no-pager
sudo journalctl -u whatsbiz-qa -n 100 --no-pager
```

## Nginx setup

Obtain the TLS certificate before enabling the HTTPS example, then install and validate it:

```bash
sudo install -o root -g root -m 0644 deployment/nginx/qa-api.khatadhari.com.conf /etc/nginx/sites-available/qa-api.khatadhari.com
sudo ln -s /etc/nginx/sites-available/qa-api.khatadhari.com /etc/nginx/sites-enabled/qa-api.khatadhari.com
sudo nginx -t
sudo systemctl reload nginx
curl --fail https://qa-api.khatadhari.com/health
```

## Angular runtime configuration

Run `ng build --configuration qa` from `frontend/WhatsBiz.Web`. The QA build copies the
QA runtime configuration to `dist/WhatsBiz.Web/browser/runtime-config.json`, so `/api`
calls are sent to `https://qa-api.khatadhari.com`. Upload the contents of that `browser`
directory to `/var/www/whatsbiz-qa-web`.

The default `public/runtime-config.json` remains empty so local development continues to
use the same-origin Angular `/api` proxy. Runtime configuration is a standalone JSON asset:
after deployment, the API origin can be changed directly in the web root's
`runtime-config.json` without rebuilding Angular. Production continues to use
`deployment/runtime-config.production.json` with the existing deployment process.
