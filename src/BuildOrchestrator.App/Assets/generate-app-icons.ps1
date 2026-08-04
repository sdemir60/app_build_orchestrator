# [design-v1.2.1 §6] Uygulama ikonu ureteci — URUN markasi (Build Orchestrator).
#
# Uretilen dosyalar (ikisi de bu klasore):
#   tray-icon-16.ico  — sistem tepsisi (AppTrayIcon 16px varyantini kullanir)
#   app-icon.ico      — cok boyutlu uygulama ikonu: 16 / 24 / 32 / 48 / 256 (<ApplicationIcon> + Window.Icon)
#
# Kaynak sanat: `.claude/outputs/2026-08-05-01-26-design-v1.2.1/prototype/assets/app-icon.svg`
#   tile (near-black gradient + 1px border) + bes pill serit + amber gradient chevron.
#   Serit ve chevron geometrisi app-mark.svg ile AYNIdir; tile ve gradient'ler yalnizca ikon varyantinda vardir.
#
# ONCEKI SURUM (T62/T64) Delta'nin kendi ikonunu (delta-app-icon.svg) rasterlestiriyordu. design-v1.2.0 ile
# uygulama kendi markasini kazandi; Delta artik FIRMA logosudur ve ikonda yer almaz.
#
# KUCUK BOYUT KARARI — ayrintili gerekce asagida `$SmallSizes`:
#   16 ve 24px'te BES SERIT OKUNMUYOR (serit yuksekligi 286-biriminde 21 → 16px'te ~1.2 piksel). Bu iki boyut
#   markanin SADELESTIRILMIS halini cizer: tile + yalnizca amber chevron. Bu, tasarimda LITERAL olarak
#   verilmemis bir turetmedir (Icon.CaptionRestore ile ayni statu) — gerekcesi burada yazilidir.
#
# Calistirma:
#   powershell -NoProfile -ExecutionPolicy Bypass -File src/BuildOrchestrator.App/Assets/generate-app-icons.ps1
# NOT: WPF rasterlestirmesi icin STA gerekir; powershell.exe varsayilan olarak STA'dir (pwsh icin -STA gerekir).
#
# Tani modu (dosya YAZMAZ, verilen boyutu ASCII olarak dokumler — kucuk boyut kararini gozle dogrulamak icin):
#   ... -File generate-app-icons.ps1 -DumpAscii 16

param(
  [int]$DumpAscii = 0,
  [switch]$FullArtAtSmallSizes   # tani: 16/24'u de tam sanatla ciz (sadelestirmeyi ATLA)
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase

# ---- kaynak sanatin uzayi ve renkleri (app-icon.svg'den BIREBIR) -------------------------------------------
$ArtSize    = 286.0     # <svg viewBox="0 0 286 286">
$TileRadius = 31.0      # <rect rx="31">
$StripDx    = 5.5       # <g transform="translate(5.5 0)">

# 16/24'te tam sanat okunmuyor: serit yuksekligi 21/286 → 16px'te 1.18 piksel. Bu boyutlarda yalnizca chevron
# cizilir (marka tanınırligini tasiyan tek ogedir; amber kontrasti near-black tile'da net kalir).
$SmallSizes = @(16, 24)
# Sadelestirilmis varyantta chevron'un tile yuksekligine orani. Tam sanatta isaret %42'dir; serit olmayinca
# o oran kucuk boyutta bos bir tile birakiyordu (16px'te isaret ~7 piksel). %60 optik olarak dengeli.
$ChevronFill = 0.60

function Rgb([string]$hex) {
  return [System.Windows.Media.Color]::FromRgb(
    [Convert]::ToByte($hex.Substring(0, 2), 16),
    [Convert]::ToByte($hex.Substring(2, 2), 16),
    [Convert]::ToByte($hex.Substring(4, 2), 16))
}

# Dikey (0,0)->(0,1) linear gradient — SVG'deki serit gradient'lerinin tamami bu yondedir.
function VerticalBrush($stops) {
  $collection = New-Object System.Windows.Media.GradientStopCollection
  foreach ($s in $stops) {
    $collection.Add((New-Object System.Windows.Media.GradientStop ((Rgb $s[1]), [double]$s[0])))
  }
  $brush = New-Object System.Windows.Media.LinearGradientBrush (
    $collection,
    (New-Object System.Windows.Point (0, 0)),
    (New-Object System.Windows.Point (0, 1)))
  $brush.Freeze()
  return $brush
}

# app-icon.svg <defs> — SVG'deki durak listeleri birebir.
$TileBrush = VerticalBrush @( @(0, '141417'), @(0.56, '0E0E10'), @(1, '0A0A0C') )
$TileBorder = Rgb '2A2A30'

$StripBrushes = @{
  TopDark    = VerticalBrush @( @(0, '54545C'), @(0.20, '494950'), @(0.50, '414147'), @(0.80, '3A3A42'), @(1, '44444B') )
  TopAmber   = VerticalBrush @( @(0, 'FFB52E'), @(0.20, 'F7AE25'), @(0.50, 'F1AB2E'), @(0.80, 'EDA10F'), @(1, 'D9910D') )
  MiddleDark = VerticalBrush @( @(0, '3A3A42'), @(0.20, '313136'), @(0.50, '2A2A30'), @(1, '202024') )
  White      = VerticalBrush @( @(0, 'EDEDEE'), @(0.18, 'DADADD'), @(0.50, 'C8C8CD'), @(0.80, 'B9B9BF'), @(1, 'A9A9B0') )
  Silver     = VerticalBrush @( @(0, 'A9A9B0'), @(0.20, '929299'), @(0.50, '85858D'), @(0.80, '76767E'), @(1, '83838B') )
}

# Seritler: x, y, genislik, yukseklik, yaricap, firca anahtari (app-icon.svg'den birebir)
$Strips = @(
  @{ X = 68; Y = 83;  W = 35; H = 21; R = 10.5; Brush = 'TopDark' },
  @{ X = 114; Y = 83;  W = 51; H = 21; R = 10.5; Brush = 'TopAmber' },
  @{ X = 46; Y = 131; W = 39; H = 21; R = 10.5; Brush = 'MiddleDark' },
  @{ X = 97; Y = 131; W = 60; H = 22; R = 11.0; Brush = 'White' },
  @{ X = 60; Y = 178; W = 57; H = 21; R = 10.5; Brush = 'Silver' }
)

$ChevronPath = 'M151 83 L171.5 83 C176.1 83 179.2 85.1 182.3 88.5 L219.8 131.5 C222.7 134.9 224 138.3 224 142.4 ' +
               'C224 146.5 222.7 149.8 219.8 153.2 L179.1 198.3 C176.0 201.6 172.3 203 168.0 203 L151.0 203 ' +
               'C145.8 203 142.1 200.4 140.5 196.4 C138.8 192.1 140.2 187.8 143.5 184.2 L177.8 146.2 ' +
               'C180.6 143.2 180.8 140.8 178.2 137.8 L144.7 103.7 C141.8 100.7 140.4 96.9 140.4 93.0 ' +
               'C140.4 87.5 145.1 83 151 83 Z'

# Chevron gradient'i userSpaceOnUse (156,82)->(177,204) — sanat uzayinda MUTLAK koordinatlar.
function New-ChevronBrush {
  $stops = @( @(0, 'FFB52E'), @(0.13, 'F9AE25'), @(0.26, 'F4A91D'), @(0.38, 'EDA10F'), @(0.51, 'DE940D'),
              @(0.64, 'CF880C'), @(0.76, 'B87A0B'), @(0.89, 'A66D09'), @(1, '8B5907') )
  $collection = New-Object System.Windows.Media.GradientStopCollection
  foreach ($s in $stops) {
    $collection.Add((New-Object System.Windows.Media.GradientStop ((Rgb $s[1]), [double]$s[0])))
  }
  $brush = New-Object System.Windows.Media.LinearGradientBrush (
    $collection,
    (New-Object System.Windows.Point (156, 82)),
    (New-Object System.Windows.Point (177, 204)))
  $brush.MappingMode = [System.Windows.Media.BrushMappingMode]::Absolute
  $brush.Freeze()
  return $brush
}

# Kaynak sanati $size'a rasterlestirir. $simplify: yalnizca tile + chevron (kucuk boyutlar).
function New-RenderedBitmap([int]$size, [bool]$simplify) {
  $scale = $size / $ArtSize
  $root = New-Object System.Windows.Media.ContainerVisual

  # --- tile ---
  $tile = New-Object System.Windows.Media.DrawingVisual
  $dc = $tile.RenderOpen()
  $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($scale, $scale)))
  $dc.DrawRoundedRectangle($TileBrush, $null,
    (New-Object System.Windows.Rect (1, 1, 284, 284)), $TileRadius, $TileRadius)
  $pen = New-Object System.Windows.Media.Pen ((New-Object System.Windows.Media.SolidColorBrush $TileBorder), 1.0)
  $dc.DrawRoundedRectangle($null, $pen,
    (New-Object System.Windows.Rect (1.5, 1.5, 283, 283)), 30.5, 30.5)
  $dc.Pop()
  $dc.Close()
  [void]$root.Children.Add($tile)

  if (-not $simplify) {
    # --- seritler (SVG'de kendi drop-shadow'u var; WPF'te gorsel-seviyesi Effect ile) ---
    $stripVisual = New-Object System.Windows.Media.DrawingVisual
    $dc = $stripVisual.RenderOpen()
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($scale, $scale)))
    $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform ($StripDx, 0)))
    foreach ($s in $Strips) {
      $dc.DrawRoundedRectangle($StripBrushes[$s.Brush], $null,
        (New-Object System.Windows.Rect ($s.X, $s.Y, $s.W, $s.H)), $s.R, $s.R)
    }
    $dc.Pop(); $dc.Pop(); $dc.Close()
    $shadow = New-Object System.Windows.Media.Effects.DropShadowEffect
    $shadow.ShadowDepth = 2 * $scale; $shadow.Direction = 270
    $shadow.BlurRadius = 3.1 * $scale; $shadow.Opacity = 0.92
    $shadow.Color = [System.Windows.Media.Colors]::Black
    $stripVisual.Effect = $shadow
    [void]$root.Children.Add($stripVisual)
  }

  # --- chevron ---
  $arrowVisual = New-Object System.Windows.Media.DrawingVisual
  $dc = $arrowVisual.RenderOpen()
  $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($scale, $scale)))
  if ($simplify) {
    # Sadelestirilmis varyantta chevron TILE'A OTURUR: tam sanatta isaret tile'in %42'si kadardir (seritlerle
    # birlikte dengeli durur), ama serit olmayinca ortada kucucuk kalirdi. Chevron'un bbox'i (145.9..229.5 x,
    # 83..203 y) tile yuksekliginin $ChevronFill'i olacak sekilde olceklenip ORTALANIR.
    $bboxX = 140.4 + $StripDx; $bboxY = 83.0; $bboxW = 83.6; $bboxH = 120.0
    $fit = ($ChevronFill * $ArtSize) / $bboxH
    $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform (
      (($ArtSize - $bboxW * $fit) / 2.0), (($ArtSize - $bboxH * $fit) / 2.0))))
    $dc.PushTransform((New-Object System.Windows.Media.ScaleTransform ($fit, $fit)))
    $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform (-$bboxX, -$bboxY)))
    $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform ($StripDx, 0)))
  }
  else {
    $dc.PushTransform((New-Object System.Windows.Media.TranslateTransform ($StripDx, 0)))
  }
  $dc.DrawGeometry((New-ChevronBrush), $null, [System.Windows.Media.Geometry]::Parse($ChevronPath))
  if ($simplify) { $dc.Pop(); $dc.Pop(); $dc.Pop() }
  $dc.Pop(); $dc.Pop(); $dc.Close()
  $arrowShadow = New-Object System.Windows.Media.Effects.DropShadowEffect
  $arrowShadow.ShadowDepth = 2.2 * $scale; $arrowShadow.Direction = 270
  $arrowShadow.BlurRadius = 3.6 * $scale; $arrowShadow.Opacity = 0.94
  $arrowShadow.Color = [System.Windows.Media.Colors]::Black
  $arrowVisual.Effect = $arrowShadow
  [void]$root.Children.Add($arrowVisual)

  $rtb = New-Object System.Windows.Media.Imaging.RenderTargetBitmap (
    $size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
  $rtb.Render($root)
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
  return ,$out            # virgul: PowerShell dizi donusunu ACMASIN
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
  $bw.Write([uint32]40); $bw.Write([int32]$size); $bw.Write([int32]($size * 2))
  $bw.Write([uint16]1); $bw.Write([uint16]32); $bw.Write([uint32]0)
  $bw.Write([uint32]($bgra.Length + $andMask.Length))
  $bw.Write([int32]0); $bw.Write([int32]0); $bw.Write([uint32]0); $bw.Write([uint32]0)
  $bw.Write($bgra); $bw.Write($andMask)
  $bw.Flush()
  return ,$ms.ToArray()
}

function New-IcoBytes($frames) {
  $ms = New-Object System.IO.MemoryStream
  $bw = New-Object System.IO.BinaryWriter($ms)
  $bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$frames.Count)
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

function Get-Frame([int]$size) {
  $simplify = (-not $FullArtAtSmallSizes) -and ($SmallSizes -contains $size)
  return New-RenderedBitmap $size $simplify
}

# --- tani modu: pikselleri ASCII olarak dokumle (dosya yazmaz) ---
if ($DumpAscii -gt 0) {
  $bmp = Get-Frame $DumpAscii
  $stride = $DumpAscii * 4
  $px = New-Object byte[] ($stride * $DumpAscii)
  $bmp.CopyPixels($px, $stride, 0)
  # Luminans kademeleri: koyu tile '.', orta seritler '+'/'o', parlak (beyaz serit/amber) '#'
  for ($y = 0; $y -lt $DumpAscii; $y++) {
    $row = ''
    for ($x = 0; $x -lt $DumpAscii; $x++) {
      $o = $y * $stride + $x * 4
      $lum = (0.114 * $px[$o]) + (0.587 * $px[$o + 1]) + (0.299 * $px[$o + 2])
      if ($px[$o + 3] -lt 32) { $row += ' ' }
      elseif ($lum -lt 40)  { $row += '.' }
      elseif ($lum -lt 90)  { $row += '+' }
      elseif ($lum -lt 150) { $row += 'o' }
      else                  { $row += '#' }
    }
    Write-Host $row
  }
  return
}

# --- tepsi ikonu: 16px (AppTrayIcon bunu kullanir) ---
$tray16 = @{ Size = 16; Body = (New-BmpFrameBytes 16 (ConvertTo-BottomUpBgra (Get-Frame 16))) }
Save-Ico 'tray-icon-16.ico' @($tray16)

# --- uygulama ikonu: 16/24 sadelestirilmis, 32/48 tam sanat BMP, 256 PNG-sikistirilmis (ICO standardi) ---
$appFrames = @($tray16)
foreach ($size in 24, 32, 48) {
  $appFrames += @{ Size = $size; Body = (New-BmpFrameBytes $size (ConvertTo-BottomUpBgra (Get-Frame $size))) }
}
# 256 icin ham BMP ~264 KB olurdu; ICO 256'da PNG govdesini destekler (Vista+/WIC) — repoda kucuk kalir.
$appFrames += @{ Size = 256; Body = (ConvertTo-PngBytes (Get-Frame 256)) }
Save-Ico 'app-icon.ico' $appFrames
