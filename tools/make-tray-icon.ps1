# Generates Assets/tray.ico (16px + 32px PNG frames, Apple Music red circle).
# Run once from the repo root: powershell -NoProfile -ExecutionPolicy Bypass -File tools/make-tray-icon.ps1
Add-Type -AssemblyName System.Drawing

$root = Split-Path $PSScriptRoot -Parent
$outDir = Join-Path $root 'src\AppleMusicWidget\Assets'
New-Item -ItemType Directory -Force $outDir | Out-Null
$out = Join-Path $outDir 'tray.ico'

function New-Png([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(250, 45, 66))
    $g.FillEllipse($brush, 1, 1, $size - 2, $size - 2)
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $g.Dispose(); $brush.Dispose(); $bmp.Dispose(); $ms.Dispose()
    return ,$bytes   # comma: emit the byte[] as ONE object, not enumerated
}

$frames = @((New-Png 16), (New-Png 32))
"frames: $($frames.Count), sizes: $($frames[0].Length), $($frames[1].Length)"

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms
$bw.Write([uint16]0)              # reserved
$bw.Write([uint16]1)              # type: icon
$bw.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
$px = @(16, 32)
for ($i = 0; $i -lt $frames.Count; $i++) {
    $bw.Write([byte]$px[$i])      # width (0 = 256)
    $bw.Write([byte]$px[$i])      # height
    $bw.Write([byte]0)            # colors
    $bw.Write([byte]0)            # reserved
    $bw.Write([uint16]1)          # planes
    $bw.Write([uint16]32)         # bpp
    $bw.Write([uint32]$frames[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $frames[$i].Length
}
foreach ($f in $frames) { $bw.Write([byte[]]$f) }
$len = $ms.Length
[System.IO.File]::WriteAllBytes($out, $ms.ToArray())
$bw.Dispose()
"wrote $out ($len bytes)"
