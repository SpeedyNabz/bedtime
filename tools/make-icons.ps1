<#
.SYNOPSIS
    Generates Bedtime's icon assets from the Segoe Fluent Icons font (Windows 11).

.DESCRIPTION
    Bedtime.ico / Bedtime.png  - app icon: indigo rounded tile with a golden crescent moon and a star.
    TrayDark.ico               - white outline moon (U+E708) for a dark taskbar.
    TrayLight.ico              - near-black outline moon for a light taskbar.

    Run once with Windows PowerShell 5.1 (System.Drawing is built in); the outputs are committed.
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\src\Bedtime\Assets'),
    [string]$PreviewPath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$IconFont = 'Segoe Fluent Icons'
$MoonOutline = 0xE708   # QuietHours - same glyph family as the other tray icons
$MoonFilled = 0xF1DB

function C([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

function New-Canvas([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear([System.Drawing.Color]::Transparent)
    return @($bmp, $g)
}

function Get-Bgra([System.Drawing.Bitmap]$bmp) {
    $rect = New-Object System.Drawing.Rectangle 0, 0, $bmp.Width, $bmp.Height
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $bytes = New-Object byte[] ($bmp.Width * $bmp.Height * 4)
        [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
        return , $bytes
    }
    finally { $bmp.UnlockBits($data) }
}

function New-Star([single]$cx, [single]$cy, [single]$r) {
    $pts = New-Object 'System.Collections.Generic.List[System.Drawing.PointF]'
    for ($i = 0; $i -lt 8; $i++) {
        $ang = [math]::PI / 4 * $i - [math]::PI / 2
        $rad = if ($i % 2 -eq 0) { $r } else { $r * 0.28 }
        $pts.Add((New-Object System.Drawing.PointF ([single]($cx + $rad * [math]::Cos($ang))), ([single]($cy + $rad * [math]::Sin($ang)))))
    }
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddPolygon($pts.ToArray())
    return $p
}

function New-GlyphPath([int]$codepoint, [int]$size, [single]$fill, [single]$dx, [single]$dy) {
    $family = New-Object System.Drawing.FontFamily $IconFont
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $p.AddString([string][char]$codepoint, $family, 0, [single]$size, (New-Object System.Drawing.PointF 0, 0), [System.Drawing.StringFormat]::GenericTypographic)
    $b = $p.GetBounds()
    $s = ($size * $fill) / [math]::Max($b.Width, $b.Height)
    $m = New-Object System.Drawing.Drawing2D.Matrix
    $m.Translate([single]($size / 2 + $dx * $size), [single]($size / 2 + $dy * $size))
    $m.Scale([single]$s, [single]$s)
    $m.Translate([single](-($b.X + $b.Width / 2)), [single](-($b.Y + $b.Height / 2)))
    $p.Transform($m)
    return $p
}

function New-RoundRect([single]$x, [single]$y, [single]$w, [single]$r) {
    $d = 2 * $r
    $t = New-Object System.Drawing.Drawing2D.GraphicsPath
    $t.AddArc($x, $y, $d, $d, 180, 90)
    $t.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $t.AddArc($x + $w - $d, $y + $w - $d, $d, $d, 0, 90)
    $t.AddArc($x, $y + $w - $d, $d, $d, 90, 90)
    $t.CloseFigure()
    return $t
}

function New-AppIcon([int]$size) {
    $bmp, $g = New-Canvas $size
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $rect = New-Object System.Drawing.RectangleF 0, 0, $size, $size

    $inset = [math]::Max(0.5, $size * 0.04)
    $tile = New-RoundRect $inset $inset ($size - 2 * $inset) ($size * 0.22)
    $g.FillPath((New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (C '#5B5BEA'), (C '#25206B'), 70), $tile)

    $moon = New-GlyphPath $MoonFilled $size 0.56 -0.03 0.03
    $g.FillPath((New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, (C '#FFF6D8'), (C '#F5BE4E'), 60), $moon)

    if ($size -ge 24) {
        $g.FillPath((New-Object System.Drawing.SolidBrush (C '#FFF1BF')), (New-Star ($size * 0.74) ($size * 0.27) ($size * 0.10)))
    }
    $g.Dispose()
    return $bmp
}

# Renders the glyph with hinting on an oversized canvas, then copies the ink, pixel-aligned, to the centre.
function New-TrayIcon([int]$size, [System.Drawing.Color]$color) {
    $canvas = $size * 3
    $tmp, $g = New-Canvas $canvas
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $font = New-Object System.Drawing.Font($IconFont, [single]$size, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $g.DrawString([string][char]$MoonOutline, $font, (New-Object System.Drawing.SolidBrush $color), [single]$size, [single]$size)
    $g.Dispose()

    $px = Get-Bgra $tmp
    $minX = $canvas; $minY = $canvas; $maxX = -1; $maxY = -1
    for ($y = 0; $y -lt $canvas; $y++) {
        $rowBase = $y * $canvas * 4
        for ($x = 0; $x -lt $canvas; $x++) {
            if ($px[$rowBase + $x * 4 + 3] -ne 0) {
                if ($x -lt $minX) { $minX = $x }
                if ($x -gt $maxX) { $maxX = $x }
                if ($y -lt $minY) { $minY = $y }
                if ($y -gt $maxY) { $maxY = $y }
            }
        }
    }
    $inkW = [math]::Min($size, $maxX - $minX + 1)
    $inkH = [math]::Min($size, $maxY - $minY + 1)

    $bmp, $g2 = New-Canvas $size
    $g2.CompositingMode = 'SourceCopy'
    $dst = New-Object System.Drawing.Rectangle ([int][math]::Floor(($size - $inkW) / 2)), ([int][math]::Floor(($size - $inkH) / 2)), $inkW, $inkH
    $src = New-Object System.Drawing.Rectangle $minX, $minY, $inkW, $inkH
    $g2.DrawImage($tmp, $dst, $src, [System.Drawing.GraphicsUnit]::Pixel)
    $g2.Dispose()
    $tmp.Dispose()
    return $bmp
}

# Classic 32-bit DIB icon entry (BITMAPINFOHEADER + bottom-up BGRA + AND mask).
function Get-DibBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width; $h = $bmp.Height
    $px = Get-Bgra $bmp
    $maskStride = [int]([math]::Ceiling($w / 32.0) * 4)
    $ms = New-Object System.IO.MemoryStream
    $bw = New-Object System.IO.BinaryWriter $ms
    $bw.Write([int32]40); $bw.Write([int32]$w); $bw.Write([int32]($h * 2))
    $bw.Write([int16]1); $bw.Write([int16]32); $bw.Write([int32]0)
    $bw.Write([int32]($w * $h * 4 + $maskStride * $h))
    $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([int32]0)
    for ($y = $h - 1; $y -ge 0; $y--) { $bw.Write($px, $y * $w * 4, $w * 4) }
    for ($y = $h - 1; $y -ge 0; $y--) {
        $row = New-Object byte[] $maskStride
        for ($x = 0; $x -lt $w; $x++) {
            if ($px[($y * $w + $x) * 4 + 3] -eq 0) { $row[$x -shr 3] = $row[$x -shr 3] -bor (0x80 -shr ($x % 8)) }
        }
        $bw.Write($row)
    }
    $bw.Flush()
    return , $ms.ToArray()
}

function Write-Ico([string]$path, [System.Drawing.Bitmap[]]$bitmaps) {
    $entries = New-Object 'System.Collections.Generic.List[byte[]]'
    foreach ($b in $bitmaps) {
        if ($b.Width -ge 256) {
            $ms = New-Object System.IO.MemoryStream
            $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $entries.Add($ms.ToArray())
        }
        else {
            $entries.Add((Get-DibBytes $b))
        }
    }
    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter $fs
    try {
        $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$bitmaps.Count)
        $offset = 6 + 16 * $bitmaps.Count
        for ($i = 0; $i -lt $bitmaps.Count; $i++) {
            $w = $bitmaps[$i].Width; $h = $bitmaps[$i].Height
            $bw.Write([byte]$(if ($w -ge 256) { 0 } else { $w }))
            $bw.Write([byte]$(if ($h -ge 256) { 0 } else { $h }))
            $bw.Write([byte]0); $bw.Write([byte]0)
            $bw.Write([uint16]1); $bw.Write([uint16]32)
            $bw.Write([uint32]$entries[$i].Length); $bw.Write([uint32]$offset)
            $offset += $entries[$i].Length
        }
        foreach ($e in $entries) { $bw.Write($e) }
    }
    finally { $bw.Close() }
}

New-Item -ItemType Directory -Force $OutDir | Out-Null
$OutDir = (Resolve-Path $OutDir).Path

$appSizes = 16, 20, 24, 32, 40, 48, 64, 96, 128, 256
$appIcons = foreach ($s in $appSizes) { New-AppIcon $s }
Write-Ico (Join-Path $OutDir 'Bedtime.ico') $appIcons
$appIcons[-1].Save((Join-Path $OutDir 'Bedtime.png'), [System.Drawing.Imaging.ImageFormat]::Png)

$traySizes = 16, 20, 24, 32, 40, 48, 64
$trayDark = foreach ($s in $traySizes) { New-TrayIcon $s (C '#FFFFFF') }
$trayLight = foreach ($s in $traySizes) { New-TrayIcon $s (C '#1C1C1C') }
Write-Ico (Join-Path $OutDir 'TrayDark.ico') $trayDark
Write-Ico (Join-Path $OutDir 'TrayLight.ico') $trayLight

if ($PreviewPath) {
    $sheet = New-Object System.Drawing.Bitmap 760, 300
    $sg = [System.Drawing.Graphics]::FromImage($sheet)
    $sg.InterpolationMode = 'NearestNeighbor'
    $sg.PixelOffsetMode = 'Half'
    $sg.FillRectangle((New-Object System.Drawing.SolidBrush (C '#1F1F1F')), 0, 0, 380, 300)
    $sg.FillRectangle((New-Object System.Drawing.SolidBrush (C '#EEEEEE')), 380, 0, 380, 300)
    foreach ($side in 0, 1) {
        $set = if ($side -eq 0) { $trayDark } else { $trayLight }
        $x = 10 + $side * 380
        foreach ($b in $set) { $sg.DrawImage($b, $x, 10, $b.Width, $b.Height); $x += $b.Width + 8 }
        $sg.DrawImage($set[0], 10 + $side * 380, 90, 64, 64)    # 16px at 4x
        $sg.DrawImage($set[2], 90 + $side * 380, 90, 96, 96)    # 24px at 4x
        $x = 200 + $side * 380
        foreach ($b in ($appIcons | Where-Object { $_.Width -le 48 })) { $sg.DrawImage($b, $x, 200, $b.Width, $b.Height); $x += $b.Width + 6 }
    }
    $sg.Dispose()
    $sheet.Save($PreviewPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $sheet.Dispose()
}

Get-ChildItem $OutDir | Select-Object Name, Length
