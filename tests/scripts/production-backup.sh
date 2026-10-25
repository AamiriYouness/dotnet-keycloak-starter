#!/bin/sh
# Failure-path tests use a Docker stub; the browser suite executes the real backup script.
set -eu
starter_root=$PWD
starter_script="$starter_root/deploy/production/backup.sh"
starter_test=$(mktemp -d)
trap 'rm -rf "$starter_test"' 0
mkdir "$starter_test/stubs" "$starter_test/secrets" "$starter_test/web"
printf '%s\n' 'retained key ring' > "$starter_test/web/key.xml"
printf '%s\n' 'original key-protection material' > "$starter_test/secrets/key-protection.pfx"
printf '%s\n' 'DEPLOYMENT_SECRETS_DIR=/this/live/path/must/not/be/used' > "$starter_test/deployment.env"
export STARTER_BACKUP_LOG="$starter_test/docker.log"
export STARTER_EXPECTED_SECRETS="$starter_test/secrets"
export STARTER_TEST_WEB="$starter_test/web"
export STARTER_TEST_REALM="$starter_root/deploy/production/starter-realm.json"
cat > "$starter_test/stubs/docker" <<'STUB'
#!/bin/sh
set -eu
[ "$DEPLOYMENT_SECRETS_DIR" = "$STARTER_EXPECTED_SECRETS" ]
printf '%s\n' "$*" >> "$STARTER_BACKUP_LOG"
if [ "$1" = run ]; then
    if [ "${STARTER_FAIL_PHASE:-}" = archive ]; then exit 43; fi
    destination=
    for argument do
        case "$argument" in type=bind,src=*,dst=/backup) destination=${argument#type=bind,src=}; destination=${destination%,dst=/backup} ;; esac
    done
    test -n "$destination"
    tar -C "$STARTER_TEST_WEB" -czf "$destination/web-auth.tar.gz" .
    exit 0
fi
case "$*" in
    *'config --format json'*) jq -n --arg source "$STARTER_TEST_REALM" '{services:{keycloak:{volumes:[{source:$source,target:"/opt/keycloak/data/import/starter-realm.json"}]}}}' ;;
    *'ps -aq web'*) printf '%s\n' fake-web-container ;;
    *'images --format json'*) printf '%s\n' '[]' ;;
    *'pg_dump '*) printf '%s\n' partial-or-complete-dump; if [ "${STARTER_FAIL_PHASE:-}" = dump ]; then exit 42; fi ;;
    *) : ;;
esac
STUB
chmod 700 "$starter_test/stubs/docker"
export PATH="$starter_test/stubs:$PATH"
# An inherited live path must be replaced by the script's explicit fourth argument.
export DEPLOYMENT_SECRETS_DIR=/this/live/path/must/not/be/used
for starter_phase in dump archive; do
    : > "$STARTER_BACKUP_LOG"
    starter_status=0
    STARTER_FAIL_PHASE="$starter_phase" sh "$starter_script" "$starter_test/deployment.env" test-prod \
        "$starter_test/failed-$starter_phase" "$STARTER_EXPECTED_SECRETS" > "$starter_test/$starter_phase.log" 2>&1 || starter_status=$?
    case "$starter_phase" in dump) test "$starter_status" -eq 42 ;; archive) test "$starter_status" -eq 43 ;; esac
    test -f "$starter_test/failed-$starter_phase/INCOMPLETE"
    test ! -e "$starter_test/failed-$starter_phase/COMPLETE"
    grep -q 'Backup failed' "$starter_test/$starter_phase.log"
    if grep -q 'start --wait' "$STARTER_BACKUP_LOG"; then echo 'Restart was attempted after a failed backup.' >&2; exit 1; fi
done
: > "$STARTER_BACKUP_LOG"
sh "$starter_script" "$starter_test/deployment.env" test-prod "$starter_test/complete" "$STARTER_EXPECTED_SECRETS" > "$starter_test/complete.log" 2>&1
test -f "$starter_test/complete/COMPLETE"
test ! -e "$starter_test/complete/INCOMPLETE"
test -s "$starter_test/complete/keycloak.dump"
tar -tzf "$starter_test/complete/web-auth.tar.gz" > /dev/null
tar -tzf "$starter_test/complete/secrets.tar.gz" > /dev/null
printf '%s\n' sentinel > "$starter_test/complete/keycloak.dump"
: > "$STARTER_BACKUP_LOG"
starter_status=0
sh "$starter_script" "$starter_test/deployment.env" test-prod "$starter_test/complete" "$STARTER_EXPECTED_SECRETS" > "$starter_test/retry.log" 2>&1 || starter_status=$?
test "$starter_status" -ne 0
grep -q '^sentinel$' "$starter_test/complete/keycloak.dump"
test ! -s "$STARTER_BACKUP_LOG"
printf '%s\n' 'Production backup checks passed (dump/archive failure, completion marker, explicit secret path, and overwrite refusal).'
