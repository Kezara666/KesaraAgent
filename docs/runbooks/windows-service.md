# Runbook: run the agent as a Windows service

Status: the service hosting is built into the API (`AddWindowsService`). The install steps below are
**not yet verified on this machine**, because they need an elevated (administrator) shell.

## Install (run as administrator)

```powershell
dotnet publish src/Agent.Api -c Release -o C:\Agent\api
sc.exe create KesaraAgent binPath= "C:\Agent\api\Agent.Api.exe" start= auto
sc.exe description KesaraAgent "Kesara autonomous agent API"
sc.exe failure KesaraAgent reset= 86400 actions= restart/5000/restart/30000/restart/60000
sc.exe start KesaraAgent
```

- `start= auto` starts the service at boot.
- `failure` restarts it after 5, 30, and 60 seconds if it crashes.

## Check

```powershell
sc.exe query KesaraAgent
Invoke-RestMethod http://localhost:5000/health/live
```

## Stop, remove

```powershell
sc.exe stop KesaraAgent
sc.exe delete KesaraAgent
```

## Notes

- Configure `Agent:ApiTokenSha256`, `Agent:DatabasePath`, and `Agent:WorkspaceRoot` in `appsettings.json` beside the
  executable, or as environment variables set on the service. Never put the raw token in a file.
- The PC must not sleep. Set the power plan to never sleep while plugged in.
- Windows updates restart the PC. Schedule updates outside working hours.
