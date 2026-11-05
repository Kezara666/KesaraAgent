# KesaraAgent

An autonomous coding and workflow agent that runs on a home PC. It exposes one versioned REST API (`/api/v1`) with OpenAPI and Swagger UI, and runs with no UI attached.

## Requirements

- Windows 11 or Linux
- .NET 10 SDK (pinned in `global.json`)
- Docker Desktop (for PostgreSQL and the tool sandbox, from `infra/postgres` onward)

## Quick start

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src/Agent.Api
```

The API currently exposes only `/health/live` and the OpenAPI document. Swagger UI and the task API arrive in `feat/core-api`.

## Project layout

| Path | Purpose |
|---|---|
| `src/Agent.Core` | Plain C#, no framework dependencies. Orchestrator, policy, ports. |
| `src/Agent.Infrastructure` | Adapters: database, queue, LLM client, notifications. |
| `src/Agent.Sandbox` | Runs generated tools and shell commands in isolation. |
| `src/Agent.Api` | HTTP API, auth, OpenAPI. |
| `src/Agent.Worker` | Background jobs and scheduled workflows. |
| `tests/` | Unit tests and architecture rules. |
| `tools/builtin` | Tools shipped with the agent. |
| `tools/generated` | Tools created by the Tool Forge (git-tracked). |
| `docs/` | Requirements, standards, stack, branch plan, ADRs, runbooks. |

## Documentation

- [Standards](docs/standards.md): how code is written and reviewed
- [Stack](docs/stack.md): tools used and their free-tier limits
- [Branch plan](docs/branches.md): build order and gates
- [ADRs](docs/adr/): recorded decisions
- [SRS](docs/SRS.md): link to the requirements document

## License

To be decided before the first public release.
