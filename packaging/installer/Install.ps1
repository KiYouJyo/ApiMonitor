#requires -Version 5.1
<#
  ApiMonitor v0.5.0 sideload installer (candidate package revision 0.5.0.1)
  ====================================
  Double-click entry: Install.cmd -> this script (Windows PowerShell 5.1).

  Responsibilities:
  1. Pre-install checks (OS, architecture, required files, exactly one MSIX).
  2. Self-elevation via UAC (Start-Process -Verb RunAs), no elevation loops.
  3. Integrity verification:
     - SHA-256 of the MSIX and the public certificate against SHA256SUMS.txt;
     - full signer thumbprint extracted from the MSIX compared with the CER;
     - certificate Subject = CN=ApiMonitorDev, Code Signing EKU, validity;
     - manifest Publisher matches the certificate Subject;
      - manifest Identity = ApiMonitor and version = 0.5.0.1.
  4. Import the public certificate into LocalMachine\TrustedPeople only.
  5. Install x64 Windows App Runtime dependencies bundled under Dependencies\x64.
  6. Fresh install / in-place upgrade of the ApiMonitor MSIX for the current user,
     preserving accounts, history, thresholds, window settings and Credential Locker data.

  Same-version safety (v0.5.0 candidate package policy):
  - Detecting an installed version identical to the incoming package stops by default.
    The installer never auto-uninstalls, never removes LocalState, never resets the
    package, and never touches Credential Locker.
  - A destructive reinstall is only possible with the explicit
    -ForceDestructiveReinstall parameter, which performs a validated LocalState backup
    first and warns that Credential Locker keys cannot be restored from files. This
    parameter is NOT part of the formal release flow.

  Exit codes are documented in INSTALL.md.
#>

[CmdletBinding()]
param(
    [string]$PackageVersion = '0.5.0.1',
    [string]$PackageIdentity = 'ApiMonitor',
    [string]$PublisherSubject = 'CN=ApiMonitorDev',
    [string]$RuntimePackageName = 'Microsoft.WindowsAppRuntime.2',
    [switch]$NoLaunch,
    [switch]$Quiet,
    [switch]$ForceDestructiveReinstall
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:InstallLogPath = $null
$script:QuietMode = $false

# 统一的 LocalState 备份/校验/恢复函数库（同仓库 packaging/tools）。
$script:BackupToolPath = Join-Path $PSScriptRoot 'SafeLocalStateBackup.ps1'
if (-not (Test-Path -LiteralPath $script:BackupToolPath)) {
    $script:BackupToolPath = Join-Path $PSScriptRoot '..\tools\SafeLocalStateBackup.ps1'
}
if (Test-Path -LiteralPath $script:BackupToolPath) {
    . $script:BackupToolPath
}

function Get-DefaultOps {
    <#
      Replaceable system operations. Tests inject fakes so decision logic can be
      validated in an isolated temp directory without touching real stores/packages.
    #>
    return @{
        GetFileHash = {
            param($Path)
            (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
        }
        GetAppxPackageForUser = {
            param($Name)
            Get-AppxPackage -Name $Name -ErrorAction SilentlyContinue
        }
        GetAppxPackageAllUsersFilter = {
            param($Filter)
            @(Get-AppxPackage -AllUsers -ErrorAction SilentlyContinue | Where-Object $Filter)
        }
        TestIsAdministrator = {
            ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
                [Security.Principal.WindowsBuiltInRole]::Administrator)
        }
        StartProcessElevated = {
            param($FilePath, $ArgumentList)
            Start-Process -FilePath $FilePath -Verb RunAs -ArgumentList $ArgumentList -Wait -PassThru
        }
        ImportCertificate = {
            param($Path, $StoreName)
            Import-Certificate -FilePath $Path -CertStoreLocation ("Cert:\LocalMachine\" + $StoreName)
        }
        GetTrustedPeopleCert = {
            param($Thumbprint)
            Get-ChildItem -LiteralPath 'Cert:\LocalMachine\TrustedPeople' -ErrorAction SilentlyContinue |
                Where-Object { $_.Thumbprint -eq $Thumbprint } |
                Select-Object -First 1
        }
        AddAppxPackage = {
            param($MainPath, [string[]]$DependencyPaths)
            if ($DependencyPaths -and $DependencyPaths.Count -gt 0) {
                Add-AppxPackage -Path $MainPath -DependencyPath $DependencyPaths -ForceApplicationShutdown
            } else {
                Add-AppxPackage -Path $MainPath -ForceApplicationShutdown
            }
        }
        RemoveAppxPackageForUser = {
            param($PackageFullName)
            Get-AppxPackage -ErrorAction SilentlyContinue |
                Where-Object { $_.PackageFullName -eq $PackageFullName } |
                Remove-AppxPackage
        }
        GetOsBuild = {
            [int](Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -Name CurrentBuildNumber -ErrorAction SilentlyContinue).CurrentBuildNumber
        }
        Is64BitOs = {
            [Environment]::Is64BitOperatingSystem
        }
    }
}

function Write-InstallerLog {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [ValidateSet('INFO', 'OK', 'WARN', 'ERROR')][string]$Level = 'INFO'
    )
    $line = ('[{0}] [{1}] {2}' -f (Get-Date -Format 'HH:mm:ss'), $Level, $Message)
    if (-not $script:QuietMode) {
        Write-Host $line
    }
    if ($script:InstallLogPath) {
        try {
            [System.IO.File]::AppendAllText(
                $script:InstallLogPath,
                $line + [Environment]::NewLine,
                (New-Object System.Text.UTF8Encoding($false)))
        } catch {
            # Logging must never abort installation.
        }
    }
}

function Get-InstallerExitCode {
    <#
      Central exit-code table. Values are documented in INSTALL.md.
    #>
    param([Parameter(Mandatory = $true)][string]$Name)
    switch ($Name) {
        'Success'                { return 0 }
        'GenericError'           { return 1 }
        'Canceled'               { return 2 }
        'HigherVersionInstalled' { return 4 }
        'IdentityConflict'       { return 5 }
        'SecurityVerificationFailed' { return 6 }
        'PreconditionFailed'     { return 7 }
        'DependencyMissing'      { return 8 }
        'InstallFailed'          { return 9 }
        'NotInstalled'           { return 10 }
        'UninstallFailed'        { return 11 }
        'AbortedByUser'          { return 12 }
        'CertCleanupBlocked'     { return 13 }
        'CertCleanupFailed'      { return 14 }
        'SameVersionBlocked'     { return 15 }
        'DestructiveBackupFailed' { return 16 }
        default                  { return 1 }
    }
}

function Get-CertificateStoreTarget {
    <#
      The only certificate store the installer ever writes to:
      Local Machine > Trusted People. Never Trusted Root.
    #>
    return 'TrustedPeople'
}

function Get-ElevatedArgumentLine {
    param([Parameter(Mandatory = $true)][string]$ScriptPath)
    return ('-NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $ScriptPath)
}

function Assert-Prerequisites {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptDir,
        [hashtable]$Ops
    )
    if ($null -eq $Ops) { $Ops = Get-DefaultOps }

    $build = (& $Ops.GetOsBuild)
    if ($build -lt 17763) {
        return @{ Ok = $false; Reason = ('当前 Windows 版本过低（build {0}）。需要 Windows 10 1809（build 17763）或更高版本。' -f $build) }
    }
    if (-not (& $Ops.Is64BitOs)) {
        return @{ Ok = $false; Reason = '仅支持 64 位（x64）Windows 系统。' }
    }

    foreach ($required in @('ApiMonitorDev.cer', 'SHA256SUMS.txt')) {
        if (-not (Test-Path -LiteralPath (Join-Path $ScriptDir $required))) {
            return @{ Ok = $false; Reason = ('缺少必需文件：{0}。请确认解压的是完整 Test.zip。' -f $required) }
        }
    }

    $msixCandidates = @(Get-ChildItem -LiteralPath $ScriptDir -Filter 'ApiMonitor*.msix' -File -ErrorAction SilentlyContinue)
    if ($msixCandidates.Count -eq 0) {
        return @{ Ok = $false; Reason = '未找到 ApiMonitor MSIX 安装包。' }
    }
    if ($msixCandidates.Count -gt 1) {
        return @{
            Ok = $false
            Reason = ('当前目录存在多份 ApiMonitor MSIX（{0}），无法确定安装目标。请只保留一份后重试。' -f (($msixCandidates.Name) -join '、'))
        }
    }
    return @{ Ok = $true; MsixPath = $msixCandidates[0].FullName; MsixName = $msixCandidates[0].Name }
}

function Get-MsixManifestInfo {
    param([Parameter(Mandatory = $true)][string]$MsixPath)

    Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue
    $zip = [System.IO.Compression.ZipFile]::OpenRead($MsixPath)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -eq 'AppxManifest.xml' } | Select-Object -First 1
        if (-not $entry) { throw 'MSIX 中缺少 AppxManifest.xml。' }
        $reader = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8)
        try { $xmlText = $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally {
        $zip.Dispose()
    }

    $xml = New-Object System.Xml.XmlDocument
    $xml.LoadXml($xmlText)
    $ns = New-Object System.Xml.XmlNamespaceManager($xml.NameTable)
    $ns.AddNamespace('d', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
    $identity = $xml.SelectSingleNode('//d:Identity', $ns)
    if (-not $identity) { throw 'AppxManifest.xml 中缺少 Identity 节点。' }

    $targetFamilies = @($xml.SelectNodes('//d:TargetDeviceFamily', $ns) | ForEach-Object { $_.GetAttribute('MinVersion') })
    return @{
        Name         = $identity.GetAttribute('Name')
        Version      = $identity.GetAttribute('Version')
        Publisher    = $identity.GetAttribute('Publisher')
        Architecture = $identity.GetAttribute('ProcessorArchitecture')
        MinVersions  = @($targetFamilies | Sort-Object -Unique)
    }
}

function Get-MsixSignerCertificate {
    <#
      Extracts the signer certificate from the MSIX AppxSignature.p7x without
      SignTool, the Windows SDK, or the PKCS#7 framework assembly: it walks the
      DER structure of the detached CMS and reads the first X.509 certificate.
      The caller still verifies Subject/EKU/validity and compares the FULL
      thumbprint against the bundled CER.
    #>
    param([Parameter(Mandatory = $true)][string]$MsixPath)

    Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue

    $zip = [System.IO.Compression.ZipFile]::OpenRead($MsixPath)
    try {
        $entry = $zip.Entries | Where-Object { $_.FullName -eq 'AppxSignature.p7x' } | Select-Object -First 1
        if (-not $entry) { throw 'MSIX 未签名（缺少 AppxSignature.p7x）。' }
        $stream = $entry.Open()
        try {
            $bytes = New-Object byte[] $entry.Length
            $read = 0
            while ($read -lt $entry.Length) {
                $n = $stream.Read($bytes, $read, $entry.Length - $read)
                if ($n -le 0) { break }
                $read += $n
            }
        } finally {
            $stream.Dispose()
        }
    } finally {
        $zip.Dispose()
    }

    $certs = @(Get-CmsCertificates $bytes)
    if ($certs.Count -eq 0) {
        throw '无法从 MSIX 提取签名证书。'
    }
    return $certs[0]
}

function Read-DerTlv {
    <#
      Reads one DER TLV at Offset. Supports short and long-form lengths.
      Returns Tag, ContentOffset, ContentLength, NextOffset and HeaderLength.
    #>
    param([byte[]]$Data, [int]$Offset)
    $tag = $Data[$Offset]
    $i = $Offset + 1
    $lenByte = $Data[$i]
    $i++
    if ($lenByte -lt 0x80) {
        $length = $lenByte
    } else {
        $count = $lenByte - 0x80
        if ($count -lt 1 -or $count -gt 4) { throw '不支持的 DER 长度编码。' }
        $length = 0
        for ($k = 0; $k -lt $count; $k++) {
            $length = ($length -shl 8) -bor $Data[$i]
            $i++
        }
    }
    return @{
        Tag           = $tag
        Offset        = $Offset
        HeaderLength  = $i - $Offset
        ContentOffset = $i
        ContentLength = $length
        NextOffset    = $i + $length
    }
}

function Get-DerChildren {
    param([byte[]]$Data, [int]$Start, [int]$Length)
    $children = @()
    $pos = $Start
    $end = $Start + $Length
    while ($pos -lt $end) {
        $tlv = Read-DerTlv $Data $pos
        $children += $tlv
        if ($tlv.NextOffset -le $tlv.Offset) { break }
        $pos = $tlv.NextOffset
    }
    return $children
}

function Get-CmsCertificates {
    <#
      Walks a DER-encoded CMS (PKCS#7) SignedData and returns the X.509
      certificates found in the certificates [0] field.
    #>
    param([byte[]]$Data)
    $certs = New-Object System.Collections.Generic.List[object]

    # MSIX AppxSignature.p7x is a PKCS#7 blob prefixed with the 4-byte magic
    # "PKCX" (0x50 0x4B 0x43 0x58). Plain CMS input is accepted as well.
    $startOffset = 0
    if ($Data.Length -ge 4 -and
        $Data[0] -eq 0x50 -and $Data[1] -eq 0x4B -and $Data[2] -eq 0x43 -and $Data[3] -eq 0x58) {
        $startOffset = 4
    }
    $outer = Read-DerTlv $Data $startOffset
    if ($outer.Tag -ne 0x30) { throw '不是有效的 CMS 数据（缺少外层 SEQUENCE）。' }
    foreach ($contentInfoChild in (Get-DerChildren $Data $outer.ContentOffset $outer.ContentLength)) {
        if ($contentInfoChild.Tag -ne 0xA0) { continue }
        foreach ($signedDataChild in (Get-DerChildren $Data $contentInfoChild.ContentOffset $contentInfoChild.ContentLength)) {
            if ($signedDataChild.Tag -ne 0x30) { continue }
            foreach ($signedDataElement in (Get-DerChildren $Data $signedDataChild.ContentOffset $signedDataChild.ContentLength)) {
                if ($signedDataElement.Tag -ne 0xA0) { continue }
                foreach ($certificateChild in (Get-DerChildren $Data $signedDataElement.ContentOffset $signedDataElement.ContentLength)) {
                    if ($certificateChild.Tag -ne 0x30) { continue }
                    # Copy the complete certificate TLV (header + content); the
                    # X.509 certificate SEQUENCE starts at Offset.
                    $certLength = $certificateChild.NextOffset - $certificateChild.Offset
                    $certBytes = New-Object byte[] $certLength
                    [Array]::Copy($Data, $certificateChild.Offset, $certBytes, 0, $certLength)
                    try {
                        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(,$certBytes)
                        $certs.Add($cert)
                    } catch {
                        # Ignore elements that are not valid certificates.
                    }
                }
            }
        }
    }
    return $certs.ToArray()
}

function Read-Checksums {
    param([Parameter(Mandatory = $true)][string]$Path)
    $map = @{}
    foreach ($line in @(Get-Content -LiteralPath $Path -Encoding UTF8 -ErrorAction SilentlyContinue)) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith('#')) { continue }
        if ($trimmed -match '^([0-9a-fA-F]{64})[ \t]+(\*?)(.+)$') {
            $map[$matches[3].Trim()] = $matches[1].ToUpperInvariant()
        }
    }
    return $map
}

function Assert-FileHashes {
    param(
        [Parameter(Mandatory = $true)][hashtable]$ChecksumMap,
        [Parameter(Mandatory = $true)][object[]]$Files,
        [hashtable]$Ops
    )
    if ($null -eq $Ops) { $Ops = Get-DefaultOps }
    foreach ($file in $Files) {
        $expected = $ChecksumMap[$file.Name]
        if (-not $expected) {
            return @{ Ok = $false; Reason = ('SHA256SUMS.txt 中缺少 {0} 的校验值。' -f $file.Name) }
        }
        $actual = & $Ops.GetFileHash $file.Path
        if ($actual -ne $expected) {
            return @{ Ok = $false; Reason = ('{0} 的 SHA-256 校验不匹配，安装已停止。' -f $file.Name) }
        }
    }
    return @{ Ok = $true }
}

function Assert-CertificatePolicy {
    param(
        $Certificate,
        [string]$ExpectedSubject = 'CN=ApiMonitorDev'
    )
    if (-not $Certificate) {
        return @{ Ok = $false; Reason = '证书对象为空。' }
    }
    $subject = [string]$Certificate.Subject
    $cnPart = ($subject.Trim() -split ',')[0].Trim()
    if ($cnPart -ne $ExpectedSubject) {
        return @{ Ok = $false; Reason = ('证书 Subject 不符："{0}"（期望 {1}）。' -f $subject, $ExpectedSubject) }
    }
    if (-not (Test-CodeSigningEku $Certificate)) {
        return @{ Ok = $false; Reason = '证书缺少代码签名（Code Signing）EKU。' }
    }
    $now = Get-Date
    if ($now -lt $Certificate.NotBefore -or $now -gt $Certificate.NotAfter) {
        return @{ Ok = $false; Reason = '证书不在有效期内。' }
    }
    return @{ Ok = $true }
}

function Test-CodeSigningEku {
    <#
      Verifies the certificate carries the Code Signing EKU (1.3.6.1.5.5.7.3.3).
      Fast path uses EnhancedKeyUsageList; the fallback parses the EKU extension
      (OID 2.5.29.37) DER directly so the check also works on systems where the
      .NET EnhancedKeyUsageList property is unavailable or misbehaves.
    #>
    param($Certificate)
    if (-not $Certificate) { return $false }

    $ekus = @()
    try {
        $ekus = @($Certificate.EnhancedKeyUsageList | ForEach-Object { $_.Value })
    } catch {
        $ekus = @()
    }
    if ($ekus -contains '1.3.6.1.5.5.7.3.3') { return $true }

    $ekuExtension = @($Certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.37' }) | Select-Object -First 1
    if (-not $ekuExtension) { return $false }
    $raw = $ekuExtension.RawData
    if (-not $raw -or $raw.Length -lt 2) { return $false }

    try {
        $sequence = Read-DerTlv $raw 0
        if ($sequence.Tag -ne 0x30) { return $false }
        $expectedOid = [byte[]]@(0x2B, 0x06, 0x01, 0x05, 0x05, 0x07, 0x03, 0x03)
        foreach ($child in (Get-DerChildren $raw $sequence.ContentOffset $sequence.ContentLength)) {
            if ($child.Tag -ne 0x06) { continue }
            $oidBytes = New-Object byte[] $child.ContentLength
            [Array]::Copy($raw, $child.ContentOffset, $oidBytes, 0, $child.ContentLength)
            if (($oidBytes -join ',') -eq ($expectedOid -join ',')) {
                return $true
            }
        }
    } catch {
        return $false
    }
    return $false
}

function Test-ThumbprintMatch {
    param($CertificateA, $CertificateB)
    if (-not $CertificateA -or -not $CertificateB) { return $false }
    return ([string]$CertificateA.Thumbprint).ToUpperInvariant() -eq ([string]$CertificateB.Thumbprint).ToUpperInvariant()
}

function Assert-ManifestIdentity {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Manifest,
        [string]$ExpectedName = 'ApiMonitor',
        [string]$ExpectedPublisher = 'CN=ApiMonitorDev',
        [string]$ExpectedVersion = '0.5.0.1'
    )
    if ($Manifest.Name -ne $ExpectedName) {
        return @{ Ok = $false; Reason = ('包 Identity Name 不符："{0}"（期望 {1}）。' -f $Manifest.Name, $ExpectedName) }
    }
    if ($Manifest.Publisher -ne $ExpectedPublisher) {
        return @{ Ok = $false; Reason = ('包 Publisher 不符："{0}"（期望 {1}）。' -f $Manifest.Publisher, $ExpectedPublisher) }
    }
    if ($Manifest.Version -ne $ExpectedVersion) {
        return @{ Ok = $false; Reason = ('包版本不符："{0}"（期望 {1}）。' -f $Manifest.Version, $ExpectedVersion) }
    }
    return @{ Ok = $true }
}

function Assert-ManifestPublisherMatchesCert {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Manifest,
        $Certificate
    )
    if (-not $Certificate) {
        return @{ Ok = $false; Reason = '缺少用于比对的签名证书。' }
    }
    if (([string]$Manifest.Publisher).Trim() -ne ([string]$Certificate.Subject).Trim()) {
        return @{ Ok = $false; Reason = '包内 Manifest Publisher 与签名证书 Subject 不一致。' }
    }
    return @{ Ok = $true }
}

function Compare-PackageVersion {
    <#
      Returns 1 when Left > Right, -1 when Left < Right, 0 when equal.
      Treats missing segments as zero ("2.3" == "2.3.0.0").
    #>
    param([string]$Left, [string]$Right)
    $lv = @($Left -split '\.')
    $rv = @($Right -split '\.')
    $count = [Math]::Max($lv.Count, $rv.Count)
    for ($i = 0; $i -lt $count; $i++) {
        $a = 0; $b = 0
        if ($i -lt $lv.Count) { [void][int]::TryParse($lv[$i], [ref]$a) }
        if ($i -lt $rv.Count) { [void][int]::TryParse($rv[$i], [ref]$b) }
        if ($a -gt $b) { return 1 }
        if ($a -lt $b) { return -1 }
    }
    return 0
}

function Resolve-PackageAction {
    <#
      Install  | package is not installed
      Upgrade  | installed lower version, in-place upgrade is safe
      SameVersion | identical version, do not reinstall
      HigherVersionInstalled | installed version is newer, refuse downgrade
      Conflict | same identity name but different Publisher / package family
    #>
    param(
        $InstalledPackage,
        [string]$NewVersion,
        [string]$NewPublisher = 'CN=ApiMonitorDev'
    )
    if (-not $InstalledPackage) { return 'Install' }
    $installedPublisher = ([string]$InstalledPackage.Publisher).Trim()
    if ($installedPublisher -ne $NewPublisher) { return 'Conflict' }
    $cmp = Compare-PackageVersion ([string]$InstalledPackage.Version) $NewVersion
    if ($cmp -eq 0) { return 'SameVersion' }
    if ($cmp -gt 0) { return 'HigherVersionInstalled' }
    return 'Upgrade'
}

function Resolve-DependencyPlan {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptDir,
        [hashtable]$Ops
    )
    if ($null -eq $Ops) { $Ops = Get-DefaultOps }

    $depDir = Join-Path $ScriptDir 'Dependencies\x64'
    $bundled = @()
    if (Test-Path -LiteralPath $depDir) {
        $bundled = @(Get-ChildItem -LiteralPath $depDir -Filter '*.msix' -File -ErrorAction SilentlyContinue)
    }
    $installed = @(& $Ops.GetAppxPackageForUser $script:RuntimePackageName)

    $needed = @()
    foreach ($dep in $bundled) {
        $info = Get-MsixManifestInfo $dep.FullName
        if ($info.Name -ne $script:RuntimePackageName) { continue }
        if ($info.Architecture -and $info.Architecture -ne 'x64') { continue }
        $have = @($installed | Where-Object {
                $_.Name -eq $info.Name -and ([string]$_.PackageFullName) -match '_x64__'
            } | Sort-Object @{ Expression = { [version]$_.Version } } -Descending) | Select-Object -First 1
        if ($have -and (Compare-PackageVersion ([string]$have.Version) $info.Version) -ge 0) {
            continue
        }
        $needed += $dep.FullName
    }

    if ($bundled.Count -eq 0) {
        $haveRuntime = @($installed | Where-Object {
                $_.Name -eq $script:RuntimePackageName -and ([string]$_.PackageFullName) -match '_x64__'
            })
        if ($haveRuntime.Count -eq 0) {
            return @{
                Ok = $false
                Reason = '压缩包中未包含 Dependencies，且系统缺少 Windows App Runtime 2（x64）。请从官方渠道安装 Windows App Runtime，或使用包含依赖的完整 Test.zip 后重试。'
                Needed = @()
            }
        }
    }
    return @{ Ok = $true; Needed = $needed }
}

function Import-ApiMonitorCertificate {
    param(
        [Parameter(Mandatory = $true)][string]$CertPath,
        [Parameter(Mandatory = $true)][string]$Thumbprint,
        [hashtable]$Ops
    )
    if ($null -eq $Ops) { $Ops = Get-DefaultOps }

    $storeTarget = Get-CertificateStoreTarget
    $existing = & $Ops.GetTrustedPeopleCert $Thumbprint
    if ($existing) {
        Write-InstallerLog ('证书已存在于 Local Machine\TrustedPeople（{0}），跳过导入。' -f $Thumbprint) 'OK'
        return @{ Ok = $true; Skipped = $true }
    }
    try {
        $imported = & $Ops.ImportCertificate $CertPath $storeTarget
        Write-InstallerLog ('已将公开证书导入 Local Machine\TrustedPeople（{0}）。' -f $imported.Thumbprint) 'OK'
        return @{ Ok = $true; Skipped = $false }
    } catch {
        return @{ Ok = $false; Reason = ('证书导入失败：{0}' -f $_.Exception.Message) }
    }
}

function Install-ApiMonitorPackage {
    param(
        [Parameter(Mandatory = $true)][string]$MsixPath,
        [string[]]$DependencyPaths,
        [hashtable]$Ops
    )
    if ($null -eq $Ops) { $Ops = Get-DefaultOps }
    try {
        & $Ops.AddAppxPackage $MsixPath $DependencyPaths
        return @{ Ok = $true }
    } catch {
        $hres = ''
        $inner = $_.Exception
        while ($inner.InnerException) { $inner = $inner.InnerException }
        if ($inner.HResult) { $hres = (' (0x{0:X8})' -f $inner.HResult) }
        return @{ Ok = $false; Reason = ('MSIX 安装/升级失败：{0}{1}' -f $_.Exception.Message, $hres) }
    }
}

function Confirm-DestructiveReinstall {
    <#
      破坏性重装的显式人工确认。静默模式一律拒绝；
      正式发布流程不得使用该参数。
    #>
    param([switch]$Quiet)
    if ($Quiet) {
        return $false
    }

    $answer = Read-Host '确实要卸载当前 ApiMonitor 并重新安装吗？本地数据会先备份，但此操作具有破坏性。[y/N]'
    return ($answer -match '^(y|Y|是)$')
}

function New-DestructiveReinstallBackup {
    <#
      破坏性重装前的 LocalState 强制备份（失败必须停止后续卸载）。
      返回 Ok / BackupDir / FileCount / Errors。
    #>
    param(
        [Parameter(Mandatory = $true)][string]$LocalState,
        [Parameter(Mandatory = $true)][string]$PackageFamilyName,
        [Parameter(Mandatory = $true)][string]$AppVersion,
        [hashtable]$Ops
    )
    $result = Backup-SafeLocalState `
        -Source $LocalState `
        -BackupRoot (Join-Path $env:TEMP 'ApiMonitor-LocalState-Backups') `
        -PackageFamilyName $PackageFamilyName `
        -AppVersion $AppVersion `
        -Ops $Ops
    return @{
        Ok         = $result.Ok
        BackupDir  = $result.BackupDir
        FileCount  = $result.FileCount
        Errors     = $result.Errors
    }
}

function Resolve-InstalledLocalState {
    <# 解析已安装包的 LocalState；测试可注入 Ops.ResolveLocalState。 #>
    param($InstalledPkg, [hashtable]$Ops)
    if ($Ops.ContainsKey('ResolveLocalState')) {
        return (& $Ops.ResolveLocalState $InstalledPkg.PackageFamilyName)
    }
    return (Join-Path (Join-Path $env:LOCALAPPDATA 'Packages') (Join-Path $InstalledPkg.PackageFamilyName 'LocalState'))
}

function Invoke-Install {
    param([hashtable]$Ops)
    if ($null -eq $Ops) { $Ops = Get-DefaultOps }

    # 脚本目录默认取 $PSScriptRoot；测试可注入 Ops.ResolveScriptDir 指向隔离临时目录。
    if ($Ops.ContainsKey('ResolveScriptDir')) {
        $scriptDir = & $Ops.ResolveScriptDir
    }
    else {
        $scriptDir = $PSScriptRoot
        if (-not $scriptDir) { $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
    }

    if (-not (& $Ops.TestIsAdministrator)) {
        Write-InstallerLog '需要管理员权限以导入证书并安装 MSIX，正在请求 UAC 提升……'
        try {
            $argumentLine = Get-ElevatedArgumentLine $PSCommandPath
            $process = & $Ops.StartProcessElevated 'powershell.exe' $argumentLine
            if ($process) { return $process.ExitCode }
            return (Get-InstallerExitCode 'Canceled')
        } catch {
            Write-InstallerLog '安装已取消（未同意 UAC 提升）。' 'WARN'
            return (Get-InstallerExitCode 'Canceled')
        }
    }

    Write-InstallerLog '==== ApiMonitor v0.6.0 自动安装开始 ===='

    # 1. Pre-install checks
    $pre = Assert-Prerequisites $scriptDir $Ops
    if (-not $pre.Ok) {
        Write-InstallerLog ('前置检查失败：{0}' -f $pre.Reason) 'ERROR'
        return (Get-InstallerExitCode 'PreconditionFailed')
    }
    Write-InstallerLog ('MSIX：{0}' -f $pre.MsixName) 'OK'

    # 2. Manifest identity + version
    try {
        $manifest = Get-MsixManifestInfo $pre.MsixPath
    } catch {
        Write-InstallerLog ('无法读取 MSIX Manifest：{0}' -f $_.Exception.Message) 'ERROR'
        return (Get-InstallerExitCode 'SecurityVerificationFailed')
    }
    $identityCheck = Assert-ManifestIdentity $manifest
    if (-not $identityCheck.Ok) {
        Write-InstallerLog ('身份校验失败：{0}' -f $identityCheck.Reason) 'ERROR'
        return (Get-InstallerExitCode 'SecurityVerificationFailed')
    }
    Write-InstallerLog ('Identity：{0}  {1}  Publisher：{2}' -f $manifest.Name, $manifest.Version, $manifest.Publisher) 'OK'

    # 3. Signature certificate from the MSIX
    try {
        $signerCert = Get-MsixSignerCertificate $pre.MsixPath
    } catch {
        Write-InstallerLog ('无法提取 MSIX 签名证书：{0}' -f $_.Exception.Message) 'ERROR'
        return (Get-InstallerExitCode 'SecurityVerificationFailed')
    }

    # 4. Public certificate from the package
    $cerPath = Join-Path $scriptDir 'ApiMonitorDev.cer'
    try {
        $cerCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cerPath)
    } catch {
        Write-InstallerLog ('无法读取 ApiMonitorDev.cer：{0}' -f $_.Exception.Message) 'ERROR'
        return (Get-InstallerExitCode 'SecurityVerificationFailed')
    }

    # 5. Full-thumbprint comparison (never a partial match)
    if (-not (Test-ThumbprintMatch $signerCert $cerCert)) {
        Write-InstallerLog (
            '安全校验失败：MSIX 签名证书 Thumbprint（{0}）与 ApiMonitorDev.cer（{1}）不一致。' -f
                $signerCert.Thumbprint, $cerCert.Thumbprint) 'ERROR'
        return (Get-InstallerExitCode 'SecurityVerificationFailed')
    }
    Write-InstallerLog ('MSIX 签名证书与随包 CER 完整 Thumbprint 一致（{0}）。' -f $cerCert.Thumbprint) 'OK'

    # 6. Certificate policy: Subject, Code Signing EKU, validity
    foreach ($cert in @($signerCert, $cerCert)) {
        $policy = Assert-CertificatePolicy $cert
        if (-not $policy.Ok) {
            Write-InstallerLog ('证书策略校验失败：{0}' -f $policy.Reason) 'ERROR'
            return (Get-InstallerExitCode 'SecurityVerificationFailed')
        }
    }
    Write-InstallerLog (
        '证书：Subject={0}  Thumbprint={1}  有效期={2} 至 {3}' -f
            $cerCert.Subject, $cerCert.Thumbprint, $cerCert.NotBefore.ToString('yyyy-MM-dd'), $cerCert.NotAfter.ToString('yyyy-MM-dd')) 'OK'

    # 7. Manifest Publisher must equal the certificate Subject
    $publisherMatch = Assert-ManifestPublisherMatchesCert $manifest $cerCert
    if (-not $publisherMatch.Ok) {
        Write-InstallerLog ('安全校验失败：{0}' -f $publisherMatch.Reason) 'ERROR'
        return (Get-InstallerExitCode 'SecurityVerificationFailed')
    }

    # 8. SHA-256 verification against SHA256SUMS.txt
    $checksumMap = Read-Checksums (Join-Path $scriptDir 'SHA256SUMS.txt')
    $hashCheck = Assert-FileHashes $checksumMap @(
        @{ Path = $pre.MsixPath; Name = $pre.MsixName },
        @{ Path = $cerPath; Name = 'ApiMonitorDev.cer' }
    ) $Ops
    if (-not $hashCheck.Ok) {
        Write-InstallerLog ('安全校验失败：{0}' -f $hashCheck.Reason) 'ERROR'
        return (Get-InstallerExitCode 'SecurityVerificationFailed')
    }
    Write-InstallerLog 'SHA-256 校验通过（MSIX 与 CER）。' 'OK'

    # 9. Certificate installation (LocalMachine\TrustedPeople only)
    $certInstall = Import-ApiMonitorCertificate $cerPath $cerCert.Thumbprint $Ops
    if (-not $certInstall.Ok) {
        Write-InstallerLog ('证书安装失败：{0}' -f $certInstall.Reason) 'ERROR'
        return (Get-InstallerExitCode 'GenericError')
    }

    # 10. Dependency plan (x64 only, skip when same or higher version installed)
    $depPlan = Resolve-DependencyPlan $scriptDir $Ops
    if (-not $depPlan.Ok) {
        Write-InstallerLog $depPlan.Reason 'ERROR'
        return (Get-InstallerExitCode 'DependencyMissing')
    }
    if ($depPlan.Needed.Count -gt 0) {
        Write-InstallerLog ('需要安装依赖（x64）：{0}' -f (($depPlan.Needed | ForEach-Object { Split-Path -Leaf $_ }) -join '、')) 'OK'
    } else {
        Write-InstallerLog '依赖已满足（Windows App Runtime 2 x64 已安装或无需安装）。' 'OK'
    }

    # 11. Decide fresh install / upgrade / same / downgrade / conflict
    $installedPkg = @(& $Ops.GetAppxPackageForUser $script:PackageIdentity |
        Where-Object { $_.Name -eq $script:PackageIdentity }) | Select-Object -First 1
    $action = Resolve-PackageAction $installedPkg $manifest.Version $manifest.Publisher
    switch ($action) {
        'Conflict' {
            Write-InstallerLog (
                '检测到同名但 Publisher 不同的包（已安装 Publisher={0}，期望 {1}）。请先手动处理冲突包后再安装。' -f
                    $installedPkg.Publisher, $manifest.Publisher) 'ERROR'
            return (Get-InstallerExitCode 'IdentityConflict')
        }
        'HigherVersionInstalled' {
            Write-InstallerLog (
                '已安装更高版本（{0}），v0.6.0 安装程序不会执行降级。' -f $installedPkg.Version) 'ERROR'
            return (Get-InstallerExitCode 'HigherVersionInstalled')
        }
        'SameVersion' {
            if (-not $ForceDestructiveReinstall) {
                Write-InstallerLog '已安装相同版本。请生成更高修订号的候选包，不要通过卸载重装替换。' 'WARN'
                return (Get-InstallerExitCode 'SameVersionBlocked')
            }

            Write-InstallerLog '检测到相同版本且指定了 -ForceDestructiveReinstall（破坏性重装，非正式发布流程）。' 'WARN'
            Write-InstallerLog '将先备份 LocalState 再卸载重装；Credential Locker 密钥无法通过文件备份恢复，重装后仍由凭据管理器保留。' 'WARN'
            if (-not (Confirm-DestructiveReinstall -Quiet:$Quiet)) {
                Write-InstallerLog '用户取消破坏性重装。' 'WARN'
                return (Get-InstallerExitCode 'Canceled')
            }

            $localState = Resolve-InstalledLocalState -InstalledPkg $installedPkg -Ops $Ops
            $backupResult = New-DestructiveReinstallBackup `
                -LocalState $localState `
                -PackageFamilyName $installedPkg.PackageFamilyName `
                -AppVersion ([string]$installedPkg.Version) `
                -Ops $Ops
            if (-not $backupResult.Ok) {
                Write-InstallerLog (
                    '破坏性重装前的 LocalState 备份失败，已停止后续操作：{0}' -f (($backupResult.Errors | Out-String).Trim())) 'ERROR'
                return (Get-InstallerExitCode 'DestructiveBackupFailed')
            }

            Write-InstallerLog (
                "LocalState 已备份：{0}（{1} 个文件）" -f $backupResult.BackupDir, $backupResult.FileCount) 'OK'
            Write-InstallerLog '正在卸载当前包（显式人工确认后执行）。' 'WARN'
            $null = & $Ops.RemoveAppxPackageForUser $installedPkg.PackageFullName
            $installedPkg = $null
        }
        'Upgrade' {
            Write-InstallerLog ('检测到已安装版本 {0}，执行原地升级（保留本地数据与凭据）。' -f $installedPkg.Version) 'OK'
            # 升级前尽力备份 LocalState；备份失败不阻塞安全的标准 MSIX 原地升级。
            try {
                $upgradeLocalState = Resolve-InstalledLocalState -InstalledPkg $installedPkg -Ops $Ops
                if (Test-Path -LiteralPath $upgradeLocalState) {
                    $upgradeBackup = Backup-SafeLocalState `
                        -Source $upgradeLocalState `
                        -BackupRoot (Join-Path $env:TEMP 'ApiMonitor-LocalState-Backups') `
                        -PackageFamilyName $installedPkg.PackageFamilyName `
                        -AppVersion ([string]$installedPkg.Version) `
                        -Ops $Ops
                    if ($upgradeBackup.Ok) {
                        Write-InstallerLog ("升级前 LocalState 已备份：{0}" -f $upgradeBackup.BackupDir) 'OK'
                    }
                    else {
                        Write-InstallerLog (
                            '升级前 LocalState 备份失败（不影响标准原地升级）：{0}' -f ($upgradeBackup.Errors -join '；')) 'WARN'
                    }
                }
            }
            catch {
                Write-InstallerLog '升级前 LocalState 备份失败（不影响标准原地升级）。' 'WARN'
            }
        }
        default {
            Write-InstallerLog '未检测到已安装的 ApiMonitor，执行全新安装。' 'OK'
        }
    }

    # 12. Install / upgrade with dependencies
    $install = Install-ApiMonitorPackage $pre.MsixPath $depPlan.Needed $Ops
    if (-not $install.Ok) {
        Write-InstallerLog $install.Reason 'ERROR'
        return (Get-InstallerExitCode 'InstallFailed')
    }

    # 13. Verify installed state
    $afterPkg = @(& $Ops.GetAppxPackageForUser $script:PackageIdentity |
        Where-Object { $_.Name -eq $script:PackageIdentity }) | Select-Object -First 1
    if (-not $afterPkg -or $afterPkg.Status -ne 'Ok' -or ([string]$afterPkg.Version) -ne $manifest.Version) {
        Write-InstallerLog '安装后验证失败：包状态或版本不正确。' 'ERROR'
        return (Get-InstallerExitCode 'InstallFailed')
    }
    Write-InstallerLog ('安装成功：ApiMonitor {0}（Package Family：{1}）。' -f $afterPkg.Version, $afterPkg.PackageFamilyName) 'OK'

    # 14. Optional launch
    if (-not $NoLaunch) {
        $answer = Read-Host '是否立即启动 ApiMonitor？[Y/N]（默认 Y）'
        if ($answer -eq '' -or $answer -match '^(y|Y|是)$') {
            try {
                Start-Process -FilePath ('shell:AppsFolder\' + $afterPkg.PackageFamilyName + '!App')
                Write-InstallerLog '已启动 ApiMonitor。' 'OK'
            } catch {
                Write-InstallerLog '安装成功，但自动启动失败，请从开始菜单手动启动。' 'WARN'
            }
        }
    }

    Write-InstallerLog '==== 安装完成 ===='
    return (Get-InstallerExitCode 'Success')
}

if ($MyInvocation.InvocationName -ne '.') {
    $script:QuietMode = [bool]$Quiet
    if (-not $Quiet) {
        $script:InstallLogPath = Join-Path $env:TEMP ('ApiMonitor-Install-{0}.log' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
        try {
            [System.IO.File]::WriteAllText($script:InstallLogPath, '', (New-Object System.Text.UTF8Encoding($false)))
            Write-InstallerLog ('安装日志：{0}' -f $script:InstallLogPath)
        } catch {
            $script:InstallLogPath = $null
        }
    }
    exit (Invoke-Install (Get-DefaultOps))
}
