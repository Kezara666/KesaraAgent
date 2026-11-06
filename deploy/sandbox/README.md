# Sandbox images

Shell commands run in Docker containers with no network, a read-only filesystem, and the workspace as the only writable
folder. The defaults are small, so commands that need more tools need a richer image.

| Need | Image | Build |
|---|---|---|
| bash commands (default) | `alpine:3.20` | none |
| bash with `dotnet` and `git` | `kesara-agent/sandbox-bash:1` | `docker build -f deploy/sandbox/Dockerfile.bash-tools -t kesara-agent/sandbox-bash:1 deploy/sandbox` |
| PowerShell (`pwsh`) with `git` | `kesara-agent/sandbox-pwsh:1` | `docker build -f deploy/sandbox/Dockerfile.powershell -t kesara-agent/sandbox-pwsh:1 deploy/sandbox` |

Configuration (`appsettings` or environment variables):

```
Agent__Sandbox__BashImage=kesara-agent/sandbox-bash:1
Agent__Sandbox__PowerShellImage=kesara-agent/sandbox-pwsh:1
```

Only the configured images are allowed. A PowerShell command is refused when no PowerShell image is configured. It is
never run on the host.
