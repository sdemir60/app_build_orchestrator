# [T62] Tepsi ikonu (16px) üreteci — TEK amaçlı, elle ayarlanmış raster.
#
# Neden elle: kaynak sanat `.claude/outputs/2026-07-15-19-00-design-v1/prototype/assets/delta-app-icon.svg`
# 64px içindir; 16px'e otomatik küçültme amber "D"yi çamura çevirir (feasibility §3.2). Bu yüzden "D" burada
# 16x16 ızgarada PİKSEL PİKSEL yazılır; zemin (yuvarlak köşe + 1px çerçeve) token renkleriyle çizilir.
#
# KAPSAM: yalnız tepsi ikonu. Tam ikon hattı (SVG → çok boyutlu ICO 16/24/32/48/256 + pencere/taskbar/XAML
# ikonları) T64'tür — bu script onun yerine geçmez.
#
# Çalıştırma (çıktı: tray-icon-16.ico, bu klasöre):
#   powershell -NoProfile -ExecutionPolicy Bypass -File src/BuildOrchestrator.App/Assets/generate-tray-icon.ps1

$ErrorActionPreference = 'Stop'

# design-v1 token'ları: surface-base zemin, border çerçeve, amber marka rengi
$bg     = @(0x0e, 0x0e, 0x10)  # #0e0e10
$border = @(0x2a, 0x2a, 0x30)  # #2a2a30
$amber  = @(0xed, 0xa1, 0x0f)  # #eda10f

# Elle ayarlanmış 16x16 "D" (2px gövde, 10px yükseklik, optik olarak ortalanmış)
$d = @(
  '................',
  '................',
  '................',
  '....#######.....',
  '....##...###....',
  '....##....##....',
  '....##....##....',
  '....##....##....',
  '....##....##....',
  '....##....##....',
  '....##....##....',
  '....##...###....',
  '....#######.....',
  '................',
  '................',
  '................'
)

function Inside([double]$x, [double]$y, [double]$inset, [double]$radius) {
  $lo = $inset; $hi = 16 - $inset
  if ($x -lt $lo -or $x -gt $hi -or $y -lt $lo -or $y -gt $hi) { return $false }
  $cx = [Math]::Min([Math]::Max($x, $lo + $radius), $hi - $radius)
  $cy = [Math]::Min([Math]::Max($y, $lo + $radius), $hi - $radius)
  $dx = $x - $cx; $dy = $y - $cy
  return (($dx * $dx + $dy * $dy) -le ($radius * $radius) + 1e-9)
}

# 32bpp BGRA, alttan üste (ICO/BMP düzeni)
$pixels = New-Object byte[] (16 * 16 * 4)
for ($y = 0; $y -lt 16; $y++) {
  for ($x = 0; $x -lt 16; $x++) {
    $px = $x + 0.5; $py = $y + 0.5
    $inShape  = Inside $px $py 0 3
    $inInner  = Inside $px $py 1 2
    if ($d[$y][$x] -eq '#')      { $rgb = $amber; $a = 255 }
    elseif ($inInner)            { $rgb = $bg;     $a = 255 }
    elseif ($inShape)            { $rgb = $border; $a = 255 }
    else                         { $rgb = @(0,0,0); $a = 0 }

    $row = 15 - $y                      # bottom-up
    $o = (($row * 16) + $x) * 4
    $pixels[$o + 0] = $rgb[2]           # B
    $pixels[$o + 1] = $rgb[1]           # G
    $pixels[$o + 2] = $rgb[0]           # R
    $pixels[$o + 3] = $a                # A
  }
}

$andMask = New-Object byte[] (16 * 4)   # 16 satır x 4 byte (1bpp, 4 byte hizalı) — hepsi 0: tümü opak/alfa geçerli

$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
# ICONDIR
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]1)
# ICONDIRENTRY
$imageSize = 40 + $pixels.Length + $andMask.Length
$bw.Write([byte]16); $bw.Write([byte]16); $bw.Write([byte]0); $bw.Write([byte]0)
$bw.Write([uint16]1); $bw.Write([uint16]32)
$bw.Write([uint32]$imageSize); $bw.Write([uint32]22)
# BITMAPINFOHEADER (biHeight = 2x: XOR + AND maskesi)
$bw.Write([uint32]40); $bw.Write([int32]16); $bw.Write([int32]32)
$bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
$bw.Write([uint32]($pixels.Length + $andMask.Length))
$bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
$bw.Write($pixels); $bw.Write($andMask)
$bw.Flush()

$out = Join-Path $PSScriptRoot 'tray-icon-16.ico'
[System.IO.File]::WriteAllBytes($out, $ms.ToArray())
Write-Host "yazildi: $out ($($ms.Length) byte)"
