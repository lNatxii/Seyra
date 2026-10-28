param(
    [ValidateRange(1, 65535)][int]$Port = 4096
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$envPath = Join-Path $PSScriptRoot '.env.local'
if (-not (Test-Path -LiteralPath $envPath)) {
    throw 'Backend/.env.local is missing. See README.md to create the file.'
}
$localServerPassword = $null
foreach ($line in [IO.File]::ReadAllLines($envPath)) {
    $entry = $line.Trim()
    if (-not $entry -or $entry.StartsWith('#')) { continue }
    if ($entry -notmatch '^([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$') {
        throw 'Invalid .env.local format: use NAME=value, one variable per line.'
    }
    $name = $Matches[1]
    $value = $Matches[2].Trim()
    if ($value.Length -ge 2 -and (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'")))) {
        $value = $value.Substring(1, $value.Length - 2)
    }
    if ($name -ceq 'OPENCODE_SERVER_PASSWORD') { $localServerPassword = $value }
    [Environment]::SetEnvironmentVariable($name, $value, 'Process')
}
if ([string]::IsNullOrWhiteSpace($env:META_API_KEY) -and -not [string]::IsNullOrWhiteSpace($env:MODEL_API_KEY)) {
    $env:META_API_KEY = $env:MODEL_API_KEY
}
if ([string]::IsNullOrWhiteSpace($env:META_API_KEY) -or $env:META_API_KEY -eq 'TU_CLAVE') {
    throw 'Add META_API_KEY (Meta Model API key from dev.meta.ai) to Backend/.env.local'
}
if ([string]::IsNullOrWhiteSpace($env:HELMCODE_API_KEY) -or $env:HELMCODE_API_KEY -eq 'TU_CLAVE') {
    Write-Warning 'seyra_glm will not work without HELMCODE_API_KEY'
}
if ([string]::IsNullOrWhiteSpace($localServerPassword)) {
    throw 'OPENCODE_SERVER_PASSWORD is missing from Backend/.env.local. Add your own HTTP password; it is separate from provider API keys. OpenCode was not started.'
}
# OpenCode 2 gives OPENCODE_PASSWORD priority over OPENCODE_SERVER_PASSWORD.
# Use the file's shared password even if a different value was inherited.
[Environment]::SetEnvironmentVariable('OPENCODE_PASSWORD', $localServerPassword, 'Process')
if (-not (Get-Command opencode2 -ErrorAction SilentlyContinue)) {
    throw 'opencode2 was not found in PATH. Install the OpenCode v2 CLI before continuing.'
}
$bridgeDirectory = Join-Path $PSScriptRoot 'bridge'
if (-not (Test-Path -LiteralPath (Join-Path $bridgeDirectory 'node_modules'))) {
    throw 'Bridge dependencies are missing. Run npm ci --prefix Backend/bridge from the repository root.'
}
& npm run build --prefix $bridgeDirectory
if ($LASTEXITCODE -ne 0) { throw 'Could not build the bridge. OpenCode was not started.' }
& opencode2 serve --hostname 127.0.0.1 --port $Port
if ($LASTEXITCODE -ne 0) { throw "OpenCode exited with code $LASTEXITCODE." }
