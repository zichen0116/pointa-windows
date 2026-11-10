[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$assets = Join-Path (Split-Path $PSScriptRoot -Parent) 'src/Pointa.Windows/Assets'
Add-Type -AssemblyName System.Drawing
$source = [Drawing.Bitmap]::new((Join-Path $assets 'Pointa.png'))
$frames = [Collections.Generic.List[byte[]]]::new()
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
try {
    foreach ($size in $sizes) {
        $bitmap = [Drawing.Bitmap]::new($size, $size)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $stream = [IO.MemoryStream]::new()
        try {
            $graphics.Clear([Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $graphics.DrawImage($source, [Drawing.Rectangle]::new(0, 0, $size, $size))
            $bitmap.Save($stream, [Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($stream.ToArray())
        } finally { $stream.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
    }
} finally { $source.Dispose() }
$output = Join-Path $assets 'Pointa.ico'
$writer = [IO.BinaryWriter]::new([IO.File]::Create($output))
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dimension = if ($sizes[$i] -eq 256) { 0 } else { $sizes[$i] }
        $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$i].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
} finally { $writer.Dispose() }
foreach ($size in $sizes | Where-Object { $_ -lt 256 }) {
    $icon = [Drawing.Icon]::new($output, $size, $size)
    try { if ($icon.Width -ne $size -or $icon.Height -ne $size) { throw "Invalid icon frame: $size" } }
    finally { $icon.Dispose() }
}
$largeStream = [IO.MemoryStream]::new($frames[$frames.Count - 1], $false)
$large = [Drawing.Bitmap]::new($largeStream)
try { if ($large.Width -ne 256 -or $large.Height -ne 256 -or $large.GetPixel(0,0).A -ne 0) { throw 'Invalid transparent 256px icon frame.' } }
finally { $large.Dispose(); $largeStream.Dispose() }
Write-Output "Generated and verified $($sizes.Count) icon sizes: $output"
