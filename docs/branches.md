# Branch plan

Work one branch at a time. Merge only when its gate passes. Tag each release.

| Branch | Goal | Gate |
|---|---|---|
| `chore/bootstrap` | Solution, shared rules, CI, docs | Build, tests, and format pass in CI |
| `feat/core-api` | Task API, OpenAPI, Swagger UI, SQLite via EF Core, in-process queue, token auth | Task runs through Swagger with the UI off (FR-8.1 to FR-8.3) |
| `feat/workspace-tools` | Read, search, edit, shell tools | Edit yields a diff; denied command is refused (FR-3.1 to FR-3.4) |
| `feat/llm-gateway` | Providers, fallback, daily budgets | Forced rate limit falls back to the next provider (FR-4.1 to FR-4.4) |
| `feat/policy-audit` | Tiers, approvals, append-only audit | Blocked action appears in the audit log (FR-3.4, FR-5.6) |
| `feat/sandbox` | Isolated execution, no network by default | Escape test suite passes |
| `feat/tool-forge` | Generate, test, promote, roll back | Gap becomes a tested, promoted tool with a commit (FR-5.1 to FR-5.5) |
| `feat/mcp-tools` | External tools via MCP | Email summary reaches phone from one prompt (FR-10.1 to FR-10.5) |
| `feat/workflows` | Dynamic multi-step plans | Failed notify retries without a duplicate send (FR-9.1 to FR-9.15) |
| `feat/headless-ops` | Scheduler, safe mode, Windows service | Schedules run with the UI and LLM stopped (FR-7.1 to FR-7.4) |
| `feat/web-ui` | Angular or React client on the API | Playwright passes for chat, diff, approvals (FR-1.x) |
| `feat/voice` | Browser speech input and output | Spoken command creates the same task as typed (FR-2.1, FR-2.2, FR-2.4) |
| `infra/postgres` | Swap SQLite for PostgreSQL | Existing tests pass on PostgreSQL via Testcontainers |
| `infra/queue` | Swap in-process queue for Hangfire | Task survives a restart |
| `sec/ci` | CodeQL, Trivy, Gitleaks, dependency checks | PR blocked on a high-severity finding |
| `sec/auth` | Scoped tokens, then ASP.NET Core Identity or Keycloak | Unauthenticated call returns 401 |
| `sec/public-access` | Cloudflare Tunnel and Access | Public URL asks for login |
| `ops/observability` | OpenTelemetry to the Grafana stack | One task produces one trace across API, worker, sandbox |
| `ops/backup` | restic to R2 or B2 | Restore drill passes |
| `feat/devices` | Mosquitto, MQTTnet, ESPHome nodes (deferred) | One real node pairs and reads a value |

Versions: `v0.1.0` after `feat/core-api`, `v0.5.0` after `feat/tool-forge`, `v1.0.0` after `feat/web-ui` and `feat/headless-ops`, `v2.0.0` after the `infra/` and `ops/` branches.
