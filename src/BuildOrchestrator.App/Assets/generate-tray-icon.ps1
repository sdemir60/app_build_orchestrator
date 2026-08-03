# [T62 · T64] Uygulama ikonu ureteci.
#
# Uretilen dosyalar (ikisi de bu klasore):
#   tray-icon-16.ico  — sistem tepsisi (AppTrayIcon 16px varyantini kullanmaya DEVAM eder)
#   app-icon.ico      — cok boyutlu uygulama ikonu: 16 / 24 / 32 / 48 / 256 (<ApplicationIcon> + Window.Icon)
#
# Kaynak sanat: `.claude/outputs/2026-07-15-19-00-design-v1/prototype/assets/delta-app-icon.svg` (64px icin) —
# BES KARENIN DE tek gorsel kaynagi budur. 16 ve 24 ELLE netlestirilir: 64px'ten otomatik kucultme amber
# isareti camura cevirir (feasibility §3.2), bu yuzden bu iki boyutta isaret piksel piksel yazilir (asagidaki
# $d16/$d24). 32/48/256 ayni SVG'nin WPF ile (RenderTargetBitmap) rasterlestirilmesidir — o olceklerde egri
# kenarlar zaten temiz cikar.
#
# [T64] $d16, T62'de elle cizilmis HARF bicimli bir "D" idi; kaynak SVG'nin isareti (ceyrek daire) ile ayni
# logo DEGILDI ve cok boyutlu ICO'da 16/24 ile 32+ birbirinden farkli iki marka gibi goruluyordu. Ikisi de
# artik ayni isaretten turetilir.
#
# Calistirma:
#   powershell -NoProfile -ExecutionPolicy Bypass -File src/BuildOrchestrator.App/Assets/generate-tray-icon.ps1
# NOT: WPF rasterlestirmesi icin STA gerekir; powershell.exe varsayilan olarak STA'dir (pwsh icin -STA gerekir).

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

# design-v1 token'lari: surface-base zemin, border cerceve, amber marka rengi
$bg     = @(0x0e, 0x0e, 0x10)  # #0e0e10
$border = @(0x2a, 0x2a, 0x30)  # #2a2a30
$amber  = @(0xed, 0xa1, 0x0f)  # #eda10f

# delta-app-icon.svg'deki amber "D" path'i + SVG transform'u (BIREBIR; SVG transform listesi sagdan sola uygulanir)
$dPath      = 'M2900.763,-525.887h-4.238H2874v-26.62h1.419a21.185,21.185,0,0,1,7.074,1.287,33.344,33.344,0,0,1,7.428,3.856c7.193,5.733,10.841,12.959,10.841,21.476Zm-21.426-20.646v15.255h15.338a22.076,22.076,0,0,0-4.584-8.7,22.167,22.167,0,0,0-10.754-6.551Z'
$svgSize    = 64.0     # <svg viewBox="0 0 64 64">
$svgRadius  = 14.0     # <rect rx="14">

# Elle netlestirilmis 16x16 amber isaret (8x8 izgarada ortalanmis; 2px govde, 2px alt cubuk, tek pikselli yay).
# TUREME: delta-app-icon.svg'nin bu boyuta rasterlestirilmesi esik'lenip yay/govde/cubuk kalinliklari tam
# piksele oturtuldu — otomatik kucultme kenarlari yariya boler ve amber "D"yi camura cevirir (feasibility §3.2).
$d16 = @(
  '................',
  '................',
  '................',
  '................',
  '....###.........',
  '....#####.......',
  '....##.###......',
  '....##..###.....',
  '....##...###....',
  '....##....##....',
  '....########....',
  '....########....',
  '................',
  '................',
  '................',
  '................'
)

# Elle netlestirilmis 24x24 amber isaret — 16px ile AYNI turetme, 12x12 izgarada.
$d24 = @(
  '........................',
  '........................',
  '........................',
  '........................',
  '........................',
  '........................',
  '......####..............',
  '......######............',
  '......##..###...........',
  '......##...###..........',
  '......##....###.........',
  '......##.....###........',
  '......##......###.......',
  '......##.......###......',
  '......##........##......',
  '......##........##......',
  '......############......',
  '......############......',
  '........................',
  '........................',
  '........................',
  '........................',
  '........................',
  '........................'
)

# Yuvarlak kose testi — $inset kadar iceri girilmis, $radius yaricapli kare icinde mi?
function Inside([double]$x, [double]$y, [double]$size, [double]$inset, [double]$radius) {
  $lo = $inset; $hi = $size - $inset
  if ($x -lt $lo -or $x -gt $hi -or $y -lt $lo -or $y -gt $hi) { return $false }
  $cx = [Math]::Min([Math]::Max($x, $lo + $radius), $hi - $radius)
  $cy = [Math]::Min([Math]::Max($y, $lo + $radius), $hi - $radius)
  $dx = $x - $cx; $dy = $y - $cy
  return (($dx * $dx + $dy * $dy) -le ($radius * $radius) + 1e-9)
}

# Elle yazilmis piksel haritasindan 32bpp BGRA (alttan uste — ICO/BMP duzeni) kare uretir.
function New-HandTunedFrame([int]$size, [string[]]$map) {
  $k = $size / 16.0
  $pixels = New-Object byte[] ($size * $size * 4)
  for ($y = 0; $y -lt $size; $y++) {
    for ($x = 0; $x -lt $size; $x++) {
      $px = $x + 0.5; $py = $y + 0.5
      $inShape = Inside $px $py $size 0 (3 * $k)
      $inInner = Inside $px $py $size (1 * $k) (2 * $k)
      if ($map[$y][$x] -eq '#') { $rgb = $amber;      $a = 255 }
      elseif ($inInner)         { $rgb = $bg;         $a = 255 }
      elseif ($inShape)         { $rgb = $border;     $a = 255 }
      else                      { $rgb = @(0, 0, 0);  $a = 0 }

      $row = $size - 1 - $y                  # bottom-up
      $o = (($row * $size) + $x) * 4
      $pixels[$o + 0] = $rgb[2]              # B
      $pixels[$o + 1] = $rgb[1]              # G
      $pixels[$o + 2] = $rgb[0]              # R
      $pixels[$o + 3] = $a                   # A
    }
  }
  return ,$pixels        # virgul: PowerShell dizi donusunu ACMASIN (byte[] byte[] kalsin)
}

# Kaynak SVG'yi WPF ile $size'a rasterlestirir; BitmapSource doner (Bgra32, premultiply COZULMUS).
function New-RenderedBitmap([int]$size) {
  $scale = $size / $svgSize
  $bgBrush     = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb($bg[0], $bg[1], $bg[2]))
  $amberBrush  = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb($amber[0], $amber[1], $amber[2]))
  $borderBrush = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.Color]::FromRgb($border[0], $border[1], $border[2]))
  $pen = New-Object System.Windows.Media.Pen ($borderBrush, 1.0)   # SVG 64-uzayinda 1px; olcekle birlikte buyur

  # Geometry::Parse DONMUS bir nesne doner (Transform atanamaz) — donusumler DrawingContext'e push edilir.
  $geo = [System.Windows.Media.Geometry]::Parse($dPath)
  # SVG transform listesi SAGDAN SOLA uygulanir: translate(15.95,16.05) scale(1.2) translate(-2874,552.507)
  $dTransform = New-Object System.Windows.Media.TransformGroup
  $dTransform.Children.Add((New-Object System.Windows.Media.TranslateTransform (-2874, 552.507)))
  $dTransform.Children.Add((New-Object System.Windows.Media.ScaleTransform (1.2, 1.2)))
  $dTransform.Children.Add((New-Object System.Windows.Media.TranslateTransform (15.95, 16.05)))

  $visual = New-Object System.Windows.Media.DrawingVisual
  $dc = $visual.RenderOpen()
  # Tum cizim SVG'nin 64'luk uzayinda yapilir; tek bir olcek onu $size'a tasir (stroke da birlikte olceklenir).
  $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($scale, $scale)))
  # SVG: <rect width=64 height=64 rx=14 fill=bg> + <rect x=.5 y=.5 w=63 h=63 rx=13.5 stroke=border>
  $dc.DrawRoundedRectangle($bgBrush, $null, (New-Object System.Windows.Rect (0, 0, $svgSize, $svgSize)), $svgRadius, $svgRadius)
  $dc.DrawRoundedRectangle($null, $pen, (New-Object System.Windows.Rect (0.5, 0.5, 63.0, 63.0)), 13.5, 13.5)
  $dc.PushTransform($dTransform)
  $dc.DrawGeometry($amberBrush, $null, $geo)
  $dc.Pop()
  $dc.Pop()
  $dc.Close()

  $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap (
    $size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
  $rtb.Render($visual)
  # ICO icerigi DUZ (premultiply edilmemis) alfa ister — donusumu WIC yapsin.
  return New-Object System.Windows.Media.Imaging.FormatConvertedBitmap (
    $rtb, [System.Windows.Media.PixelFormats]::Bgra32, $null, 0.0)
}

# BitmapSource -> 32bpp BGRA, alttan uste (ICO/BMP duzeni)
function ConvertTo-BottomUpBgra([System.Windows.Media.Imaging.BitmapSource]$bmp) {
  $size = $bmp.PixelWidth
  $stride = $size * 4
  $top = New-Object byte[] ($stride * $size)
  $bmp.CopyPixels($top, $stride, 0)
  $out = New-Object byte[] ($stride * $size)
  for ($y = 0; $y -lt $size; $y++) {
    [Array]::Copy($top, $y * $stride, $out, ($size - 1 - $y) * $stride, $stride)
  }
  return ,$out            # bkz. New-HandTunedFrame — virgul zorunlu
}

function ConvertTo-PngBytes([System.Windows.Media.Imaging.BitmapSource]$bmp) {
  $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
  $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
  $ms = New-Object System.IO.MemoryStream
  $enc.Save($ms)
  return ,$ms.ToArray()
}

# Bir kare icin ICO govdesi: BITMAPINFOHEADER + XOR (BGRA) + AND maskesi (1bpp, satir 4 bayta hizali).
function New-BmpFrameBytes([int]$size, [byte[]]$bgra) {
  $maskStride = [Math]::Ceiling($size / 8.0)
  $maskStride = [int]([Math]::Ceiling($maskStride / 4.0) * 4)
  $andMask = New-Object byte[] ($maskStride * $size)   # hepsi 0: tumu opak/alfa gecerli

  $ms = New-Object System.IO.MemoryStream
  $bw = New-Object System.IO.BinaryWriter($ms)
  # BITMAPINFOHEADER (biHeight = 2x: XOR + AND maskesi)
  $bw.Write([uint32]40); $bw.Write([int32]$size); $bw.Write([int32]($size * 2))
  $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
  $bw.Write([uint32]($bgra.Length + $andMask.Length))
  $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
  $bw.Write($bgra); $bw.Write($andMask)
  $bw.Flush()
  return ,$ms.ToArray()
}

# $frames: her biri @{ Size = <int>; Body = <byte[]> } (Body = BMP govdesi VEYA ham PNG)
function New-IcoBytes($frames) {
  $ms = New-Object System.IO.MemoryStream
  $bw = New-Object System.IO.BinaryWriter($ms)
  # ICONDIR
  $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$frames.Count)
  # ICONDIRENTRY'ler — ilk govde dizinin hemen ardindan baslar
  $offset = 6 + (16 * $frames.Count)
  foreach ($f in $frames) {
    $dim = if ($f.Size -ge 256) { 0 } else { $f.Size }   # 256 => 0 (ICO alan genisligi 1 bayt)
    $bw.Write([byte]$dim); $bw.Write([byte]$dim); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$f.Body.Length); $bw.Write([uint32]$offset)
    $offset += $f.Body.Length
  }
  foreach ($f in $frames) { $bw.Write($f.Body) }
  $bw.Flush()
  return ,$ms.ToArray()
}

function Save-Ico([string]$name, $frames) {
  $bytes = New-IcoBytes $frames
  $out = Join-Path $PSScriptRoot $name
  [System.IO.File]::WriteAllBytes($out, $bytes)
  $sizes = ($frames | ForEach-Object { $_.Size }) -join '/'
  Write-Host "written: $out ($($bytes.Length) bytes, frames: $sizes)"
}

# --- tepsi ikonu: yalnizca elle netlestirilmis 16px (AppTrayIcon bunu kullanir) ---
$tray16 = @{ Size = 16; Body = (New-BmpFrameBytes 16 (New-HandTunedFrame 16 $d16)) }
Save-Ico 'tray-icon-16.ico' @($tray16)

# --- uygulama ikonu: 16/24 elle, 32/48 rasterlestirilmis BMP, 256 PNG-sikistirilmis (ICO standardi) ---
$appFrames = @(
  $tray16,
  @{ Size = 24; Body = (New-BmpFrameBytes 24 (New-HandTunedFrame 24 $d24)) }
)
foreach ($size in 32, 48) {
  $bmp = New-RenderedBitmap $size
  $appFrames += @{ Size = $size; Body = (New-BmpFrameBytes $size (ConvertTo-BottomUpBgra $bmp)) }
}
# 256 icin ham BMP ~264 KB olurdu; ICO 256'da PNG govdesini destekler (Vista+/WIC) — repoda kucuk kalir.
$appFrames += @{ Size = 256; Body = (ConvertTo-PngBytes (New-RenderedBitmap 256)) }
Save-Ico 'app-icon.ico' $appFrames
