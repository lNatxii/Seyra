param(
    [Parameter(Position=0,Mandatory)][ValidateSet('init-tls','pair','list','revoke')][string]$Command,
    [string]$Name,
    [string]$Url,
    [string]$Root = $PSScriptRoot,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$arguments = @("$PSScriptRoot/seyra.mjs", $Command, '--root', $Root)
if ($PSBoundParameters.ContainsKey('Name')) { $arguments += @('--name', $Name) }
if ($PSBoundParameters.ContainsKey('Url')) { $arguments += @('--url', $Url) }
if ($Force) { $arguments += '--force' }
& node @arguments
exit $LASTEXITCODE
