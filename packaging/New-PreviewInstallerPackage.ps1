#requires -Version 5.1
<#
  Assembles the ApiMonitor sideload release folder and Test.zip:

    ApiMonitor_<Version>_x64_Test\
      Install.cmd / Install.ps1
      Uninstall.cmd / Uninstall.ps1
      SafeLocalStateBackup.ps1       (备份/恢复函数库，供 Install.ps1 dot-source)
      INSTALL.md / UNINSTALL.md
      ApiMonitor_<Version>_x64.msix      (signed, passed via -MsixPath)
      ApiMonitorDev.cer                  (public certificate only)
      SHA256SUMS.txt                     (SHA-256 of MSIX + CER)
      Dependencies\x64\Microsoft.WindowsAppRuntime.2.msix

  Then writes ApiMonitor_<Version>_x64_Test.zip plus a release-root
  SHA256SUMS.txt covering the MSIX and the zip.
  Since v0.5.0 the stage folder is created directly under the output
  directory (packaging\output\ApiMonitor_<Version>_x64_Test).

  The script never copies private keys (PFX/P12), LocalState, Credential Locker
  content, user JSON, logs, Debug builds, or source caches.
#>

[CmdletBinding()]
param(
    [string]$Version = '0.7.0.6',
    [Parameter(Mandatory = $true)][string]$MsixPath,
    [string]$CertificateThumbprint = '545198E3BC78BE49BDF861C3EA6863FFD285689F',
    [string]$RuntimeSdkVersion = '2.3.1',
    [string]$OutputDirectory = ''
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot 'packaging\output'
}

$releaseDir = $OutputDirectory
$stageName = ('ApiMonitor_{0}_x64_Test' -f $Version)
$stageDir = Join-Path $releaseDir $stageName
$msixName = ('ApiMonitor_{0}_x64.msix' -f $Version)
$zipName = ('ApiMonitor_{0}_x64_Test.zip' -f $Version)

function Write-Step {
    param([string]$Message)
    Write-Host ('[{0}] {1}' -f (Get-Date -Format 'HH:mm:ss'), $Message)
}

function Get-Sha256 {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

# ---------------------------------------------------------------------------
# 1. Prepare the stage directory (inside packaging/output, git-ignored).
# ---------------------------------------------------------------------------
if (-not (Test-Path -LiteralPath $MsixPath)) {
    throw "MSIX 不存在：$MsixPath"
}
$resolvedStage = [System.IO.Path]::GetFullPath($stageDir)
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not $resolvedStage.StartsWith($resolvedOutput, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw '输出目录必须位于 packaging/output 内。'
}
if (Test-Path -LiteralPath $resolvedStage) {
    Remove-Item -LiteralPath $resolvedStage -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedStage | Out-Null

# ---------------------------------------------------------------------------
# 2. Copy the auditable installer files (tracked in packaging/installer).
# ---------------------------------------------------------------------------
$installerDir = Join-Path $repoRoot 'packaging\installer'
foreach ($name in @('Install.cmd', 'Install.ps1', 'Uninstall.cmd', 'Uninstall.ps1', 'INSTALL.md', 'UNINSTALL.md')) {
    $src = Join-Path $installerDir $name
    if (-not (Test-Path -LiteralPath $src)) {
        throw "缺少安装器文件：$src"
    }
    Copy-Item -LiteralPath $src -Destination (Join-Path $resolvedStage $name)
}
Write-Step '已复制安装器脚本与文档。'

# 备份/恢复函数库（与 Install.ps1 同目录，供其 dot-source）。
$backupTool = Join-Path $repoRoot 'packaging\tools\SafeLocalStateBackup.ps1'
if (-not (Test-Path -LiteralPath $backupTool)) {
    throw '缺少 SafeLocalStateBackup.ps1 备份工具。'
}
Copy-Item -LiteralPath $backupTool -Destination (Join-Path $resolvedStage 'SafeLocalStateBackup.ps1')

# ---------------------------------------------------------------------------
# 3. Copy the signed MSIX.
# ---------------------------------------------------------------------------
$msixDest = Join-Path $resolvedStage $msixName
Copy-Item -LiteralPath $MsixPath -Destination $msixDest
Write-Step "MSIX：$msixName ($((Get-Item -LiteralPath $msixDest).Length) bytes)"

# ---------------------------------------------------------------------------
# 4. Export the public certificate (public key only; never PFX/P12/private key).
# ---------------------------------------------------------------------------
$cert = Get-ChildItem -LiteralPath 'Cert:\CurrentUser\My', 'Cert:\LocalMachine\My' -ErrorAction SilentlyContinue |
    Where-Object { $_.Thumbprint -eq $CertificateThumbprint } |
    Select-Object -First 1
if (-not $cert) {
    throw "在证书存储中找不到 Thumbprint $CertificateThumbprint 的 ApiMonitorDev 证书。"
}
$cerPath = Join-Path $resolvedStage 'ApiMonitorDev.cer'
Export-Certificate -Cert $cert -FilePath $cerPath -Type CERT | Out-Null
$exported = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cerPath)
if ($exported.HasPrivateKey) {
    throw '导出的 CER 不应包含私钥，打包已中止。'
}
if (([string]$exported.Subject) -ne 'CN=ApiMonitorDev') {
    throw ('证书 Subject 不符：{0}' -f $exported.Subject)
}
Write-Step ('公开证书：CN=ApiMonitorDev  {0}' -f $exported.Thumbprint)

# ---------------------------------------------------------------------------
# 5. Copy the Windows App Runtime x64 framework package from the NuGet cache.
# ---------------------------------------------------------------------------
$depSource = Join-Path $env:USERPROFILE ('.nuget\packages\microsoft.windowsappsdk.runtime\' + $RuntimeSdkVersion + '\tools\MSIX\win10-x64\Microsoft.WindowsAppRuntime.2.msix')
if (-not (Test-Path -LiteralPath $depSource)) {
    throw "找不到 Windows App Runtime 依赖：$depSource"
}
$depDir = Join-Path $resolvedStage 'Dependencies\x64'
New-Item -ItemType Directory -Path $depDir | Out-Null
Copy-Item -LiteralPath $depSource -Destination (Join-Path $depDir 'Microsoft.WindowsAppRuntime.2.msix')

Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
$depZip = [System.IO.Compression.ZipFile]::OpenRead((Join-Path $depDir 'Microsoft.WindowsAppRuntime.2.msix'))
$depManifest = $null
try {
    $depEntry = $depZip.Entries | Where-Object { $_.FullName -eq 'AppxManifest.xml' } | Select-Object -First 1
    if ($depEntry) {
        $depReader = New-Object System.IO.StreamReader($depEntry.Open(), [System.Text.Encoding]::UTF8)
        try { $depManifest = $depReader.ReadToEnd() } finally { $depReader.Dispose() }
    }
} finally {
    $depZip.Dispose()
}
if ($depManifest -notmatch '<Identity Name="Microsoft\.WindowsAppRuntime\.2"') {
    throw '依赖包 Identity 校验失败（期望 Microsoft.WindowsAppRuntime.2）。'
}
if ($depManifest -notmatch 'ProcessorArchitecture="x64"') {
    throw '依赖包架构校验失败（期望 x64）。'
}
Write-Step '依赖：Dependencies\x64\Microsoft.WindowsAppRuntime.2.msix（x64，2.3.x）'

# ---------------------------------------------------------------------------
# 6. Internal SHA256SUMS.txt (verified by Install.ps1).
# ---------------------------------------------------------------------------
$internalLines = @(
    ('{0}  {1}' -f (Get-Sha256 $msixDest), $msixName),
    ('{0}  {1}' -f (Get-Sha256 $cerPath), 'ApiMonitorDev.cer')
)
[System.IO.File]::WriteAllLines(
    (Join-Path $resolvedStage 'SHA256SUMS.txt'),
    $internalLines,
    (New-Object System.Text.UTF8Encoding($false)))

# ---------------------------------------------------------------------------
# 7. Forbidden-content audit of the stage folder.
# ---------------------------------------------------------------------------
$forbiddenPatterns = @('\.pfx$', '\.p12$', '\.pvk$', '\.key$', 'LocalState', 'CredentialsBackup', '\.log$')
$forbidden = @(Get-ChildItem -LiteralPath $resolvedStage -Recurse -File |
    Where-Object {
        $name = $_.Name
        # 备份工具文件名包含 LocalState 字样，属于预期必需文件，不在禁止清单内。
        if ($name -eq 'SafeLocalStateBackup.ps1') { return $false }
        $patternHit = $false
        foreach ($p in $forbiddenPatterns) {
            if ($name -match $p) { $patternHit = $true; break }
        }
        $patternHit
    })
if ($forbidden.Count -gt 0) {
    throw ('打包内容包含禁止文件：{0}' -f (($forbidden | ForEach-Object { $_.Name }) -join '、'))
}

$requiredFiles = @(
    'Install.cmd', 'Install.ps1', 'Uninstall.cmd', 'Uninstall.ps1',
    'SafeLocalStateBackup.ps1',
    $msixName, 'ApiMonitorDev.cer', 'SHA256SUMS.txt', 'INSTALL.md', 'UNINSTALL.md'
)
foreach ($required in $requiredFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $resolvedStage $required))) {
        throw "发布目录缺少必需文件：$required"
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $resolvedStage 'Dependencies\x64\Microsoft.WindowsAppRuntime.2.msix'))) {
    throw '发布目录缺少依赖包。'
}
Write-Step '发布目录内容检查通过（无 PFX/私钥/本地数据/日志）。'

# ---------------------------------------------------------------------------
# 8. Create the Test.zip and the release-root SHA256SUMS.txt.
# ---------------------------------------------------------------------------
$zipPath = Join-Path $releaseDir $zipName
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path $resolvedStage -DestinationPath $zipPath -CompressionLevel Optimal
Write-Step "Test.zip：$zipName ($((Get-Item -LiteralPath $zipPath).Length) bytes)"

$releaseChecksumLines = @(
    ('{0}  {1}' -f (Get-Sha256 $msixDest), $msixName),
    ('{0}  {1}' -f (Get-Sha256 $zipPath), $zipName)
)
[System.IO.File]::WriteAllLines(
    (Join-Path $releaseDir 'SHA256SUMS.txt'),
    $releaseChecksumLines,
    (New-Object System.Text.UTF8Encoding($false)))

Write-Step "完成：$releaseDir"
Write-Step 'SHA256SUMS.txt：'
$releaseChecksumLines | ForEach-Object { Write-Host ('  ' + $_) }
