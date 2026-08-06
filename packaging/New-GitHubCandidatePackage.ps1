[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^1\.0\.0\.\d+$')][string]$PackageVersion,
    [ValidateSet('Release')][string]$Configuration = 'Release',
    [ValidateSet('x64')][string]$Platform = 'x64',
    [string]$OutputDirectory = '',
    [string]$MsBuildPath,
    [string]$CertificateThumbprint = '545198E3BC78BE49BDF861C3EA6863FFD285689F'
)

# v1.0.0: Builds a signed GitHub sideload candidate (1.0.0.x) and assembles
# the installer folder + Test.zip + SHA256SUMS.txt via
# New-PreviewInstallerPackage.ps1. Output stays under
# packaging\output\v1.0.0\github and is separate from the Store output.

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot 'packaging\output\v1.0.0\github'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)

$workingTreeState = & git -C $repoRoot status --porcelain
if ($LASTEXITCODE -ne 0 -or $workingTreeState) {
    throw 'The source working tree must be clean before a GitHub candidate build.'
}

if (-not $MsBuildPath) {
    $vswhere = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) { throw "vswhere.exe was not found: $vswhere" }
    $vsInstall = (& $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -property installationPath).Trim()
    if (-not $vsInstall) { throw 'Visual Studio with MSBuild was not found.' }
    $MsBuildPath = Join-Path $vsInstall 'MSBuild\Current\Bin\amd64\MSBuild.exe'
}
if (-not (Test-Path -LiteralPath $MsBuildPath -PathType Leaf)) {
    throw "MSBuild.exe was not found: $MsBuildPath"
}

New-Item -ItemType Directory -Force -Path $output | Out-Null
$buildDir = Join-Path $output 'build'
New-Item -ItemType Directory -Force -Path $buildDir | Out-Null

& dotnet restore (Join-Path $repoRoot 'ApiMonitor.slnx') "-p:Configuration=$Configuration" "-p:Platform=$Platform"
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }

& $MsBuildPath (Join-Path $repoRoot 'ApiMonitor.csproj') /t:Build /m `
    "/p:Configuration=$Configuration" "/p:Platform=$Platform" `
    /p:DistributionChannel=GitHubSideload /p:RuntimeIdentifier=win-x64 `
    /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=true `
    "/p:PackageCertificateThumbprint=$CertificateThumbprint" `
    /p:AppxBundle=Never "/p:AppxPackageDir=$buildDir\\" /p:Restore=false
if ($LASTEXITCODE -ne 0) { throw 'GitHub sideload MSIX build failed.' }

$msix = @(Get-ChildItem -LiteralPath $buildDir -Recurse -Filter '*.msix' -File |
    Where-Object {
        $_.FullName -notmatch '(?i)\\Dependencies\\' -and
        $_.FullName -notmatch '(?i)Add-AppDevPackage' -and
        $_.Name -match '^ApiMonitor_.*_x64\.msix$'
    })
if ($msix.Count -ne 1) {
    throw "Expected exactly one .msix; found $($msix.Count)."
}

# Verify package version and identity.
$versionOk = $msix[0].Name -match ([regex]::Escape($PackageVersion))
if (-not $versionOk) {
    throw "Built MSIX version does not match $PackageVersion : $($msix[0].Name)"
}

& (Join-Path $PSScriptRoot 'New-PreviewInstallerPackage.ps1') `
    -Version $PackageVersion `
    -MsixPath $msix[0].FullName `
    -CertificateThumbprint $CertificateThumbprint `
    -OutputDirectory $output
if ($LASTEXITCODE -ne 0) { throw 'Installer package assembly failed.' }

$zip = Get-ChildItem -LiteralPath $output -Filter 'ApiMonitor_*_x64_Test.zip' -File |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $zip) { throw 'Test.zip was not created.' }

$report = [ordered]@{
    packageVersion = $PackageVersion
    channel = 'GitHubSideload'
    msix = $msix[0].FullName
    msixSha256 = (Get-FileHash -LiteralPath $msix[0].FullName -Algorithm SHA256).Hash.ToUpperInvariant()
    zip = $zip.FullName
    zipSha256 = (Get-FileHash -LiteralPath $zip.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
    certificateThumbprint = $CertificateThumbprint
    buildUtc = [DateTime]::UtcNow.ToString('O')
}
$report | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $output 'github-candidate-build.json') -Encoding UTF8

Write-Output "GITHUB_MSIX=$($msix[0].FullName)"
Write-Output "GITHUB_ZIP=$($zip.FullName)"
Write-Output "GITHUB_CANDIDATE_VERSION=$PackageVersion"
