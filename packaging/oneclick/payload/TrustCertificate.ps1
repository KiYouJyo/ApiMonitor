#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublicCertificatePath,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{40}$')][string]$ExpectedThumbprint
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

# Only certificate trust requires elevation. Do NOT use this helper to
# Add-AppxPackage: that would install the application for a different account
# when standard users supply separate administrator credentials for UAC.
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Certificate trust requires approval from an administrator.'
}
if (-not (Test-Path -LiteralPath $PublicCertificatePath -PathType Leaf)) {
    throw 'The expected public certificate is missing.'
}
$cert = [Security.Cryptography.X509Certificates.X509Certificate2]::new($PublicCertificatePath)
if ($cert.HasPrivateKey -or
    [string]$cert.Subject -cne 'CN=ApiMonitorDev' -or
    $cert.Thumbprint.ToUpperInvariant() -cne $ExpectedThumbprint.ToUpperInvariant() -or
    [DateTime]::Now -lt $cert.NotBefore -or [DateTime]::Now -gt $cert.NotAfter) {
    throw 'Public certificate identity, fingerprint or validity was rejected.'
}
$eku = @($cert.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' } | Select-Object -First 1)
if ($eku.Count -ne 1) { throw 'Code signing EKU is missing.' }
$ekuParsed = [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($eku[0], $false)
$allowCodeSigning = @($ekuParsed.EnhancedKeyUsages |
    Where-Object { $_.Value -ceq '1.3.6.1.5.5.7.3.3' })
if ($allowCodeSigning.Count -ne 1) {
    throw 'The public certificate does not have the required code-signing usage.'
}
$alreadyTrusted = @(Get-ChildItem -LiteralPath 'Cert:\LocalMachine\TrustedPeople' -ErrorAction SilentlyContinue |
    Where-Object { $_.Thumbprint -ceq $cert.Thumbprint })
if ($alreadyTrusted.Count -gt 0) {
    Write-Output 'The exact ApiMonitor publisher certificate is already trusted.'
    exit 0
}
$imported = Import-Certificate -FilePath $PublicCertificatePath -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' -ErrorAction Stop
if (-not $imported -or [string]$imported.Thumbprint -cne [string]$cert.Thumbprint) {
    throw 'Certificate import did not return the expected exact fingerprint.'
}
$confirmed = @(Get-ChildItem -LiteralPath 'Cert:\LocalMachine\TrustedPeople' |
    Where-Object { $_.Thumbprint -ceq $cert.Thumbprint })
if ($confirmed.Count -ne 1) { throw 'Certificate trust did not persist.' }
Write-Output 'Trusted only the exact ApiMonitor publisher in LocalMachine/TrustedPeople.'
