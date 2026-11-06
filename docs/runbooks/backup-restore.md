# Runbook: backup and restore

## What is backed up

| Item | How | Where |
|---|---|---|
| PostgreSQL database (tasks, audit, approvals, schedules, usage) | `pg_dump` custom format, from the database container | `backups/<timestamp>/database.dump` |
| Tools and project history | `git bundle` of the repository | `backups/<timestamp>/history.bundle` |

`backups/` is in `.gitignore`. Copy it off the PC to Cloudflare R2 or Backblaze B2 (free tier) with `rclone`
or the provider's CLI. Off-site copy setup is not yet automated (see the open items).

## Nightly backup

```bash
scripts/ops/backup.sh agent-pg agent postgres
```

Schedule it with Windows Task Scheduler at 02:00, running Git Bash:

```
"C:\Program Files\Git\bin\bash.exe" -lc "cd /c/Users/ACER/Desktop/Projects/KesaraAgent && scripts/ops/backup.sh"
```

## Restore drill (monthly)

The drill restores the latest dump into a scratch database (`agent_restore_drill`), checks that the tables and the
audit append-only triggers exist, then drops the scratch database. It never touches the live database.

```bash
scripts/ops/restore-drill.sh agent-pg
```

Expected output: `restore drill PASSED: tables=6 audit_triggers=2 from ...`. Record each drill's result in this file.

## Real restore (live database is lost)

1. Stop the agent service: `sc.exe stop KesaraAgent`.
2. Recreate the database container, then restore into it:
   `docker exec -i agent-pg pg_restore -U postgres -d agent --no-owner < backups/<timestamp>/database.dump`
3. Start the service and check `/health/ready`.
4. Restore the tools history if needed: `git clone backups/<timestamp>/history.bundle restored-repo`.

## Drill log

| Date | Result | Backup used |
|---|---|---|
| 2026-11-05 | PASSED (tables=6, audit_triggers=2) | 20261105T162830Z |

## Open items

- Off-site upload is not automated yet.
- Retention (keep 14 nightly, 12 monthly) is not enforced by a script yet.
