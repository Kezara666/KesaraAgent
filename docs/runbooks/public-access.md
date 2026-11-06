# Runbook: public access through Cloudflare Tunnel and Access

Status: **configuration only, not verified.** Creating the tunnel and the Access policy needs a Cloudflare
account and a domain, which were not available when this was written. Follow the steps once and record the
result in the log at the bottom.

## Why this design

- The PC opens no inbound ports on the router. `cloudflared` makes an outbound connection to Cloudflare.
- The agent listens on `127.0.0.1` only, so it cannot be reached directly from the LAN or the internet.
- Cloudflare Access requires a login (email one-time PIN or identity provider) before any request reaches the tunnel.
- The API still requires its own token (FR-8.4), so Access is a second layer, not the only one.

## Setup

1. Install `cloudflared` (free): `winget install Cloudflare.cloudflared`.
2. Log in and create a named tunnel:
   ```powershell
   cloudflared tunnel login
   cloudflared tunnel create kesara-agent
   cloudflared tunnel route dns kesara-agent agent.example.com
   ```
3. Copy `deploy/cloudflared/config.example.yml` to `config.yml` and replace the placeholders.
4. In the Cloudflare Zero Trust dashboard (free for up to 50 users), create an Access application for
   `agent.example.com` with a policy that allows only your email address. Choose the one-time PIN method.
5. Start the agent bound to loopback:
   ```powershell
   $env:ASPNETCORE_URLS = "http://127.0.0.1:5000"
   dotnet run --project src/Agent.Api
   ```
6. Run the tunnel: `cloudflared tunnel --config config.yml run kesara-agent`. Install it as a service with
   `cloudflared service install` once it works.

## Checks

- From a phone on mobile data, `https://agent.example.com/health/live` asks for an Access login first.
- After login, `https://agent.example.com/health/live` returns `live`.
- `http://<LAN-IP>:5000/health/live` from another device on the LAN is refused, because the agent listens on loopback only.

## Log

| Date | Step | Result |
|---|---|---|
| (not yet run) | | |
