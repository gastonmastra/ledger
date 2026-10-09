#!/usr/bin/env bash
# Deploys one image tag (a commit sha) on the server: pull, migrate, start. Run from /opt/ledger.
#   ./deploy.sh <tag>     used by .github/workflows/deploy-vps.yml; also the way to roll back by hand
# The images must be pullable: the workflow logs in to ghcr.io before calling it.
set -euo pipefail

tag=${1:?usage: deploy.sh <image tag>}
cd "$(dirname "$0")"

compose() {
  docker compose --env-file .env --env-file release.env "$@"
}

[ -f .env ] || { echo "Missing /opt/ledger/.env (see .env.example and documentation §19.4)" >&2; exit 1; }

previous=$(sed -n 's/^LEDGER_TAG=//p' release.env 2>/dev/null || true)
echo "LEDGER_TAG=$tag" > release.env
echo "Deploying $tag (previous: ${previous:-none})"

compose pull --quiet
compose up -d --wait postgres rabbitmq
# Migrations first: the new API expects the new schema. A failure stops here, the old version keeps running.
if ! compose run --rm migrator; then
  echo "LEDGER_TAG=${previous}" > release.env
  echo "Migrations failed: still on ${previous:-nothing}" >&2
  exit 1
fi
compose up -d --wait --remove-orphans

docker image prune -f > /dev/null
compose ps --format 'table {{.Service}}\t{{.Status}}'
