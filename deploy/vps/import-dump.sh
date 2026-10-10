#!/usr/bin/env bash
# Replaces the server's "ledger" database with a dump of another environment (e.g. local development)
# and points each user at their account in this server's Keycloak. Documentation §19.8.
#
#   ./import-dump.sh [--yes] <ledger.dump> <email>=<keycloak user id> [<email>=<keycloak user id> ...]
#
# The dump: pg_dump -Fc of the "ledger" database (all module schemas), made with the API stopped and taken at
# the same or an older migration level than this server (the migrator runs afterwards).
# Keycloak users are not copied: create them in the "ledger" realm first and pass their ID (Users -> user -> ID).
# Users of the dump that are not mapped keep their old ID: nobody can log in as them until ./link-user.sh does it.
#
# Safe order: everything is restored and checked in a separate database ("ledger_import"); the current
# database is saved to backups/pre-import-<stamp>/ and only then replaced. Any failure before the swap leaves
# the server as it was.
set -euo pipefail

cd "$(dirname "$0")"

usage() {
  echo "usage: $0 [--yes] <ledger.dump> <email>=<keycloak user id> [...]" >&2
  exit 2
}

assume_yes=false
if [ "${1:-}" = "--yes" ]; then
  assume_yes=true
  shift
fi

dump=${1:-}
[ -n "$dump" ] || usage
shift
[ "$#" -gt 0 ] || usage
[ -r "$dump" ] || { echo "Cannot read $dump" >&2; exit 1; }
[ -f .env ] && [ -f release.env ] || { echo "Run it from /opt/ledger after the first deploy (.env and release.env)" >&2; exit 1; }

uuid_pattern='^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$'
emails=()
subjects=()
for mapping in "$@"; do
  email=${mapping%%=*}
  subject=${mapping#*=}
  if [ "$email" = "$mapping" ] || [[ ! "$email" =~ ^[^@[:space:]]+@[^@[:space:]]+$ ]] || [[ ! "$subject" =~ $uuid_pattern ]]; then
    echo "Invalid mapping '$mapping': expected <email>=<keycloak user id (UUID)>" >&2
    exit 2
  fi
  emails+=("$email")
  subjects+=("$subject")
done
if [ "$(printf '%s\n' "${subjects[@]}" | sort -u | wc -l)" -ne "${#subjects[@]}" ]; then
  echo "The same Keycloak user id is mapped twice" >&2
  exit 2
fi

compose() {
  docker compose --env-file .env --env-file release.env "$@"
}

psql_admin() {
  compose exec -T postgres psql -U postgres -v ON_ERROR_STOP=1 -q "$@"
}

if ! compose exec -T postgres pg_restore --list < "$dump" > /dev/null; then
  echo "$dump is not a pg_dump custom-format (-Fc) file" >&2
  exit 1
fi

echo "This replaces ALL the data of the server's ledger database with $dump and maps:"
for i in "${!emails[@]}"; do echo "  ${emails[$i]} -> ${subjects[$i]}"; done
if ! $assume_yes; then
  read -r -p "Type 'import' to continue: " answer
  [ "$answer" = "import" ] || { echo "Cancelled"; exit 1; }
fi

stamp=$(date -u +%Y-%m-%dT%H%M%SZ)
swapped=false

cleanup() {
  status=$?
  if [ "$status" -ne 0 ] && ! $swapped; then
    echo "Import failed before replacing anything: dropping ledger_import and restarting the app" >&2
    psql_admin -d postgres -c 'DROP DATABASE IF EXISTS ledger_import WITH (FORCE)' || true
    compose up -d --wait api web || true
  fi
  exit "$status"
}
trap cleanup EXIT

echo "Stopping the API and the web app (maintenance)"
compose stop api web

# A message still queued for the current database would be consumed against the imported one.
queued=$(compose exec -T rabbitmq rabbitmqctl -q list_queues --no-table-headers messages | awk '{ total += $1 } END { print total + 0 }')
if [ "$queued" -ne 0 ]; then
  echo "RabbitMQ still has $queued queued message(s): start the API, let them be processed and try again" >&2
  exit 1
fi

mkdir -p "backups/pre-import-$stamp"
compose exec -T postgres pg_dump -U postgres -Fc ledger > "backups/pre-import-$stamp/ledger.dump"
echo "Current database saved to backups/pre-import-$stamp/ledger.dump"

psql_admin -d postgres -c 'DROP DATABASE IF EXISTS ledger_import WITH (FORCE)' -c 'CREATE DATABASE ledger_import'
compose exec -T postgres pg_restore -U postgres -d ledger_import --no-owner --no-privileges --exit-on-error < "$dump"
echo "Dump restored into ledger_import"

for i in "${!emails[@]}"; do
  # The values reach the SQL as psql variables (quoted by psql), never pasted in. Fails unless exactly one user
  # has that email, and refuses an id that another user already has.
  psql_admin -d ledger_import -v email="${emails[$i]}" -v subject="${subjects[$i]}" <<'SQL'
SELECT set_config('ledger_import.email', :'email', false), set_config('ledger_import.subject', :'subject', false) \g /dev/null
DO $$
DECLARE
  wanted_email text := current_setting('ledger_import.email');
  wanted_subject text := current_setting('ledger_import.subject');
  matches int;
BEGIN
  SELECT count(*) INTO matches FROM identity.users WHERE lower("Email") = lower(wanted_email);
  IF matches = 0 THEN
    RAISE EXCEPTION 'No user with email % in the dump', wanted_email;
  ELSIF matches > 1 THEN
    RAISE EXCEPTION 'More than one user with email % in the dump', wanted_email;
  END IF;
  IF EXISTS (SELECT 1 FROM identity.users WHERE "ExternalId" = wanted_subject AND lower("Email") <> lower(wanted_email)) THEN
    RAISE EXCEPTION 'Keycloak id % already belongs to another user in the dump', wanted_subject;
  END IF;
  UPDATE identity.users SET "ExternalId" = wanted_subject WHERE lower("Email") = lower(wanted_email);
END
$$;
SQL
  echo "Mapped ${emails[$i]}"
done

echo "Users in the imported data:"
psql_admin -d ledger_import -c 'SELECT "Email", "DisplayName", "ExternalId" FROM identity.users ORDER BY "CreatedAt"'

psql_admin -d postgres -c 'DROP DATABASE ledger WITH (FORCE)' -c 'ALTER DATABASE ledger_import RENAME TO ledger'
swapped=true
echo "ledger replaced by the imported data"

# Brings an older dump up to this version, then starts the app (a new API process: no cached user ids).
compose run --rm migrator
compose up -d --wait

echo
echo "Done. Log in with each mapped user and check the balances."
echo "To go back to the previous data, restore backups/pre-import-$stamp/ledger.dump (documentation §19.6)."
