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
            -not $text.Contains('仅支持 64 位')) {
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
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
