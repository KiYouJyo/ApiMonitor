#requires -Version 5.1
<#
ApiMonitor v0.9.0 uninstaller
  =============================
  Double-click entry: Uninstall.cmd -> this script (Windows PowerShell 5.1).

  Default behavior:
  - Remove the ApiMonitor package for the current user only (exact identity match).
  - Never uses -AllUsers for the removal.
  - After uninstall, optionally removes the ApiMonitorDev self-signed certificate
    from LocalMachine\TrustedPeople (elevated, full-thumbprint match, only when no
    other package still uses the CN=ApiMonitorDev publisher).

  Advanced use:
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File Uninstall.ps1 -RemoveCertificate
#>

[CmdletBinding()]
param(
    [string]$PackageIdentity = 'ApiMonitor',
    [string]$PublisherSubject = 'CN=ApiMonitorDev',
    [string]$CertificateThumbprint = '545198E3BC78BE49BDF861C3EA6863FFD285689F',
    [switch]$RemoveCertificate,
    [switch]$CertificateCleanupOnly,
    [switch]$Quiet
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

$script:UninstallLogPath = $null
$script:QuietMode = $false

function Get-DefaultOps {
    return @{
        GetAppxPackageForUser = {
            param($Name)
            Get-AppxPackage -Name $Name -ErrorAction SilentlyContinue
        }
        GetAppxPackageAllUsers = {
            param($Publisher)
            @(Get-AppxPackage -AllUsers -ErrorAction SilentlyContinue |
                Where-Object { ([string]$_.Publisher).Trim() -eq $Publisher })
        }
        TestIsAdministrator = {
            ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
                [Security.Principal.WindowsBuiltInRole]::Administrator)
        }
        StartProcessElevated = {
            param($FilePath, $ArgumentList)
            Start-Process -FilePath $FilePath -Verb RunAs -ArgumentList $ArgumentList -Wait -PassThru
        }
        GetRunningProcess = {
            param($Name)
            @(Get-Process -Name $Name -ErrorAction SilentlyContinue)
        }
        CloseMainWindow = {
            param($Process)
            [void]$Process.CloseMainWindow()
        }
        StopProcess = {
            param($Process)
            $Process | Stop-Process -Force -ErrorAction SilentlyContinue
        }
        RemoveAppxPackageForUser = {
            param($PackageFullName)
            Remove-AppxPackage -Package $PackageFullName
        }
        GetTrustedPeopleCert = {
            param($Thumbprint)
            Get-ChildItem -LiteralPath 'Cert:\LocalMachine\TrustedPeople' -ErrorAction SilentlyContinue |
                Where-Object { $_.Thumbprint -eq $Thumbprint } |
                Select-Object -First 1
        }
        RemoveTrustedPeopleCertByThumbprint = {
            param($Thumbprint)
            $targets = @(Get-ChildItem -LiteralPath 'Cert:\LocalMachine\TrustedPeople' -ErrorAction SilentlyContinue |
                Where-Object { $_.Thumbprint -eq $Thumbprint })
            foreach ($target in $targets) {
                Remove-Item -LiteralPath $target.PSPath -Force
            }
            $remaining = @(Get-ChildItem -LiteralPath 'Cert:\LocalMachine\TrustedPeople' -ErrorAction SilentlyContinue |
                Where-Object { $_.Thumbprint -eq $Thumbprint })
            return ($remaining.Count -eq 0)
        }
    }
}

function Write-UninstallerLog {
    param(
        [Parameter(Mandatory = $true)][string]$Message,
        [ValidateSet('INFO', 'OK', 'WARN', 'ERROR')][string]$Level = 'INFO'
    )
    $line = ('[{0}] [{1}] {2}' -f (Get-Date -Format 'HH:mm:ss'), $Level, $Message)
    if (-not $script:QuietMode) {
        Write-Host $line
    }
    if ($script:UninstallLogPath) {
        try {
            [System.IO.File]::AppendAllText(
                $script:UninstallLogPath,
                $line + [Environment]::NewLine,
                (New-Object System.Text.UTF8Encoding($false)))
        } catch {
            # Logging must never abort uninstallation.
        }
    }
}

function Get-InstallerExitCode {
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
    return 'TrustedPeople'
}

function Get-ElevatedArgumentLine {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [switch]$IncludeCertificateCleanup
    )
    $line = '-NoProfile -ExecutionPolicy Bypass -File "{0}"' -f $ScriptPath
    if ($IncludeCertificateCleanup) {
        $line += ' -RemoveCertificate -CertificateCleanupOnly'
    }
    return $line
}

function Get-CurrentUserApiMonitorPackage {
    <#
      Exact match on Package Identity name only. Get-AppxPackage -Name is already an
      exact filter; we additionally require $_.Name -eq $Name so a similarly named
      package is never mistaken for ApiMonitor.
    #>
    param(
        [string]$Name = 'ApiMonitor',
        [hashtable]$Ops
    )
    if ($null -eq $Ops) { $Ops = Get-DefaultOps }
    $found = @(& $Ops.GetAppxPackageForUser $Name | Where-Object { $_.Name -eq $Name })
    if ($found.Count -eq 0) { return $null }
    return $found[0]
}

function Resolve-UninstallPlan {
    <#
      The default plan removes the package for the current user only.
      AllUsers is always $false unless the caller explicitly overrides it.
    #>
    param($Package)
    if (-not $Package) {
        return @{
            Action    = 'NotInstalled'
            ExitCode  = (Get-InstallerExitCode 'NotInstalled')
            AllUsers  = $false
        }
    }
    return @{
        Action           = 'Uninstall'
        PackageFullName  = $Package.PackageFullName
        AllUsers         = $false
        Version          = $Package.Version
        PackageFamilyName = $Package.PackageFamilyName
        ExitCode         = (Get-InstallerExitCode 'Success')
    }
}

function Invoke-GracefulClose {
    param([hashtable]$Ops)
    if ($null -eq $Ops) { $Ops = Get-DefaultOps }
    $procs = @(& $Ops.GetRunningProcess 'ApiMonitor')
    if ($procs.Count -eq 0) { return @{ Ok = $true; Closed = $false } }

    Write-UninstallerLog '检测到 ApiMonitor 正在运行，先请求正常关闭……' 'INFO'
    foreach ($proc in $procs) { & $Ops.CloseMainWindow $proc }

    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline -and (@(& $Ops.GetRunningProcess 'ApiMonitor')).Count -gt 0) {
        Start-Sleep -Milliseconds 500
    }
    if ((@(& $Ops.GetRunningProcess 'ApiMonitor')).Count -eq 0) {
        return @{ Ok = $true; Closed = $true }
    }
    return @{ Ok = $false; Closed = $false; Reason = 'ApiMonitor 正在运行且无法正常关闭。' }
}

function Remove-UserPackage {
    param(
        [Parameter(Mandatory = $true)][string]$PackageFullName,
        [hashtable]$Ops
    )
    if ($null -eq $Ops) { $Ops = Get-DefaultOps }
    try {
        & $Ops.RemoveAppxPackageForUser $PackageFullName
        return @{ Ok = $true }
    } catch {
        return @{ Ok = $false; Reason = $_.Exception.Message }
    }
}

function Assert-PackageRemoved {
    param(
        [string]$Name = 'ApiMonitor',
        [hashtable]$Ops
    )
    if ($null -eq $Ops) { $Ops = Get-DefaultOps }
    $remaining = @(& $Ops.GetAppxPackageForUser $Name | Where-Object { $_.Name -eq $Name })
    return ($remaining.Count -eq 0)
}

function Test-OtherApiMonitorDevPackages {
    <#
      Looks across all users for packages still published by CN=ApiMonitorDev.
      Requires elevation; callers only invoke this in the elevated context.
    #>
    param(
        [string]$Publisher = 'CN=ApiMonitorDev',
        [hashtable]$Ops
    )
    if ($null -eq $Ops) { $Ops = Get-DefaultOps }
    $all = @(& $Ops.GetAppxPackageAllUsers $Publisher)
    return @{ Exists = ($all.Count -gt 0); Count = $all.Count }
}

function Assert-CertRemovalSafe {
    <#
      Only safe to delete the certificate when no other package still uses the
      CN=ApiMonitorDev publisher.
    #>
    param([object[]]$OtherPackages)
    if ($OtherPackages -and $OtherPackages.Count -gt 0) {
        return @{ Ok = $false; Count = $OtherPackages.Count }
    }
    return @{ Ok = $true; Count = 0 }
}

function Invoke-CertificateCleanup {
    <#
      Elevated path: verify no other CN=ApiMonitorDev packages exist for any user,
      then delete exactly the matching thumbprint from LocalMachine\TrustedPeople.
    #>
    param([hashtable]$Ops)
    if ($null -eq $Ops) { $Ops = Get-DefaultOps }

    if (-not (& $Ops.TestIsAdministrator)) {
        Write-UninstallerLog '证书清理需要管理员权限，正在请求 UAC 提升……'
        try {
            $argumentLine = Get-ElevatedArgumentLine $PSCommandPath -IncludeCertificateCleanup
            $process = & $Ops.StartProcessElevated 'powershell.exe' $argumentLine
            if ($process) { return $process.ExitCode }
            Write-UninstallerLog '证书清理已取消（未同意 UAC 提升）。' 'WARN'
            return (Get-InstallerExitCode 'Canceled')
        } catch {
            Write-UninstallerLog '证书清理已取消（未同意 UAC 提升）。' 'WARN'
            return (Get-InstallerExitCode 'Canceled')
        }
    }

    Write-UninstallerLog '检查所有用户的 CN=ApiMonitorDev 已安装包……'
    $other = Test-OtherApiMonitorDevPackages -Publisher $script:PublisherSubject -Ops $Ops
    if ($other.Exists) {
        Write-UninstallerLog (
            '仍有 {0} 个使用 Publisher=CN=ApiMonitorDev 的包已安装（可能在其他用户下），证书不会被删除。' -f $other.Count) 'WARN'
        return (Get-InstallerExitCode 'CertCleanupBlocked')
    }

    $storeTarget = Get-CertificateStoreTarget
    $cert = & $Ops.GetTrustedPeopleCert $script:CertificateThumbprint
    if (-not $cert) {
        Write-UninstallerLog 'Local Machine\TrustedPeople 中不存在目标证书，无需清理。' 'OK'
        return (Get-InstallerExitCode 'Success')
    }

    Write-UninstallerLog (
        '删除证书：{0} / {1}（仅 Local Machine\TrustedPeople，按完整 Thumbprint 精确匹配）。' -f
            $cert.Subject, $cert.Thumbprint) 'INFO'
    try {
        $removed = & $Ops.RemoveTrustedPeopleCertByThumbprint $script:CertificateThumbprint
        if (-not $removed) {
            Write-UninstallerLog '证书删除失败：目标证书仍存在。' 'ERROR'
            return (Get-InstallerExitCode 'CertCleanupFailed')
        }
        Write-UninstallerLog '证书已删除。' 'OK'
        return (Get-InstallerExitCode 'Success')
    } catch {
        Write-UninstallerLog ('证书删除失败：{0}' -f $_.Exception.Message) 'ERROR'
        return (Get-InstallerExitCode 'CertCleanupFailed')
    }
}

function Invoke-Uninstall {
    param([hashtable]$Ops)
    if ($null -eq $Ops) { $Ops = Get-DefaultOps }

    # Certificate-only path used by the elevated relaunch.
    if ($CertificateCleanupOnly) {
        return (Invoke-CertificateCleanup $Ops)
    }

    $package = Get-CurrentUserApiMonitorPackage -Name $script:PackageIdentity -Ops $Ops
    $plan = Resolve-UninstallPlan $package
    if ($plan.Action -eq 'NotInstalled') {
        Write-UninstallerLog '未检测到当前用户安装的 ApiMonitor，无需卸载。' 'WARN'
        if ($RemoveCertificate) {
            return (Invoke-CertificateCleanup $Ops)
        }
        return $plan.ExitCode
    }

    Write-UninstallerLog '==== ApiMonitor 卸载开始 ===='
    Write-UninstallerLog ('已安装：{0}（{1}）' -f $plan.Version, $plan.PackageFamilyName)
    Write-UninstallerLog '卸载将移除当前用户的 ApiMonitor 应用；本地账户、历史、设置与 Credential Locker 凭据可能不再可用，卸载程序不会承诺保留这些数据。' 'WARN'

    # Graceful close, then ask before forcing.
    $close = Invoke-GracefulClose $Ops
    if (-not $close.Ok) {
Write-UninstallerLog 'ApiMonitor 正在运行且无法通过关闭窗口退出（v0.9.0 关闭主窗口仅隐藏到通知区域）。请先从通知区域托盘菜单选择“退出 ApiMonitor”。' 'WARN'
        $answer = Read-Host 'ApiMonitor 正在运行且无法正常关闭，是否强制结束进程以继续卸载？[Y/N]（默认 Y）'
        if ($answer -ne '' -and $answer -notmatch '^(y|Y|是)$') {
            Write-UninstallerLog '用户取消卸载。' 'WARN'
            return (Get-InstallerExitCode 'AbortedByUser')
        }
        foreach ($proc in @(& $Ops.GetRunningProcess 'ApiMonitor')) {
            & $Ops.StopProcess $proc
        }
    }

    # Current-user removal only (no -AllUsers).
    $result = Remove-UserPackage $plan.PackageFullName $Ops
    if (-not $result.Ok) {
        Write-UninstallerLog ('卸载失败：{0}' -f $result.Reason) 'ERROR'
        return (Get-InstallerExitCode 'UninstallFailed')
    }

    if (-not (Assert-PackageRemoved -Name $script:PackageIdentity -Ops $Ops)) {
        Write-UninstallerLog '卸载后验证失败：当前用户的 ApiMonitor 仍存在。' 'ERROR'
        return (Get-InstallerExitCode 'UninstallFailed')
    }
    Write-UninstallerLog 'ApiMonitor 已成功卸载，当前用户包已不存在。' 'OK'

    # Certificate cleanup prompt (default: keep the certificate).
    if ($RemoveCertificate) {
        return (Invoke-CertificateCleanup $Ops)
    }
    $answer = Read-Host '是否同时移除 ApiMonitor 自签名开发证书？[Y/N]（默认 N）'
    if ($answer -match '^(y|Y|是)$') {
        return (Invoke-CertificateCleanup $Ops)
    }
    Write-UninstallerLog '保留 ApiMonitorDev 证书（未执行证书清理）。' 'OK'
    return (Get-InstallerExitCode 'Success')
}

if ($MyInvocation.InvocationName -ne '.') {
    $script:QuietMode = [bool]$Quiet
    if (-not $Quiet) {
        $script:UninstallLogPath = Join-Path $env:TEMP ('ApiMonitor-Uninstall-{0}.log' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
        try {
            [System.IO.File]::WriteAllText($script:UninstallLogPath, '', (New-Object System.Text.UTF8Encoding($false)))
            Write-UninstallerLog ('卸载日志：{0}' -f $script:UninstallLogPath)
        } catch {
            $script:UninstallLogPath = $null
        }
    }
    exit (Invoke-Uninstall (Get-DefaultOps))
}
