# Regenerates the app icon and logo mark from the master logo artwork.
# Usage: powershell -File tools\branding\Build-BrandAssets.ps1 [-Source tools\branding\prosys-logo-master.png]
param([string]$Source = (Join-Path $PSScriptRoot 'prosys-logo-master.png'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$brand = Join-Path $root 'src\ProSyS.App\Assets\Brand'
New-Item -ItemType Directory -Force $brand | Out-Null

# The master artwork is a rounded tile centred on a navy field; crop the tile and give it transparent corners.
$master = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Source))
$scale = $master.Width / 640.0
$crop = New-Object System.Drawing.RectangleF ([float](156 * $scale)), ([float](154 * $scale)), ([float](326 * $scale)), ([float](326 * $scale))

function New-Mark([int]$size) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = 'AntiAlias'; $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $radius = [Math]::Max(2, $size * 0.22)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc(0, 0, $d, $d, 180, 90); $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90); $path.AddArc(0, $size - $d, $d, $d, 90, 90); $path.CloseFigure()
    $g.SetClip($path)
    $g.DrawImage($master, (New-Object System.Drawing.RectangleF 0, 0, $size, $size), $crop, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose(); $path.Dispose()
    return $bitmap
}

function Get-PngBytes($bitmap) { $ms = New-Object System.IO.MemoryStream; $bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bytes = $ms.ToArray(); $ms.Dispose(); return ,$bytes }

$mark = New-Mark 256
$mark.Save((Join-Path $brand 'prosys-mark.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$mark.Dispose()

# Multi-resolution ICO with PNG-compressed frames (supported by Windows Vista and later).
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$frames = foreach ($s in $sizes) { $b = New-Mark $s; $png = Get-PngBytes $b; $b.Dispose(); ,@($s, $png) }
$stream = [System.IO.File]::Create((Join-Path $brand 'prosys.ico'))
$writer = New-Object System.IO.BinaryWriter $stream
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($frame in $frames) {
    $s = $frame[0]; $len = $frame[1].Length
    $dim = if ($s -ge 256) { 0 } else { $s }
    $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32); $writer.Write([uint32]$len); $writer.Write([uint32]$offset)
    $offset += $len
}
foreach ($frame in $frames) { $writer.Write([byte[]]$frame[1]) }
$writer.Dispose()
$master.Dispose()
Write-Output "Brand assets written to $brand"
