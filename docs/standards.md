# Standards

These rules are enforced by the build (warnings are errors), by analyzers, by architecture tests, and by CI. Reviews check what the tools cannot.

## Code

- Target `net10.0`. `Nullable` and `ImplicitUsings` are on. Warnings are errors.
- File-scoped namespaces. Braces on all control statements.
- One public type per file. File name matches the type.
- Async methods end in `Async` and take a `CancellationToken`.
- Public members have XML documentation. CS1591 is suppressed only until `feat/core-api`, then removed.
- No escape hatches: no `dynamic`, no `object` parameters for data, no `!` suppression without a comment.
- Dependency injection: constructor injection only. No service locator.

## Architecture

- `Agent.Core` has no references to ASP.NET, EF Core, hosting, drivers, or LLM SDKs. `Agent.Architecture.Tests` enforces it.
- `Agent.Core` uses `BannedSymbols.txt` to ban process, socket, and environment access. Those capabilities go through ports.
- Each adapter lives in `Agent.Infrastructure` (or `Agent.Sandbox`) and implements a port from `Agent.Core`.
- Generated tools are data. They are never referenced by a project and never loaded into the API or worker process.

## API

- Versioned under `/api/v1`. Breaking changes need a new version.
- Errors use RFC 9457 Problem Details.
- `POST /tasks` accepts an `Idempotency-Key` header.
- `/health/live` and `/health/ready` are separate endpoints.
- OpenAPI document is generated from code and checked for drift in CI.

## Security

- OWASP ASVS Level 2 is the target for the API.
- OWASP Top 10 for LLM Applications applies to the agent. Email, web, and device text are untrusted data.
- No secrets in code, logs, prompts, or generated tools. Secrets come from configuration and are redacted.
- Dependencies: `dotnet list package --vulnerable --include-transitive` must be clean. A transitive vulnerability is fixed by pinning a patched version in `Directory.Packages.props`, not by suppressing the warning.
- Central package management: versions only in `Directory.Packages.props`.

## Testing

- xUnit for unit and architecture tests. Testcontainers for integration tests against real PostgreSQL (from `infra/postgres`). No database mocks in integration tests.
- Test names carry the requirement ID without underscores, because CA1707 rejects underscores: `FR34DeniedCommandIsRefused`.
- Sandbox escape tests run on every change to `Agent.Sandbox`.

## Git and review

- `main` is protected. Changes arrive by pull request, with CI green.
- Conventional Commits, with the requirement ID in the subject: `feat(tools): FR-3.2 patch-based edits`.
- Squash merge. One commit per feature on `main`.
- Do not commit generated output, `bin/`, `obj/`, logs, backups, or `.env`.

## Formatting

- `.editorconfig` is authoritative. `dotnet format --verify-no-changes` runs in CI.
- Line endings are LF in the repo. PowerShell scripts use CRLF.

## Documentation

- Requirements live in the SRS. Decisions live in ADRs (`docs/adr/`, MADR format). Operations live in runbooks (`docs/runbooks/`).
- One source of truth per topic. Do not copy requirements into code comments.
