# TabTaskExplorer icon generator.
# Builds a multi-size app.ico plus window logo PNGs from one vector design.
# Run: powershell -NoProfile -ExecutionPolicy Bypass -File tools\generate-app-icon.ps1
# ASCII-only on purpose: Windows PowerShell reads BOM-less .ps1 files as ANSI.

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$assetDir = Join-Path $root 'src\TabTaskExplorer.App\Assets'
$icoPath = Join-Path $assetDir 'app.ico'
$logoTarget = Join-Path $assetDir 'TabX\logo.png'
$logoDarkTarget = Join-Path $assetDir 'TabX-Dark\logo.png'

# Skip generation when the assets are already newer than this script.
if ((Test-Path $icoPath) -and (Test-Path $logoTarget) -and (Test-Path $logoDarkTarget)) {
    $scriptTime = (Get-Item $PSCommandPath).LastWriteTime
    $icoTime = (Get-Item $icoPath).LastWriteTime
    $icoLength = (Get-Item $icoPath).Length
    $logoLength = (Get-Item $logoTarget).Length
    if (($icoLength -ge 10000) -and ($logoLength -ge 1000) -and ($icoTime -ge $scriptTime)) {
        Write-Host 'Icons are up to date, skipping.'
        exit 0
    }
}

# Brand colors: blue/cyan gradient, white folder, blue list lines.
$gradientStart = [System.Drawing.Color]::FromArgb(47, 124, 246)
$gradientEnd = [System.Drawing.Color]::FromArgb(18, 194, 233)
$linePrimary = [System.Drawing.Color]::FromArgb(47, 124, 246)
$lineSecondary = [System.Drawing.Color]::FromArgb(43, 111, 224)

function New-RoundedPath([double]$x, [double]$y, [double]$width, [double]$height, [double]$radius) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $radius * 2
    if ($diameter -le 0) {
        $path.AddRectangle([System.Drawing.RectangleF]::new([single]$x, [single]$y, [single]$width, [single]$height))
        return $path
    }
    $path.AddArc([single]$x, [single]$y, [single]$diameter, [single]$diameter, 180, 90)
    $path.AddArc([single]($x + $width - $diameter), [single]$y, [single]$diameter, [single]$diameter, 270, 90)
    $path.AddArc([single]($x + $width - $diameter), [single]($y + $height - $diameter), [single]$diameter, [single]$diameter, 0, 90)
    $path.AddArc([single]$x, [single]($y + $height - $diameter), [single]$diameter, [single]$diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function Draw-Logo([System.Drawing.Graphics]$graphics, [int]$size) {
    $s = $size / 24.0
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    # Rounded gradient badge.
    $brush = [System.Drawing.Drawing2D.LinearGradientBrush]::new(
        [System.Drawing.PointF]::new([single]0, [single]0),
        [System.Drawing.PointF]::new([single]$size, [single]$size),
        $gradientStart,
        $gradientEnd)
    $background = New-RoundedPath 0 0 $size $size (5.4 * $s)
    $graphics.FillPath($brush, $background)
    $background.Dispose()
    $brush.Dispose()

    # Top highlight.
    $highlightBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(26, 255, 255, 255))
    $highlight = New-RoundedPath (1.2 * $s) (1.2 * $s) (21.6 * $s) (9.2 * $s) (4.6 * $s)
    $graphics.FillPath($highlightBrush, $highlight)
    $highlight.Dispose()
    $highlightBrush.Dispose()

    # Folder back plate and tab.
    $backBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(85, 255, 255, 255))
    [System.Drawing.PointF[]]$backPoints = @(
        [System.Drawing.PointF]::new([single](4.2 * $s), [single](18.8 * $s)),
        [System.Drawing.PointF]::new([single](4.2 * $s), [single](7.4 * $s)),
        [System.Drawing.PointF]::new([single](5.6 * $s), [single](6.7 * $s)),
        [System.Drawing.PointF]::new([single](9.2 * $s), [single](6.7 * $s)),
        [System.Drawing.PointF]::new([single](10.8 * $s), [single](8.4 * $s)),
        [System.Drawing.PointF]::new([single](19.8 * $s), [single](8.4 * $s)),
        [System.Drawing.PointF]::new([single](19.8 * $s), [single](11.1 * $s)),
        [System.Drawing.PointF]::new([single](4.2 * $s), [single](11.1 * $s))
    )
    $graphics.FillPolygon($backBrush, $backPoints)
    $backBrush.Dispose()

    # Folder front plate.
    $frontBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::White)
    $front = New-RoundedPath (4.2 * $s) (11.0 * $s) (15.6 * $s) (8.6 * $s) (2.2 * $s)
    $graphics.FillPath($frontBrush, $front)
    $front.Dispose()
    $frontBrush.Dispose()

    # Front plate highlight and two list lines.
    $edgeBrush = [System.Drawing.SolidBrush]::new([System.Drawing.Color]::FromArgb(242, 255, 255, 255))
    $edge = New-RoundedPath (4.2 * $s) (11.0 * $s) (15.6 * $s) (1.2 * $s) (0.6 * $s)
    $graphics.FillPath($edgeBrush, $edge)
    $edge.Dispose()
    $edgeBrush.Dispose()

    $primaryBrush = [System.Drawing.SolidBrush]::new($linePrimary)
    $lineOne = New-RoundedPath (6.6 * $s) (13.5 * $s) (8.0 * $s) (1.4 * $s) (0.7 * $s)
    $graphics.FillPath($primaryBrush, $lineOne)
    $lineOne.Dispose()
    $primaryBrush.Dispose()

    $secondaryBrush = [System.Drawing.SolidBrush]::new($lineSecondary)
    $lineTwo = New-RoundedPath (6.6 * $s) (16.0 * $s) (5.2 * $s) (1.4 * $s) (0.7 * $s)
    $graphics.FillPath($secondaryBrush, $lineTwo)
    $lineTwo.Dispose()
    $secondaryBrush.Dispose()
}

function New-LogoBitmap([int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    Draw-Logo $graphics $size
    $graphics.Dispose()
    return $bitmap
}

# Window logo: 256x256 PNG shared by light and dark title bars.
$bitmap = New-LogoBitmap 256
$bitmap.Save($logoTarget, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Save($logoDarkTarget, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()

# Multi-size ICO: 16 / 20 / 24 / 32 / 40 / 48 / 64 / 96 / 128 / 256.
$iconSizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
$images = New-Object System.Collections.ArrayList
foreach ($size in $iconSizes) {
    $bitmap = New-LogoBitmap $size
    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    [void]$images.Add($stream.ToArray())
    $stream.Dispose()
    $bitmap.Dispose()
}

$fileStream = [System.IO.File]::Create($icoPath)
$writer = [System.IO.BinaryWriter]::new($fileStream)
$writer.Write([UInt16]0)
$writer.Write([UInt16]1)
$writer.Write([UInt16]$iconSizes.Count)
$offset = 6 + 16 * $iconSizes.Count
for ($index = 0; $index -lt $iconSizes.Count; $index++) {
    $size = $iconSizes[$index]
    $dimension = 0
    if ($size -lt 256) {
        $dimension = $size
    }
    $writer.Write([Byte]$dimension)
    $writer.Write([Byte]$dimension)
    $writer.Write([Byte]0)
    $writer.Write([Byte]0)
    $writer.Write([UInt16]1)
    $writer.Write([UInt16]32)
    $writer.Write([UInt32]$images[$index].Length)
    $writer.Write([UInt32]$offset)
    $offset += $images[$index].Length
}
foreach ($image in $images) {
    $writer.Write($image)
}
$writer.Close()
$fileStream.Close()

Write-Host ('Icon generated: ' + $icoPath)
Write-Host ('Logo generated: ' + $logoTarget)
