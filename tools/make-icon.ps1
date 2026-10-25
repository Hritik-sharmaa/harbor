# Generates assets\Harbor.ico (16-256 px, PNG-compressed entries). Run: powershell -File tools\make-icon.ps1
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Force (Join-Path $root 'assets') | Out-Null

function Round-Rect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw-Icon([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $m = $s * 0.04
    $w = $s - 2 * $m
    # background squircle: deep blue -> violet
    $bg = Round-Rect $m $m $w $w ($w * 0.235)
    $rect = New-Object System.Drawing.RectangleF $m, $m, $w, $w
    $br = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(255, 72, 128, 255)), ([System.Drawing.Color]::FromArgb(255, 132, 76, 245)), 60
    $g.FillPath($br, $bg)
    # soft top shine
    $shine = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(70, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255)), 90
    $g.FillPath($shine, $bg)
    # menu bar
    $barH = [Math]::Max(1.5, $w * 0.075)
    $bar = Round-Rect ($m + $w * 0.14) ($m + $w * 0.15) ($w * 0.72) $barH ($barH / 2)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(225, 255, 255, 255))), $bar)
    # dock pill
    $dh = $w * 0.26
    $dock = Round-Rect ($m + $w * 0.12) ($m + $w * 0.62) ($w * 0.76) $dh ($dh * 0.42)
    $g.FillPath((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(235, 255, 255, 255))), $dock)
    # three app tiles in the dock
    $colors = @([System.Drawing.Color]::FromArgb(255, 255, 149, 0), [System.Drawing.Color]::FromArgb(255, 52, 199, 89), [System.Drawing.Color]::FromArgb(255, 10, 132, 255))
    $ts = $dh * 0.62
    $gap = ($w * 0.76 - 3 * $ts) / 4
    for ($i = 0; $i -lt 3; $i++) {
        $tx = $m + $w * 0.12 + $gap + $i * ($ts + $gap)
        $ty = $m + $w * 0.62 + ($dh - $ts) / 2
        $t = Round-Rect $tx $ty $ts $ts ($ts * 0.28)
        $g.FillPath((New-Object System.Drawing.SolidBrush $colors[$i]), $t)
    }
    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $b = Draw-Icon $s
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    if ($s -eq 256) { $b.Save((Join-Path $root 'assets\Harbor.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $b.Dispose()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $dim = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([UInt16]1); $w.Write([UInt16]32)
    $w.Write([UInt32]$pngs[$i].Length); $w.Write([UInt32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $root 'assets\Harbor.ico'), $out.ToArray())
"wrote assets\Harbor.ico"
