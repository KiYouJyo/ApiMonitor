#requires -Version 5.1
<#
  Isolated tests for the ApiMonitor installer/uninstaller tooling.

  The real Install.ps1 / Uninstall.ps1 scripts are dot-sourced (function
  definitions only). System operations (certificate stores, Appx packages,
  processes, elevation) are replaced with fake ops, so this harness never
  modifies machine-level certificate stores and never installs a real package.
  Everything runs inside an isolated temp directory whose path contains spaces
  and Chinese characters.

  Run with Windows PowerShell:
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File tests\installer\Installer.Tests.ps1
#>

[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$script:PassCount = 0
$script:FailCount = 0
$script:Failures = @()

function Assert-Equal {
    param($Expected, $Actual, [string]$Name)
    if ("$Expected" -ceq "$Actual") {
        $script:PassCount++
        Write-Host "  [PASS] $Name"
    } else {
        $script:FailCount++
        $script:Failures += $Name
        Write-Host "  [FAIL] $Name  (expected '$Expected', got '$Actual')" -ForegroundColor Red
    }
}

function Assert-True {
    param($Condition, [string]$Name)
    if ($Condition) {
        $script:PassCount++
        Write-Host "  [PASS] $Name"
    } else {
        $script:FailCount++
        $script:Failures += $Name
        Write-Host "  [FAIL] $Name" -ForegroundColor Red
    }
}

function Assert-False {
    param($Condition, [string]$Name)
    Assert-True (-not $Condition) $Name
}

function Write-TestSection {
    param([string]$Title)
    Write-Host "`n== $Title ==" -ForegroundColor Cyan
}

function New-TestCert {
    param(
        [string]$Subject = 'CN=ApiMonitorDev',
        [switch]$WithCodeSigningEku,
        [datetime]$NotBefore = (Get-Date).AddDays(-1),
        [datetime]$NotAfter = (Get-Date).AddYears(2)
    )
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    $request = [Activator]::CreateInstance(
        [System.Security.Cryptography.X509Certificates.CertificateRequest],
        [object[]]@($Subject, $rsa, [System.Security.Cryptography.HashAlgorithmName]::SHA256, [System.Security.Cryptography.RSASignaturePadding]::Pkcs1))
    if ($WithCodeSigningEku) {
        # 1.3.6.1.5.5.7.3.3 (Code Signing) as raw DER:
        # SEQUENCE { OID 1.3.6.1.5.5.7.3.3 }
        $ekuRaw = [byte[]]@(0x30, 0x0A, 0x06, 0x08, 0x2B, 0x06, 0x01, 0x05, 0x05, 0x07, 0x03, 0x03)
        $asn = New-Object System.Security.Cryptography.AsnEncodedData('2.5.29.37', $ekuRaw)
        $eku = New-Object System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension($asn, $false)
        [void]$request.CertificateExtensions.Add($eku)
    }
    return $request.CreateSelfSigned($NotBefore, $NotAfter)
}

function Write-CertFile {
    param($Certificate, [string]$Path)
    [System.IO.File]::WriteAllBytes($Path, $Certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
}

function ConvertTo-DerLength {
    param([int]$Length)
    if ($Length -lt 0x80) { return [byte[]]@($Length) }
    if ($Length -lt 0x100) { return [byte[]]@(0x81, $Length) }
    if ($Length -lt 0x10000) {
        return [byte[]]@(0x82, (($Length -shr 8) -band 0xFF), ($Length -band 0xFF))
    }
    return [byte[]]@(0x83, (($Length -shr 16) -band 0xFF), (($Length -shr 8) -band 0xFF), ($Length -band 0xFF))
}

function New-DerTlv {
    param([byte]$Tag, [byte[]]$Content)
    $lengthBytes = ConvertTo-DerLength $Content.Length
    $out = New-Object byte[] (1 + $lengthBytes.Length + $Content.Length)
    $out[0] = $Tag
    [Array]::Copy($lengthBytes, 0, $out, 1, $lengthBytes.Length)
    [Array]::Copy($Content, 0, $out, 1 + $lengthBytes.Length, $Content.Length)
    return $out
}

function New-FakeSignatureP7x {
    <#
      Builds a minimal detached CMS SignedData structure around the given
      certificate (DER). It is structurally equivalent to what the installer's
      DER parser needs: ContentInfo { OID signedData, [0] { SignedData {
      version, digestAlgorithms, encapContentInfo, certificates [0] { cert },
      signerInfos } } }.
    #>
    param($Certificate)
    $certDer = $Certificate.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert)
    $signedDataOid = [byte[]]@(0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x07, 0x02)
    $dataOid = [byte[]]@(0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x07, 0x01)

    $version = New-DerTlv 0x02 ([byte[]]@(0x01))
    $digestAlgorithms = New-DerTlv 0x31 ([byte[]]@())
    $encapContentInfo = New-DerTlv 0x30 $dataOid
    $certificatesSet = New-DerTlv 0xA0 $certDer
    $signerInfos = New-DerTlv 0x31 ([byte[]]@())

    $signedDataContent = $version + $digestAlgorithms + $encapContentInfo + $certificatesSet + $signerInfos
    $signedData = New-DerTlv 0x30 $signedDataContent
    $signedDataContext = New-DerTlv 0xA0 $signedData
    $contentInfoContent = $signedDataOid + $signedDataContext
    $contentInfo = New-DerTlv 0x30 $contentInfoContent
    # Real MSIX AppxSignature.p7x files carry the "PKCX" magic prefix.
    $magic = [byte[]]@(0x50, 0x4B, 0x43, 0x58)
    return ($magic + $contentInfo)
}

function New-FakeMsix {
    param(
        [string]$Path,
        [string]$ManifestXml,
        $SignerCertificate
    )
    Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue

    $fileStream = [System.IO.File]::Create($Path)
    $archive = New-Object System.IO.Compression.ZipArchive($fileStream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $manifestEntry = $archive.CreateEntry('AppxManifest.xml')
        $writer = New-Object System.IO.StreamWriter($manifestEntry.Open(), (New-Object System.Text.UTF8Encoding($false)))
        try { $writer.Write($ManifestXml) } finally { $writer.Dispose() }

        $signatureBytes = New-FakeSignatureP7x $SignerCertificate

        $signatureEntry = $archive.CreateEntry('AppxSignature.p7x')
        $signatureStream = $signatureEntry.Open()
        try {
            $signatureStream.Write($signatureBytes, 0, $signatureBytes.Length)
        } finally {
            $signatureStream.Dispose()
        }
    } finally {
        $archive.Dispose()
        $fileStream.Dispose()
    }
}

# ---------------------------------------------------------------------------
# Isolated workspace (path intentionally contains spaces and Chinese).
# ---------------------------------------------------------------------------
$script:TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ('ApiMonitor 安装测试 ' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $script:TempRoot | Out-Null

$script:InstallerScriptDir = Join-Path $PSScriptRoot '..\..\packaging\installer'
$script:InstallPs1 = Join-Path $script:InstallerScriptDir 'Install.ps1'
$script:UninstallPs1 = Join-Path $script:InstallerScriptDir 'Uninstall.ps1'
if (-not (Test-Path -LiteralPath $script:InstallPs1) -or -not (Test-Path -LiteralPath $script:UninstallPs1)) {
    throw '找不到 packaging/installer 下的脚本。'
}

try {
    # Dot-source only the function definitions (main execution is guarded).
    . $script:InstallPs1
    . $script:UninstallPs1
    $script:BackupToolPath = Join-Path (Join-Path $PSScriptRoot '..\..\packaging') 'tools\SafeLocalStateBackup.ps1'
    if (-not (Test-Path -LiteralPath $script:BackupToolPath)) {
        throw '找不到 packaging/tools/SafeLocalStateBackup.ps1。'
    }
    . $script:BackupToolPath
    # The scripts set StrictMode and ErrorActionPreference in this scope; relax them
    # for the harness so it can use loose comparison conventions.
    Set-StrictMode -Off
    $ErrorActionPreference = 'Continue'

    # -----------------------------------------------------------------------
    # Shared fixtures
    # -----------------------------------------------------------------------
    Write-TestSection 'Fixtures'
    $certA = New-TestCert -Subject 'CN=ApiMonitorDev' -WithCodeSigningEku
    $certB = New-TestCert -Subject 'CN=ApiMonitorDev' -WithCodeSigningEku
    $certWrongSubject = New-TestCert -Subject 'CN=OtherDev' -WithCodeSigningEku
    $certNoEku = New-TestCert -Subject 'CN=ApiMonitorDev'
    $certExpired = New-TestCert -Subject 'CN=ApiMonitorDev' -WithCodeSigningEku -NotBefore (Get-Date).AddDays(-800) -NotAfter (Get-Date).AddDays(-400)
    Assert-True ($certA.Thumbprint -ne $certB.Thumbprint) '两个测试证书的 Thumbprint 不同'

    $msixName = 'ApiMonitor_0.8.0.0_x64.msix'
    $goodManifest = @'
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
  <Identity Name="ApiMonitor" Publisher="CN=ApiMonitorDev" Version="0.8.0.0" ProcessorArchitecture="x64" />
  <Dependencies>
    <TargetDeviceFamily Name="Windows.Universal" MinVersion="10.0.17763.0" MaxVersionTested="12.0.0.0" />
  </Dependencies>
</Package>
'@

    $msixPath = Join-Path $script:TempRoot $msixName
    New-FakeMsix -Path $msixPath -ManifestXml $goodManifest -SignerCertificate $certA
    $cerPath = Join-Path $script:TempRoot 'ApiMonitorDev.cer'
    Write-CertFile -Certificate $certA -Path $cerPath
    $cerBPath = Join-Path $script:TempRoot 'ApiMonitorDev-B.cer'
    Write-CertFile -Certificate $certB -Path $cerBPath
    $goodHash = (Get-FileHash -LiteralPath $msixPath -Algorithm SHA256).Hash
    $cerHash = (Get-FileHash -LiteralPath $cerPath -Algorithm SHA256).Hash
    $checksumPath = Join-Path $script:TempRoot 'SHA256SUMS.txt'
    [System.IO.File]::WriteAllLines(
        $checksumPath,
        @(('{0}  {1}' -f $goodHash, $msixName), ('{0}  {1}' -f $cerHash, 'ApiMonitorDev.cer')),
        (New-Object System.Text.UTF8Encoding($false)))
    Assert-True (Test-Path -LiteralPath $msixPath) 'fixture MSIX 已创建'
    Assert-True (Test-Path -LiteralPath $cerPath) 'fixture CER 已创建'

    # Fake ops: only in-memory/in-temp behavior.
    $script:InstalledPackages = @()
    $script:AllUsersPackages = @()
    $script:CertImportCalls = @()
    $script:RemoveCalls = @()
    $script:AddAppxCalls = 0
    $script:DummyCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cerPath)
    $fakeOps = @{
        GetFileHash = { param($Path) (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
        GetAppxPackageForUser = { param($Name) $script:InstalledPackages | Where-Object { $_.Name -eq $Name } }
        GetAppxPackageAllUsers = { param($Publisher) @($script:AllUsersPackages | Where-Object { ([string]$_.Publisher).Trim() -eq $Publisher }) }
        TestIsAdministrator = { $true }
        StartProcessElevated = { param($FilePath, $ArgumentList) $null }
        ImportCertificate = {
            param($Path, $StoreName)
            $script:CertImportCalls += $StoreName
            $script:DummyCert
        }
        GetTrustedPeopleCert = { param($Thumbprint) $null }
        AddAppxPackage = {
            param($MainPath, [string[]]$DependencyPaths)
            $script:AddAppxCalls++
            $true
        }
        GetOsBuild = { 19045 }
        Is64BitOs = { $true }
        RemoveAppxPackageForUser = {
            param($PackageFullName)
            $script:RemoveCalls += $PackageFullName
            $true
        }
        GetRunningProcess = { param($Name) @() }
        CloseMainWindow = { param($Process) $true }
        StopProcess = { param($Process) $true }
        GetTrustedPeopleCertForRemoval = { param($Thumbprint) $null }
        RemoveTrustedPeopleCertByThumbprint = { param($Thumbprint) $true }
    }

    # -----------------------------------------------------------------------
    Write-TestSection 'Manifest：版本与 Identity'
    $info = Get-MsixManifestInfo $msixPath
    Assert-Equal 'ApiMonitor' $info.Name 'Identity Name = ApiMonitor'
    Assert-Equal '0.8.0.0' $info.Version '包版本 = 0.8.0.0'
    Assert-Equal 'CN=ApiMonitorDev' $info.Publisher 'Publisher = CN=ApiMonitorDev'
    Assert-True (Assert-ManifestIdentity $info).Ok 'Assert-ManifestIdentity 通过'

    $wrongPublisher = @{ Name = 'ApiMonitor'; Version = '0.8.0.0'; Publisher = 'CN=WrongPublisher' }
    Assert-False (Assert-ManifestIdentity $wrongPublisher).Ok '错误 Publisher 被拒绝'
    $wrongName = @{ Name = 'ApiMonitorOther'; Version = '0.8.0.0'; Publisher = 'CN=ApiMonitorDev' }
    Assert-False (Assert-ManifestIdentity $wrongName).Ok '错误 Identity Name 被拒绝'
    $wrongVersion = @{ Name = 'ApiMonitor'; Version = '0.5.0.2'; Publisher = 'CN=ApiMonitorDev' }
    Assert-False (Assert-ManifestIdentity $wrongVersion).Ok '错误版本被拒绝'

    # -----------------------------------------------------------------------
    Write-TestSection '证书：Thumbprint / Subject / EKU / 有效期'
    $signerFromMsix = Get-MsixSignerCertificate $msixPath
    Assert-True (Test-ThumbprintMatch $signerFromMsix $certA) 'MSIX 签名证书与 CER-A 完整 Thumbprint 一致'
    Assert-False (Test-ThumbprintMatch $signerFromMsix $certB) '错误证书 Thumbprint（CER-B）被拒绝'
    $signatureBytes = $null
    Add-Type -AssemblyName System.IO.Compression -ErrorAction SilentlyContinue
    $sigZip = [System.IO.Compression.ZipFile]::OpenRead($msixPath)
    try {
        $sigEntry = $sigZip.Entries | Where-Object { $_.FullName -eq 'AppxSignature.p7x' } | Select-Object -First 1
        $sigStream = $sigEntry.Open()
        try {
            $signatureBytes = New-Object byte[] $sigEntry.Length
            $sigRead = 0
            while ($sigRead -lt $sigEntry.Length) { $sigN = $sigStream.Read($signatureBytes, $sigRead, $sigEntry.Length - $sigRead); if ($sigN -le 0) { break }; $sigRead += $sigN }
        } finally { $sigStream.Dispose() }
    } finally { $sigZip.Dispose() }
    $strip = New-Object byte[] ($signatureBytes.Length - 4)
    [Array]::Copy($signatureBytes, 4, $strip, 0, $strip.Length)
    Assert-True ((Get-CmsCertificates $strip).Count -eq 1) 'DER 无 PKCX 前缀时仍可解析'
    Assert-True (Assert-CertificatePolicy $certA).Ok '合法证书通过策略校验'
    Assert-False (Assert-CertificatePolicy $certWrongSubject).Ok '错误 Subject 被拒绝'
    Assert-False (Assert-CertificatePolicy $certNoEku).Ok '缺少代码签名 EKU 被拒绝'
    Assert-False (Assert-CertificatePolicy $certExpired).Ok '过期证书被拒绝'
    Assert-True (Assert-ManifestPublisherMatchesCert $info $certA).Ok 'Manifest Publisher 与证书 Subject 一致时通过'
    Assert-False (Assert-ManifestPublisherMatchesCert $info $certWrongSubject).Ok 'Manifest Publisher 与证书 Subject 不一致时拒绝'

    # -----------------------------------------------------------------------
    Write-TestSection 'SHA-256 校验'
    $map = Read-Checksums $checksumPath
    $hashOk = Assert-FileHashes $map @(
        @{ Path = $msixPath; Name = $msixName },
        @{ Path = $cerPath; Name = 'ApiMonitorDev.cer' }
    ) $fakeOps
    Assert-True $hashOk.Ok '正确哈希通过校验'

    $tampered = Join-Path $script:TempRoot 'SHA256SUMS-tampered.txt'
    [System.IO.File]::WriteAllLines(
        $tampered,
        @(('{0}  {1}' -f ('0' * 64), $msixName), ('{0}  {1}' -f $cerHash, 'ApiMonitorDev.cer')),
        (New-Object System.Text.UTF8Encoding($false)))
    $badMap = Read-Checksums $tampered
    $hashBad = Assert-FileHashes $badMap @(@{ Path = $msixPath; Name = $msixName }) $fakeOps
    Assert-False $hashBad.Ok 'SHA-256 不匹配时停止'

    $missingMap = @{ 'ApiMonitorDev.cer' = $cerHash }
    $hashMissing = Assert-FileHashes $missingMap @(@{ Path = $msixPath; Name = $msixName }) $fakeOps
    Assert-False $hashMissing.Ok '校验文件缺少条目时停止'

    # -----------------------------------------------------------------------
    Write-TestSection '安装决策：Install / Upgrade / Same / Downgrade / Conflict'
    function New-InstalledPkg {
        param([string]$Version = '0.3.0.0', [string]$Publisher = 'CN=ApiMonitorDev')
        [pscustomobject]@{
            Name              = 'ApiMonitor'
            Version           = $Version
            Publisher         = $Publisher
            PackageFullName   = ('ApiMonitor_{0}_x64__cx0n152q1hsh2' -f $Version)
            PackageFamilyName = 'ApiMonitor_cx0n152q1hsh2'
            Status            = 'Ok'
        }
    }
    Assert-Equal 'Install' (Resolve-PackageAction $null '0.8.0.0' 'CN=ApiMonitorDev') '未安装 -> Install'
    Assert-Equal 'Upgrade' (Resolve-PackageAction (New-InstalledPkg '0.4.0.0') '0.8.0.0' 'CN=ApiMonitorDev') '低版本允许原地升级'
    Assert-Equal 'Upgrade' (Resolve-PackageAction (New-InstalledPkg '0.6.0.1') '0.8.0.0' 'CN=ApiMonitorDev') 'v0.6.0.1 允许原地升级到 0.8.0.0'
    Assert-Equal 'SameVersion' (Resolve-PackageAction (New-InstalledPkg '0.8.0.0') '0.8.0.0' 'CN=ApiMonitorDev') '相同版本不重复安装'
    Assert-Equal 'HigherVersionInstalled' (Resolve-PackageAction (New-InstalledPkg '0.8.0.1') '0.8.0.0' 'CN=ApiMonitorDev') '更高版本拒绝降级'
    Assert-Equal 'Conflict' (Resolve-PackageAction (New-InstalledPkg '0.8.0.0' 'CN=SomeoneElse') '0.8.0.0' 'CN=ApiMonitorDev') '同名不同 Publisher 判定为冲突'
    Assert-True ((Compare-PackageVersion '2.3.1.0' '2.3.1.0') -eq 0) '版本比较：相等'
    Assert-True ((Compare-PackageVersion '2.3.1.0' '2.3.0.0') -gt 0) '版本比较：更高'
    Assert-True ((Compare-PackageVersion '2.3.0.0' '2.3.1.0') -lt 0) '版本比较：更低'
    Assert-True ((Compare-PackageVersion '2.3' '2.3.0.0') -eq 0) '版本比较：缺段按 0 处理'

    # -----------------------------------------------------------------------
    Write-TestSection '证书安装：只导入 TrustedPeople'
    $certInstall = Import-ApiMonitorCertificate $cerPath $certA.Thumbprint $fakeOps
    Assert-True $certInstall.Ok '证书导入成功'
    Assert-True ($script:CertImportCalls.Count -eq 1) '证书导入恰好调用一次'
    Assert-Equal 'TrustedPeople' $script:CertImportCalls[0] '导入目标为 TrustedPeople'
    Assert-True (($script:CertImportCalls | Where-Object { $_ -match 'Root' }).Count -eq 0) '绝不导入 Trusted Root'
    Assert-Equal 'TrustedPeople' (Get-CertificateStoreTarget) '证书目标常量 = TrustedPeople'

    $skipOps = @{
        GetTrustedPeopleCert = { param($Thumbprint) $script:DummyCert }
        ImportCertificate = {
            param($Path, $StoreName)
            $script:CertImportCalls += $StoreName
            $script:DummyCert
        }
    }
    $skipInstall = Import-ApiMonitorCertificate $cerPath $certA.Thumbprint $skipOps
    Assert-True $skipInstall.Ok '已存在相同 Thumbprint 时跳过'
    Assert-True $skipInstall.Skipped '跳过标记为 Skipped'

    # -----------------------------------------------------------------------
    Write-TestSection '依赖计划'
    $depDir = Join-Path $script:TempRoot 'Dependencies\x64'
    New-Item -ItemType Directory -Path $depDir -Force | Out-Null
    $depManifest = @'
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10">
  <Identity Name="Microsoft.WindowsAppRuntime.2" Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" Version="2.3.1.0" ProcessorArchitecture="x64" />
</Package>
'@
    $depMsix = Join-Path $depDir 'Microsoft.WindowsAppRuntime.2.msix'
    New-FakeMsix -Path $depMsix -ManifestXml $depManifest -SignerCertificate $certA

    $script:InstalledPackages = @([pscustomobject]@{
            Name = 'Microsoft.WindowsAppRuntime.2'
            Version = '2.3.1.0'
            PackageFullName = 'Microsoft.WindowsAppRuntime.2_2.3.1.0_x64__8wekyb3d8bbwe'
            Publisher = 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US'
        })
    $planSkip = Resolve-DependencyPlan $script:TempRoot $fakeOps
    Assert-True $planSkip.Ok '依赖计划：正常'
    Assert-True ($planSkip.Needed.Count -eq 0) '已安装相同版本依赖时跳过'

    $script:InstalledPackages = @([pscustomobject]@{
            Name = 'Microsoft.WindowsAppRuntime.2'
            Version = '2.3.0.0'
            PackageFullName = 'Microsoft.WindowsAppRuntime.2_2.3.0.0_x64__8wekyb3d8bbwe'
            Publisher = 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US'
        })
    $planNeed = Resolve-DependencyPlan $script:TempRoot $fakeOps
    Assert-True ($planNeed.Needed.Count -eq 1) '低版本依赖列入安装计划'
    Assert-True (($planNeed.Needed[0] -replace '\\', '/') -match 'Dependencies/x64/') '依赖计划只包含 Dependencies\x64'

    $emptyDir = Join-Path $script:TempRoot 'no-deps'
    New-Item -ItemType Directory -Path $emptyDir | Out-Null
    $script:InstalledPackages = @()
    $planMissing = Resolve-DependencyPlan $emptyDir $fakeOps
    Assert-False $planMissing.Ok '缺少依赖且系统无 Runtime 时明确报错'

    # -----------------------------------------------------------------------
    Write-TestSection '卸载：精确匹配与默认参数'
    $pkgExact = New-InstalledPkg '0.3.0.0'
    $pkgLookAlike = [pscustomobject]@{
        Name = 'ApiMonitorToo'
        Version = '1.0.0.0'
        Publisher = 'CN=ApiMonitorDev'
        PackageFullName = 'ApiMonitorToo_1.0.0.0_x64__abc'
        PackageFamilyName = 'ApiMonitorToo_abc'
        Status = 'Ok'
    }
    $script:InstalledPackages = @($pkgLookAlike)
    Assert-True ($null -eq (Get-CurrentUserApiMonitorPackage -Ops $fakeOps)) '相似名称（ApiMonitorToo）不会被匹配'
    $script:InstalledPackages = @($pkgLookAlike, $pkgExact)
    $matched = Get-CurrentUserApiMonitorPackage -Ops $fakeOps
    Assert-Equal 'ApiMonitor_0.3.0.0_x64__cx0n152q1hsh2' $matched.PackageFullName '卸载精确匹配 ApiMonitor'

    $plan = Resolve-UninstallPlan $pkgExact
    Assert-Equal 'Uninstall' $plan.Action '卸载计划：Uninstall'
    Assert-False $plan.AllUsers '卸载计划默认不使用 AllUsers'
    $planNone = Resolve-UninstallPlan $null
    Assert-Equal 'NotInstalled' $planNone.Action '未安装 -> NotInstalled'
    Assert-Equal 10 $planNone.ExitCode '未安装退出码 10'

    $removeResult = Remove-UserPackage $pkgExact.PackageFullName $fakeOps
    Assert-True $removeResult.Ok 'Remove-UserPackage 成功'
    Assert-Equal 'ApiMonitor_0.3.0.0_x64__cx0n152q1hsh2' $script:RemoveCalls[0] '移除命令只传 PackageFullName（无 -AllUsers）'
    Assert-True ((($script:RemoveCalls | Out-String) -match 'AllUsers') -eq $false) '移除调用中从未出现 AllUsers'
    Assert-True (Assert-PackageRemoved -Ops @{ GetAppxPackageForUser = { param($Name) @() } }) '卸载后验证包已不存在'

    # -----------------------------------------------------------------------
    Write-TestSection '证书清理安全决策'
    $script:AllUsersPackages = @([pscustomobject]@{ Name = 'ApiMonitor'; Publisher = 'CN=ApiMonitorDev' })
    $others = Test-OtherApiMonitorDevPackages -Ops $fakeOps
    Assert-True $others.Exists '检测到其他 CN=ApiMonitorDev 包'
    Assert-False (Assert-CertRemovalSafe @([pscustomobject]@{ Name = 'ApiMonitor' })).Ok '有其他相关包时不删除证书'
    Assert-True (Assert-CertRemovalSafe @()).Ok '无其他相关包时允许删除证书'
    $script:AllUsersPackages = @()
    $none = Test-OtherApiMonitorDevPackages -Ops $fakeOps
    Assert-False $none.Exists '无其他 CN=ApiMonitorDev 包时允许清理'
    Assert-Equal 'TrustedPeople' (Get-CertificateStoreTarget) '证书清理目标仍为 TrustedPeople'

    # -----------------------------------------------------------------------
    Write-TestSection '前置检查'
    $preGood = Assert-Prerequisites $script:TempRoot $fakeOps
    Assert-True $preGood.Ok '前置检查在中文+空格路径下通过'
    Assert-Equal $msixName $preGood.MsixName '前置检查识别唯一 MSIX'

    $oldOsOps = @{ GetOsBuild = { 17762 }; Is64BitOs = { $true } }
    $preOld = Assert-Prerequisites $script:TempRoot $oldOsOps
    Assert-False $preOld.Ok 'Windows 10 1809 之前版本被拒绝'
    $x86Ops = @{ GetOsBuild = { 19045 }; Is64BitOs = { $false } }
    $preX86 = Assert-Prerequisites $script:TempRoot $x86Ops
    Assert-False $preX86.Ok 'x86 系统被拒绝'

    $multiDir = Join-Path $script:TempRoot 'multi'
    New-Item -ItemType Directory -Path $multiDir | Out-Null
    Copy-Item -LiteralPath $msixPath -Destination (Join-Path $multiDir $msixName)
    Copy-Item -LiteralPath $msixPath -Destination (Join-Path $multiDir 'ApiMonitor_other_x64.msix')
    Copy-Item -LiteralPath $cerPath -Destination (Join-Path $multiDir 'ApiMonitorDev.cer')
    Copy-Item -LiteralPath $checksumPath -Destination (Join-Path $multiDir 'SHA256SUMS.txt')
    $preMulti = Assert-Prerequisites $multiDir $fakeOps
    Assert-False $preMulti.Ok '多份 ApiMonitor MSIX 被拒绝'

    $emptyPreDir = Join-Path $script:TempRoot 'empty-pre'
    New-Item -ItemType Directory -Path $emptyPreDir | Out-Null
    $preEmpty = Assert-Prerequisites $emptyPreDir $fakeOps
    Assert-False $preEmpty.Ok '缺少必需文件被拒绝'

    # -----------------------------------------------------------------------
    Write-TestSection '提升参数与退出码'
    $scriptPathWithSpaces = Join-Path $script:TempRoot '带空格 目录\Install.ps1'
    New-Item -ItemType Directory -Path (Split-Path $scriptPathWithSpaces) | Out-Null
    Copy-Item -LiteralPath $script:InstallPs1 -Destination $scriptPathWithSpaces
    $argLine = Get-ElevatedArgumentLine $scriptPathWithSpaces
    Assert-True ($argLine -like '-NoProfile -ExecutionPolicy Bypass -File "*"') '提升命令行以 -File "<路径>" 形式构造'
    Assert-True ($argLine -match [regex]::Escape($scriptPathWithSpaces)) '提升命令行包含含空格/中文的脚本路径'

    $installerCode = (Get-InstallerExitCode 'Success'), (Get-InstallerExitCode 'Canceled'), (Get-InstallerExitCode 'HigherVersionInstalled'),
        (Get-InstallerExitCode 'IdentityConflict'), (Get-InstallerExitCode 'SecurityVerificationFailed'), (Get-InstallerExitCode 'PreconditionFailed'),
        (Get-InstallerExitCode 'DependencyMissing'), (Get-InstallerExitCode 'InstallFailed'), (Get-InstallerExitCode 'NotInstalled'),
        (Get-InstallerExitCode 'UninstallFailed'), (Get-InstallerExitCode 'AbortedByUser'), (Get-InstallerExitCode 'CertCleanupBlocked'),
        (Get-InstallerExitCode 'CertCleanupFailed'), (Get-InstallerExitCode 'SameVersionBlocked'),
        (Get-InstallerExitCode 'DestructiveBackupFailed')
    Assert-Equal '0,2,4,5,6,7,8,9,10,11,12,13,14,15,16' ($installerCode -join ',') '退出码表正确'

    # -----------------------------------------------------------------------
    Write-TestSection 'SafeLocalStateBackup：备份与校验'
    $backupRoot = Join-Path $script:TempRoot ('备份 根' + [guid]::NewGuid().ToString('N'))
    $backupSource = Join-Path $script:TempRoot ('源 LocalState ' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $backupSource | Out-Null
    $hiddenDir = Join-Path $backupSource '子目录 中文'
    New-Item -ItemType Directory -Path $hiddenDir | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $backupSource 'accounts.json'), '{ "schemaVersion": 3, "accounts": [] }', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $backupSource 'balance-records.json'), '{ "schemaVersion": 3, "records": [] }', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $backupSource 'tray-settings.json'), '{ "schemaVersion": 5 }', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $backupSource 'floating-window-settings.json'), '{ "schemaVersion": 1 }', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $hiddenDir 'notification-settings.json'), '{ "schemaVersion": 1, "settings": {} }', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $hiddenDir 'app.log'), 'info line', (New-Object System.Text.UTF8Encoding($false)))
    $hiddenFile = Join-Path $backupSource '.hidden-config'
    [System.IO.File]::WriteAllText($hiddenFile, 'hidden', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::SetAttributes($hiddenFile, [System.IO.FileAttributes]::Hidden)

    $backupResult = Backup-SafeLocalState `
        -Source $backupSource `
        -BackupRoot $backupRoot `
        -PackageFamilyName 'ApiMonitor_cx0n152q1hsh2' `
        -AppVersion '0.8.0.0'
    Assert-True $backupResult.Ok '备份成功（空格+中文路径、多层子目录、隐藏文件）'
    Assert-Equal 7 $backupResult.FileCount '备份文件数量正确（含隐藏文件与子目录文件）'
    $backupDir = $backupResult.BackupDir
    Assert-True (Test-Path -LiteralPath $backupDir) '备份目录存在'
    Assert-True (Test-Path -LiteralPath (Join-Path $backupDir '子目录 中文\app.log')) '子目录文件已备份'
    Assert-True (Test-Path -LiteralPath (Join-Path $backupDir '.hidden-config')) '隐藏文件已备份'
    $manifest = Get-Content (Join-Path $backupDir 'LocalState-backup-manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Assert-Equal 'ApiMonitor_cx0n152q1hsh2' $manifest.packageFamilyName '清单 Package Family 正确'
    Assert-Equal '0.8.0.0' $manifest.appVersion '清单应用版本正确'
    Assert-True ($manifest.files.Count -eq 7) '清单文件条目数量正确'
    Assert-True ($manifest.files[0].PSObject.Properties.Name -notcontains 'apiKey') '清单不含 apiKey'
    Assert-True ($manifest.files[0].PSObject.Properties.Name -notcontains 'credential') '清单不含凭据字段'

    $backupValidation = Test-SafeLocalStateBackup -Source $backupSource -BackupDir $backupDir
    Assert-True $backupValidation.Ok '备份校验通过（数量/字节/哈希/JSON）'

    # 空源目录被识别（不崩溃），且空备份不能通过验证。
    $emptySource = Join-Path $script:TempRoot 'empty-source'
    New-Item -ItemType Directory -Path $emptySource | Out-Null
        $emptyBackup = Backup-SafeLocalState -Source $emptySource -BackupRoot (Join-Path $script:TempRoot 'empty-bak') -PackageFamilyName 'ApiMonitor_cx0n152q1hsh2' -AppVersion '0.8.0.0'
    Assert-False $emptyBackup.Ok '空源目录被识别且验证失败（缺少核心 JSON）'

    $emptyDirValidation = Test-SafeLocalStateBackup -BackupDir (Join-Path $script:TempRoot 'not-a-backup')
    Assert-False $emptyDirValidation.Ok '空备份目录不能通过验证'

    # 数量不一致。
    $countBroken = Join-Path $script:TempRoot 'count-broken'
    New-Item -ItemType Directory -Path $countBroken | Out-Null
    Copy-Item -LiteralPath (Join-Path $backupDir 'accounts.json') -Destination $countBroken
    $countValidation = Test-SafeLocalStateBackup -BackupDir $countBroken
    Assert-False $countValidation.Ok '备份数量不一致时失败'

    # 大小/哈希不一致。
    $tamperedDir = Join-Path $script:TempRoot 'tampered'
    Copy-Item -LiteralPath $backupDir -Destination $tamperedDir -Recurse -Force
    $tamperedFile = Join-Path $tamperedDir 'accounts.json'
    [System.IO.File]::AppendAllText($tamperedFile, 'tampered', (New-Object System.Text.UTF8Encoding($false)))
    $tamperedValidation = Test-SafeLocalStateBackup -BackupDir $tamperedDir
    Assert-False $tamperedValidation.Ok '文件大小/哈希不一致时失败'

    # 损坏 JSON。
    $corruptSource = Join-Path $script:TempRoot ('corrupt ' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $corruptSource | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $corruptSource 'accounts.json'), '{ broken !!!', (New-Object System.Text.UTF8Encoding($false)))
        $corruptBackup = Backup-SafeLocalState -Source $corruptSource -BackupRoot (Join-Path $script:TempRoot 'corrupt-bak') -PackageFamilyName 'ApiMonitor_cx0n152q1hsh2' -AppVersion '0.8.0.0'
    Assert-False $corruptBackup.Ok 'JSON 损坏时备份验证失败'

    # 0 字节文件。
    $zeroSource = Join-Path $script:TempRoot ('zero ' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $zeroSource | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $zeroSource 'accounts.json'), '{ "schemaVersion": 3, "accounts": [] }', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $zeroSource 'tray-settings.json'), '', (New-Object System.Text.UTF8Encoding($false)))
        $zeroBackup = Backup-SafeLocalState -Source $zeroSource -BackupRoot (Join-Path $script:TempRoot 'zero-bak') -PackageFamilyName 'ApiMonitor_cx0n152q1hsh2' -AppVersion '0.8.0.0'
    Assert-False $zeroBackup.Ok '0 字节文件导致备份验证失败'

    # v0.6.0 升级源：只有旧 compact-window-settings.json（尚无 floating 文件）仍视为核心文件齐全。
    $legacySource = Join-Path $script:TempRoot ('legacy ' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $legacySource | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $legacySource 'accounts.json'), '{ "schemaVersion": 3, "accounts": [] }', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $legacySource 'balance-records.json'), '{ "schemaVersion": 3, "records": [] }', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $legacySource 'tray-settings.json'), '{ "schemaVersion": 5 }', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $legacySource 'compact-window-settings.json'), '{ "schemaVersion": 3 }', (New-Object System.Text.UTF8Encoding($false)))
    $legacyBackup = Backup-SafeLocalState -Source $legacySource -BackupRoot (Join-Path $script:TempRoot 'legacy-bak') -PackageFamilyName 'ApiMonitor_cx0n152q1hsh2' -AppVersion '0.6.0.1'
    Assert-True $legacyBackup.Ok '仅含旧 compact-window-settings.json 的升级源备份成功'

    # -----------------------------------------------------------------------
    Write-TestSection 'Restore-SafeLocalState：二次备份与保护'
    $restoreTarget = Join-Path $script:TempRoot 'restore-target'
    New-Item -ItemType Directory -Path $restoreTarget | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $restoreTarget 'accounts.json'), '{ "schemaVersion": 3, "accounts": [] }', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $restoreTarget 'tray-settings.json'), '{ "schemaVersion": 5 }', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $restoreTarget 'balance-records.json'), '{ "schemaVersion": 3, "records": [] }', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $restoreTarget 'floating-window-settings.json'), '{ "schemaVersion": 1 }', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $restoreTarget 'new-unknown.json'), '{}', (New-Object System.Text.UTF8Encoding($false)))

    $restoreBlocked = Restore-SafeLocalState `
        -BackupDir $backupDir `
        -Target $restoreTarget `
        -TargetPackageFamilyName 'ApiMonitor_cx0n152q1hsh2' `
        -RestoreBackupRoot (Join-Path $script:TempRoot 'restore-bak')
    Assert-False $restoreBlocked.Ok '目标存在备份之外的新文件时拒绝覆盖'

    Remove-Item -LiteralPath (Join-Path $restoreTarget 'new-unknown.json') -Force
    $restoreOk = Restore-SafeLocalState `
        -BackupDir $backupDir `
        -Target $restoreTarget `
        -TargetPackageFamilyName 'ApiMonitor_cx0n152q1hsh2' `
        -RestoreBackupRoot (Join-Path $script:TempRoot 'restore-bak-ok')
    Assert-True $restoreOk.Ok '恢复成功（先二次备份、逐文件写入、JSON 复验）'
    Assert-True ($restoreOk.SecondBackupDir -and (Test-Path -LiteralPath $restoreOk.SecondBackupDir)) '恢复前创建当前数据二次备份'
    Assert-True (Test-Path -LiteralPath (Join-Path $restoreTarget '.hidden-config')) '恢复包含隐藏文件'

    $pfmBlocked = Restore-SafeLocalState `
        -BackupDir $backupDir `
        -Target $restoreTarget `
        -TargetPackageFamilyName 'ApiMonitor_OTHER' `
        -RestoreBackupRoot (Join-Path $script:TempRoot 'restore-bak-pfm')
    Assert-False $pfmBlocked.Ok 'Package Family 不一致时拒绝恢复'

    # -----------------------------------------------------------------------
    Write-TestSection '同版本安装保护与破坏性重装'
    $fakeLocalState = Join-Path $script:TempRoot 'flow-localstate'
    New-Item -ItemType Directory -Path $fakeLocalState | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $fakeLocalState 'accounts.json'), '{ "schemaVersion": 3, "accounts": [] }', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $fakeLocalState 'tray-settings.json'), '{ "schemaVersion": 5 }', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $fakeLocalState 'balance-records.json'), '{ "schemaVersion": 3, "records": [] }', (New-Object System.Text.UTF8Encoding($false)))
    [System.IO.File]::WriteAllText((Join-Path $fakeLocalState 'floating-window-settings.json'), '{ "schemaVersion": 1 }', (New-Object System.Text.UTF8Encoding($false)))

    $flowOps = @{ }
    foreach ($key in $fakeOps.Keys) { $flowOps[$key] = $fakeOps[$key] }
    $flowOps['ResolveScriptDir'] = { $script:TempRoot }
    $flowOps['ResolveLocalState'] = { param($PackageFamilyName) $fakeLocalState }
    $flowOps['AddAppxPackage'] = {
        param($MainPath, [string[]]$DependencyPaths)
        $script:AddAppxCalls++
        $script:InstalledPackages = @($script:InstalledPackages | Where-Object { $_.Name -ne 'ApiMonitor' })
        $script:InstalledPackages += New-InstalledPkg '0.8.0.0'
        $true
    }
    $flowOps['RemoveAppxPackageForUser'] = {
        param($PackageFullName)
        $script:RemoveCalls += $PackageFullName
        $script:InstalledPackages = @($script:InstalledPackages | Where-Object { $_.PackageFullName -ne $PackageFullName })
        $true
    }

    $script:InstalledPackages = @(
        [pscustomobject]@{ Name = 'Microsoft.WindowsAppRuntime.2'; Version = '2.3.1.0'; PackageFullName = 'Microsoft.WindowsAppRuntime.2_2.3.1.0_x64__8wekyb3d8bbwe'; Publisher = 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US' },
        (New-InstalledPkg '0.8.0.0')
    )
    $script:AddAppxCalls = 0
    $script:RemoveCalls = @()
    $sameCode = Invoke-Install $flowOps
    Assert-Equal 15 $sameCode '相同版本默认停止（SameVersionBlocked=15）'
    Assert-Equal 0 $script:AddAppxCalls '相同版本不会调用 Add-AppxPackage'
    Assert-Equal 0 $script:RemoveCalls.Count '相同版本不会调用卸载'

    # 静默模式禁止破坏性重装。
    $ForceDestructiveReinstall = $true
    $script:AddAppxCalls = 0
    $script:RemoveCalls = @()
    $quietFlowOps = @{ }
    foreach ($key in $flowOps.Keys) { $quietFlowOps[$key] = $flowOps[$key] }
    $Quiet = $true
    $quietCode = Invoke-Install $quietFlowOps
    Assert-Equal 2 $quietCode '静默模式破坏性重装被拒绝（Canceled=2）'
    Assert-Equal 0 $script:RemoveCalls.Count '静默模式不会调用卸载'
    $Quiet = $false

    # 备份失败时破坏性重装停止，不卸载。
    function Confirm-DestructiveReinstall { return $true }
    $script:AddAppxCalls = 0
    $script:RemoveCalls = @()
    $brokenFlowOps = @{ }
    foreach ($key in $flowOps.Keys) { $brokenFlowOps[$key] = $flowOps[$key] }
    $brokenFlowOps['ResolveLocalState'] = { param($PackageFamilyName) (Join-Path $script:TempRoot 'missing-localstate') }
    $brokenCode = Invoke-Install $brokenFlowOps
    Assert-Equal 16 $brokenCode '破坏性重装备份失败时停止（DestructiveBackupFailed=16）'
    Assert-Equal 0 $script:RemoveCalls.Count '备份失败后不会继续卸载'

    # 破坏性重装（显式确认 + 备份成功）才允许卸载重装。
    $script:AddAppxCalls = 0
    $script:RemoveCalls = @()
    $destructiveCode = Invoke-Install $flowOps
    Assert-Equal 0 $destructiveCode '显式确认 + 备份成功时破坏性重装完成'
    Assert-Equal 1 $script:RemoveCalls.Count '破坏性重装显式确认后执行卸载'
    Assert-Equal 1 $script:AddAppxCalls '破坏性重装后重新安装'
    $savedBackups = @(Get-ChildItem (Join-Path $env:TEMP 'ApiMonitor-LocalState-Backups') -Directory -ErrorAction SilentlyContinue)
    Assert-True ($savedBackups.Count -ge 1) '破坏性重装前 LocalState 已备份到临时目录'

    # 原地升级：保留 LocalState、不卸载、不操作 Credential Locker。
    $script:InstalledPackages = @(
        [pscustomobject]@{ Name = 'Microsoft.WindowsAppRuntime.2'; Version = '2.3.1.0'; PackageFullName = 'Microsoft.WindowsAppRuntime.2_2.3.1.0_x64__8wekyb3d8bbwe'; Publisher = 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US' },
        (New-InstalledPkg '0.6.0.1')
    )
    $script:AddAppxCalls = 0
    $script:RemoveCalls = @()
    $beforeUpgradeHash = (Get-FileHash -LiteralPath (Join-Path $fakeLocalState 'accounts.json') -Algorithm SHA256).Hash
    $upgradeCode = Invoke-Install $flowOps
    Assert-Equal 0 $upgradeCode 'v0.6.0.1 -> v0.8.0.0 原地升级成功'
    Assert-Equal 0 $script:RemoveCalls.Count '原地升级不会调用卸载'
    Assert-Equal 1 $script:AddAppxCalls '原地升级只调用一次 Add-AppxPackage'
    Assert-True (Test-Path -LiteralPath (Join-Path $fakeLocalState 'accounts.json')) '原地升级保留 LocalState'
    $afterUpgradeHash = (Get-FileHash -LiteralPath (Join-Path $fakeLocalState 'accounts.json') -Algorithm SHA256).Hash
    Assert-Equal $beforeUpgradeHash $afterUpgradeHash '原地升级不修改 LocalState 文件内容'
    $ForceDestructiveReinstall = $false

    # -----------------------------------------------------------------------
    Write-TestSection '完整安装器脚本可解析'
    $tokens = $null; $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($script:InstallPs1, [ref]$tokens, [ref]$parseErrors) | Out-Null
    Assert-Equal 0 $parseErrors.Count 'Install.ps1 语法解析无错误'
    $tokens2 = $null; $parseErrors2 = $null
    [System.Management.Automation.Language.Parser]::ParseFile($script:UninstallPs1, [ref]$tokens2, [ref]$parseErrors2) | Out-Null
    Assert-Equal 0 $parseErrors2.Count 'Uninstall.ps1 语法解析无错误'
    $tokens3 = $null; $parseErrors3 = $null
    [System.Management.Automation.Language.Parser]::ParseFile($script:BackupToolPath, [ref]$tokens3, [ref]$parseErrors3) | Out-Null
    Assert-Equal 0 $parseErrors3.Count 'SafeLocalStateBackup.ps1 语法解析无错误'

} finally {
    if ($script:TempRoot -and (Test-Path -LiteralPath $script:TempRoot)) {
        Remove-Item -LiteralPath $script:TempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "`n=============================================="
Write-Host ("Installer tests: PASS {0}  FAIL {1}" -f $script:PassCount, $script:FailCount)
if ($script:FailCount -gt 0) {
    Write-Host 'Failed assertions:'
    $script:Failures | ForEach-Object { Write-Host ('  - ' + $_) -ForegroundColor Red }
    exit 1
}
exit 0
