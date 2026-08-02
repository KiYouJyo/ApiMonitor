param()

# 生成本地验收用的占位应用资产（纯色背景 + 白色字母 A）。
# 正式图标可后续替换，文件路径保持不变即可。

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
$assetsDir = Join-Path $repoRoot 'Assets'
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
$graphics.DrawString('ApiBalanceMonitor', $font, [System.Drawing.Brushes]::White, $rect, $format)
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
$stream = [System.IO.File]::Create((Join-Path $assetsDir 'ApiBalanceMonitor.ico'))
$icon.Save($stream)
$stream.Dispose()
$iconBitmap.Dispose()

Write-Output "Generated assets under $assetsDir"
