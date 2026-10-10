#!/usr/bin/env bash
# Nightly PostgreSQL backup (cron, installed by cloud-init.yaml):
#   1. pg_dump (custom format) of the "ledger" and "keycloak" databases into backups/<UTC timestamp>/
#   2. keeps the last 14 days on the server
#   3. copies them to S3-compatible storage (Hetzner Object Storage) with rclone, keeping 30 days there
# Restore: documentation §19.6.
set -euo pipefail

cd "$(dirname "$0")"
LEDGER_DIR=$(pwd)
BACKUP_DIR="$LEDGER_DIR/backups"
LOCAL_DAYS=14
REMOTE_DAYS=30

# Only the BACKUP_S3_* settings (.env is written for Compose, not for bash: values may contain spaces).
while IFS='=' read -r key value; do
  case "$key" in
    BACKUP_S3_*)
      value=${value%\"}; value=${value#\"}
      export "$key=$value"
      ;;
  esac
done < "$LEDGER_DIR/.env"

compose() {
  docker compose --env-file "$LEDGER_DIR/.env" --env-file "$LEDGER_DIR/release.env" "$@"
}

stamp=$(date -u +%Y-%m-%dT%H%MZ)
target="$BACKUP_DIR/$stamp"
mkdir -p "$target"

for database in ledger keycloak; do
  compose exec -T postgres pg_dump -U postgres -Fc "$database" > "$target/$database.dump"
  # An empty or truncated dump must fail the job, not be uploaded as a "backup".
  compose exec -T postgres pg_restore --list < "$target/$database.dump" > /dev/null
done
echo "$(date -u +%FT%TZ) dumped to $target ($(du -sh "$target" | cut -f1))"

find "$BACKUP_DIR" -mindepth 1 -maxdepth 1 -type d -mtime +"$LOCAL_DAYS" -exec rm -rf {} +

if [ -n "${BACKUP_S3_ENDPOINT:-}" ]; then
  export RCLONE_CONFIG_BACKUPS_TYPE=s3
  export RCLONE_CONFIG_BACKUPS_PROVIDER=Other
  export RCLONE_CONFIG_BACKUPS_ENDPOINT="$BACKUP_S3_ENDPOINT"
  export RCLONE_CONFIG_BACKUPS_ACCESS_KEY_ID="$BACKUP_S3_ACCESS_KEY"
  export RCLONE_CONFIG_BACKUPS_SECRET_ACCESS_KEY="$BACKUP_S3_SECRET_KEY"
  export RCLONE_CONFIG_BACKUPS_ACL=private

  rclone copy "$target" "backups:$BACKUP_S3_BUCKET/$stamp"
  rclone delete --min-age "${REMOTE_DAYS}d" "backups:$BACKUP_S3_BUCKET"
  rclone rmdirs --leave-root "backups:$BACKUP_S3_BUCKET"
  echo "$(date -u +%FT%TZ) copied to $BACKUP_S3_BUCKET/$stamp"
else
  echo "$(date -u +%FT%TZ) BACKUP_S3_ENDPOINT not set: kept on the server only" >&2
fi
