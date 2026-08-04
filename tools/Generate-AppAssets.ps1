param()

# v0.7.0 起应用图标整体替换为 TerminalShare 资产包（Assets 下的
# Square150x150Logo.scale-* / Square44x44Logo.* / StoreLogo.scale-* /
# SplashScreen.scale-* / ApiMonitor.ico / TrayIcon.ico）。
# 本脚本仅保留用于生成 v0.7.0 之前的占位资产；检测到新资产包后拒绝运行，
# 避免把正式图标覆盖回旧占位图。

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$assetsDir = Join-Path $repoRoot 'Assets'
if (Test-Path -LiteralPath (Join-Path $assetsDir 'Square150x150Logo.scale-100.png')) {
    Write-Error '检测到 v0.7.0 TerminalShare 图标资产包，本脚本不会覆盖正式图标。请勿运行。'
}
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null

function New-LogoBitmap {
    param(
        [int]$Size,
        [string]$Path
    )

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::FromArgb(255, 11, 107, 203))

    $font = New-Object System.Drawing.Font(
        'Segoe UI',
        [single]($Size * 0.58),
        [System.Drawing.FontStyle]::Bold,
        [System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rect = New-Object System.Drawing.RectangleF(0, 0, $Size, $Size)

    $graphics.DrawString('A', $font, [System.Drawing.Brushes]::White, $rect, $format)
    $graphics.Dispose()
    $font.Dispose()
    $format.Dispose()

    $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
}

New-LogoBitmap -Size 150 -Path (Join-Path $assetsDir 'Square150x150Logo.png')
New-LogoBitmap -Size 44 -Path (Join-Path $assetsDir 'Square44x44Logo.png')
New-LogoBitmap -Size 100 -Path (Join-Path $assetsDir 'StoreLogo.png')

# SplashScreen: 620x300
$splash = New-Object System.Drawing.Bitmap(620, 300)
$graphics = [System.Drawing.Graphics]::FromImage($splash)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(255, 11, 107, 203))
$font = New-Object System.Drawing.Font(
    'Segoe UI',
    28.0,
    [System.Drawing.FontStyle]::Bold,
    [System.Drawing.GraphicsUnit]::Pixel)
$format = New-Object System.Drawing.StringFormat
$format.Alignment = [System.Drawing.StringAlignment]::Center
$format.LineAlignment = [System.Drawing.StringAlignment]::Center
$rect = New-Object System.Drawing.RectangleF(0, 0, 620, 300)
$graphics.DrawString('ApiMonitor', $font, [System.Drawing.Brushes]::White, $rect, $format)
$graphics.Dispose()
$font.Dispose()
$format.Dispose()
$splash.Save((Join-Path $assetsDir 'SplashScreen.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$splash.Dispose()

# ICO: 256x256
$iconBitmap = New-Object System.Drawing.Bitmap(256, 256)
$graphics = [System.Drawing.Graphics]::FromImage($iconBitmap)
$graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$graphics.Clear([System.Drawing.Color]::FromArgb(255, 11, 107, 203))
$font = New-Object System.Drawing.Font(
    'Segoe UI',
    148.0,
    [System.Drawing.FontStyle]::Bold,
    [System.Drawing.GraphicsUnit]::Pixel)
$rect = New-Object System.Drawing.RectangleF(0, 0, 256, 256)
$graphics.DrawString('A', $font, [System.Drawing.Brushes]::White, $rect, $format)
$graphics.Dispose()
$font.Dispose()

$icon = [System.Drawing.Icon]::FromHandle($iconBitmap.GetHicon())
$stream = [System.IO.File]::Create((Join-Path $assetsDir 'ApiMonitor.ico'))
$icon.Save($stream)
$stream.Dispose()
$iconBitmap.Dispose()

Write-Output "Generated assets under $assetsDir"
