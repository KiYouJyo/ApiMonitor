#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PublishedOfflineZipPath,
    [string]$PackageVersion = '1.3.0.1'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$bootstrap = Join-Path $PSScriptRoot '..\..\packaging\oneclick\payload\Bootstrap.ps1'
. $bootstrap
$work = Join-Path $env:TEMP ('ApiMonitor-PS51-RealPublishedZIP-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work -Force | Out-Null
try {
    $null = Assert-OneClickZipLayout -ZipPath $PublishedOfflineZipPath -PackageVersion $PackageVersion
    $unpacked = Join-Path $work 'offline'
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    [IO.Compression.ZipFile]::ExtractToDirectory($PublishedOfflineZipPath, $unpacked)
    $offline = Join-Path $unpacked ("ApiMonitor_${PackageVersion}_x64_Test")
    $required = @('Install.ps1', 'Uninstall.ps1', 'SafeLocalStateBackup.ps1')
    $expectedChinese = [string]::Concat(([char[]]@(0x4EC5,0x652F,0x6301,0x20,0x36,0x34,0x20,0x4F4D)))

    # This assertion explicitly examines the *published ZIP* rather than
    # merely the current Git repository source files. The original v1.3.0.1
    # Test.zip was generated with UTF-8 WITHOUT BOM for versioned scripts.
    $installBytes = [IO.File]::ReadAllBytes((Join-Path $offline 'Install.ps1'))
    $originalHadBom = ($installBytes.Length -ge 3 -and
        $installBytes[0] -eq 0xEF -and $installBytes[1] -eq 0xBB -and $installBytes[2] -eq 0xBF)
    Write-Output "Source signed installer has UTF-8 BOM: $originalHadBom"
    Repair-ExtractedPowerShellEncoding -OfflineRoot $offline

    foreach ($name in $required) {
        $file = Join-Path $offline $name
        $bytes = [IO.File]::ReadAllBytes($file)
        if ($bytes.Length -lt 3 -or
            $bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) {
            throw "Expected UTF-8 BOM after repair for $name."
        }
        $tokens = $null
        $parseErrors = $null
        [void][Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$parseErrors)
        if ($parseErrors -and $parseErrors.Count -gt 0) {
            throw "REAL ZIP script $name cannot be parsed in Windows PowerShell 5.1: $($parseErrors[0].Message)"
        }
        $text = [IO.File]::ReadAllText($file)
        if ($name -eq 'Install.ps1' -and
            -not $text.Contains($expectedChinese)) {
            throw 'Windows PowerShell decoded the published Chinese installer text incorrectly.'
        }
        Write-Output "PASS: Windows PowerShell 5.1 parsed actual v$PackageVersion published script $name."
    }

    # Dot-source the actual published installer after repair. This must not
    # execute Invoke-Install (which requires elevation and can modify the PC).
    $installedScript = Join-Path $offline 'Install.ps1'
    . $installedScript -PackageVersion $PackageVersion -Quiet -NoLaunch
    foreach ($name in @('Assert-Prerequisites','Get-MsixManifestInfo',
        'Get-MsixSignerCertificate','Test-ThumbprintMatch','Assert-CertificatePolicy',
        'Read-Checksums','Assert-FileHashes','Resolve-PackageAction',
        'Resolve-DependencyPlan','Install-ApiMonitorPackage')) {
        if (-not (Get-Command $name -CommandType Function -ErrorAction SilentlyContinue)) {
            throw "Published offline installer is missing required function: $name"
        }
    }
    Write-Output 'PASS: all real published script functions loaded successfully in Windows PowerShell 5.1.'
    # Non-mutating live preflight of the EXACT signed MSIX shipped by GitHub.
    # This catches missing functions, parameter mismatches, signature/CER
    # extraction issues and checksum omissions before the end user sees them.
    $ops = Get-DefaultOps
    $preflight = Assert-Prerequisites $offline $ops
    if (-not $preflight.Ok) { throw "Real offline preflight failed: $($preflight.Reason)" }
    $manifest = Get-MsixManifestInfo $preflight.MsixPath
    $identity = Assert-ManifestIdentity $manifest 'ApiMonitor' 'CN=ApiMonitorDev' $PackageVersion
    if (-not $identity.Ok) { throw "Real package identity check failed: $($identity.Reason)" }
    $publisher = Get-MsixSignerCertificate $preflight.MsixPath
    $certFile = Join-Path $offline 'ApiMonitorDev.cer'
    $publicCert = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certFile)
    if (-not (Test-ThumbprintMatch $publisher $publicCert)) {
        throw 'Real signed MSIX and bundled CER have different publisher fingerprints.'
    }
    foreach ($cert in @($publisher, $publicCert)) {
        $policy = Assert-CertificatePolicy $cert
        if (-not $policy.Ok) { throw "Real signed certificate policy check failed: $($policy.Reason)" }
    }
    $publisherMatch = Assert-ManifestPublisherMatchesCert $manifest $publicCert
    if (-not $publisherMatch.Ok) { throw 'Real signed publisher identity differs from the certificate.' }
    $hashes = Read-Checksums (Join-Path $offline 'SHA256SUMS.txt')
    $integrity = Assert-FileHashes $hashes @(
        @{ Path = $preflight.MsixPath; Name = $preflight.MsixName },
        @{ Path = $certFile; Name = 'ApiMonitorDev.cer' }
    ) $ops
    if (-not $integrity.Ok) { throw "Real offline MSIX/CER checksum validation failed: $($integrity.Reason)" }
    $dependencies = Resolve-DependencyPlan $offline $ops
    if (-not $dependencies.Ok) { throw "Real bundled Windows App Runtime dependency failed: $($dependencies.Reason)" }
    Write-Output 'PASS: real GitHub Test.zip identity, signing certificate, SHA256 and runtime dependency preflight succeeded.'

}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
