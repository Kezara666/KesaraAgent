# Project status

Last updated: 2026-11-05. The status of each branch, what was verified and how, and what is still open.
Test counts come from full runs on `main`.

## Test results (main)

| Project | Tests | Result | What it covers |
|---|---|---|---|
| Agent.Core.Tests | 104 | Passed | Task service, processor, recovery, LLM router, workspace, command policy, governance, tool forge, workflows, headless, tokens |
| Agent.Architecture.Tests | 2 | Passed | Core has no dependency on outer layers or framework namespaces |
| Agent.Api.Tests | 26 | Passed on SQLite and on PostgreSQL | HTTP API, auth and scopes, approvals, audit, schedules, tokens |
| Agent.Infrastructure.Tests | 26 | Passed | PostgreSQL parity (Testcontainers), MCP over stdio, notifier, audit append-only, tool forge adapters |
| Agent.Sandbox.Tests | 10 | Passed (real Docker) | Escape suite SB-1 to SB-8; generated tests run in restore and test containers |
| apps/web unit (Vitest) | 13 | Passed | Sign-in storage, API client, token interceptor, error messages, speech detection |
| apps/web e2e (Playwright) | 4 | Passed (real API) | Sign-in redirect, chat to Done, diff, approval |

## Branches

| Branch | Status | Verified | Not verified / gaps |
|---|---|---|---|
| chore/bootstrap | Merged | Build with warnings as errors; architecture tests | CI not run on GitHub |
| feat/core-api | Merged | Task runs with the UI off; Swagger served | FR-8.8 drift check not yet in CI |
| feat/workspace-tools | Merged | Diff returned; denied command refused | FR-3.5 git checkpoint not built |
| feat/llm-gateway | Merged | Fallback on 429 and 5xx; budgets persisted | Retry within a profile is not built |
| feat/policy-audit | Merged | Denied and held commands audited; approvals expire | Single approver identity |
| feat/sandbox | Merged | 8 of 8 escape tests against real containers | **Approved shell commands still run on the host** |
| feat/tool-forge | Merged | Real restore and test in containers; promotion commit | Promotion decision method exists but the API does not expose it |
| feat/workflows | Merged | Retry without duplicate send; resume; cancel; approval hold | Templates (FR-9.14) not built; invoker must honor the key |
| feat/mcp-tools | Merged | Real MCP server over stdio; host secrets blanked | No Gmail server (needs your OAuth setup) |
| feat/headless-ops | Merged | Scheduler with no client; safe mode; ntfy request format | Windows service install not run (needs admin); NL scheduling not built |
| feat/web-ui (includes voice) | Merged | 13 unit and 4 e2e tests | Tool browser (FR-1.5) and device console not built |
| infra/postgres | Merged | Whole API suite on PostgreSQL (two runs and a fresh database) | Migrations are for PostgreSQL only |
| infra/queue | Merged | Queued and interrupted tasks recovered | Hangfire not used; full host restart not automated |
| sec/ci | Merged | Gitleaks: no leaks in 14 commits; Trivy: no HIGH or CRITICAL | GitHub workflow files not run on GitHub |
| sec/auth | Merged | Scoped tokens; revocation; secrecy of hashes; works on PostgreSQL | Bootstrap token still has every scope |
| sec/public-access | Merged (configuration only) | Files and runbook written | **Not verified**: needs a Cloudflare account and a domain |
| ops/observability | Merged | Jaeger shows the service; Prometheus shows request metrics | No prebuilt dashboards |
| ops/backup | Merged | Restore drill passed on a populated database | Off-site upload and retention not automated |
| fix/approval-kind | Merged | Regression test for a real defect | Unified approval decision router not built |
| feat/devices | **Not built** | Deferred by your request (IoT later) | Everything device-related |

## Open items, in priority order

1. **Route approved shell commands through the sandbox.** Policy and approvals run first, but an approved command still
   executes on the host. This is the largest remaining security gap.
2. **Unified approval decision router.** The API decides held commands only. Plans (`workflow.plan`) and promotions
   (`tool.promote`) need their own routing so the UI can approve them.
3. **Run the Windows service install** and the Cloudflare tunnel with your accounts; both are documented, neither verified here.
4. **Run the GitHub workflows** after pushing the repository.
5. **Gmail MCP server** with your Google OAuth credentials.
6. **Device layer** (`feat/devices`), when you want to start it.

## How to reproduce

```powershell
dotnet test
cd apps/web; npm test
# End to end: see docs/runbooks/web-ui.md
```

Docker must be running for the sandbox, PostgreSQL parity, and tool-test tests. The tests skip with a message when Docker is absent (sandbox), or fail (PostgreSQL parity).
