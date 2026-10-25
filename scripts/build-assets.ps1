$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null

function Draw-Mark([int]$size) {
    $bitmap = [System.Drawing.Bitmap]::new($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.ColorTranslator]::FromHtml('#18262D'))
    $scale = $size / 256.0
    $g.ScaleTransform($scale, $scale)
    $pen = [System.Drawing.Pen]::new([System.Drawing.ColorTranslator]::FromHtml('#B6DED5'), 12)
    $g.DrawRectangle($pen, 51, 53, 67, 93)
    $g.DrawRectangle($pen, 138, 110, 67, 93)
    $arrow = [System.Drawing.Pen]::new([System.Drawing.Color]::White, 13)
    $g.DrawLine($arrow, 85, 128, 171, 128)
    $g.DrawLine($arrow, 149, 106, 171, 128)
    $g.DrawLine($arrow, 171, 128, 149, 150)
    $pen.Dispose(); $arrow.Dispose(); $g.Dispose()
    return $bitmap
}
$images = @()
foreach ($size in @(16, 32, 64, 256)) {
    $bitmap = Draw-Mark $size
    $memory = [System.IO.MemoryStream]::new()
    $bitmap.Save($memory, [System.Drawing.Imaging.ImageFormat]::Png)
    $images += ,$memory.ToArray()
    if ($size -eq 256) { $bitmap.Save((Join-Path $assets 'icon.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $memory.Dispose(); $bitmap.Dispose()
}
$stream = [System.IO.File]::Create((Join-Path $assets 'MoveSense.ico'))
$writer = [System.IO.BinaryWriter]::new($stream)
$writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]4)
$offset = 6 + 16 * 4
for ($i=0; $i -lt 4; $i++) {
    $size = @(16,32,64,256)[$i]
    $dimension = if ($size -eq 256) { 0 } else { $size }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$images[$i].Length); $writer.Write([uint32]$offset)
    $offset += $images[$i].Length
}
foreach ($bytes in $images) { $writer.Write([byte[]]$bytes) }
$writer.Dispose()

$banner = [System.Drawing.Bitmap]::new(1280, 640)
$g = [System.Drawing.Graphics]::FromImage($banner)
$g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::ClearTypeGridFit
$g.Clear([System.Drawing.ColorTranslator]::FromHtml('#18262D'))
$mark = Draw-Mark 150
$g.DrawImage($mark, 78, 72, 150, 150)
$white = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#F4F7F7'))
$muted = [System.Drawing.SolidBrush]::new([System.Drawing.ColorTranslator]::FromHtml('#A9C3C1'))
$title = [System.Drawing.Font]::new('Segoe UI', 58, [System.Drawing.FontStyle]::Bold)
$body = [System.Drawing.Font]::new('Segoe UI', 27)
$small = [System.Drawing.Font]::new('Segoe UI', 17)
$g.DrawString('MoveSense', $title, $white, 252, 104)
$g.DrawString('Know before you move.', $body, $white, 86, 300)
$g.DrawString('Preview broken references. Review repairs. Keep a way back.', $small, $muted, 90, 380)
$g.DrawString('WINDOWS  /  LOCAL FILES  /  OPEN SOURCE', $small, $muted, 90, 525)
$banner.Save((Join-Path $assets 'social-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$mark.Dispose(); $title.Dispose(); $body.Dispose(); $small.Dispose()
$white.Dispose(); $muted.Dispose(); $g.Dispose(); $banner.Dispose()
Write-Output 'Identity assets generated.'

