param(
    [Parameter(Position=0,Mandatory)][ValidateSet('init-tls','pair','list','revoke')][string]$Command,
    [string]$Name,
    [string]$Url,
    [string]$Root = $PSScriptRoot,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
$dataRoot = Join-Path ([IO.Path]::GetFullPath($Root)) '.seyra'
$tlsRoot = Join-Path $dataRoot 'tls'
$devicesPath = Join-Path $dataRoot 'devices.json'
function New-RandomToken {
    $bytes = New-Object byte[] 32
    $random = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $random.GetBytes($bytes) } finally { $random.Dispose() }
    return [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+','-').Replace('/','_')
}
function Get-TokenHash([string]$Token) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Token)))).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose() }
}
function Get-Fingerprint {
    $certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2((Join-Path $tlsRoot 'gateway.pfx'), ([IO.File]::ReadAllText((Join-Path $tlsRoot 'gateway.pass')).Trim()))
    try {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { return ([BitConverter]::ToString($sha.ComputeHash($certificate.RawData))).Replace('-','').ToLowerInvariant() }
        finally { $sha.Dispose() }
    } finally { $certificate.Dispose() }
}
function Save-Devices($Devices) {
    [IO.File]::WriteAllText($devicesPath, (ConvertTo-Json -InputObject @($Devices) -Depth 5), [Text.UTF8Encoding]::new($false))
}
New-Item -ItemType Directory -Force $dataRoot | Out-Null
$devices = if (Test-Path -LiteralPath $devicesPath) { @(Get-Content -LiteralPath $devicesPath -Raw | ConvertFrom-Json) } else { @() }
if ($Command -eq 'init-tls') {
    $pfxPath = Join-Path $tlsRoot 'gateway.pfx'
    if ((Test-Path -LiteralPath $pfxPath) -and -not $Force) { throw 'TLS already exists. Use -Force explicitly to rotate it, then pair devices again.' }
    New-Item -ItemType Directory -Force $tlsRoot | Out-Null
    $password = New-RandomToken
    $certificate = New-SelfSignedCertificate -Type Custom -Subject 'CN=seyra-gateway' -KeyAlgorithm ECDSA_nistP256 -HashAlgorithm SHA256 -KeyExportPolicy Exportable -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(2)
    try {
        [IO.File]::WriteAllBytes($pfxPath, $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,$password))
        [IO.File]::WriteAllText((Join-Path $tlsRoot 'gateway.pass'),$password)
    } finally { Remove-Item -LiteralPath $certificate.PSPath; $certificate.Dispose() }
    Write-Output ('Gateway certificate SHA-256: ' + (Get-Fingerprint))
    return
}
if ($Command -eq 'list') { $devices | Select-Object id,name,createdAt,revoked; return }
if ($Name -notmatch '\A[A-Za-z0-9_-]{1,64}\z') { throw 'Name must contain 1-64 ASCII letters, digits, underscores or hyphens.' }
if ($Command -eq 'revoke') {
    $matches = @($devices | Where-Object { $_.name -ceq $Name })
    if (-not $matches.Count) { throw 'Device not found.' }
    foreach ($device in $matches) { $device.revoked = $true }
    Save-Devices $devices
    Write-Output ('Revoked device: ' + $Name)
    return
}
$endpoint = $null
if (-not [Uri]::TryCreate($Url,[UriKind]::Absolute,[ref]$endpoint) -or $endpoint.Scheme -ne 'https' -or $endpoint.UserInfo -or $endpoint.AbsolutePath -ne '/' -or $endpoint.Query -or $endpoint.Fragment) { throw 'Url must be an HTTPS origin with no credentials, path, query or fragment.' }
if (@($devices | Where-Object { $_.name -ceq $Name -and -not $_.revoked }).Count) { throw 'An active device with that name exists. Revoke it before pairing again.' }
$token = New-RandomToken
$devices += [pscustomobject]@{ id=[Guid]::NewGuid().ToString(); name=$Name; tokenSha256=(Get-TokenHash $token); createdAt=[DateTime]::UtcNow.ToString('o'); revoked=$false }
$fingerprint = Get-Fingerprint
Save-Devices $devices
$pairRoot = Join-Path $dataRoot 'pairing'
New-Item -ItemType Directory -Force $pairRoot | Out-Null
$outputPath = Join-Path $pairRoot ($Name + '.env')
[IO.File]::WriteAllText($outputPath,"SEYRA_GATEWAY_URL=$($endpoint.GetLeftPart([UriPartial]::Authority))`nSEYRA_DEVICE_TOKEN=$token`nSEYRA_GATEWAY_CERT_SHA256=$fingerprint`n")
Write-Output ('Pairing file: ' + $outputPath)
Write-Output 'Copy this file to the device as persistentDataPath/.env.local, then delete the pairing file from this PC. Never copy Backend/.env.local.'
