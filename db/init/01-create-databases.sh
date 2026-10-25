#!/bin/bash
# Runs only when PostgreSQL initializes an empty data directory.
set -euo pipefail

: "${POSTGRES_USER:?POSTGRES_USER is required}"

if [ -n "${KEYCLOAK_DB_PASSWORD_FILE:-}" ]; then
    KEYCLOAK_DB_PASSWORD=$(cat -- "$KEYCLOAK_DB_PASSWORD_FILE")
fi

: "${KEYCLOAK_DB_PASSWORD:?KEYCLOAK_DB_PASSWORD is required}"

psql \
    --no-psqlrc \
    --no-password \
    --set=ON_ERROR_STOP=1 \
    --username "$POSTGRES_USER" \
    --dbname postgres \
    --set=keycloak_password="$KEYCLOAK_DB_PASSWORD" <<'SQL'
CREATE ROLE keycloak
    LOGIN
    NOSUPERUSER
    NOCREATEDB
    NOCREATEROLE
    NOINHERIT
    NOREPLICATION
    NOBYPASSRLS
    PASSWORD :'keycloak_password';

CREATE DATABASE keycloak OWNER keycloak;
SQL

unset KEYCLOAK_DB_PASSWORD
