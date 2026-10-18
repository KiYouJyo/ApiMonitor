#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Install', 'Uninstall')][string]$Action = 'Install',
    [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

function Test-SafeOneClickMetadata {
    param($Metadata)
    if (-not $Metadata -or
        [string]$Metadata.repository -cne 'KiYouJyo/ApiMonitor' -or
        [string]$Metadata.publisher -cne 'CN=ApiMonitorDev' -or
        [string]$Metadata.packageName -cne 'ApiMonitor' -or
        [string]$Metadata.architecture -cne 'x64' -or
        [string]$Metadata.displayVersion -cnotmatch '^\d+\.\d+\.\d+$' -or
        [string]$Metadata.packageVersion -cnotmatch '^\d+\.\d+\.\d+\.\d+$' -or
        [string]$Metadata.releaseTag -cne ('v' + [string]$Metadata.displayVersion) -or
        -not ([string]$Metadata.packageVersion).StartsWith(([string]$Metadata.displayVersion + '.'), [StringComparison]::Ordinal) -or
        [string]$Metadata.offlineZipName -cne ('ApiMonitor_' + [string]$Metadata.packageVersion + '_x64_Test.zip') -or
        [string]$Metadata.offlineZipSha256 -cnotmatch '^[A-Fa-f0-9]{64}$') {
        throw 'The one-click installer metadata does not match the ApiMonitor release identity.'
    }
    return $true
}

function Assert-OneClickZipLayout {
    param([Parameter(Mandatory)][string]$ZipPath,
          [Parameter(Mandatory)][string]$PackageVersion)
    Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    $expectedRoot = 'ApiMonitor_' + $PackageVersion + '_x64_Test'
    $required = @('Install.ps1', 'Uninstall.ps1', 'ApiMonitorDev.cer',
                  'SHA256SUMS.txt', 'SafeLocalStateBackup.ps1',
                  ('ApiMonitor_' + $PackageVersion + '_x64.msix'),
                  'Dependencies/x64/Microsoft.WindowsAppRuntime.2.msix')
    $present = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    $archive = [IO.Compression.ZipFile]::OpenRead($ZipPath)
    try {
        if ($archive.Entries.Count -gt 1600) { throw 'Excessive ZIP entry count.' }
        [long]$size = 0
        foreach ($entry in $archive.Entries) {
            $name = $entry.FullName.Replace('\','/')
            if ([string]::IsNullOrWhiteSpace($name) -or $name.StartsWith('/') -or
                $name.Contains(':') -or $name.Contains([char]0) -or
                $name.Contains('//') -or
                @($name.TrimEnd('/').Split('/') | Where-Object { $_ -in @('.', '..', '') }).Count -gt 0) {
                throw "Unsafe ZIP entry: $name"
            }
            if (-not ($name -ceq ($expectedRoot + '/') -or
                $name.StartsWith(($expectedRoot + '/'), [StringComparison]::Ordinal))) {
                throw 'The archive contains unexpected top-level folders.'
            }
            $size += $entry.Length
            if ($size -gt 600MB) { throw 'Archive expanded size exceeds the expected limit.' }
            [void]$present.Add($name)
        }
        foreach ($name in $required) {
            if (-not $present.Contains($expectedRoot + '/' + $name)) {
                throw "Required offline installer payload is missing: $name"
            }
        }
    }
    finally { $archive.Dispose() }
    return $true
}

function Repair-ExtractedPowerShellEncoding {
    <#
      The v1.3.0.1 offline ZIP contains UTF-8 (without BOM) .ps1 scripts.
      Windows PowerShell 5.1 reads those scripts using the active ANSI codepage
      when no BOM exists. Chinese punctuation is corrupted and causes syntax
      errors before any installer code can run. Preserve the immutable ZIP's
      hash/signature; normalize only extracted, private temp script copies.
    #>
    param([Parameter(Mandatory)][string]$OfflineRoot)
    $strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
    $utf8Bom = [Text.UTF8Encoding]::new($true, $true)
    foreach ($name in @('Install.ps1', 'Uninstall.ps1', 'SafeLocalStateBackup.ps1')) {
        $path = Join-Path $OfflineRoot $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Required PowerShell installer script is missing: $name"
        }
        $rawBytes = [IO.File]::ReadAllBytes($path)
        # Fail closed on invalid UTF-8. Do not rely on the local ANSI codepage
        # or silently replace undecodable characters.
        $unicode = $strictUtf8.GetString($rawBytes).TrimStart([char]0xFEFF)
        if ([string]::IsNullOrWhiteSpace($unicode)) {
            throw "Installer script is empty: $name"
        }
        [IO.File]::WriteAllText($path, $unicode, $utf8Bom)

        # This is executed under Windows PowerShell 5.1 in the published .cmd
        # launcher, so ParseFile validates the exact file decoding/runtime.
        $tokens = $null
        $errors = $null
        [void][Management.Automation.Language.Parser]::ParseFile(
            $path, [ref]$tokens, [ref]$errors)
        if ($errors -and $errors.Count -gt 0) {
            $first = $errors[0]
            throw "Installer script '$name' failed Windows PowerShell parsing at line $($first.Extent.StartLineNumber): $($first.Message)"
        }
    }
    Write-Host 'PowerShell 5.1 UTF-8/BOM compatibility and script syntax verified.'
}

function Invoke-ReliableOneClickDownload {
    param([Parameter(Mandatory)][string]$Uri,
          [Parameter(Mandatory)][string]$Destination)
    $delays = @(2,5,10)
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        if (Test-Path -LiteralPath $Destination) {
            Remove-Item -LiteralPath $Destination -Force -ErrorAction SilentlyContinue
        }
        try {
            Invoke-WebRequest -Uri $Uri -UseBasicParsing -OutFile $Destination -TimeoutSec 600 -ErrorAction Stop
            if ((Get-Item -LiteralPath $Destination).Length -lt 1024) { throw 'Unexpectedly short download.' }
            return
        }
        catch {
            if ($attempt -eq 3) { throw }
            Start-Sleep -Seconds $delays[$attempt - 1]
        }
    }
}

function Invoke-OfflineUninstall {
    # Uninstall is deliberately local. It never asks for a Release,
    # downloads an offline ZIP, or executes downloaded code.
    $matches = @(Get-AppxPackage -Name 'ApiMonitor' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ceq 'ApiMonitor' -and $_.Publisher -ceq 'CN=ApiMonitorDev' })
    if ($matches.Count -eq 0) {
        Write-Output 'ApiMonitor GitHub sideload edition is not installed for this Windows user.'
        return 0
    }
    if ($matches.Count -ne 1) { throw 'Multiple matching ApiMonitor packages; aborting ambiguous removal.' }
    $pkg = $matches[0]
    Write-Warning "Uninstall removes this Windows user's ApiMonitor sideload package. Package-local accounts, settings and history may be lost. Microsoft Store installations will not be touched."
    $answer = Read-Host ('Uninstall ApiMonitor {0} for this user? Type YES to continue' -f $pkg.Version)
    if ($answer -cne 'YES') {
        Write-Output 'Uninstall cancelled without changing apps or certificates.'
        return 0
    }
    Remove-AppxPackage -Package $pkg.PackageFullName -ErrorAction Stop
    $remaining = @(Get-AppxPackage -Name 'ApiMonitor' -ErrorAction SilentlyContinue |
        Where-Object { $_.PackageFullName -ceq $pkg.PackageFullName })
    if ($remaining.Count -gt 0) { throw 'Windows still reports this ApiMonitor package as installed.' }
    Write-Output 'ApiMonitor GitHub package uninstalled for the current user.'
    Write-Output 'Shared Windows App Runtime and certificate trust were not removed.'
    return 0
}

function Install-VerifiedOfflinePackageForCurrentUser {
    param([Parameter(Mandatory)][string]$OfflineRoot,
          [Parameter(Mandatory)][string]$PackageVersion)

    $installFile = Join-Path $OfflineRoot 'Install.ps1'
    if (-not (Test-Path -LiteralPath $installFile -PathType Leaf)) {
        throw 'The verified offline installer is missing its security validation functions.'
    }
    # Reuse the existing, tested manifest, checksum, CMS signing, certificate
    # policy and dependency checks, but do not invoke its blanket UAC elevation.
    # A standard Windows user can supply a different admin's credentials; a
    # full elevated Add-AppxPackage would then install to the WRONG account.
    . $installFile -PackageVersion $PackageVersion -NoLaunch -Quiet

    $ops = Get-DefaultOps
    $pre = Assert-Prerequisites $OfflineRoot $ops
    if (-not $pre.Ok) { throw "Package prerequisites failed: $($pre.Reason)" }
    $manifest = Get-MsixManifestInfo $pre.MsixPath
    $identity = Assert-ManifestIdentity $manifest 'ApiMonitor' 'CN=ApiMonitorDev' $PackageVersion
    if (-not $identity.Ok) { throw "Package identity mismatch: $($identity.Reason)" }
    $certificatePath = Join-Path $OfflineRoot 'ApiMonitorDev.cer'
    $publicCert = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificatePath)
    $msixSigner = Get-MsixSignerCertificate $pre.MsixPath
    if (-not (Test-ThumbprintMatch $msixSigner $publicCert)) {
        throw 'MSIX signature certificate does not match the published certificate.'
    }
    foreach ($cert in @($msixSigner, $publicCert)) {
        $policy = Assert-CertificatePolicy $cert
        if (-not $policy.Ok) { throw "Signing certificate policy failure: $($policy.Reason)" }
    }
    $publisherCheck = Assert-ManifestPublisherMatchesCert $manifest $publicCert
    if (-not $publisherCheck.Ok) { throw "Publisher mismatch: $($publisherCheck.Reason)" }
    $checksums = Read-Checksums (Join-Path $OfflineRoot 'SHA256SUMS.txt')
    $integrity = Assert-FileHashes $checksums @(
        @{ Path = $pre.MsixPath; Name = $pre.MsixName },
        @{ Path = $certificatePath; Name = 'ApiMonitorDev.cer' }
    ) $ops
    if (-not $integrity.Ok) { throw "Signed payload SHA-256 verification failed: $($integrity.Reason)" }
    Write-Host "Validated offline MSIX and public signer: $($publicCert.Thumbprint)."

    $existing = @(Get-AppxPackage -Name 'ApiMonitor' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ceq 'ApiMonitor' } | Select-Object -First 1)
    $installed = if ($existing.Count -eq 0) { $null } else { $existing[0] }
    $action = Resolve-PackageAction $installed $manifest.Version $manifest.Publisher
    switch ($action) {
        'Conflict' { throw 'An ApiMonitor package with another publisher already exists.' }
        'HigherVersionInstalled' { throw 'A newer ApiMonitor sideload version is already installed.' }
        'SameVersion' {
            Write-Host "ApiMonitor $PackageVersion is already installed. Nothing to change."
            return $installed
        }
        'Install' { }
        'Upgrade' { }
        default { throw "Unexpected package installation action: $action" }
    }

    $thumbprint = $publicCert.Thumbprint.ToUpperInvariant()
    $trusted = @(Get-ChildItem -LiteralPath 'Cert:\LocalMachine\TrustedPeople' -ErrorAction SilentlyContinue |
        Where-Object { $_.Thumbprint -ceq $thumbprint })
    if ($trusted.Count -eq 0) {
        $elevationScript = Join-Path $PSScriptRoot 'TrustCertificate.ps1'
        if (-not (Test-Path -LiteralPath $elevationScript -PathType Leaf)) {
            throw 'The certificate trust helper is missing from this one-click ZIP.'
        }
        Write-Host 'Requesting Windows UAC only to trust the verified public publisher certificate...'
        $args = '-NoLogo -NoProfile -ExecutionPolicy Bypass -File "{0}" -PublicCertificatePath "{1}" -ExpectedThumbprint {2}' -f $elevationScript, $certificatePath, $thumbprint
        try {
            $child = Start-Process -FilePath 'powershell.exe' -ArgumentList $args -Verb RunAs -Wait -PassThru -ErrorAction Stop
        }
        catch {
            throw "Certificate trust approval cancelled or failed: $($_.Exception.Message)"
        }
        if ($child.ExitCode -ne 0) {
            throw "Certificate trust helper exited with $($child.ExitCode). Installation has not started. Check the UAC window output."
        }
        $trusted = @(Get-ChildItem -LiteralPath 'Cert:\LocalMachine\TrustedPeople' -ErrorAction SilentlyContinue |
            Where-Object { $_.Thumbprint -ceq $thumbprint })
        if ($trusted.Count -ne 1) { throw 'Expected certificate trust was not installed successfully.' }
    }

    $authenticode = Get-AuthenticodeSignature -FilePath $pre.MsixPath
    if (-not $authenticode.SignerCertificate -or
        $authenticode.SignerCertificate.Thumbprint -cne $thumbprint) {
        throw 'The MSIX Authenticode signer does not match the verified published certificate.'
    }
    # A self-signed but otherwise intact MSIX can be reported as UnknownError
    # by Authenticode on Windows, even when TrustedPeople contains the signer.
    # Never allow hash/signature mismatch; Windows Add-AppxPackage still performs
    # mandatory cryptographic package signature checks before installation.
    if ($authenticode.Status -notin @('Valid', 'UnknownError')) {
        throw "Windows rejected the signed MSIX: $($authenticode.Status) ($($authenticode.StatusMessage))"
    }
    if ($authenticode.Status -eq 'UnknownError') {
        Write-Warning 'Windows could not build a full trust chain for this self-signed MSIX; continuing only with the pinned SHA-256, exact signer and Windows package deployment verification.'
    }
    $plan = Resolve-DependencyPlan $OfflineRoot $ops
    if (-not $plan.Ok) { throw "Windows App Runtime dependency unavailable: $($plan.Reason)" }

    # Crucially this Add-AppxPackage runs in the original logged-in user
    # session, not in the elevated admin's potentially different account.
    Write-Host "Installing ApiMonitor $PackageVersion for the current Windows account..."
    $result = Install-ApiMonitorPackage $pre.MsixPath $plan.Needed $ops
    if (-not $result.Ok) { throw "MSIX installation failed: $($result.Reason)" }
    $after = @(Get-AppxPackage -Name 'ApiMonitor' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -ceq 'ApiMonitor' -and $_.Publisher -ceq 'CN=ApiMonitorDev' })
    if ($after.Count -ne 1 -or [string]$after[0].Version -cne $PackageVersion -or
        [string]$after[0].Status -cne 'Ok') {
        throw 'Installed MSIX identity, version or state differs from the verified offline package.'
    }
    return $after[0]
}

function Invoke-OneClickBootstrap {
    param([ValidateSet('Install','Uninstall')][string]$Operation,
          [switch]$SuppressLaunch)
    if ($Operation -eq 'Uninstall') {
        return Invoke-OfflineUninstall
    }

    $metaPath = Join-Path $PSScriptRoot 'InstallerMetadata.json'
    if (-not (Test-Path -LiteralPath $metaPath -PathType Leaf)) { throw 'The installer metadata is missing.' }
    $meta = Get-Content -LiteralPath $metaPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $null = Test-SafeOneClickMetadata $meta
    if (-not [Environment]::Is64BitOperatingSystem -or [Environment]::OSVersion.Version.Build -lt 17763) {
        throw 'ApiMonitor requires x64 Windows 10 1809 or newer.'
    }

    # Rerunning the same installer must never trigger a destructive reinstall.
    if ($Operation -eq 'Install') {
        $existing = @(Get-AppxPackage -Name 'ApiMonitor' -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -ceq 'ApiMonitor' }) | Select-Object -First 1
        if ($existing -and [string]$existing.Publisher -cne 'CN=ApiMonitorDev') {
            throw 'An incompatible ApiMonitor package is already installed.'
        }
        if ($existing -and ([version]$existing.Version -ge [version]$meta.packageVersion)) {
            Write-Output "ApiMonitor is already installed (version $($existing.Version)). No action is required."
            return 0
        }
    }

    $work = Join-Path ([IO.Path]::GetTempPath()) ('ApiMonitor-OneClick-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    try {
        $zip = Join-Path $work ([string]$meta.offlineZipName)
        $tag = [string]$meta.releaseTag
        $releaseUri = 'https://api.github.com/repos/KiYouJyo/ApiMonitor/releases/tags/' + $tag
        $headers = @{ Accept = 'application/vnd.github+json'; 'User-Agent' = 'ApiMonitor-OneClick/' + [string]$meta.displayVersion }
        $release = Invoke-RestMethod -Uri $releaseUri -Headers $headers -Method Get -ErrorAction Stop
        if ($release.tag_name -cne $tag -or $release.draft -or $release.prerelease) {
            throw 'The expected stable official GitHub Release is unavailable.'
        }
        $asset = @($release.assets | Where-Object { $_.name -ceq $meta.offlineZipName })
        if ($asset.Count -ne 1 -or [long]$asset[0].size -lt 1024 -or [long]$asset[0].size -gt 300MB) {
            throw 'The expected offline installer asset is absent or has an invalid size.'
        }
        $trustedUriPrefix = 'https://github.com/KiYouJyo/ApiMonitor/releases/download/' + $tag + '/'
        if (-not ([string]$asset[0].browser_download_url).StartsWith($trustedUriPrefix, [StringComparison]::Ordinal)) {
            throw 'The GitHub Release asset URL is not official.'
        }
        Write-Output "Downloading verified ApiMonitor $($meta.displayVersion) package..."
        Invoke-ReliableOneClickDownload -Uri ([string]$asset[0].browser_download_url) -Destination $zip
        if ((Get-Item -LiteralPath $zip).Length -ne [long]$asset[0].size) {
            throw 'The downloaded installer is incomplete.'
        }
        $actualSha = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToUpperInvariant()
        if ($actualSha -cne ([string]$meta.offlineZipSha256).ToUpperInvariant()) {
            throw 'Offline installer SHA-256 differs from the immutable one-click manifest.'
        }
        $digest = [string]$asset[0].digest
        if ($digest -match '^(?i:sha256):([A-Fa-f0-9]{64})$' -and
            $Matches[1].ToUpperInvariant() -cne $actualSha) {
            throw 'GitHub asset digest differs from the pinned installer SHA-256.'
        }
        $null = Assert-OneClickZipLayout -ZipPath $zip -PackageVersion ([string]$meta.packageVersion)

        $extracted = Join-Path $work 'expanded'
        [IO.Compression.ZipFile]::ExtractToDirectory($zip, $extracted)
        $root = Join-Path $extracted ('ApiMonitor_' + [string]$meta.packageVersion + '_x64_Test')
        # Must precede dot-sourcing Install.ps1 or any helper library:
        # Windows PowerShell 5.1 cannot parse Chinese UTF-8 without BOM.
        Repair-ExtractedPowerShellEncoding -OfflineRoot $root
        $installed = Install-VerifiedOfflinePackageForCurrentUser -OfflineRoot $root -PackageVersion ([string]$meta.packageVersion)
        Write-Output "ApiMonitor $($meta.displayVersion) installation verified."
        if (-not $SuppressLaunch) {
            try {
                Start-Process -FilePath ('shell:AppsFolder\' + $installed.PackageFamilyName + '!App')
            }
            catch {
                Write-Warning 'App installed successfully, but automatic launch failed. Use the Windows Start menu.'
            }
        }
        return 0
    }
    finally {
        Remove-Item -LiteralPath $work -Force -Recurse -ErrorAction SilentlyContinue
    }
}

if ($MyInvocation.InvocationName -ne '.') {
    $logDirectory = Join-Path $env:LOCALAPPDATA 'ApiMonitor\Logs'
    New-Item -ItemType Directory -Path $logDirectory -Force | Out-Null
    $log = Join-Path $logDirectory ('OneClick-{0:yyyyMMdd-HHmmss}.log' -f (Get-Date))
    try {
        Invoke-OneClickBootstrap -Operation $Action -SuppressLaunch:$NoLaunch *>&1 |
            Tee-Object -FilePath $log | Out-Host
        exit 0
    }
    catch {
        $message = "One-click $Action failed: $($_.Exception.Message)"
        $trace = $_ | Out-String
        Write-Host $message -ForegroundColor Red
        Write-Host "Detailed log: $log" -ForegroundColor Yellow
        Add-Content -LiteralPath $log -Value @($message, $trace, $_.ScriptStackTrace) -Encoding UTF8
        exit 1
    }
}
