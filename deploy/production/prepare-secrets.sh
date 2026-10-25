#!/bin/sh
# One-time, local secret generation for a fresh deployment. Never rotates existing credentials.
set -eu
umask 077
: "${DEPLOYMENT_SECRETS_DIR:?Set an absolute DEPLOYMENT_SECRETS_DIR}"
case "$DEPLOYMENT_SECRETS_DIR" in /*) ;; *) echo 'The secrets directory must be absolute.' >&2; exit 1 ;; esac
for program in openssl jq; do command -v "$program" >/dev/null || { echo "$program is required." >&2; exit 1; }; done
if [ -e "$DEPLOYMENT_SECRETS_DIR" ]; then echo 'Refusing to overwrite an existing secrets directory.' >&2; exit 1; fi
mkdir -m 700 "$DEPLOYMENT_SECRETS_DIR"
trap '
    status=$?
    if [ "$status" -ne 0 ]; then
        printf "%s\n" \
            "Secret generation failed. The directory may be incomplete; inspect it before proceeding." >&2
    fi
' 0
for name in \
    postgres-admin-password \
    keycloak-db-password \
    keycloak-admin-password \
    web-client-secret \
    exchange-client-secret \
    worker-client-secret \
    key-protection-password
do
    value=$(openssl rand -hex 32)
    printf '%s' "$value" > "$DEPLOYMENT_SECRETS_DIR/$name"
done
unset value
jq -n --rawfile value "$DEPLOYMENT_SECRETS_DIR/exchange-client-secret" '{TokenExchange:{ClientSecret:$value}}' > "$DEPLOYMENT_SECRETS_DIR/api.json"
jq -n --rawfile value "$DEPLOYMENT_SECRETS_DIR/worker-client-secret" '{Worker:{ClientSecret:$value}}' > "$DEPLOYMENT_SECRETS_DIR/worker.json"
jq -n --rawfile secret "$DEPLOYMENT_SECRETS_DIR/web-client-secret" --rawfile password "$DEPLOYMENT_SECRETS_DIR/key-protection-password" \
    '{Keycloak:{ClientSecret:$secret},DataProtection:{CertificatePassword:$password}}' > "$DEPLOYMENT_SECRETS_DIR/web.json"
# This certificate protects the Web key ring; it is separate from the public TLS certificate.
openssl req -x509 -newkey rsa:3072 -nodes -days 3650 -subj '/CN=Starter key protection' \
    -keyout "$DEPLOYMENT_SECRETS_DIR/protection-key.pem" -out "$DEPLOYMENT_SECRETS_DIR/protection-cert.pem" 2>/dev/null
openssl pkcs12 -export -inkey "$DEPLOYMENT_SECRETS_DIR/protection-key.pem" -in "$DEPLOYMENT_SECRETS_DIR/protection-cert.pem" \
    -out "$DEPLOYMENT_SECRETS_DIR/key-protection.pfx" -passout "file:$DEPLOYMENT_SECRETS_DIR/key-protection-password"
rm "$DEPLOYMENT_SECRETS_DIR/protection-key.pem"
# Secret mounts must be readable by the non-root application/container users.
# The enclosing host directory stays 0700; each service receives only its own files.
chmod 444 "$DEPLOYMENT_SECRETS_DIR"/api.json "$DEPLOYMENT_SECRETS_DIR"/web.json "$DEPLOYMENT_SECRETS_DIR"/worker.json \
    "$DEPLOYMENT_SECRETS_DIR"/key-protection.pfx "$DEPLOYMENT_SECRETS_DIR"/*-password "$DEPLOYMENT_SECRETS_DIR"/*-client-secret
printf '%s\n' 'Secrets generated. Supply tls-chain.pem, tls-key.pem and ca-bundle.pem before starting Compose.'
