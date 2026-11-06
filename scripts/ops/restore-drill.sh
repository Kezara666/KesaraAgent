#!/usr/bin/env bash
# Restore drill: restores the latest dump into a scratch database and checks the result.
# It never touches the live database. Usage: scripts/ops/restore-drill.sh [container] [backup-dir]
set -euo pipefail

CONTAINER="${1:-agent-pg}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
BACKUP_DIR="${2:-$(ls -1d "$ROOT"/backups/*/ | sort | tail -n 1)}"
SCRATCH="agent_restore_drill"
DB_USER="postgres"

docker exec "$CONTAINER" psql -U "$DB_USER" -d postgres -c "DROP DATABASE IF EXISTS $SCRATCH;" >/dev/null
docker exec "$CONTAINER" psql -U "$DB_USER" -d postgres -c "CREATE DATABASE $SCRATCH;" >/dev/null
docker exec -i "$CONTAINER" pg_restore -U "$DB_USER" -d "$SCRATCH" --no-owner < "$BACKUP_DIR/database.dump"

TABLES="$(docker exec "$CONTAINER" psql -U "$DB_USER" -d "$SCRATCH" -tAc "SELECT count(*) FROM information_schema.tables WHERE table_schema='public';")"
TRIGGERS="$(docker exec "$CONTAINER" psql -U "$DB_USER" -d "$SCRATCH" -tAc "SELECT count(*) FROM information_schema.triggers WHERE event_object_table='audit';")"

docker exec "$CONTAINER" psql -U "$DB_USER" -d postgres -c "DROP DATABASE $SCRATCH;" >/dev/null

if [ "$TABLES" -lt 1 ] || [ "$TRIGGERS" -lt 2 ]; then
  echo "restore drill FAILED: tables=$TABLES audit_triggers=$TRIGGERS" >&2
  exit 1
fi

echo "restore drill PASSED: tables=$TABLES audit_triggers=$TRIGGERS from $BACKUP_DIR"
