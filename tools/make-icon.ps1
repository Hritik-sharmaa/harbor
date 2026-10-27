# Generates assets\Harbor.ico (16-256 px, PNG-compressed entries) and assets\Harbor.png.
# Run: powershell -File tools\make-icon.ps1
# Design: a dark glass squircle with a soft blue glow, a glossy "island" pill at the top and a glass dock with
# app tiles at the bottom - Harbor's notch and dock. Every size is drawn at 4x and scaled down smoothly; the
# smallest sizes get a simpler version so they stay crisp.
Add-Type -AssemblyName System.Drawing
$root = Split-Path $PSScriptRoot -Parent
New-Item -ItemType Directory -Force (Join-Path $root 'assets') | Out-Null

function Round-Rect([single]$x, [single]$y, [single]$w, [single]$h, [single]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [Math]::Min($r * 2, [Math]::Min($w, $h))
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function C([int]$a, [int]$r, [int]$g, [int]$b) { [System.Drawing.Color]::FromArgb($a, $r, $g, $b) }

function Linear($rect, $c1, $c2) {
    # the brush rectangle overshoots a little: GDI+ wraps gradients at their edge
    $o = New-Object System.Drawing.RectangleF ($rect.X - 1), ($rect.Y - 2), ($rect.Width + 2), ($rect.Height + 4)
    New-Object System.Drawing.Drawing2D.LinearGradientBrush $o, $c1, $c2, 90
}

function Draw-Icon([int]$s, [bool]$simple) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $m = $s * 0.045
    $w = $s - 2 * $m
    $rect = New-Object System.Drawing.RectangleF $m, $m, $w, $w
    $bg = Round-Rect $m $m $w $w ($w * 0.23)

    # dark glass body
    $g.FillPath((Linear $rect (C 255 36 46 82) (C 255 8 10 20)), $bg)
    $g.SetClip($bg)
    # soft blue glow rising from the dock
    $glow = New-Object System.Drawing.Drawing2D.GraphicsPath
    $glow.AddEllipse($m - $w * 0.25, $m + $w * 0.42, $w * 1.5, $w * 1.0)
    $pg = New-Object System.Drawing.Drawing2D.PathGradientBrush $glow
    $pg.CenterColor = C 150 70 120 255
    $pg.SurroundColors = @(C 0 70 120 255)
    $g.FillPath($pg, $glow)
    # glossy sheen over the top half
    $sheenRect = New-Object System.Drawing.RectangleF $m, $m, $w, ($w * 0.5)
    $g.FillRectangle((Linear $sheenRect (C 52 255 255 255) (C 0 255 255 255)), $sheenRect)
    $g.ResetClip()
    # light rim, brightest at the top
    if (-not $simple) {
        $pen = New-Object System.Drawing.Pen ((Linear $rect (C 120 255 255 255) (C 18 255 255 255))), ([Math]::Max(1.0, $s / 110.0))
        $g.DrawPath($pen, $bg)
    }

    # the island: a dark glossy pill hanging at the top, with a live "activity" pip
    $iw = $w * ($(if ($simple) { 0.5 } else { 0.44 })); $ih = $w * ($(if ($simple) { 0.15 } else { 0.12 }))
    $ix = $m + ($w - $iw) / 2; $iy = $m + $w * 0.12
    $island = Round-Rect $ix $iy $iw $ih ($ih / 2)
    $g.FillPath((New-Object System.Drawing.SolidBrush (C 255 4 5 10)), $island)
    if (-not $simple) {
        $g.DrawPath((New-Object System.Drawing.Pen (C 90 255 255 255), ([Math]::Max(1.0, $s / 160.0))), $island)
        $pw = $ih * 0.9; $ph = $ih * 0.36
        $pip = Round-Rect ($ix + $iw - $pw - $ih * 0.4) ($iy + ($ih - $ph) / 2) $pw $ph ($ph / 2)
        $g.FillPath((New-Object System.Drawing.SolidBrush (C 255 52 210 120)), $pip)
    }

    # the dock: a glass pill with app tiles
    $dw = $w * 0.76; $dh = $w * ($(if ($simple) { 0.26 } else { 0.22 }))
    $dx = $m + ($w - $dw) / 2; $dy = $m + $w - $w * 0.1 - $dh
    $dock = Round-Rect $dx $dy $dw $dh ($dh * 0.42)
    $dockRect = New-Object System.Drawing.RectangleF $dx, $dy, $dw, $dh
    $g.FillPath((Linear $dockRect (C 78 255 255 255) (C 34 255 255 255)), $dock)
    if (-not $simple) { $g.DrawPath((New-Object System.Drawing.Pen ((Linear $dockRect (C 150 255 255 255) (C 40 255 255 255))), ([Math]::Max(1.0, $s / 128.0))), $dock) }
    $tiles = if ($simple) {
        @(@((C 255 255 170 40), (C 255 255 110 0)), @((C 255 70 215 110), (C 255 30 160 70)), @((C 255 40 150 255), (C 255 0 90 220)))
    } else {
        @(@((C 255 255 170 40), (C 255 255 110 0)), @((C 255 70 215 110), (C 255 30 160 70)), @((C 255 40 150 255), (C 255 0 90 220)), @((C 255 255 90 130), (C 255 215 35 80)))
    }
    $n = $tiles.Count
    $ts = $dh * ($(if ($simple) { 0.64 } else { 0.62 }))
    $gap = ($dw - $n * $ts) / ($n + 1)
    for ($i = 0; $i -lt $n; $i++) {
        $tx = $dx + $gap + $i * ($ts + $gap); $ty = $dy + ($dh - $ts) / 2
        $tr = New-Object System.Drawing.RectangleF $tx, $ty, $ts, $ts
        $tile = Round-Rect $tx $ty $ts $ts ($ts * 0.3)
        $g.FillPath((Linear $tr $tiles[$i][0] $tiles[$i][1]), $tile)
        if (-not $simple) {
            $hl = New-Object System.Drawing.RectangleF $tx, $ty, $ts, ($ts * 0.5)
            $g.SetClip($tile); $g.FillRectangle((Linear $hl (C 90 255 255 255) (C 0 255 255 255)), $hl); $g.ResetClip()
        }
    }
    $g.Dispose()
    return $bmp
}

function Render([int]$size) {
    # draw big, scale down: smooth edges at every size
    $simple = $size -le 32
    $big = Draw-Icon ($size * 4) $simple
    $out = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($out)
    $g.InterpolationMode = 'HighQualityBicubic'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $g.DrawImage($big, 0, 0, $size, $size)
    $g.Dispose(); $big.Dispose()
    return $out
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $b = Render $s
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
