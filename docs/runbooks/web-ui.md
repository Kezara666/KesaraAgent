# Runbook: web UI and end-to-end tests

## Run the UI locally

1. Start the agent API on port 5055 with a token hash in configuration:
   ```powershell
   $env:ASPNETCORE_URLS = "http://127.0.0.1:5055"
   $env:Agent__ApiTokenSha256 = "<sha256 of your token>"
   dotnet run --project src/Agent.Api
   ```
2. In `apps/web`, start the dev server. It proxies `/api` to port 5055 (`proxy.conf.json`):
   ```powershell
   npm install
   npm start
   ```
3. Open `http://localhost:4200` and paste your token.

## Tests

- Unit tests (Vitest): `npm test` in `apps/web` (13 tests: sign-in storage, API headers and URLs,
  token interceptor, error messages, speech feature detection).
- End-to-end (Playwright, Chromium): start the API with `Agent__ApiTokenSha256` set to the hash of
  `e2e-token` and `Agent__WorkspaceRoot` set to `apps/web/e2e-workspace`, start `npm start`, then run:
  ```powershell
  $env:AGENT_E2E_TOKEN = "e2e-token"
  $env:AGENT_E2E_WORKSPACE = "<repo>/apps/web/e2e-workspace"
  npx playwright test
  ```

## What the UI covers

| Screen | Requirement | Notes |
|---|---|---|
| Sign in | FR-8.4 | Token kept in sessionStorage for this tab only |
| Tasks | FR-1.1, FR-1.2 | Submit with an idempotency key, live status, results |
| Voice | FR-2.1, FR-2.2 | Browser speech input and read-aloud. Audio stays in the browser. Shows a message when unsupported |
| Approvals | FR-1.7, FR-9.7 | Approve or reject held actions |
| Schedules | FR-9.8 | Preview next runs in UTC, then confirm |
| Workspace | FR-3.2, FR-1.3 | Exact edit and the resulting diff |
| Audit | FR-8.7 | Read-only view |

## Known gaps

- Tool browser (FR-1.5) and device console (FR-1.6) screens are not built. Devices are deferred.
- Browser speech recognition depends on the browser. Chromium and Edge support it; some others do not.
- The e2e suite depends on a local API and a Chromium install. It is not yet in CI.
