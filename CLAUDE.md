# KesaraAgent: guide for Claude Code

## Commands

- Restore: `dotnet restore`
- Build: `dotnet build` (warnings are errors)
- Test: `dotnet test`
- Format check: `dotnet format --verify-no-changes`
- Vulnerable packages: `dotnet list package --vulnerable --include-transitive`

Run build, test, and format check before reporting a task as done.

## Rules

- Read `docs/standards.md` before writing code. It is the source of truth for style and review.
- Read `docs/branches.md` and work on one branch at a time. Stop at each gate and report.
- `src/Agent.Core` must not reference ASP.NET, EF Core, drivers, or SDKs. The architecture tests enforce this.
- Generated tool code is never referenced by any project. It runs only through `Agent.Sandbox`.
- Never write secrets to code, logs, prompts, or tool output. Use `.env` (gitignored) and `.env.example` for names only.
- External-send and write actions require approval through the policy layer. Do not bypass it for convenience.
- Requirement IDs (FR-x.y, NFR-x) go in commit messages and test names.
- Do not add dependencies without a reason in the commit message. Pin versions in `Directory.Packages.props`.
- Do not commit or push unless asked.

## Current state

- Merged to `main`: `chore/bootstrap`, `feat/core-api`, `feat/workspace-tools`, `feat/llm-gateway`. `feat/policy-audit` passes its gate and is ready to merge.
- Next branch: `feat/mcp-tools`.
- Known gaps from `feat/policy-audit`: approvals are single-approver with no identity beyond the one API token; the audit table is append-only through triggers in SQLite only; approvals cover shell commands only, not writes or MCP actions yet.
- Local `agent.db` files from before `feat/policy-audit` must be deleted. The schema changed (timestamps are now stored as ticks), and `EnsureCreated` does not migrate.
- Known gaps from `feat/llm-gateway`: providers are configured by base URL and environment variable only; there is no retry within a profile (a transient failure moves straight to the next profile); the daily budget check is not atomic under concurrent calls; NFR-9 quota alerts at 80 percent are not built.
- Known gaps from `feat/core-api`: FR-8.4 uses one token with all scopes, FR-8.8 (OpenAPI drift check) is not yet in CI, and CS1591 is suppressed until public docs are added.
- Known gaps from `feat/workspace-tools`: FR-3.5 (git checkpoint before edits) is not built. Commands that need approval are refused, because the approval flow arrives with `feat/policy-audit`. Edits can only change existing files.
- Schema is created with `EnsureCreated` until `infra/postgres` adds migrations.
