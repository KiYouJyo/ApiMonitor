#requires -Version 5.1
<#
  SafeLocalStateBackup.ps1
  ========================
  统一的 ApiMonitor 本地数据备份/校验/恢复函数库，供验收与发布脚本调用。

  安全规则（对应 v0.5.0 候选包替换事故的修复）：
  - 备份源必须通过当前 Package Family 动态解析实际 LocalState，不依赖模糊路径；
  - 禁止 `-LiteralPath` 后附加通配符；逐项枚举后逐项 Copy-Item -LiteralPath；
  - 备份后必须逐项验证（数量/字节/哈希/JSON/非零/清单），任一失败即停止破坏性操作；
  - 备份清单只包含：相对文件名、文件大小、SHA-256、备份时间、Package Family、应用版本；
    绝不包含 API Key、Credential Locker 内容或 Authorization；
  - 恢复只处理普通文件；Credential Locker 密钥无法通过 LocalState 文件备份恢复。

  CI 中只使用临时目录与假数据。
#>

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function Get-SafeLocalStateDefaultOps {
    <# 可替换的系统操作（测试注入假实现，真实运行使用默认实现）。 #>
    return @{
        GetAppxPackage = {
            param($Name)
            Get-AppxPackage -Name $Name -ErrorAction SilentlyContinue
        }
        TestPath = { param($Path) Test-Path -LiteralPath $Path }
        NewDirectory = {
            param($Path)
            New-Item -ItemType Directory -Path $Path -Force | Out-Null
        }
        EnumerateFiles = {
            param($Path)
            @(Get-ChildItem -LiteralPath $Path -Recurse -Force -File)
        }
        CopyFile = {
            param($Source, $Destination)
            Copy-Item -LiteralPath $Source -Destination $Destination -Force
        }
        GetFileHash = {
            param($Path)
            (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
        }
        GetFileLength = {
            param($Path)
            (Get-Item -LiteralPath $Path -Force).Length
        }
        WriteAllText = {
            param($Path, $Content)
            [System.IO.File]::WriteAllText($Path, $Content, (New-Object System.Text.UTF8Encoding($false)))
        }
        ReadAllText = {
            param($Path)
            [System.IO.File]::ReadAllText($Path)
        }
        MoveFile = {
            param($Source, $Destination)
            Move-Item -LiteralPath $Source -Destination $Destination -Force
        }
        RemoveFile = {
            param($Path)
            if (Test-Path -LiteralPath $Path) {
                Remove-Item -LiteralPath $Path -Force
            }
        }
    }
}

function Merge-SafeLocalStateOps {
    <# 调用方提供的 Ops 覆盖默认实现；缺省键回退到真实实现。 #>
    param([hashtable]$Ops)
    $effective = Get-SafeLocalStateDefaultOps
    if ($null -ne $Ops) {
        foreach ($key in @($Ops.Keys)) {
            $effective[$key] = $Ops[$key]
        }
    }
    return $effective
}
function Resolve-SafeLocalStatePackageFamily {
    <#
      通过当前安装的 ApiMonitor 包解析 Package Family（动态，不依赖模糊路径）。
      返回字符串或 $null（未安装）。
    #>
    param(
        [string]$PackageName = 'ApiMonitor',
        [hashtable]$Ops
    )
    $Ops = Merge-SafeLocalStateOps $Ops
    $pkg = @(& $Ops.GetAppxPackage $PackageName | Where-Object { $_.Name -eq $PackageName }) | Select-Object -First 1
    if (-not $pkg -or [string]::IsNullOrWhiteSpace([string]$pkg.PackageFamilyName)) {
        return $null
    }
    return ([string]$pkg.PackageFamilyName).Trim()
}

function Resolve-SafeLocalStateDirectory {
    <# 按 Package Family 解析真实 LocalState 目录；未安装时返回 $null。 #>
    param(
        [string]$PackageName = 'ApiMonitor',
        [hashtable]$Ops
    )
    $family = Resolve-SafeLocalStatePackageFamily -PackageName $PackageName -Ops $Ops
    if (-not $family) {
        return $null
    }
    return (Join-Path (Join-Path $env:LOCALAPPDATA 'Packages') (Join-Path $family 'LocalState'))
}

function Backup-SafeLocalState {
    <#
      把 Source（LocalState）整体备份到 BackupRoot 下的时间戳目录。
      逐项复制（-LiteralPath，禁止通配符），随后执行完整验证；
      任一验证失败时 Ok=false 并记录具体错误。
      返回：Ok / BackupDir / ManifestPath / FileCount / TotalBytes / Errors / Warnings。
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$BackupRoot,
        [Parameter(Mandatory = $true)][string]$PackageFamilyName,
        [Parameter(Mandatory = $true)][string]$AppVersion,
        [hashtable]$Ops
    )
    $Ops = Merge-SafeLocalStateOps $Ops

    $result = @{
        Ok          = $false
        BackupDir   = $null
        ManifestPath = $null
        FileCount   = 0
        TotalBytes  = 0
        Errors      = @()
        Warnings    = @()
    }

    if (-not (& $Ops.TestPath $Source)) {
        $result.Errors += "源目录不存在：$Source"
        return $result
    }

    $sourceFull = [System.IO.Path]::GetFullPath($Source)
    $stamp = Get-Date -Format 'yyyyMMddHHmmssfff'
    $backupDir = Join-Path $BackupRoot ("LocalState-backup-" + $stamp)
    & $Ops.NewDirectory $backupDir
    $result.BackupDir = [System.IO.Path]::GetFullPath($backupDir)

    $sourceItems = @(& $Ops.EnumerateFiles $sourceFull)
    foreach ($item in $sourceItems) {
        $rel = $item.FullName.Substring($sourceFull.Length).TrimStart('\', '/')
        if ([string]::IsNullOrWhiteSpace($rel)) {
            continue
        }

        $dest = Join-Path $backupDir $rel
        $destDir = Split-Path -Parent $dest
        if (-not (& $Ops.TestPath $destDir)) {
            & $Ops.NewDirectory $destDir
        }

        try {
            & $Ops.CopyFile $item.FullName $dest
        }
        catch {
            $result.Errors += "复制失败：$rel（$($_.Exception.Message)）"
        }
    }

    if ($result.Errors.Count -gt 0) {
        return $result
    }

    # 生成备份清单（只含相对文件名/大小/SHA-256/备份时间/Package Family/应用版本）。
    $manifestEntries = @()
    $totalBytes = [long]0
    foreach ($item in $sourceItems) {
        $rel = $item.FullName.Substring($sourceFull.Length).TrimStart('\', '/')
        if ([string]::IsNullOrWhiteSpace($rel)) {
            continue
        }

        $hash = & $Ops.GetFileHash $item.FullName
        $manifestEntries += @{
            relativePath = $rel
            size         = [long]$item.Length
            sha256       = $hash
        }
        $totalBytes += [long]$item.Length
    }

    $manifest = @{
        packageFamilyName = $PackageFamilyName
        appVersion        = $AppVersion
        backedUpAtUtc     = (Get-Date).ToUniversalTime().ToString('o')
        files             = @($manifestEntries)
    }
    $manifestPath = Join-Path $backupDir 'LocalState-backup-manifest.json'
    & $Ops.WriteAllText $manifestPath ($manifest | ConvertTo-Json -Depth 5)
    $result.ManifestPath = $manifestPath
    $result.FileCount = $sourceItems.Count
    $result.TotalBytes = $totalBytes

    $validation = Test-SafeLocalStateBackup -Source $sourceFull -BackupDir $result.BackupDir -Ops $Ops
    if (-not $validation.Ok) {
        $result.Errors += $validation.Errors
        return $result
    }

    if ($validation.FileCount -ne $result.FileCount -or $validation.TotalBytes -ne $result.TotalBytes) {
        $result.Errors += '备份数量/字节校验不一致。'
        return $result
    }

    $result.Ok = $true
    return $result
}

function Test-SafeLocalStateBackup {
    <#
      校验备份：
      -Source 提供时，与源目录逐项对比（数量/字节/哈希/JSON/非零/额外文件）；
      不提供 Source 时，只做备份自校验（清单哈希、数量、JSON、非零、额外文件）。
      返回 Ok / Errors / FileCount / TotalBytes。
    #>
    param(
        [string]$Source,
        [Parameter(Mandatory = $true)][string]$BackupDir,
        [hashtable]$Ops
    )
    $Ops = Merge-SafeLocalStateOps $Ops

    $errors = @()
    if (-not (& $Ops.TestPath $BackupDir)) {
        return @{ Ok = $false; Errors = @('备份目录不存在。'); FileCount = 0; TotalBytes = 0 }
    }

    $manifestPath = Join-Path $BackupDir 'LocalState-backup-manifest.json'
    if (-not (& $Ops.TestPath $manifestPath)) {
        return @{ Ok = $false; Errors = @('备份缺少清单文件。'); FileCount = 0; TotalBytes = 0 }
    }

    $manifestText = & $Ops.ReadAllText $manifestPath
    if ([string]::IsNullOrWhiteSpace($manifestText)) {
        $errors += '备份清单为空。'
        return @{ Ok = $false; Errors = $errors; FileCount = 0; TotalBytes = 0 }
    }

    try {
        $manifest = $manifestText | ConvertFrom-Json
    }
    catch {
        $errors += "备份清单 JSON 无法解析：$($_.Exception.Message)"
        return @{ Ok = $false; Errors = $errors; FileCount = 0; TotalBytes = 0 }
    }

    if ([string]::IsNullOrWhiteSpace([string]$manifest.packageFamilyName)) {
        $errors += '备份清单缺少 Package Family。'
    }
    if ([string]::IsNullOrWhiteSpace([string]$manifest.appVersion)) {
        $errors += '备份清单缺少应用版本。'
    }
    if ($null -eq $manifest.files) {
        $errors += '备份清单缺少文件列表。'
        return @{ Ok = $false; Errors = $errors; FileCount = 0; TotalBytes = 0 }
    }

    $expectedSet = @{}
    foreach ($entry in $manifest.files) {
        $rel = [string]$entry.relativePath
        if ([string]::IsNullOrWhiteSpace($rel)) {
            $errors += '备份清单包含空文件路径。'
            continue
        }
        $expectedSet[$rel] = $entry
    }

    $backupFiles = @(& $Ops.EnumerateFiles $BackupDir)
    $backupSet = @{}
    foreach ($bf in $backupFiles) {
        $rel = $bf.FullName.Substring([System.IO.Path]::GetFullPath($BackupDir).Length).TrimStart('\', '/')
        $backupSet[$rel] = $bf
    }
    $backupSet.Remove('LocalState-backup-manifest.json') | Out-Null

    $totalBytes = [long]0
    foreach ($rel in $expectedSet.Keys) {
        $entry = $expectedSet[$rel]
        if (-not $backupSet.ContainsKey($rel)) {
            $errors += "备份缺少文件：$rel"
            continue
        }

        $bf = $backupSet[$rel]
        if ([long]$entry.size -ne [long]$bf.Length) {
            $errors += "文件大小不一致：$rel（清单 $($entry.size)，实际 $($bf.Length)）"
        }
        if ([long]$entry.size -eq 0) {
            $errors += "文件意外为 0 字节：$rel"
        }

        $actualHash = & $Ops.GetFileHash $bf.FullName
        if ([string]$actualHash -ne [string]$entry.sha256) {
            $errors += "文件哈希不一致：$rel"
        }
        $totalBytes += [long]$entry.size
    }

    # 备份中不允许出现清单之外的额外文件。
    foreach ($rel in $backupSet.Keys) {
        if (-not $expectedSet.ContainsKey($rel)) {
            $errors += "备份包含未知文件：$rel"
        }
    }

    # 所有 JSON 文件必须可解析且非零。
    foreach ($rel in $expectedSet.Keys) {
        if ($rel -notlike '*.json') {
            continue
        }

        $path = Join-Path $BackupDir $rel
        try {
            $text = & $Ops.ReadAllText $path
            if ([string]::IsNullOrWhiteSpace($text)) {
                $errors += "JSON 文件为空：$rel"
                continue
            }
            $null = $text | ConvertFrom-Json
        }
        catch {
            $errors += "JSON 无法解析：$rel（$($_.Exception.Message)）"
        }
    }

    # 预期核心 JSON 必须存在（账户文件为强依赖）。
    foreach ($core in @('accounts.json', 'balance-records.json', 'tray-settings.json')) {
        if (-not $expectedSet.ContainsKey($core)) {
            $errors += "备份缺少核心文件：$core"
        }
    }
    # 窗口设置：v0.7.0 起为 floating-window-settings.json；v0.6.0 及更早为
    # compact-window-settings.json（旧版本升级时仍可能只有旧文件）。
    if (-not $expectedSet.ContainsKey('floating-window-settings.json') -and
        -not $expectedSet.ContainsKey('compact-window-settings.json')) {
        $errors += '备份缺少核心文件：floating-window-settings.json'
    }

    # 与源目录逐项对比（数量/字节）。
    if (-not [string]::IsNullOrWhiteSpace($Source)) {
        if (-not (& $Ops.TestPath $Source)) {
            $errors += "源目录不存在：$Source"
        }
        else {
            $sourceFull = [System.IO.Path]::GetFullPath($Source)
            $sourceFiles = @(& $Ops.EnumerateFiles $sourceFull)
            if ($sourceFiles.Count -ne $expectedSet.Count) {
                $errors += "源文件数量不一致：源 $($sourceFiles.Count)，清单 $($expectedSet.Count)"
            }

            $sourceTotal = [long]0
            foreach ($sf in $sourceFiles) {
                $sourceTotal += [long]$sf.Length
                $rel = $sf.FullName.Substring($sourceFull.Length).TrimStart('\', '/')
                if (-not $expectedSet.ContainsKey($rel)) {
                    $errors += "源文件未进入备份：$rel"
                }
            }
            if ($sourceTotal -ne $totalBytes) {
                $errors += "源总字节数不一致：源 $sourceTotal，备份 $totalBytes"
            }
        }
    }

    return @{
        Ok         = ($errors.Count -eq 0)
        Errors     = $errors
        FileCount  = $expectedSet.Count
        TotalBytes = $totalBytes
    }
}

function Restore-SafeLocalState {
    <#
      从合法备份恢复 LocalState。
      流程：备份自校验 → Package Family 一致性 → 当前数据二次备份 →
      未知新文件保护 → 逐文件安全写入 → JSON 复验。
      不接触 Credential Locker。
    #>
    param(
        [Parameter(Mandatory = $true)][string]$BackupDir,
        [Parameter(Mandatory = $true)][string]$Target,
        [Parameter(Mandatory = $true)][string]$TargetPackageFamilyName,
        [Parameter(Mandatory = $true)][string]$RestoreBackupRoot,
        [hashtable]$Ops
    )
    $Ops = Merge-SafeLocalStateOps $Ops

    $result = @{
        Ok              = $false
        Errors          = @()
        SecondBackupDir = $null
    }

    $validation = Test-SafeLocalStateBackup -BackupDir $BackupDir -Ops $Ops
    if (-not $validation.Ok) {
        $result.Errors += '备份自校验失败：' + ($validation.Errors -join '；')
        return $result
    }

    $manifestPath = Join-Path $BackupDir 'LocalState-backup-manifest.json'
    $manifest = (& $Ops.ReadAllText $manifestPath) | ConvertFrom-Json
    if ([string]$manifest.packageFamilyName -ne $TargetPackageFamilyName) {
        $result.Errors += "Package Family 不一致：备份 $($manifest.packageFamilyName)，目标 $TargetPackageFamilyName"
        return $result
    }

    # 当前数据二次备份（目标不存在时跳过，视为全新安装）。
    if (& $Ops.TestPath $Target) {
        $second = Backup-SafeLocalState `
            -Source $Target `
            -BackupRoot $RestoreBackupRoot `
            -PackageFamilyName $TargetPackageFamilyName `
            -AppVersion ([string]$manifest.appVersion) `
            -Ops $Ops
        if (-not $second.Ok) {
            $result.Errors += '当前数据二次备份失败：' + ($second.Errors -join '；')
            return $result
        }
        $result.SecondBackupDir = $second.BackupDir
    }

    # 不覆盖无法识别的新版本数据：目标中存在清单之外的任何文件都中止。
    if (& $Ops.TestPath $Target) {
        $targetFiles = @(& $Ops.EnumerateFiles $Target)
        $expectedRels = @{}
        foreach ($entry in $manifest.files) {
            $expectedRels[[string]$entry.relativePath] = $true
        }
        foreach ($tf in $targetFiles) {
            $rel = $tf.FullName.Substring([System.IO.Path]::GetFullPath($Target).Length).TrimStart('\', '/')
            if (-not $expectedRels.ContainsKey($rel)) {
                $result.Errors += "目标存在备份之外的文件，拒绝覆盖：$rel"
            }
        }
        if ($result.Errors.Count -gt 0) {
            return $result
        }
    }
    else {
        & $Ops.NewDirectory $Target
    }

    # 逐文件安全写入（临时文件 + 替换）。
    foreach ($entry in $manifest.files) {
        $rel = [string]$entry.relativePath
        if ([string]::IsNullOrWhiteSpace($rel)) {
            continue
        }

        $sourcePath = Join-Path $BackupDir $rel
        $destPath = Join-Path $Target $rel
        $destDir = Split-Path -Parent $destPath
        if (-not (& $Ops.TestPath $destDir)) {
            & $Ops.NewDirectory $destDir
        }

        $tempPath = $destPath + '.restore-tmp'
        try {
            & $Ops.CopyFile $sourcePath $tempPath
            if (& $Ops.TestPath $destPath) {
                & $Ops.RemoveFile $destPath
            }
            & $Ops.MoveFile $tempPath $destPath
        }
        catch {
            $result.Errors += "恢复文件失败：$rel（$($_.Exception.Message)）"
        }
    }

    if ($result.Errors.Count -gt 0) {
        return $result
    }

    # 恢复完成后重新验证 JSON 与清单哈希。
    $postCheck = Test-SafeLocalStateBackup -Source $Target -BackupDir $BackupDir -Ops $Ops
    if (-not $postCheck.Ok) {
        $result.Errors += '恢复后校验失败：' + ($postCheck.Errors -join '；')
        return $result
    }

    $result.Ok = $true
    return $result
}
