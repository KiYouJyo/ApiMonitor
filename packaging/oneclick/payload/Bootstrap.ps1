#requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Install', 'Uninstall')][string]$Action = 'Install',
    [switch]$NoLaunch,
    [switch]$TestOnly
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

function Invoke-OneClickBootstrap {
    param([ValidateSet('Install','Uninstall')][string]$Operation,
          [switch]$SuppressLaunch)
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
        $scriptName = if ($Operation -eq 'Install') { 'Install.ps1' } else { 'Uninstall.ps1' }
        $scriptPath = Join-Path $root $scriptName
        if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
            throw 'Verified offline installer script was not found.'
        }

        if ($Operation -eq 'Install') {
            # UAC elevation is requested once, after downloading and checking
            # the exact pinned ZIP. The installed certificate comes only from
            # that authenticated archive and only enters TrustedPeople.
            $arguments = '-NoLogo -NoProfile -ExecutionPolicy Bypass -File "{0}" -PackageVersion "{1}" -NoLaunch -Quiet' -f $scriptPath, $meta.packageVersion
            try {
                $elevated = Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Verb RunAs -Wait -PassThru -ErrorAction Stop
            }
            catch {
                throw "Windows UAC approval was cancelled or could not be completed: $($_.Exception.Message)"
            }
            if ($elevated.ExitCode -ne 0) {
                throw "The official installer returned exit code $($elevated.ExitCode)."
            }
            $installed = @(Get-AppxPackage -Name 'ApiMonitor' -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -ceq 'ApiMonitor' -and $_.Publisher -ceq 'CN=ApiMonitorDev' }) | Select-Object -First 1
            if (-not $installed -or [string]$installed.Version -cne [string]$meta.packageVersion -or
                [string]$installed.Status -cne 'Ok') {
                throw 'Installed package identity, status or version verification failed.'
            }
            Write-Output "ApiMonitor $($meta.displayVersion) was installed successfully."
            if (-not $SuppressLaunch) {
                try {
                    Start-Process -FilePath ('shell:AppsFolder\' + $installed.PackageFamilyName + '!App')
                }
                catch {
                    Write-Warning 'The app is installed. Open ApiMonitor from Start if automatic launch is unavailable.'
                }
            }
        }
        else {
            Write-Warning 'Uninstalling the package may remove application-local data. Follow the offline uninstall prompts.'
            $arguments = '-NoLogo -NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $scriptPath
            $child = Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Wait -PassThru
            if ($child.ExitCode -ne 0) { throw "Uninstaller returned exit code $($child.ExitCode)." }
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
        $result = Invoke-OneClickBootstrap -Operation $Action -SuppressLaunch:$NoLaunch *>&1 |
            Tee-Object -FilePath $log
        if ($result -match '^\d+$') { exit [int]$result }
        exit 0
    }
    catch {
        $message = "One-click $Action failed: $($_.Exception.Message)"
        Write-Host $message -ForegroundColor Red
        Add-Content -LiteralPath $log -Value $message -Encoding UTF8
        exit 1
    }
}
