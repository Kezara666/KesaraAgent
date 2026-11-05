# Stack

All components are free to use. Hosted services list their free-tier limits.
Last verified: 2026-11-05. Re-check before relying on any limit.

## Runtime and framework (free, open source)

| Component | License | Use |
|---|---|---|
| .NET 10 (LTS) | MIT | Runtime and SDK |
| ASP.NET Core, OpenAPI | MIT | HTTP API and OpenAPI document |
| EF Core, Npgsql, Pgvector.EntityFrameworkCore | MIT / PostgreSQL / MIT | Data access, vector search |
| Microsoft.Extensions.AI | MIT | Provider-neutral LLM client |
| Microsoft.Extensions.AI.Evaluation | MIT | Eval measurements |
| Polly (Microsoft.Extensions.Resilience) | BSD-3-Clause | Retries, timeouts, circuit breakers |
| .NET Aspire | MIT | Local multi-service runs and dashboard |
| Model Context Protocol C# SDK | MIT | MCP client and server |
| Roslyn, BannedApiAnalyzers | MIT | Code analysis, banned APIs |
| Hangfire (free edition) | LGPL-3.0 | Background jobs (from `infra/queue`) |
| Serilog, OpenTelemetry .NET | Apache-2.0 | Logs and traces |
| xUnit, Testcontainers, Playwright for .NET | Apache-2.0 / MIT / Apache-2.0 | Tests |
| Microsoft.OpenApi | MIT | Pinned to 2.7.5 (patched version) |
| Whisper.net | MIT | Speech-to-text (later) |
| Wasmtime .NET | Apache-2.0 | Pure-compute sandbox (later) |
| OllamaSharp | MIT | Local model fallback (later) |

## Hosted services (free tiers only)

| Service | Free tier | Limits |
|---|---|---|
| GitHub (repo, Actions, Container Registry) | Free private repos | 2,000 Actions minutes a month |
| NVIDIA NIM | Free developer access | Rate limits; model list changes |
| OpenRouter | Free models | Rate limits; model list changes |
| Gemini API, Groq, Cerebras, Mistral | Free tiers | Rate limits; model list changes |
| Cloudflare Tunnel and Access | Free for small teams | User limits on Access |
| Langfuse Cloud | Hobby tier | Monthly event limits |
| Sentry | Developer plan | Monthly error limits |
| Infisical Cloud or Doppler | Free tiers | Project and user limits |
| Backblaze B2 or Cloudflare R2 | About 10 GB free | Storage limits |

## Local tools (free)

Visual Studio Code with C# Dev Kit, Docker Desktop (free for personal use and small businesses under the license thresholds), Git for Windows, WSL2.

## Not used

No Azure services. No paid cloud services. No managed Kubernetes.
