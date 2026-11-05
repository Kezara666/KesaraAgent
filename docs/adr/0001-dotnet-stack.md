# 0001: Use .NET 10 with ASP.NET Core, EF Core, and Microsoft.Extensions.AI

Status: Accepted (2026-11-05)

## Context

The project needs an API with OpenAPI, a background worker, a sandbox for generated tools, LLM access through free-tier providers, and a team skill base in .NET. Free and open-source tooling is required.

## Decision

Build the backend in .NET 10 (LTS): ASP.NET Core for the API, a worker host for jobs, EF Core with PostgreSQL, Microsoft.Extensions.AI for LLM access, the official MCP C# SDK for external tools, and Roslyn analyzers to enforce rules. Use Polly for resilience and .NET Aspire for local runs.

## Consequences

- The core library stays framework-free, so the agent logic can be tested without a host.
- Generated tools are C# and are checked by Roslyn before any test runs.
- Smaller LLM and agent community than Python. Voice and device work uses sidecars or MQTT where .NET libraries are weaker.
- Everything used is free and open source. Hosted services are free-tier only (see `docs/stack.md`).
- Azure is not used.

## Alternatives considered

- Python with FastAPI: stronger AI and voice ecosystem. Not chosen, to keep the stack aligned with the team's .NET skills and the target job market.
- TypeScript with NestJS: framework-heavy, and the core would depend on framework decorators.
- LangGraph and LiteLLM: not needed for the orchestrator or LLM routing. Revisit if workflow complexity grows.
