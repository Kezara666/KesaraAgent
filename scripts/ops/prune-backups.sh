#!/usr/bin/env bash
# Keeps the newest backups and deletes older ones. Backups are the timestamped folders written by backup.sh.
# Usage: scripts/ops/prune-backups.sh [keep-count] [--dry-run]
# Default keep-count is 14. Folders that do not match the backup timestamp pattern are never touched.
set -euo pipefail

KEEP="${1:-14}"
DRY_RUN="${2:-}"
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
BACKUPS="$ROOT/backups"

if ! [[ "$KEEP" =~ ^[0-9]+$ ]] || [ "$KEEP" -lt 1 ]; then
  echo "keep-count must be a positive whole number" >&2
  exit 2
fi

if [ ! -d "$BACKUPS" ]; then
  echo "no backups folder at $BACKUPS"
  exit 0
fi

# Timestamps sort lexically, so the newest come last.
mapfile -t ALL < <(find "$BACKUPS" -mindepth 1 -maxdepth 1 -type d -name '[0-9]*T[0-9]*Z' -printf '%f\n' | sort)

TOTAL="${#ALL[@]}"
if [ "$TOTAL" -le "$KEEP" ]; then
  echo "nothing to prune: $TOTAL backup(s), keeping $KEEP"
  exit 0
fi

PRUNE_COUNT=$((TOTAL - KEEP))
for ((i = 0; i < PRUNE_COUNT; i++)); do
  TARGET="$BACKUPS/${ALL[$i]}"
  if [ "$DRY_RUN" = "--dry-run" ]; then
    echo "would delete $TARGET"
  else
    rm -rf -- "$TARGET"
    echo "deleted $TARGET"
  fi
done

echo "kept $KEEP of $TOTAL backup(s)"
