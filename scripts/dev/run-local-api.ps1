# Runs the API locally on port 5055 with the model provider and token settings.
# Values come from .env in the repo root (git-ignored). Copy .env.example to .env and fill it in.
# Usage: powershell -ExecutionPolicy Bypass -File scripts/dev/run-local-api.ps1
$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$envFile = Join-Path $root ".env"

if (-not (Test-Path $envFile)) {
    throw "No .env file found. Copy .env.example to .env and fill in the values."
}

foreach ($line in Get-Content $envFile) {
    $trimmed = $line.Trim()
    if ($trimmed -eq "" -or $trimmed.StartsWith("#") -or -not $trimmed.Contains("=")) { continue }
    $key, $value = $trimmed.Split("=", 2)
    [Environment]::SetEnvironmentVariable($key.Trim(), $value.Trim(), "Process")
}

# Providers are enabled only when their key is set in .env. Each one gets its own model profile, in this order:
# NVIDIA NIM first (NIM_API_KEY and NIM_MODEL), then OpenRouter (OPENROUTER_API_KEY and AGENT_MODEL).
$providers = @(
    @{ Id = "nim"; BaseUrl = "https://integrate.api.nvidia.com/v1"; KeyVariable = "NIM_API_KEY"; ModelVariable = "NIM_MODEL" },
    @{ Id = "openrouter"; BaseUrl = "https://openrouter.ai/api/v1"; KeyVariable = "OPENROUTER_API_KEY"; ModelVariable = "AGENT_MODEL" }
)

$index = 0
foreach ($provider in $providers) {
    $key = [Environment]::GetEnvironmentVariable($provider.KeyVariable, "Process")
    $model = [Environment]::GetEnvironmentVariable($provider.ModelVariable, "Process")
    if ([string]::IsNullOrWhiteSpace($key) -or [string]::IsNullOrWhiteSpace($model)) { continue }

    [Environment]::SetEnvironmentVariable("Agent__Providers__${index}__Id", $provider.Id, "Process")
    [Environment]::SetEnvironmentVariable("Agent__Providers__${index}__BaseUrl", $provider.BaseUrl, "Process")
    [Environment]::SetEnvironmentVariable("Agent__Providers__${index}__ApiKeyVariable", $provider.KeyVariable, "Process")
    [Environment]::SetEnvironmentVariable("Agent__ModelProfiles__${index}__Id", "$($provider.Id)-main", "Process")
    [Environment]::SetEnvironmentVariable("Agent__ModelProfiles__${index}__ProviderId", $provider.Id, "Process")
    [Environment]::SetEnvironmentVariable("Agent__ModelProfiles__${index}__Model", $model, "Process")
    [Environment]::SetEnvironmentVariable("Agent__ModelProfiles__${index}__SupportsToolCalling", "false", "Process")
    [Environment]::SetEnvironmentVariable("Agent__ModelProfiles__${index}__ContextTokens", "8000", "Process")
    $index++
}

if ($index -eq 0) {
    throw "No provider is ready. Set NIM_API_KEY and NIM_MODEL, or OPENROUTER_API_KEY and AGENT_MODEL, in .env."
}

$env:Agent__ApiTokenSha256 = [Environment]::GetEnvironmentVariable("API_TOKEN_HASH", "Process")
$env:ASPNETCORE_URLS = "http://127.0.0.1:5055"

Set-Location $root
dotnet run --project src/Agent.Api
