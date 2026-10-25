#!/bin/sh
# Run explicitly during a maintenance window. No existing backup directory is reused.
set -eu
umask 077
if [ "$#" -ne 4 ]; then
    echo 'Usage: sh backup.sh ENV_FILE PROJECT_NAME NEW_ABSOLUTE_BACKUP_DIR ABSOLUTE_SECRETS_DIR' >&2
    exit 1
fi
starter_env=$1
starter_project=$2
starter_backup=$3
# Explicit assignment prevents an inherited live secret path overriding the selected configuration.
DEPLOYMENT_SECRETS_DIR=$4
export DEPLOYMENT_SECRETS_DIR
starter_directory=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
case "$starter_backup" in /*) ;; *) echo 'Backup directory must be absolute.' >&2; exit 1 ;; esac
case "$DEPLOYMENT_SECRETS_DIR" in /*) ;; *) echo 'Secrets directory must be absolute.' >&2; exit 1 ;; esac
[ -r "$starter_env" ] || { echo 'Deployment environment file is not readable.' >&2; exit 1; }
[ -d "$DEPLOYMENT_SECRETS_DIR" ] || { echo 'Secrets directory does not exist.' >&2; exit 1; }
[ ! -e "$starter_backup" ] || { echo 'Refusing to overwrite an existing backup directory.' >&2; exit 1; }
for starter_program in docker jq tar; do command -v "$starter_program" > /dev/null || exit 1; done
starter_dc() {
    if [ -n "${STARTER_COMPOSE_OVERRIDE:-}" ]; then
        docker compose --env-file "$starter_env" -f "$starter_directory/compose.yml" \
            -f "$STARTER_COMPOSE_OVERRIDE" -p "$starter_project" "$@"
    else
        docker compose --env-file "$starter_env" -f "$starter_directory/compose.yml" -p "$starter_project" "$@"
    fi
}
starter_dc config --quiet
starter_web=$(starter_dc ps -aq web)
[ -n "$starter_web" ] || { echo 'The selected project has no Web container to archive.' >&2; exit 1; }
# Atomic creation: a concurrent invocation cannot truncate an existing backup.
mkdir -m 700 "$starter_backup"
trap '
    starter_status=$?
    if [ "$starter_status" -ne 0 ]; then
        printf "%s\n" "Backup failed; $starter_backup is incomplete. No COMPLETE marker was written." \
            "Services may remain stopped. Inspect the failure before explicitly restarting the selected project." >&2
    fi
' 0
printf '%s\n' 'Backup in progress; do not use this directory for recovery.' > "$starter_backup/INCOMPLETE"
printf '%s\n' "$starter_project" > "$starter_backup/project-name.txt"
starter_dc config --format json > "$starter_backup/deployment-model.json"
starter_dc images --format json > "$starter_backup/images.json"
cp "$starter_env" "$starter_backup/deployment.env"
mkdir -m 700 "$starter_backup/configuration"
for starter_file in compose.yml Caddyfile Caddyfile.internal Keycloak.Dockerfile keycloak-entrypoint.sh; do
    cp "$starter_directory/$starter_file" "$starter_backup/configuration/$starter_file"
done
starter_realm=$(jq -er '.services.keycloak.volumes[] | select(.target == "/opt/keycloak/data/import/starter-realm.json") | .source' "$starter_backup/deployment-model.json")
cp "$starter_realm" "$starter_backup/configuration/starter-realm.json"
if git -C "$starter_directory" rev-parse HEAD > "$starter_backup/source-revision.txt" 2>/dev/null; then :
else printf '%s\n' 'Source revision unavailable; preserve the deployment sources separately.' > "$starter_backup/source-revision.txt"
fi
starter_dc stop gateway worker web internal-proxy api transactions keycloak
starter_dc exec -T postgres pg_dump -U postgres -Fc keycloak > "$starter_backup/keycloak.dump"
[ -s "$starter_backup/keycloak.dump" ]
starter_dc exec -T postgres pg_restore --list < "$starter_backup/keycloak.dump" > /dev/null
# Read-only source volume; the isolated helper writes only into the fresh backup directory.
docker run --rm --network none --volumes-from "$starter_web":ro \
    --mount "type=bind,src=$starter_backup,dst=/backup" \
    mcr.microsoft.com/dotnet/aspnet:10.0.13 \
    tar -C /app/authentication -czf /backup/web-auth.tar.gz .
[ -s "$starter_backup/web-auth.tar.gz" ]
tar -tzf "$starter_backup/web-auth.tar.gz" > /dev/null
# Follow certificate symlinks so recovery does not depend on paths on the original host.
tar -h -C "$DEPLOYMENT_SECRETS_DIR" -czf "$starter_backup/secrets.tar.gz" .
tar -tzf "$starter_backup/secrets.tar.gz" > /dev/null
# Restart exactly the quiesced services; do not recreate containers or change their configuration.
starter_dc start --wait --wait-timeout 240 keycloak api transactions internal-proxy web gateway worker
rm "$starter_backup/INCOMPLETE"
printf '%s\n' 'Database and archives validated; configuration, secrets and image metadata captured; services restarted.' > "$starter_backup/COMPLETE"
printf '%s\n' "Backup complete: $starter_backup. Encrypt it before copying off-host; verify recovery in an isolated deployment."
