param(
    [ValidateRange(1, 65535)][int]$Port = 4096
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$envPath = Join-Path $PSScriptRoot '.env.local'
if (-not (Test-Path -LiteralPath $envPath)) {
    throw 'Falta Backend/.env.local. Consulta README.md para crear el archivo.'
}
$localServerPassword = $null
foreach ($line in [IO.File]::ReadAllLines($envPath)) {
    $entry = $line.Trim()
    if (-not $entry -or $entry.StartsWith('#')) { continue }
    if ($entry -notmatch '^([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.*)$') {
        throw 'Formato inválido en .env.local: usa NOMBRE=valor, una variable por línea.'
    }
    $name = $Matches[1]
    $value = $Matches[2].Trim()
    if ($value.Length -ge 2 -and (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'")))) {
        $value = $value.Substring(1, $value.Length - 2)
    }
    if ($name -ceq 'OPENCODE_SERVER_PASSWORD') { $localServerPassword = $value }
    [Environment]::SetEnvironmentVariable($name, $value, 'Process')
}
if ([string]::IsNullOrWhiteSpace($env:HELMCODE_API_KEY) -or $env:HELMCODE_API_KEY -eq 'TU_CLAVE') {
    throw 'Añade tu HELMCODE_API_KEY real en Backend/.env.local antes de arrancar.'
}
if ([string]::IsNullOrWhiteSpace($localServerPassword)) {
    throw 'Falta OPENCODE_SERVER_PASSWORD en Backend/.env.local. Añade una contraseña HTTP propia; es distinta de HELMCODE_API_KEY. No se ha iniciado OpenCode.'
}
# OpenCode 2 gives OPENCODE_PASSWORD priority over OPENCODE_SERVER_PASSWORD.
# Use the file's shared password even if a different value was inherited.
[Environment]::SetEnvironmentVariable('OPENCODE_PASSWORD', $localServerPassword, 'Process')
if (-not (Get-Command opencode2 -ErrorAction SilentlyContinue)) {
    throw 'No se encuentra opencode2 en PATH. Instala el CLI de OpenCode v2 antes de continuar.'
}
$bridgeDirectory = Join-Path $PSScriptRoot 'bridge'
if (-not (Test-Path -LiteralPath (Join-Path $bridgeDirectory 'node_modules'))) {
    throw 'Faltan dependencias del bridge. Ejecuta npm ci --prefix Backend/bridge desde la raíz del repo.'
}
& npm run build --prefix $bridgeDirectory
if ($LASTEXITCODE -ne 0) { throw 'No se ha podido compilar el bridge. OpenCode no se ha iniciado.' }
& opencode2 serve --hostname 127.0.0.1 --port $Port
if ($LASTEXITCODE -ne 0) { throw "OpenCode terminó con código $LASTEXITCODE." }
