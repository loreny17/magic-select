# Gera assets\icon.ico (16 a 256 px) e assets\logo.png desenhando o logo do Magic Select.
param([string]$OutDir = (Join-Path $PSScriptRoot '..\assets'))
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

function Draw-Logo([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = [float]$size

    # rounded square with a blue -> violet gradient
    $r = $s * 0.22; $d = $r * 2; $m = $s * 0.03
    $rect = New-Object System.Drawing.RectangleF $m, $m, ($s - 2 * $m), ($s - 2 * $m)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(66, 133, 244)), ([System.Drawing.Color]::FromArgb(150, 80, 240)), 45.0
    $g.FillPath($brush, $path)

    # dashed selection rectangle
    if ($size -ge 32) {
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(150, 255, 255, 255)), ([Math]::Max(1.0, $s * 0.035))
        $pen.DashStyle = 'Dash'
        $g.DrawRectangle($pen, $s * 0.20, $s * 0.20, $s * 0.60, $s * 0.60)
    }

    # four-point sparkle
    $cx = $s * 0.5; $cy = $s * 0.5; $R = $s * 0.30; $k = $s * 0.075
    $pts = @(
        (New-Object System.Drawing.PointF $cx, ($cy - $R)), (New-Object System.Drawing.PointF ($cx + $k), ($cy - $k)),
        (New-Object System.Drawing.PointF ($cx + $R), $cy), (New-Object System.Drawing.PointF ($cx + $k), ($cy + $k)),
        (New-Object System.Drawing.PointF $cx, ($cy + $R)), (New-Object System.Drawing.PointF ($cx - $k), ($cy + $k)),
        (New-Object System.Drawing.PointF ($cx - $R), $cy), (New-Object System.Drawing.PointF ($cx - $k), ($cy - $k)))
    $g.FillPolygon([System.Drawing.Brushes]::White, $pts)
    $g.Dispose()
    return $bmp
}

# ICO entries: classic 32-bit DIB for small sizes (works everywhere, incl. .NET's Icon class), PNG for 256.
function To-Dib([System.Drawing.Bitmap]$b) {
    $w = $b.Width; $h = $b.Height
    $ms = New-Object IO.MemoryStream; $bw = New-Object IO.BinaryWriter $ms
    $maskRow = [int]([Math]::Ceiling($w / 32.0) * 4)
    $bw.Write([UInt32]40); $bw.Write([Int32]$w); $bw.Write([Int32]($h * 2)); $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]0); $bw.Write([UInt32]($w * $h * 4 + $maskRow * $h)); $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
    $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
    $data = $b.LockBits($rect, 'ReadOnly', [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($data.Stride * $h)
    [Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $b.UnlockBits($data)
    for ($y = $h - 1; $y -ge 0; $y--) { $bw.Write($bytes, $y * $data.Stride, $w * 4) }   # bottom-up BGRA
    $bw.Write((New-Object byte[] ($maskRow * $h)))                                        # AND mask (alpha does the work)
    $bw.Flush(); return , $ms.ToArray()
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($sz in $sizes) {
    $b = Draw-Logo $sz
    if ($sz -ge 256) { $ms = New-Object IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bytes = $ms.ToArray() }
    else { $bytes = To-Dib $b }
    $b.Dispose()
    , $bytes
}
$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]; $len = $pngs[$i].Length
    $w.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz }))); $w.Write([byte]($(if ($sz -ge 256) { 0 } else { $sz })))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$len); $w.Write([UInt32]$offset); $offset += $len
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $OutDir 'icon.ico'), $out.ToArray())

$logo = Draw-Logo 256
$logo.Save((Join-Path $OutDir 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
$logo.Dispose()
Write-Host "OK -> $OutDir"
