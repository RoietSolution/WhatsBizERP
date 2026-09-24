#!/usr/bin/env bash
set -Eeuo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_dir="$repo_root/frontend/KhataDhari.Customer"
build_dir="$project_dir/dist/KhataDhari.Customer/browser"
destination="/var/www/khatadhari-customer/browser"

if [[ ! -f "$project_dir/package-lock.json" ]]; then
  echo "Missing customer PWA package-lock.json; refusing deployment." >&2
  exit 1
fi

cd "$project_dir"
npm ci
npm run build:production

if [[ ! -f "$build_dir/index.html" || ! -f "$build_dir/ngsw.json" ]]; then
  echo "Production PWA output is incomplete; refusing deployment." >&2
  exit 1
fi

# This path is intentionally limited to the dedicated customer storefront.
sudo install -d -o root -g www-data -m 0755 /var/www/khatadhari-customer
if [[ -L /var/www/khatadhari-customer || -L "$destination" ]]; then
  echo "Storefront destination must not be a symlink; refusing deployment." >&2
  exit 1
fi
sudo install -d -o root -g www-data -m 0755 "$destination"
sudo rsync -a --delete --delay-updates --chmod=D755,F644 "$build_dir/" "$destination/"
sudo nginx -t

echo "Customer PWA files synchronized to $destination."
echo "Nginx was validated only; reload it separately after approved vhost changes."
