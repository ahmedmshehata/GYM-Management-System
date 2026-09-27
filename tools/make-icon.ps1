<#
  Generates the GymPro application icon (multi-size .ico) and a 512 px PNG logo.
  Design: tilted white dumbbell with orange outer plates on a deep-blue rounded tile.
  One bold silhouette, no text, so it stays recognisable at 16 px on the taskbar.

  Usage:  pwsh tools/make-icon.ps1
#>
param([string]$OutDir = (Join-Path $PSScriptRoot '..\src\GymPro.App\Assets'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null

function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = [Math]::Min($r * 2, [Math]::Min($w, $h))
  $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
  $p.CloseFigure(); return $p
}

function Render([int]$size) {
  $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
  $s = $size / 256.0
  $g.ScaleTransform($s, $s)

  # tile: deep navy -> indigo diagonal gradient
  $tile = RoundRect 8 8 240 240 56
  $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 8, 8), (New-Object System.Drawing.PointF 248, 248),
    ([System.Drawing.Color]::FromArgb(255, 15, 23, 42)), ([System.Drawing.Color]::FromArgb(255, 37, 99, 235))
  $g.FillPath($grad, $tile)

  # subtle top highlight (skipped at tiny sizes where it only adds mud)
  if ($size -ge 48) {
    $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 8), (New-Object System.Drawing.PointF 0, 128),
      ([System.Drawing.Color]::FromArgb(40, 255, 255, 255)), ([System.Drawing.Color]::FromArgb(0, 255, 255, 255))
    $g.FillPath($hl, (RoundRect 8 8 240 120 56))
  }

  # dumbbell tilted 35 degrees: a diagonal silhouette can't be misread as the letter "H"
  $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
  $orange = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 249, 115, 22))
  $g.TranslateTransform(128, 128); $g.RotateTransform(-35); $g.TranslateTransform(-128, -128)
  $g.FillPath($white, (RoundRect 62 119 132 18 6))                       # bar
  foreach ($mirror in @($false, $true)) {
    $X = { param($x, $w) if ($mirror) { 256 - $x - $w } else { $x } }
    $g.FillPath($white, (RoundRect (& $X 50 26) 76 26 104 9))            # inner plate
    $g.FillPath($orange, (RoundRect (& $X 28 20) 92 20 72 7))            # outer plate (energy accent)
    $g.FillPath($white, (RoundRect (& $X 16 12) 114 12 28 4))            # end cap
  }
  $g.ResetTransform()

  $g.Dispose()
  return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($sz in $sizes) {
  $b = Render $sz; $ms = New-Object System.IO.MemoryStream
  $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose(); , $ms.ToArray()
}

# ICO container with PNG-compressed entries (supported since Windows Vista)
$ico = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $ico
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
  $sz = $sizes[$i]; $dim = if ($sz -ge 256) { 0 } else { $sz }
  $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
  $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $OutDir 'GymPro.ico'), $ico.ToArray())

$big = Render 512; $big.Save((Join-Path $OutDir 'app-logo.png'), [System.Drawing.Imaging.ImageFormat]::Png); $big.Dispose()
"Wrote GymPro.ico ($($sizes -join ', ') px) and app-logo.png to $OutDir"
