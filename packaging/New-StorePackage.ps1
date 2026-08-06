[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceCommit,
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string]$PackageVersion,
    [ValidateSet('Release')][string]$Configuration = 'Release',
    [ValidateSet('x64')][string]$Platform = 'x64',
    [string]$OutputDirectory = '',
    [switch]$CreateLocalTestPackage,
    [string]$MsBuildPath,
    [string]$CertificateThumbprint = '545198E3BC78BE49BDF861C3EA6863FFD285689F'
)

# v1.0.0: Builds the final Microsoft Store candidate (.msixupload) from an
# isolated worktree using the official Partner Center identity. The Store
# package is unsigned (Partner Center re-signs); a separate local-acceptance
# MSIX can be signed with the development certificate for on-device testing
# only and is never the Store upload artifact.
#
# Hard rules:
#   - PackageVersion must be exactly 1.0.0.0 (never reused for different bits).
#   - The Store output directory is packaging\output\v1.0.0\store and must never
#     contain sideload tools, ApiMonitorDev.cer, PFX/private keys, logs, or
#     user data.
#   - Nothing is uploaded to Partner Center by this script.

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot 'packaging\output\v1.0.0\store'
}
$output = [IO.Path]::GetFullPath($OutputDirectory)

if ($PackageVersion -ne '1.0.0.0') {
    throw "This v1.0.0 workflow only accepts Store package version 1.0.0.0. Got: $PackageVersion"
}

if ([IO.Path]::GetFullPath($repoRoot) -eq $output) {
    throw 'Store package output must not be the repository root.'
}

if (Test-Path -LiteralPath $output) {
    if (@(Get-ChildItem -LiteralPath $output -Force -ErrorAction SilentlyContinue).Count -gt 0) {
        throw "Store package output directory must be new or empty: $output"
    }
}

$sourceCommitResolved = (& git -C $repoRoot rev-parse --verify "$SourceCommit^{commit}").Trim()
if ($LASTEXITCODE -ne 0) { throw "Source commit does not exist: $SourceCommit" }
$headCommit = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($headCommit -ne $sourceCommitResolved) {
    throw "SourceCommit must be the current HEAD. HEAD=$headCommit SourceCommit=$sourceCommitResolved"
}
$workingTreeState = & git -C $repoRoot status --porcelain
if ($LASTEXITCODE -ne 0 -or $workingTreeState) {
    throw 'The source working tree must be clean before a Store package build.'
}

# ---- 1. Validate the Store manifest identity ----
$manifestPath = Join-Path $repoRoot 'Package.Store.appxmanifest'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Store manifest not found: $manifestPath"
}
[xml]$manifest = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($manifestPath))
$identity = $manifest.Package.Identity
$expectedName = 'JoKiy.ApiMonitor'
$expectedPublisher = 'CN=C4E4B33A-7B77-4121-897C-7D720A5471F8'
if ($identity.Name -ne $expectedName -or $identity.Publisher -ne $expectedPublisher -or $identity.Version -ne $PackageVersion) {
    throw "Store manifest identity, publisher, or version is invalid: Name=$($identity.Name) Publisher=$($identity.Publisher) Version=$($identity.Version)"
}
$expectedDisplayName = 'Jo Kiy' + [char]333
if ($manifest.Package.Properties.PublisherDisplayName -cne $expectedDisplayName) {
    throw 'Store publisher display name is invalid.'
}

# ---- 2. Locate MSBuild ----
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

# ---- 3. Build the Store upload package in an isolated worktree ----
New-Item -ItemType Directory -Force -Path $output | Out-Null
$temporaryWorktree = Join-Path ([IO.Path]::GetTempPath()) ('ApiMonitor-store-' + [Guid]::NewGuid().ToString('N'))
$worktreeAdded = $false
try {
    & git -C $repoRoot worktree add --detach $temporaryWorktree $sourceCommitResolved
    if ($LASTEXITCODE -ne 0) { throw 'Unable to create the isolated Store build worktree.' }
    $worktreeAdded = $true

    # Store identity manifest replaces the sideload manifest in the isolated tree only.
    Copy-Item -LiteralPath (Join-Path $temporaryWorktree 'Package.Store.appxmanifest') `
        -Destination (Join-Path $temporaryWorktree 'Package.appxmanifest') -Force

    $packageDirectory = Join-Path $output 'AppPackages'
    & dotnet restore (Join-Path $temporaryWorktree 'ApiMonitor.slnx') "-p:Configuration=$Configuration" "-p:Platform=$Platform"
    if ($LASTEXITCODE -ne 0) { throw 'Restore in the isolated Store build worktree failed.' }

    & $MsBuildPath (Join-Path $temporaryWorktree 'ApiMonitor.csproj') /t:Build /m `
        "/p:Configuration=$Configuration" "/p:Platform=$Platform" `
        /p:DistributionChannel=MicrosoftStore /p:RuntimeIdentifier=win-x64 `
        /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=false `
        /p:AppxBundle=Never /p:UapAppxPackageBuildMode=StoreUpload `
        "/p:AppxPackageDir=$packageDirectory\\" /p:Restore=false
    if ($LASTEXITCODE -ne 0) { throw 'Store package build failed.' }

    $upload = @(Get-ChildItem -LiteralPath $packageDirectory -Recurse -Filter '*.msixupload' -File)
    if ($upload.Count -ne 1) {
        throw "Expected exactly one .msixupload; found $($upload.Count)."
    }

    # ---- 4. Validate identity/version/contents ----
    $validation = (& (Join-Path $PSScriptRoot 'Test-StorePackageIdentity.ps1') `
        -PackagePath $upload[0].FullName `
        -ExpectedIdentityName $expectedName `
        -ExpectedPublisher $expectedPublisher `
        -ExpectedVersion $PackageVersion `
        -OutputDirectory (Join-Path $output 'validation') | Out-String | ConvertFrom-Json)
    if ($LASTEXITCODE -ne 0 -or -not $validation) { throw 'Store package identity validation failed.' }

    $sensitive = Get-ChildItem -LiteralPath $output -Recurse -File |
        Where-Object { $_.Extension -in '.pfx', '.p12', '.cer', '.key', '.pvk', '.pem', '.log' }
    if ($sensitive) {
        throw "Sensitive file found in Store output: $($sensitive.FullName -join ', ')"
    }

    # ---- 5. Record report + checksums ----
    $hash = (Get-FileHash -LiteralPath $upload[0].FullName -Algorithm SHA256).Hash.ToUpperInvariant()
    $report = [ordered]@{
        sourceCommit = $sourceCommitResolved
        packageVersion = $PackageVersion
        package = $upload[0].FullName
        sha256 = $hash
        channel = 'MicrosoftStore'
        signed = $false
        manifestIdentity = $expectedName
        manifestPublisher = $expectedPublisher
        packageFamilyName = 'JoKiy.ApiMonitor_4wdwgytaw3v2m'
        storeProductId = '9N6KR2XFMKQ2'
        architecture = $validation.Architecture
        languages = @($validation.Languages)
        capabilities = @($validation.Capabilities)
        forbiddenFilesFound = @($validation.ForbiddenFiles)
        validationResult = $validation.ValidationResult
        buildUtc = [DateTime]::UtcNow.ToString('O')
    }
    $report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'store-package-build.json') -Encoding UTF8

    $upload | ForEach-Object {
        ('{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToUpperInvariant(), $_.Name)
    } | Set-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt') -Encoding UTF8

    Write-Output "MSIXUPLOAD=$($upload[0].FullName)"
    Write-Output "SHA256=$hash"
    Write-Output "STORE_MANIFEST_IDENTITY=$($validation.ManifestIdentity)"
    Write-Output "STORE_PACKAGE_FAMILY=$($validation.PackageFamilyName)"
    Write-Output "STORE_LANGUAGES=$($validation.Languages -join ',')"
    Write-Output "STORE_ARCHITECTURE=$($validation.Architecture)"

    # ---- 6. Optional local-acceptance package (dev-signed, never uploaded) ----
    if ($CreateLocalTestPackage) {
        $localDir = Join-Path $output 'local-test'
        New-Item -ItemType Directory -Force -Path $localDir | Out-Null
        $localMsix = Join-Path $localDir ('ApiMonitor_{0}_x64_local-acceptance.msix' -f $PackageVersion)

        # The local acceptance package is built with the same msbuild packaging
        # pipeline as the sideload candidate (which signs from the store cert),
        # keeping the official Store identity but adding the dev signature so it
        # can be installed on this machine for manual acceptance.
        $localAppPackages = Join-Path $localDir 'AppPackages'
        & $MsBuildPath (Join-Path $temporaryWorktree 'ApiMonitor.csproj') /t:Build /m `
            "/p:Configuration=$Configuration" "/p:Platform=$Platform" `
            /p:DistributionChannel=MicrosoftStore /p:RuntimeIdentifier=win-x64 `
            /p:GenerateAppxPackageOnBuild=true /p:AppxPackageSigningEnabled=true `
            "/p:PackageCertificateThumbprint=$CertificateThumbprint" `
            /p:AppxBundle=Never "/p:AppxPackageDir=$localAppPackages\\" /p:Restore=false
        if ($LASTEXITCODE -ne 0) { throw 'Local acceptance package build failed.' }

        $signedMsix = @(Get-ChildItem -LiteralPath $localAppPackages -Recurse -Filter '*.msix' -File |
            Where-Object {
                $_.FullName -notmatch '(?i)\\Dependencies\\' -and
                $_.FullName -notmatch '(?i)Add-AppDevPackage' -and
                $_.Name -match '^ApiMonitor_.*_x64\.msix$'
            })
        if ($signedMsix.Count -ne 1) {
            throw "Expected exactly one signed local-acceptance .msix; found $($signedMsix.Count)."
        }
        Copy-Item -LiteralPath $signedMsix[0].FullName -Destination $localMsix -Force

        $localHash = (Get-FileHash -LiteralPath $localMsix -Algorithm SHA256).Hash.ToUpperInvariant()
        $readme = @(
            'LOCAL ACCEPTANCE ONLY - NOT A STORE UPLOAD ARTIFACT',
            '',
            'This MSIX uses the official Microsoft Store identity (JoKiy.ApiMonitor)',
            "with the official Publisher (CN=C4E4B33A-7B77-4121-897C-7D720A5471F8) and is signed",
            "by a locally generated test certificate whose subject matches that publisher",
            "(thumbprint $CertificateThumbprint) for on-device manual acceptance only.",
            '',
            'The Store upload artifact is the unsigned .msixupload in the parent directory.',
            'The test certificate is NOT the Microsoft publisher certificate and must never',
            'be exported to or used by any other machine or submission.',
            '',
            "SHA-256: $localHash",
            'Installing this package replaces the GitHub sideload identity on this machine.',
            'Old sideload accounts, history, settings and credentials are NOT migrated.'
        )
        $readme | Set-Content -LiteralPath (Join-Path $localDir 'README.txt') -Encoding UTF8
        Write-Output "LOCAL_ACCEPTANCE_MSIX=$localMsix"
        Write-Output "LOCAL_ACCEPTANCE_SHA256=$localHash"
    }
}
finally {
    if ($worktreeAdded) { & git -C $repoRoot worktree remove --force $temporaryWorktree | Out-Null }
    elseif (Test-Path -LiteralPath $temporaryWorktree) { Remove-Item -LiteralPath $temporaryWorktree -Recurse -Force }
}
