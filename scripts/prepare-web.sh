#!/bin/bash
set -euo pipefail
mkdir -p .local/certs
if [ ! -f .local/certs/web.pfx ]; then
    dotnet dev-certs https --export-path .local/certs/web.pfx \
        --password "${HTTPS_CERT_PASSWORD:-local-demo-certificate-password}"
fi
chmod 600 .local/certs/web.pfx
printf 'Development HTTPS certificate is ready. Keep .local/certs with the authentication volume.\n'
