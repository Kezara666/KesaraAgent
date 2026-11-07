# Runs the agent evaluation cases against a running API and reports pass or fail for each.
# Each case sends a prompt, waits for the task to finish, then checks the answer and the audit trail. Tool calls
# appear in the audit trail as agent.tool.call (ran) or agent.tool.held (waiting for approval).
#
# Usage:
#   $env:AGENT_TOKEN = "<your API token>"
#   powershell -ExecutionPolicy Bypass -File scripts/eval/run-evals.ps1
#   powershell -ExecutionPolicy Bypass -File scripts/eval/run-evals.ps1 -BaseUrl http://localhost:4200 -Cases evals/cases.json
#
# The token is read from the environment only. It is never written to a file.
param(
    [string]$BaseUrl = "http://127.0.0.1:5055",
    [string]$Cases = (Join-Path $PSScriptRoot "..\..\evals\cases.json"),
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = "Stop"
$token = $env:AGENT_TOKEN
if ([string]::IsNullOrWhiteSpace($token)) {
    throw "Set AGENT_TOKEN to your API token first."
}

$headers = @{ Authorization = "Bearer $token" }
$caseList = Get-Content $Cases -Raw | ConvertFrom-Json
$passed = 0
$failed = 0

foreach ($case in $caseList) {
    $started = (Get-Date).ToUniversalTime()
    $submitHeaders = @{ Authorization = "Bearer $token"; "Idempotency-Key" = "eval-$($case.name -replace '\W', '-')-$([guid]::NewGuid().ToString('N'))" }
    $task = Invoke-RestMethod -Method Post -Uri "$BaseUrl/api/v1/tasks" -Headers $submitHeaders -ContentType "application/json" -Body (@{ prompt = $case.prompt } | ConvertTo-Json)

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Seconds 2
        $task = Invoke-RestMethod -Uri "$BaseUrl/api/v1/tasks/$($task.id)" -Headers $headers
    } while ($task.status -in @("Queued", "Running") -and (Get-Date) -lt $deadline)

    $auditJson = (Invoke-WebRequest -Uri "$BaseUrl/api/v1/audit?limit=200" -Headers $headers -UseBasicParsing).Content
    $rawAudit = ConvertFrom-Json -InputObject $auditJson
    $audit = @($rawAudit | Where-Object { ([datetime]$_.time).ToUniversalTime() -ge $started.AddSeconds(-1) })
    $ran = @($audit | Where-Object { $_.action -eq "agent.tool.call" } | ForEach-Object { $_.target })
    $held = @($audit | Where-Object { $_.action -eq "agent.tool.held" } | ForEach-Object { $_.target })

    $problems = New-Object System.Collections.Generic.List[string]
    if ($task.status -ne "Done") { $problems.Add("task status was $($task.status) ($($task.error))") }

    foreach ($name in @($case.expectTools)) {
        if ($name -and ($ran -notcontains $name)) { $problems.Add("expected tool '$name' to run; ran: [$($ran -join ', ')]") }
    }
    foreach ($name in @($case.expectHeld)) {
        if ($name -and ($held -notcontains $name)) { $problems.Add("expected '$name' to be held for approval; held: [$($held -join ', ')]") }
    }
    if ($case.expectNoTools -eq $true -and ($ran.Count + $held.Count) -gt 0) {
        $problems.Add("expected no tool calls; saw: [$(@($ran + $held) -join ', ')]")
    }
    if ($case.answerMatches -and ($task.result -notmatch $case.answerMatches)) {
        $problems.Add("answer did not match '$($case.answerMatches)'")
    }
    if ($task.result -match "\[offline\]") {
        $problems.Add("no model answered (offline reply)")
    }

    if ($problems.Count -eq 0) {
        $passed++
        Write-Host "PASS  $($case.name)"
    } else {
        $failed++
        Write-Host "FAIL  $($case.name)"
        foreach ($problem in $problems) { Write-Host "      - $problem" }
    }
}

Write-Host ""
Write-Host "$passed passed, $failed failed, $($passed + $failed) total"
if ($failed -gt 0) { exit 1 }
