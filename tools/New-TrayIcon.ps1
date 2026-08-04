param(
    # Output ICO path; defaults to Assets\TrayIcon.ico.
    [string]$OutputPath = ''
)

# v0.7.0 起托盘图标使用 TerminalShare 资产包中的多尺寸 ICO（Assets\TrayIcon.ico）。
# 本脚本仅保留用于生成 v0.7.0 之前的占位托盘图标；检测到新资产后拒绝运行，
# 避免覆盖正式图标。

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputPath) {
    $OutputPath = Join-Path $repoRoot 'Assets\TrayIcon.ico'
}
if (Test-Path -LiteralPath (Join-Path $repoRoot 'Assets\Square150x150Logo.scale-100.png')) {
    Write-Error '检测到 v0.7.0 TerminalShare 图标资产包，本脚本不会覆盖正式托盘图标。请勿运行。'
}

$sizes = @(16, 20, 24, 32, 48, 256)

# Same background as the existing Assets (0B6BCB blue).
$backgroundColor = [System.Drawing.Color]::FromArgb(255, 11, 107, 203)

function New-IconFrameBytes {
    param([int]$Size)

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.Clear([System.Drawing.Color]::Transparent)

    # Rounded background so the tray icon never shows as a big opaque square.
    $radius = [single]([Math]::Max([double]1, [double]($Size * 0.18)))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [single]($radius * 2)
    $r = New-Object System.Drawing.RectangleF(0, 0, [single]$Size, [single]$Size)
    $x0 = [single]$r.X; $y0 = [single]$r.Y
    $x1 = [single]($r.Right - $d); $y1 = [single]($r.Bottom - $d)
    $path.AddArc($x0, $y0, $d, $d, [single]180, [single]90)
    $path.AddArc($x1, $y0, $d, $d, [single]270, [single]90)
    $path.AddArc($x1, $y1, $d, $d, [single]0, [single]90)
    $path.AddArc($x0, $y1, $d, $d, [single]90, [single]90)
    $path.CloseFigure()

    $brush = New-Object System.Drawing.SolidBrush($backgroundColor)
    $graphics.FillPath($brush, $path)

    # Bold white "A"; large enough to stay legible at 16px.
    $fontSize = [single]($Size * 0.62)
    $font = New-Object System.Drawing.Font('Segoe UI', $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $graphics.DrawString('A', $font, [System.Drawing.Brushes]::White, $r, $format)

    $graphics.Dispose()
    $font.Dispose()
    $format.Dispose()
    $brush.Dispose()
    $path.Dispose()

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $stream.ToArray()
    $stream.Dispose()
    $bitmap.Dispose()
    # Unary comma prevents PowerShell from unrolling the byte[] into Object[].
    return , $bytes
}

# ---------------------------------------------------------------------------
# Assemble the ICO: ICONDIR + ICONDIRENTRY x N + PNG frame data.
# ---------------------------------------------------------------------------
$frames = @()
foreach ($size in $sizes) {
    $frames += @{ Size = $size; Data = New-IconFrameBytes -Size $size }
}

$output = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($output)

# ICONDIR
$writer.Write([uint16]0)              # reserved
$writer.Write([uint16]1)              # type = icon
$writer.Write([uint16]$frames.Count)  # count

# ICONDIRENTRY (PNG frames: planes=1, bitCount=32)
$offset = 6 + (16 * $frames.Count)
foreach ($frame in $frames) {
    $size = $frame.Size
    $writer.Write([byte]($size -band 0xFF))            # width (0 means 256)
    $writer.Write([byte]($size -band 0xFF))            # height
    $writer.Write([byte]0)                             # colorCount
    $writer.Write([byte]0)                             # reserved
    $writer.Write([uint16]1)                           # planes
    $writer.Write([uint16]32)                          # bitCount
    $writer.Write([uint32]$frame.Data.Length)          # bytesInRes
    $writer.Write([uint32]$offset)                     # imageOffset
    $offset += $frame.Data.Length
}

foreach ($frame in $frames) {
    $writer.Write($frame.Data)
}

$writer.Flush()
$fileStream = [System.IO.File]::Create($OutputPath)
try {
    $output.WriteTo($fileStream)
}
finally {
    $fileStream.Dispose()
}
$writer.Dispose()
$output.Dispose()

$iconFile = Get-Item -LiteralPath $OutputPath
Write-Output "Generated multi-size tray icon: $OutputPath ($($iconFile.Length) bytes, sizes: $($sizes -join '/'))"
