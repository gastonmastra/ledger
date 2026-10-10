#!/usr/bin/env bash
# Points a user of the imported data at their account in this server's Keycloak (e.g. someone who had data
# locally and gets their Keycloak account later). Documentation §19.8.
#
#   ./link-user.sh <email> <keycloak user id>
#
# Run it BEFORE their first login here: a first login with an unlinked Keycloak id creates a new, empty user
# with the same email, and then this script refuses to link (fix that by hand, see the documentation).
set -euo pipefail

cd "$(dirname "$0")"

email=${1:-}
subject=${2:-}
if [ "$#" -ne 2 ] || [[ ! "$email" =~ ^[^@[:space:]]+@[^@[:space:]]+$ ]] \
  || [[ ! "$subject" =~ ^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$ ]]; then
  echo "usage: $0 <email> <keycloak user id (UUID)>" >&2
  exit 2
fi

# The values reach the SQL as psql variables (quoted by psql), never pasted in.
docker compose --env-file .env --env-file release.env exec -T postgres \
  psql -U postgres -d ledger -v ON_ERROR_STOP=1 -q -v email="$email" -v subject="$subject" <<'SQL'
SELECT set_config('ledger_link.email', :'email', false), set_config('ledger_link.subject', :'subject', false) \g /dev/null
DO $$
DECLARE
  wanted_email text := current_setting('ledger_link.email');
  wanted_subject text := current_setting('ledger_link.subject');
  matches int;
BEGIN
  SELECT count(*) INTO matches FROM identity.users WHERE lower("Email") = lower(wanted_email);
  IF matches = 0 THEN
    RAISE EXCEPTION 'No user with email %', wanted_email;
  ELSIF matches > 1 THEN
    RAISE EXCEPTION 'There are % users with email %: they probably logged in before being linked (documentation §19.8)', matches, wanted_email;
  END IF;
  IF EXISTS (SELECT 1 FROM identity.users WHERE "ExternalId" = wanted_subject AND lower("Email") <> lower(wanted_email)) THEN
    RAISE EXCEPTION 'Keycloak id % already belongs to another user', wanted_subject;
  END IF;
  UPDATE identity.users SET "ExternalId" = wanted_subject WHERE lower("Email") = lower(wanted_email);
END
$$;
SQL

echo "Linked $email: they can log in now."
