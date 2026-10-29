param(
    [ValidateRange(1, 65535)][int]$Port = 4096,
    [switch]$Gateway,
    [string]$GatewayHost = '0.0.0.0',
    [ValidateRange(1, 65535)][int]$GatewayPort = 8443,
    [string]$Root = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'
$arguments = @("$PSScriptRoot/seyra.mjs", 'start', '--port', [string]$Port, '--gateway-host', $GatewayHost, '--gateway-port', [string]$GatewayPort, '--root', $Root)
if ($PSBoundParameters.ContainsKey('Gateway')) {
    $arguments += if ($Gateway) { '--gateway' } else { '--no-gateway' }
}
& node @arguments
exit $LASTEXITCODE
