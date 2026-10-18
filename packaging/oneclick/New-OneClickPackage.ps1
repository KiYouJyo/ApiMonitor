#requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$DisplayVersion,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$PackageVersion,
    [Parameter(Mandatory)][string]$PublishedOfflineZipPath,
    [Parameter(Mandatory)][ValidatePattern('^[A-Fa-f0-9]{64}$')][string]$PublishedOfflineZipSha256,
    [Parameter(Mandatory)][string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not $PackageVersion.StartsWith(($DisplayVersion + '.'), [StringComparison]::Ordinal)) {
    throw 'The installer package and display versions do not agree.'
}
$offlineAssetName = 'ApiMonitor_' + $PackageVersion + '_x64_Test.zip'
$zip = Get-Item -LiteralPath $PublishedOfflineZipPath -ErrorAction Stop
if ($zip.Name -cne $offlineAssetName) {
    throw 'The existing signed offline ZIP has a different version/name.'
}
$sha = (Get-FileHash -LiteralPath $zip.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
if ($sha -cne $PublishedOfflineZipSha256.ToUpperInvariant()) {
    throw 'The existing published offline installer checksum is invalid.'
}

$templates = $PSScriptRoot
$builderRoot = (Resolve-Path (Join-Path $templates '..')).Path
$destination = [IO.Path]::GetFullPath($OutputDirectory)
$sourcePrefix = $builderRoot.TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
if ($destination.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase) -or
    $destination -ceq $builderRoot) {
    throw 'Generated installers cannot overwrite their own source templates.'
}
$rootName = "ApiMonitor-v$DisplayVersion-x64-one-click"
$root = Join-Path $destination $rootName
$payload = Join-Path $root 'payload'
$archivePath = Join-Path $destination ($rootName + '.zip')
if ((Test-Path -LiteralPath $root) -or (Test-Path -LiteralPath $archivePath)) {
    throw 'The one-click artifact already exists. Never overwrite a published release asset.'
}
New-Item -ItemType Directory -Path $payload -Force | Out-Null
try {
    foreach ($name in @('1-Install-ApiMonitor.cmd','2-Uninstall-ApiMonitor.cmd','README.txt')) {
        $src = Join-Path $templates $name
        if (-not (Test-Path -LiteralPath $src -PathType Leaf)) { throw "Missing installer template: $name" }
        if ($name -eq 'README.txt') {
            $text = (Get-Content -LiteralPath $src -Raw -Encoding UTF8).
                Replace('{{DISPLAY_VERSION}}',$DisplayVersion).
                Replace('{{PACKAGE_VERSION}}',$PackageVersion)
            [IO.File]::WriteAllText((Join-Path $root $name),$text,[Text.UTF8Encoding]::new($true))
        }
        else { Copy-Item -LiteralPath $src -Destination (Join-Path $root $name) }
    }
    $scriptPath = Join-Path $templates 'payload\Bootstrap.ps1'
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
        throw 'Missing one-click payload script.'
    }
    # UTF-8 BOM ensures compatibility with Windows PowerShell 5.1 and Japanese paths.
    $sourceScript = Get-Content -LiteralPath $scriptPath -Raw -Encoding UTF8
    [IO.File]::WriteAllText((Join-Path $payload 'Bootstrap.ps1'),$sourceScript,[Text.UTF8Encoding]::new($true))
    $metadata = [ordered]@{
        schemaVersion=1
        repository='KiYouJyo/ApiMonitor'
        displayVersion=$DisplayVersion
        packageVersion=$PackageVersion
        releaseTag="v$DisplayVersion"
        packageName='ApiMonitor'
        publisher='CN=ApiMonitorDev'
        architecture='x64'
        offlineZipName=$offlineAssetName
        offlineZipSha256=$sha
    }
    $metadata | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $payload 'InstallerMetadata.json') -Encoding UTF8

    $rootFiles = @(Get-ChildItem -LiteralPath $root -File | Select-Object -ExpandProperty Name | Sort-Object)
    if (($rootFiles -join ',') -cne ((@('1-Install-ApiMonitor.cmd','2-Uninstall-ApiMonitor.cmd','README.txt' | Sort-Object)) -join ',')) {
        throw 'Unexpected root files in one-click ZIP.'
    }
    if (@(Get-ChildItem -LiteralPath $root -Recurse -File |
        Where-Object { $_.Extension -in @('.msix','.msixbundle','.appinstaller','.pfx','.p12','.key') }).Count -ne 0) {
        throw 'One-click payload must never contain packages or private keys.'
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    [IO.Compression.ZipFile]::CreateFromDirectory($root, $archivePath, [IO.Compression.CompressionLevel]::Optimal, $true)
    $created = Get-Item -LiteralPath $archivePath
    if ($created.Length -gt 2MB) { throw 'One-click bootstrap unexpectedly contains large files.' }
    Write-Output "ONECLICK_ZIP=$($created.FullName)"
    Write-Output "ONECLICK_SHA256=$((Get-FileHash -LiteralPath $created.FullName -Algorithm SHA256).Hash.ToUpperInvariant())"
}
catch {
    Remove-Item -LiteralPath $root -Force -Recurse -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
    throw
}
