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

Status: see docs/status.md for the full branch table, test results, and open items.

- All planned branches are merged to `main` except `feat/devices`, which is deferred by request.
- Top open items: approved shell commands still run on the host (route them through `Agent.Sandbox`); unify the approval decision router for plans and promotions; verify the Windows service, Cloudflare tunnel, and GitHub workflows on your accounts.
- Schema: PostgreSQL uses EF migrations. SQLite (dev and tests) uses EnsureCreated; delete an old local `agent.db` after schema changes.
- Never push or commit without being asked.
