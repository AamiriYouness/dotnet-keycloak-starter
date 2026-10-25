#!/bin/sh
# Run from the repository root. Exercises real generation and injected OpenSSL failures.
set -eu
starter_setup="$PWD/deploy/production/prepare-secrets.sh"
starter_verification=$(mktemp -d)
trap 'rm -rf "$starter_verification"' 0
export STARTER_OPENSSL_REAL
STARTER_OPENSSL_REAL=$(command -v openssl)

DEPLOYMENT_SECRETS_DIR="$starter_verification/success" sh "$starter_setup" > "$starter_verification/success.log" 2>&1
starter_secrets="$starter_verification/success"
[ ! -e "$starter_secrets/protection-key.pem" ]
[ -f "$starter_secrets/key-protection.pfx" ]
starter_permissions=$(ls -ld "$starter_secrets")
case "$starter_permissions" in drwx------*) ;; *) echo 'Unexpected secrets directory permissions.' >&2; exit 1 ;; esac
for name in api.json web.json worker.json key-protection.pfx; do
    starter_permissions=$(ls -l "$starter_secrets/$name")
    case "$starter_permissions" in -r--r--r--*) ;; *) echo 'Unexpected mounted secret file permissions.' >&2; exit 1 ;; esac
done
jq -e --rawfile expected "$starter_secrets/exchange-client-secret" '.TokenExchange.ClientSecret == $expected' "$starter_secrets/api.json" > /dev/null
jq -e --rawfile expected "$starter_secrets/worker-client-secret" '.Worker.ClientSecret == $expected' "$starter_secrets/worker.json" > /dev/null
openssl pkcs12 -in "$starter_secrets/key-protection.pfx" -noout -passin "file:$starter_secrets/key-protection-password"
starter_status=0
DEPLOYMENT_SECRETS_DIR="$starter_secrets" sh "$starter_setup" > "$starter_verification/retry.log" 2>&1 || starter_status=$?
[ "$starter_status" -ne 0 ]
grep -q 'Refusing to overwrite' "$starter_verification/retry.log"

mkdir "$starter_verification/stubs"
cat > "$starter_verification/stubs/openssl" <<'STUB'
#!/bin/sh
if [ "$1" = "$STARTER_FAIL_COMMAND" ]; then exit 42; fi
exec "$STARTER_OPENSSL_REAL" "$@"
STUB
chmod 700 "$starter_verification/stubs/openssl"
for starter_failure in rand pkcs12; do
    starter_status=0
    PATH="$starter_verification/stubs:$PATH" STARTER_FAIL_COMMAND="$starter_failure" \
        DEPLOYMENT_SECRETS_DIR="$starter_verification/failed-$starter_failure" \
        sh "$starter_setup" > "$starter_verification/$starter_failure.log" 2>&1 || starter_status=$?
    [ "$starter_status" -eq 42 ]
    [ -d "$starter_verification/failed-$starter_failure" ]
    grep -q 'directory may be incomplete' "$starter_verification/$starter_failure.log"
done
printf '%s\n' 'Production secret preparation checks passed (success, overwrite refusal, OpenSSL failure reporting and key cleanup).'
