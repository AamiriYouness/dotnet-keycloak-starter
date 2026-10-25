#!/bin/bash
set -euo pipefail
export KC_DB_PASSWORD="$(cat /run/secrets/keycloak-db-password)"
export KC_BOOTSTRAP_ADMIN_PASSWORD="$(cat /run/secrets/keycloak-admin-password)"
export STARTER_WEB_CLIENT_SECRET="$(cat /run/secrets/web-client-secret)"
export STARTER_EXCHANGE_CLIENT_SECRET="$(cat /run/secrets/exchange-client-secret)"
export STARTER_WORKER_CLIENT_SECRET="$(cat /run/secrets/worker-client-secret)"
exec /opt/keycloak/bin/kc.sh start --optimized --import-realm
