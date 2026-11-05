#!/usr/bin/env bash
# Nightly backup: PostgreSQL dump (custom format) plus a git bundle of the tools history.
# Usage: scripts/ops/backup.sh [container] [database] [user]
set -euo pipefail

CONTAINER="${1:-agent-pg}"
DATABASE="${2:-agent}"
DB_USER="${3:-postgres}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
TARGET="$ROOT/backups/$STAMP"

mkdir -p "$TARGET"

docker exec "$CONTAINER" pg_dump -U "$DB_USER" -d "$DATABASE" -Fc > "$TARGET/database.dump"

if [ -d "$ROOT/.git" ]; then
  git -C "$ROOT" bundle create "$TARGET/history.bundle" --all >/dev/null 2>&1 || true
fi

SIZE="$(wc -c < "$TARGET/database.dump" | tr -d ' ')"
if [ "$SIZE" -lt 1 ]; then
  echo "backup failed: empty dump" >&2
  exit 1
fi

echo "backup written: $TARGET (database.dump: $SIZE bytes)"
