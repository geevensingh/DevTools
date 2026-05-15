#requires -Version 5.1
<#
.SYNOPSIS
    Regenerates DiffViewer\Assets\diffviewer.ico from the D2 "chunky hunk
    blocks" design defined inline below.

.DESCRIPTION
    Uses WPF (PresentationCore) to draw the design as a DrawingVisual,
    rasterises it via RenderTargetBitmap at every standard Windows icon
    size (16, 20, 24, 32, 40, 48, 64, 96, 128, 256), encodes each frame
    as PNG, and packs them into a single multi-size .ico file.

    The drawing primitives in this script are kept in sync with
    Assets\diffviewer.svg by hand. If you tweak the design, edit BOTH
    files.

.NOTES
    Run from any working directory:
        powershell -ExecutionPolicy Bypass -File .\DiffViewer\Assets\generate-icon.ps1
#>

[CmdletBinding()]
param(
    [string] $OutputPath = (Join-Path $PSScriptRoot 'diffviewer.ico')
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase

# ---- Palette (matches Assets\diffviewer.svg) ----------------------------
function FromHex([string]$hex) {
    return [System.Windows.Media.ColorConverter]::ConvertFromString($hex)
}
$pageFillColor   = FromHex '#FFFFFF'
$pageStrokeColor = FromHex '#24292F'
$contextColor    = FromHex '#D0D7DE'
$removedColor    = FromHex '#FFC1C0'
$addedColor      = FromHex '#ACEEBB'
$dividerColor    = FromHex '#FFFFFF'

$pageFill   = [System.Windows.Media.SolidColorBrush]::new($pageFillColor)
$contextBr  = [System.Windows.Media.SolidColorBrush]::new($contextColor)
$removedBr  = [System.Windows.Media.SolidColorBrush]::new($removedColor)
$addedBr    = [System.Windows.Media.SolidColorBrush]::new($addedColor)
$dividerBr  = [System.Windows.Media.SolidColorBrush]::new($dividerColor)
$dividerBr.Opacity = 0.55

$pageStrokeBrush = [System.Windows.Media.SolidColorBrush]::new($pageStrokeColor)
$pagePen         = [System.Windows.Media.Pen]::new($pageStrokeBrush, 8.0)

$dividerPen      = [System.Windows.Media.Pen]::new($dividerBr, 3.0)

foreach ($b in @($pageFill,$contextBr,$removedBr,$addedBr,$dividerBr,$pageStrokeBrush)) { $b.Freeze() }
$pagePen.Freeze()
$dividerPen.Freeze()

# ---- Draw the icon at native 256x256 coordinate space -------------------
function Render-Icon([int] $size) {
    $dv  = [System.Windows.Media.DrawingVisual]::new()
    $ctx = $dv.RenderOpen()

    $scale = $size / 256.0
    $ctx.PushTransform([System.Windows.Media.ScaleTransform]::new($scale, $scale))

    # Page background + outline
    $pageRect = [System.Windows.Rect]::new(40, 20, 176, 216)
    $ctx.DrawRoundedRectangle($pageFill, $pagePen, $pageRect, 20, 20)

    # Top context line
    $ctx.DrawRoundedRectangle($contextBr, $null,
        [System.Windows.Rect]::new(64, 48, 100, 14), 3, 3)

    # Removed hunk
    $ctx.DrawRoundedRectangle($removedBr, $null,
        [System.Windows.Rect]::new(64, 74, 128, 48), 6, 6)
    $ctx.DrawLine($dividerPen,
        [System.Windows.Point]::new(64, 90),  [System.Windows.Point]::new(192, 90))
    $ctx.DrawLine($dividerPen,
        [System.Windows.Point]::new(64, 106), [System.Windows.Point]::new(192, 106))

    # Added hunk
    $ctx.DrawRoundedRectangle($addedBr, $null,
        [System.Windows.Rect]::new(64, 134, 128, 48), 6, 6)
    $ctx.DrawLine($dividerPen,
        [System.Windows.Point]::new(64, 150), [System.Windows.Point]::new(192, 150))
    $ctx.DrawLine($dividerPen,
        [System.Windows.Point]::new(64, 166), [System.Windows.Point]::new(192, 166))

    # Bottom context line
    $ctx.DrawRoundedRectangle($contextBr, $null,
        [System.Windows.Rect]::new(64, 194, 86, 14), 3, 3)

    $ctx.Pop()
    $ctx.Close()

    $rtb = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
        $size, $size, 96, 96,
        [System.Windows.Media.PixelFormats]::Pbgra32)
    $rtb.Render($dv)

    $encoder = [System.Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($rtb))
    $ms = [System.IO.MemoryStream]::new()
    $encoder.Save($ms)
    return ,$ms.ToArray()
}

# ---- Build the multi-size ICO -------------------------------------------
$sizes = @(16, 20, 24, 32, 40, 48, 64, 96, 128, 256)
Write-Host "Rendering frames at sizes: $($sizes -join ', ')"
$pngs  = foreach ($s in $sizes) { ,(Render-Icon $s) }

$out    = [System.IO.MemoryStream]::new()
$writer = [System.IO.BinaryWriter]::new($out)

# ICONDIR: reserved=0, type=1 (ICO), count
$writer.Write([uint16]0)
$writer.Write([uint16]1)
$writer.Write([uint16]$sizes.Count)

# ICONDIRENTRY[]: header is 6 bytes, each entry is 16 bytes
$dataOffset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $size   = $sizes[$i]
    $pngLen = $pngs[$i].Length
    # 0 in the byte-sized width/height fields means "256"
    $w = if ($size -ge 256) { [byte]0 } else { [byte]$size }
    $h = if ($size -ge 256) { [byte]0 } else { [byte]$size }
    $writer.Write($w)              # width
    $writer.Write($h)              # height
    $writer.Write([byte]0)         # palette colours (0 = none)
    $writer.Write([byte]0)         # reserved
    $writer.Write([uint16]1)       # colour planes
    $writer.Write([uint16]32)      # bits per pixel
    $writer.Write([uint32]$pngLen) # image size
    $writer.Write([uint32]$dataOffset)
    $dataOffset += $pngLen
}

# Image data (PNG-encoded; ICO-with-PNG is supported on Vista+)
foreach ($png in $pngs) { $writer.Write($png) }

$writer.Flush()
[System.IO.File]::WriteAllBytes($OutputPath, $out.ToArray())
Write-Host "Wrote $OutputPath ($([math]::Round((Get-Item $OutputPath).Length / 1KB, 1)) KB)"
